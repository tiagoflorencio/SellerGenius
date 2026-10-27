using AgentOffice.Api.Analysis;
using AgentOffice.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.MercadoLivre;

// Rotas relacionadas ao Mercado Livre.
// "static class" com método de extensão (this WebApplication app): permite chamar
// app.MapMercadoLivreEndpoints() no Program.cs, mantendo as rotas organizadas aqui.
public static class MercadoLivreEndpoints
{
    public static void MapMercadoLivreEndpoints(this WebApplication app)
    {
        // GET /auth/login: redireciona para a tela de login do Mercado Livre.
        // Só local: você abre https://localhost:7215/auth/login no seu navegador.
        app.MapGet("/auth/login", (MercadoLivreAuthService auth) =>
            {
                try
                {
                    return Results.Redirect(auth.BuildAuthorizationUrl());
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .AddEndpointFilter(LocalOnly);

        // GET /auth/callback: o Mercado Livre devolve o usuário aqui (via túnel ou domínio público) com ?code=...&state=...
        // Esta rota precisa ficar acessível pela internet; a segurança vem do "state" validado.
        // No fim, volta para o painel (Frontend:Url, definido só por configuração: nunca por parâmetro da requisição).
        app.MapGet("/auth/callback", async (
            string? code, string? state, string? error,
            MercadoLivreAuthService auth, IConfiguration config, CancellationToken ct) =>
        {
            var panel = (config["Frontend:Url"] ?? "http://localhost:5173").TrimEnd('/');
            IResult Back(string result, string? message = null) =>
                Results.Redirect($"{panel}/?ml={result}" + (message is null ? "" : $"&message={Uri.EscapeDataString(message)}"));

            if (error is not null || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Back("error", "Login não autorizado ou parâmetros ausentes.");

            try
            {
                await auth.ExchangeCodeAsync(code, state, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Back("error", ex.Message);
            }
            catch (HttpRequestException)
            {
                return Back("error", "Não foi possível falar com o Mercado Livre. Tente novamente.");
            }

            return Back("connected");
        });

        // GET /ml/me: teste rápido. Mostra a conta conectada (leitura). Só local.
        app.MapGet("/ml/me", async (MercadoLivreClient ml, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await ml.GetMeAsync(ct));
                }
                catch (InvalidOperationException ex)
                {
                    // Sem login feito ainda: o painel usa este 401 para mostrar o botão "Conectar".
                    return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
                }
            })
            .AddEndpointFilter(LocalOnly);

        // POST /ml/sync: lê os anúncios no Mercado Livre (somente leitura) e salva no banco local.
        // POST porque altera o NOSSO banco; no Mercado Livre não é alterado nada. Só local.
        app.MapPost("/ml/sync", async (ListingSyncService sync, CancellationToken ct) =>
            {
                try
                {
                    var count = await sync.SyncAsync(ct);
                    return Results.Ok(new { synced = count });
                }
                catch (InvalidOperationException ex)
                {
                    // Ainda não autenticado (ou resposta inválida).
                    return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
                }
                catch (HttpRequestException ex)
                {
                    // O Mercado Livre respondeu com erro.
                    return Results.Json(new { error = $"Erro ao consultar o Mercado Livre ({ex.StatusCode})." },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .AddEndpointFilter(LocalOnly);

        // GET /listings: lista os anúncios já salvos no banco (não chama o Mercado Livre). Só local.
        app.MapGet("/listings", async (AgentOfficeDb db, CancellationToken ct) =>
                Results.Ok(await db.Listings.OrderBy(l => l.Title).ToListAsync(ct)))
            .AddEndpointFilter(LocalOnly);

        // GET /listings/readiness: para cada anúncio, se todos os campos obrigatórios para CRIAR uma variação
        // dele estão disponíveis e, se não, quais faltam. Só lê o banco local.
        app.MapGet("/listings/readiness", async (AgentOfficeDb db, CancellationToken ct) =>
            {
                var listings = await db.Listings.ToListAsync(ct);
                var details = await db.ListingDetails.ToDictionaryAsync(d => d.ItemId, ct);
                var categories = await db.Categories.ToDictionaryAsync(c => c.CategoryId, ct);

                var result = listings
                    .Where(l => details.ContainsKey(l.ItemId))
                    .Select(l =>
                    {
                        var report = ListingDraftBuilder.Build(
                            details[l.ItemId],
                            l.CategoryId is not null && categories.TryGetValue(l.CategoryId, out var c) ? c : null);
                        return new { l.ItemId, l.Title, report.Ready, report.Missing, report.Warnings };
                    })
                    .OrderBy(r => r.Ready)
                    .ToList();

                return Results.Ok(result);
            })
            .AddEndpointFilter(LocalOnly);

        // GET /listings/{itemId}/draft: o corpo no formato do POST /items montado a partir do anúncio salvo,
        // mais as pendências. NÃO envia nada ao Mercado Livre.
        app.MapGet("/listings/{itemId}/draft", async (string itemId, AgentOfficeDb db, CancellationToken ct) =>
            {
                var detail = await db.ListingDetails.FindAsync([itemId], ct);
                if (detail is null) return Results.NotFound(new { error = "Anúncio não encontrado. Rode /ml/sync primeiro." });

                var categoryId = await db.Listings.Where(l => l.ItemId == itemId).Select(l => l.CategoryId).FirstOrDefaultAsync(ct);
                var category = categoryId is null ? null : await db.Categories.FindAsync([categoryId], ct);

                return Results.Ok(ListingDraftBuilder.Build(detail, category));
            })
            .AddEndpointFilter(LocalOnly);
    }

    // Filtro de endpoint: só deixa passar requisições feitas na própria máquina.
    // Como o túnel expõe a API na internet, rotas sensíveis respondem 404 para quem vem de fora.
    // Detecta "vindo de fora" por: Host diferente de localhost OU cabeçalhos X-Forwarded-* (colocados por proxies).
    internal static async ValueTask<object?> LocalOnly(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var hostIsLocal = request.Host.Host is "localhost" or "127.0.0.1" or "[::1]";
        var viaProxy = request.Headers.ContainsKey("X-Forwarded-For")
                       || request.Headers.ContainsKey("X-Forwarded-Host");

        if (!hostIsLocal || viaProxy) return Results.NotFound();

        return await next(context);
    }
}
