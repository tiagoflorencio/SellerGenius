using System.Text.Json;
using AgentOffice.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ads;

public record DiaryChangeLine(int Id, string Field, string OldValue, string NewValue);

public record DiaryEvent(
    string Key,
    string Kind,                 // "change" (detectada na sincronização) ou "applied" (aplicação marcada por você)
    DateOnly Date,
    long? CampaignId,
    string? CampaignName,
    List<DiaryChangeLine> Changes,
    int? RecommendationId,
    string? RecommendationTitle,
    string? RecommendationAction,
    string? Note,
    int? NoteChangeId,           // mudança usada para gravar o motivo (vale para o grupo todo)
    string? SuccessMetric,
    string? SuccessCriterion,
    List<string> Confounders,
    EvaluationResult Evaluation);

public record DiaryNoteView(int Id, DateOnly Date, long? CampaignId, string? CampaignName, string Text);

public record AwaitingApplication(int RecommendationId, string Title, string Action, long? CampaignId, string? CampaignName, DateTimeOffset? DecidedAt);

public record DiaryResult(List<DiaryEvent> Events, List<DiaryNoteView> Notes, List<AwaitingApplication> Awaiting);

// Liga mudanças detectadas às recomendações aprovadas que as motivaram. Só as ações de configuração da campanha
// podem ser reconhecidas sozinhas (orçamento e meta de ROAS); as demais dependem de "marcar como aplicada".
public static class AdsDiaryLinker
{
    public static string? FieldOf(string action) => action switch
    {
        "increase_budget" or "decrease_budget" => "Budget",
        "adjust_roas_target" or "increase_roas_target" or "decrease_roas_target" => "RoasTarget",
        _ => null,
    };

    // Retorna changeId -> recommendationId. Cada recomendação liga a no máximo uma mudança.
    public static Dictionary<int, int> Link(IEnumerable<AdChange> unlinked, IEnumerable<AdRecommendation> approved, ISet<int> alreadyUsed)
    {
        var used = new HashSet<int>(alreadyUsed);
        var result = new Dictionary<int, int>();

        foreach (var change in unlinked.OrderBy(c => c.DetectedAt))
        {
            var match = approved
                .Where(r => r.Status == "Approved" && r.CampaignId == change.CampaignId && !used.Contains(r.Id)
                            && FieldOf(r.Action) == change.Field && r.DecidedAt is not null && r.DecidedAt <= change.DetectedAt)
                .OrderByDescending(r => r.DecidedAt)
                .FirstOrDefault();
            if (match is null) continue;

            used.Add(match.Id);
            result[change.Id] = match.Id;
        }

        return result;
    }
}

