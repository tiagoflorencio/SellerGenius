using AgentOffice.Api.Analysis;
using AgentOffice.Api.Data;
using AgentOffice.Api.MercadoLivre;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ai;

// Rotas do agente de anúncios. Todas só locais (nada de dados do negócio pela internet).
public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/agent").AddEndpointFilter(MercadoLivreEndpoints.LocalOnly);

        // GET /agent/diagnosis: alertas por regras para todos os anúncios. Gratuito (não chama a IA).
        group.MapGet("/diagnosis", async (AgentOfficeDb db, CancellationToken ct) =>
        {
            var listings = await db.Listings.ToListAsync(ct);
            return Results.Ok(ListingAnalyzer.Analyze(listings)
                .OrderByDescending(d => d.Flags.Count)
                .ToList());
        });

        // POST /agent/suggest/{itemId}: pede à IA sugestões para UM anúncio (gera custo na OpenAI).
        // De propósito é um por vez, para você controlar o gasto. Salva como "Pending" e NÃO altera o Mercado Livre.
        group.MapPost("/suggest/{itemId}", async (string itemId, ListingAdvisor advisor, CancellationToken ct) =>
        {
            try
            {
                var suggestion = await advisor.SuggestAsync(itemId, ct);
                return suggestion is null
                    ? Results.NotFound(new { error = "Anúncio não encontrado. Rode /ml/sync primeiro." })
                    : Results.Ok(suggestion);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (OpenAiException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // GET /agent/suggestions: sugestões já geradas, aguardando a sua decisão.
        group.MapGet("/suggestions", async (AgentOfficeDb db, CancellationToken ct) =>
            Results.Ok(await db.ListingSuggestions.OrderByDescending(s => s.Id).ToListAsync(ct)));

        // POST /agent/suggestions/{id}/approve ou /reject: registra a SUA decisão sobre a sugestão.
        // Altera só o banco local. A publicação no Mercado Livre não existe e continua bloqueada pela trava.
        group.MapPost("/suggestions/{id:int}/{decision}", async (
            int id, string decision, AgentOfficeDb db, CancellationToken ct) =>
        {
            var newStatus = decision switch
            {
                "approve" => "Approved",
                "reject" => "Rejected",
                _ => null,
            };
            if (newStatus is null) return Results.BadRequest(new { error = "Decisão inválida." });

            var suggestion = await db.ListingSuggestions.FindAsync([id], ct);
            if (suggestion is null) return Results.NotFound();

            // Uma sugestão já decidida não muda de novo.
            if (suggestion.Status != "Pending")
                return Results.Conflict(new { error = "Esta sugestão já foi decidida." });

            suggestion.Status = newStatus;
            await db.SaveChangesAsync(ct);
            return Results.Ok(suggestion);
        });
    }
}
