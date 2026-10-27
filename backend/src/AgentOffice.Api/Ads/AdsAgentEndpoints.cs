using System.Text.Json;
using AgentOffice.Api.Ai;
using AgentOffice.Api.Data;
using AgentOffice.Api.MercadoLivre;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ads;

public record RecommendationView(
    int Id,
    string Status,
    string Scope,
    long? CampaignId,
    string? CampaignName,
    long? AdGroupId,
    string? AdGroupTitle,
    string Action,
    string Where,
    string Title,
    double Priority,
    string Confidence,
    decimal? ImpactBrl,
    bool ImpactUnknown,
    string[] FindingCodes,
    JsonElement Content,
    DateTimeOffset? DecidedAt,
    string? DecisionNote);

public record RunView(
    int Id,
    DateTimeOffset CreatedAt,
    string Model,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string ManualHash,
    string Summary,
    JsonElement DataQuality,
    JsonElement Discarded,
    int? PromptTokens,
    int? CompletionTokens);

public record RunSummary(int Id, DateTimeOffset CreatedAt, int Recommendations, int Pending);

public record DecisionRequest(string? Decision, string? Note);

// Rotas do agente de Product Ads. Todas só locais. Só /analyze chama a IA (gera custo na OpenAI).
public static class AdsAgentEndpoints
{
    // Impede duas análises ao mesmo tempo (duplo clique = custo em dobro).
    private static readonly SemaphoreSlim AnalyzeLock = new(1, 1);

    public static void MapAdsAgentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/ads").AddEndpointFilter(MercadoLivreEndpoints.LocalOnly);

        // POST /ads/analyze?days=30: roda o agente (IA) sobre os dados salvos e grava as recomendações como Pending.
        group.MapPost("/analyze", async (AdsAdvisor advisor, CancellationToken ct, int days = 30) =>
        {
            if (!await AnalyzeLock.WaitAsync(0, ct))
                return Results.Json(new { error = "Já existe uma análise em andamento." }, statusCode: StatusCodes.Status409Conflict);

            try
            {
                var result = await advisor.RunAsync(days, ct);
                return Results.Ok(new { runId = result.Run.Id, recommendations = result.Recommendations.Count });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (OpenAiException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(new { error = $"Falha de rede ao falar com a OpenAI: {ex.Message}" }, statusCode: StatusCodes.Status502BadGateway);
            }
            finally
            {
                AnalyzeLock.Release();
            }
        });

        // GET /ads/recommendations?runId=: recomendações de uma análise (a mais recente por padrão), com o histórico de análises.
        group.MapGet("/recommendations", async (AgentOfficeDb db, CancellationToken ct, int? runId) =>
        {
            var runs = await db.AdAnalysisRuns.ToListAsync(ct);
            var counts = await db.AdRecommendations
                .GroupBy(r => r.RunId)
                .Select(g => new { RunId = g.Key, Total = g.Count(), Pending = g.Count(r => r.Status == "Pending") })
                .ToDictionaryAsync(x => x.RunId, ct);

            var history = runs
                .OrderByDescending(r => r.Id)
                .Select(r => new RunSummary(
                    r.Id, r.CreatedAt,
                    counts.TryGetValue(r.Id, out var c) ? c.Total : 0,
                    counts.TryGetValue(r.Id, out var p) ? p.Pending : 0))
                .ToList();
            var pendingTotal = history.Sum(h => h.Pending);

            var run = runId is null ? runs.MaxBy(r => r.Id) : runs.FirstOrDefault(r => r.Id == runId);
            if (run is null) return Results.Ok(new { run = (RunView?)null, recommendations = Array.Empty<RecommendationView>(), runs = history, pendingTotal });

            var campaigns = await db.AdCampaigns.ToDictionaryAsync(c => c.CampaignId, c => c.Name, ct);
            var groupTitles = await db.AdGroupMetrics.ToDictionaryAsync(g => g.AdGroupId, g => g.Title, ct);

            var recs = (await db.AdRecommendations.Where(r => r.RunId == run.Id).ToListAsync(ct))
                .OrderByDescending(r => r.Priority)
                .Select(r => new RecommendationView(
                    r.Id, r.Status, r.Scope,
                    r.CampaignId, r.CampaignId is null ? null : campaigns.GetValueOrDefault(r.CampaignId.Value),
                    r.AdGroupId, r.AdGroupId is null ? null : groupTitles.GetValueOrDefault(r.AdGroupId.Value),
                    r.Action, r.Where, r.Title, r.Priority, r.Confidence, r.ImpactBrl, r.ImpactUnknown,
                    r.FindingCodes.Split(',', StringSplitOptions.RemoveEmptyEntries),
                    JsonSerializer.Deserialize<JsonElement>(r.ContentJson),
                    r.DecidedAt, r.DecisionNote))
                .ToList();

            var runView = new RunView(
                run.Id, run.CreatedAt, run.Model, run.PeriodFrom, run.PeriodTo, run.ManualHash, run.Summary,
                JsonSerializer.Deserialize<JsonElement>(string.IsNullOrEmpty(run.DataQualityJson) ? "{}" : run.DataQualityJson),
                JsonSerializer.Deserialize<JsonElement>(string.IsNullOrEmpty(run.DiscardedJson) ? "[]" : run.DiscardedJson),
                run.PromptTokens, run.CompletionTokens);

            return Results.Ok(new { run = runView, recommendations = recs, runs = history, pendingTotal });
        });

        // POST /ads/recommendations/{id}/decision: registra a SUA decisão (approve ou reject) e, opcionalmente, o motivo.
        // Altera só o banco local. Aplicar a mudança continua sendo um passo manual no Mercado Livre.
        group.MapPost("/recommendations/{id:int}/decision", async (int id, DecisionRequest body, AgentOfficeDb db, CancellationToken ct) =>
        {
            var status = body.Decision switch
            {
                "approve" => "Approved",
                "reject" => "Rejected",
                _ => null,
            };
            if (status is null) return Results.BadRequest(new { error = "Decisão inválida (use approve ou reject)." });

            var rec = await db.AdRecommendations.FindAsync([id], ct);
            if (rec is null) return Results.NotFound();
            if (rec.Status != "Pending") return Results.Conflict(new { error = "Esta recomendação já foi decidida." });

            rec.Status = status;
            rec.DecidedAt = DateTimeOffset.UtcNow;
            rec.DecisionNote = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { rec.Id, rec.Status, rec.DecidedAt, rec.DecisionNote });
        });
    }
}
