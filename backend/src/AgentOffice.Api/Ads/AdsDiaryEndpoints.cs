using AgentOffice.Api.Data;
using AgentOffice.Api.MercadoLivre;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ads;

public record NoteRequest(string? Note);
public record DiaryNoteRequest(DateOnly? Date, long? CampaignId, string? Text);
public record AppliedRequest(DateOnly? Date);

// Rotas do diário de mudanças e da avaliação antes/depois. Só leem e gravam o banco local (nada vai ao Mercado Livre nem à IA).
public static class AdsDiaryEndpoints
{
    private const int MaxTextLength = 1000;

    public static void MapAdsDiaryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/ads").AddEndpointFilter(MercadoLivreEndpoints.RequireAccess);

        // GET /ads/diary: linha do tempo de mudanças, cada uma com a comparação antes/depois.
        group.MapGet("/diary", async (AdsDiaryService diary, CancellationToken ct) => Results.Ok(await diary.BuildAsync(ct)));

        // PUT /ads/changes/{id}/note: motivo da mudança (vale para todas as mudanças da mesma campanha e dia).
        group.MapPut("/changes/{id:int}/note", async (int id, NoteRequest body, AgentOfficeDb db, CancellationToken ct) =>
        {
            var note = body.Note?.Trim();
            if (note is { Length: > MaxTextLength }) return Results.BadRequest(new { error = $"O motivo pode ter até {MaxTextLength} caracteres." });

            var change = await db.AdChanges.FindAsync([id], ct);
            if (change is null) return Results.NotFound();

            var day = DateOnly.FromDateTime(change.DetectedAt.LocalDateTime);
            var peers = (await db.AdChanges.Where(c => c.CampaignId == change.CampaignId).ToListAsync(ct))
                .Where(c => DateOnly.FromDateTime(c.DetectedAt.LocalDateTime) == day);
            foreach (var peer in peers) peer.Note = string.IsNullOrEmpty(note) ? null : note;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id, note = string.IsNullOrEmpty(note) ? null : note });
        });

        // POST /ads/diary/notes: anotação livre (promoção, mudança de preço, falta de estoque...).
        group.MapPost("/diary/notes", async (DiaryNoteRequest body, AgentOfficeDb db, CancellationToken ct) =>
        {
            var text = body.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return Results.BadRequest(new { error = "Escreva o texto da anotação." });
            if (text.Length > MaxTextLength) return Results.BadRequest(new { error = $"A anotação pode ter até {MaxTextLength} caracteres." });
            if (body.Date is null) return Results.BadRequest(new { error = "Informe a data da anotação." });
            if (body.CampaignId is not null && !await db.AdCampaigns.AnyAsync(c => c.CampaignId == body.CampaignId, ct))
                return Results.BadRequest(new { error = "Campanha inexistente." });

            var note = new AdDiaryNote { Date = body.Date.Value, CampaignId = body.CampaignId, Text = text, CreatedAt = DateTimeOffset.UtcNow };
            db.AdDiaryNotes.Add(note);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { note.Id });
        });

        group.MapDelete("/diary/notes/{id:int}", async (int id, AgentOfficeDb db, CancellationToken ct) =>
        {
            var note = await db.AdDiaryNotes.FindAsync([id], ct);
            if (note is null) return Results.NotFound();

            db.AdDiaryNotes.Remove(note);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // PUT /ads/recommendations/{id}/applied: informa o dia em que você aplicou a mudança (null desfaz).
        group.MapPut("/recommendations/{id:int}/applied", async (int id, AppliedRequest body, AgentOfficeDb db, CancellationToken ct) =>
        {
            var rec = await db.AdRecommendations.FindAsync([id], ct);
            if (rec is null) return Results.NotFound();
            if (rec.Status != "Approved") return Results.Conflict(new { error = "Só recomendações aprovadas podem ser marcadas como aplicadas." });

            if (body.Date is not null)
            {
                if (body.Date > DateOnly.FromDateTime(DateTime.Now)) return Results.BadRequest(new { error = "A data de aplicação não pode ser futura." });
                if (rec.DecidedAt is not null && body.Date < DateOnly.FromDateTime(rec.DecidedAt.Value.LocalDateTime))
                    return Results.BadRequest(new { error = "A data de aplicação não pode ser anterior à aprovação." });
            }

            rec.AppliedOn = body.Date;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { rec.Id, rec.AppliedOn });
        });
    }
}