// Monta o diário: linha do tempo de mudanças (detectadas ou marcadas por você), com a avaliação antes/depois de cada uma.
public class AdsDiaryService(AgentOfficeDb db, AdsSettingsService settingsService)
{
    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["Budget"] = "orçamento",
        ["RoasTarget"] = "meta de ROAS",
        ["Status"] = "status",
        ["Strategy"] = "estratégia",
        ["AutomaticBudget"] = "orçamento automático",
    };

    private record Draft(
        string Key, string Kind, DateOnly Date, DateOnly GapStart, long? CampaignId, List<AdChange> Changes,
        AdRecommendation? Rec, bool Indirect);

    public async Task<DiaryResult> BuildAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Now);

        // O SQLite não ordena DateTimeOffset no banco: ordena em memória.
        var changes = (await db.AdChanges.ToListAsync(ct)).OrderBy(c => c.DetectedAt).ToList();
        var recs = await db.AdRecommendations.Where(r => r.Status == "Approved").ToListAsync(ct);
        var notes = await db.AdDiaryNotes.OrderByDescending(n => n.Date).ToListAsync(ct);
        var campaigns = await db.AdCampaigns.ToListAsync(ct);
        var names = campaigns.ToDictionary(c => c.CampaignId, c => c.Name);
        var syncedOn = campaigns.ToDictionary(c => c.CampaignId, c => Local(c.SyncedAt));
        var snapshotDates = (await db.AdCampaignSnapshots.Select(s => new { s.CampaignId, s.Date }).ToListAsync(ct))
            .ToLookup(s => s.CampaignId, s => s.Date);
        var daily = (await db.AdCampaignDaily.ToListAsync(ct)).ToLookup(d => d.CampaignId);

        // 1) Liga mudanças novas às recomendações aprovadas (e grava, para a ligação não mudar depois).
        var links = AdsDiaryLinker.Link(
            changes.Where(c => c.RecommendationId is null), recs,
            changes.Where(c => c.RecommendationId is not null).Select(c => c.RecommendationId!.Value).ToHashSet());
        foreach (var change in changes.Where(c => links.ContainsKey(c.Id)))
            change.RecommendationId = links[change.Id];
        if (links.Count > 0) await db.SaveChangesAsync(ct);

        var recById = recs.ToDictionary(r => r.Id);

        // 2) Eventos: mudanças detectadas (agrupadas por campanha e dia) + aplicações marcadas por você sem mudança detectada.
        var drafts = new List<Draft>();
        foreach (var g in changes.GroupBy(c => (c.CampaignId, Day: Local(c.DetectedAt))))
        {
            var list = g.ToList();
            var rec = list.Select(c => c.RecommendationId).FirstOrDefault(id => id is not null) is { } rid ? recById.GetValueOrDefault(rid) : null;

            // Se você informou o dia da aplicação, ele é mais preciso que o dia da sincronização.
            var date = rec?.AppliedOn ?? g.Key.Day;
            var previous = snapshotDates[g.Key.CampaignId].Where(d => d < g.Key.Day).DefaultIfEmpty().Max();
            var gapStart = rec?.AppliedOn ?? (previous == default ? g.Key.Day : previous);

            drafts.Add(new Draft($"c{list[0].Id}", "change", date, gapStart, g.Key.CampaignId, list, rec, false));
        }

        var linkedRecIds = changes.Where(c => c.RecommendationId is not null).Select(c => c.RecommendationId!.Value).ToHashSet();
        foreach (var rec in recs.Where(r => r.AppliedOn is not null && !linkedRecIds.Contains(r.Id)))
        {
            drafts.Add(new Draft($"r{rec.Id}", "applied", rec.AppliedOn!.Value, rec.AppliedOn.Value, rec.CampaignId, [], rec, rec.Scope == "ad_group"));
        }

        // 3) Avalia cada evento.
        var events = new List<DiaryEvent>();
        foreach (var d in drafts)
        {
            var content = ReadContent(d.Rec);
            var window = Math.Clamp(content.WindowDays ?? settings.EvaluationWindowDays, 7, 30);
            var name = d.CampaignId is null ? null : names.GetValueOrDefault(d.CampaignId.Value);

            var confounders = d.CampaignId is null ? [] : FindConfounders(d, drafts, notes, window);
            var evaluation = d.CampaignId is null
                ? AdsBeforeAfter.Unavailable("A recomendação não tem campanha associada; não há como medir antes e depois.", window)
                : AdsBeforeAfter.Evaluate(daily[d.CampaignId.Value].ToList(), new EvaluationInput(d.Date, d.GapStart, window, d.Indirect, confounders.Count), settings, today,
                    syncedOn.TryGetValue(d.CampaignId.Value, out var synced) ? synced : null);
            evaluation.Notes.AddRange(confounders.Select(c => "Fator no período: " + c));

            var note = d.Changes.Select(c => c.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            events.Add(new DiaryEvent(
                d.Key, d.Kind, d.Date, d.CampaignId, name,
                d.Changes.Select(c => new DiaryChangeLine(c.Id, c.Field, c.OldValue, c.NewValue)).ToList(),
                d.Rec?.Id, d.Rec?.Title, d.Rec?.Action, note, d.Changes.FirstOrDefault()?.Id,
                content.SuccessMetric, content.SuccessCriterion, confounders, evaluation));
        }

        var awaiting = recs
            .Where(r => r.AppliedOn is null && !linkedRecIds.Contains(r.Id) && r.Action is not ("no_action" or "collect_more_data"))
            .OrderByDescending(r => r.DecidedAt)
            .Select(r => new AwaitingApplication(r.Id, r.Title, r.Action, r.CampaignId,
                r.CampaignId is null ? null : names.GetValueOrDefault(r.CampaignId.Value), r.DecidedAt))
            .ToList();

        var noteViews = notes
            .Select(n => new DiaryNoteView(n.Id, n.Date, n.CampaignId, n.CampaignId is null ? null : names.GetValueOrDefault(n.CampaignId.Value), n.Text))
            .ToList();

        return new DiaryResult(events.OrderByDescending(e => e.Date).ToList(), noteViews, awaiting);
    }

    // Outras mudanças e anotações da mesma campanha (ou da conta) dentro do período comparado.
    private static List<string> FindConfounders(Draft d, List<Draft> all, List<AdDiaryNote> notes, int window)
    {
        var from = d.GapStart.AddDays(-window);
        var to = d.Date.AddDays(window);
        var result = new List<string>();

        foreach (var other in all.Where(o => o.Key != d.Key && o.CampaignId == d.CampaignId && o.Date >= from && o.Date <= to))
        {
            var what = other.Changes.Count > 0
                ? string.Join(", ", other.Changes.Select(c => FieldNames.GetValueOrDefault(c.Field, c.Field)))
                : other.Rec?.Title ?? "mudança";
            result.Add($"{other.Date:dd/MM}: mudança em {what}");
        }

        foreach (var n in notes.Where(n => (n.CampaignId is null || n.CampaignId == d.CampaignId) && n.Date >= from && n.Date <= to))
            result.Add($"{n.Date:dd/MM}: {n.Text}");

        return result;
    }

    private record Content(int? WindowDays, string? SuccessMetric, string? SuccessCriterion);

    // Lê, do JSON da recomendação, a janela de avaliação e o critério de sucesso que a IA sugeriu.
    private static Content ReadContent(AdRecommendation? rec)
    {
        if (rec is null || string.IsNullOrEmpty(rec.ContentJson)) return new Content(null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(rec.ContentJson);
            if (!doc.RootElement.TryGetProperty("teste_sugerido", out var t) || t.ValueKind != JsonValueKind.Object)
                return new Content(null, null, null);

            int? window = t.TryGetProperty("janela_avaliacao_dias", out var w) && w.TryGetInt32(out var days) ? days : null;
            return new Content(window, Text(t, "metrica_sucesso"), Text(t, "criterio"));
        }
        catch (JsonException)
        {
            return new Content(null, null, null);
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateOnly Local(DateTimeOffset value) => DateOnly.FromDateTime(value.LocalDateTime);
}
