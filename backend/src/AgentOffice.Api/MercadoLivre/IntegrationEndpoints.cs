namespace AgentOffice.Api.MercadoLivre;

public record CredentialsRequest(string? AppId, string? ClientSecret, string? RedirectUri, string? PanelOrigin);

// Tela de Configurações: o próprio usuário informa as credenciais do app, conecta e desconecta a conta do Mercado Livre.
// Todas as rotas são só locais e NUNCA devolvem a chave secreta.
public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/settings").AddEndpointFilter(MercadoLivreEndpoints.LocalOnly);

        // GET /settings/integrations: situação de cada integração (credenciais, conexão e conta).
        group.MapGet("/integrations", async (
            MercadoLivreCredentialsStore credentials, TokenStore tokens, MercadoLivreClient ml,
            IConfiguration config, CancellationToken ct, string? origin = null) =>
        {
            var creds = credentials.Resolve();
            var connected = tokens.Load() is not null;

            MercadoLivreUser? account = null;
            string? accountError = null;
            if (connected)
            {
                try
                {
                    account = await ml.GetMeAsync(ct);
                }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
                {
                    accountError = "A conexão está salva, mas não foi possível consultar a conta agora. Reconecte se o problema persistir.";
                }
            }

            return Results.Ok(new
            {
                mercadoLivre = new
                {
                    configured = creds.IsComplete,
                    source = creds.Source,
                    appId = creds.AppId,
                    hasSecret = creds.ClientSecret.Length > 0,
                    redirectUri = creds.RedirectUri,
                    suggestedRedirectUri = RedirectUriSuggester.Suggest(config["App:PublicUrl"], origin),
                    connected,
                    account = account is null ? null : new { account.Id, account.Nickname, account.SiteId },
                    accountError,
                },
            });
        });

        // PUT /settings/mercadolivre/credentials: salva (criptografado) o App ID, a chave secreta e a URL de redirecionamento.
        // Chave secreta em branco mantém a atual. Trocar de app invalida o login anterior.
        // URL de redirecionamento em branco = gerada automaticamente (ver RedirectUriSuggester); preenchida = uso avançado.
        group.MapPut("/mercadolivre/credentials", (
            CredentialsRequest body, MercadoLivreCredentialsStore credentials, TokenStore tokens, IConfiguration config) =>
        {
            var current = credentials.Resolve();
            var appId = (body.AppId ?? "").Trim();
            var secret = string.IsNullOrWhiteSpace(body.ClientSecret) ? current.ClientSecret : body.ClientSecret.Trim();
            var redirect = (body.RedirectUri ?? "").Trim();

            if (redirect.Length == 0)
                redirect = RedirectUriSuggester.Suggest(config["App:PublicUrl"], body.PanelOrigin) ?? current.RedirectUri;
            if (redirect.Length == 0)
                return Results.BadRequest(new { error = "Não foi possível gerar a URL de redirecionamento: o painel está sendo aberto em um endereço local. Em \"Avançado\", informe o endereço público HTTPS (por exemplo, o de um túnel)." });

            var error = MercadoLivreCredentialsValidator.Validate(appId, secret, redirect);
            if (error is not null) return Results.BadRequest(new { error });

            credentials.Save(new MercadoLivreCredentials(appId, secret, redirect));

            if (appId != current.AppId || secret != current.ClientSecret) tokens.Delete();
            return Results.Ok(new { saved = true });
        });

        // DELETE /settings/mercadolivre/credentials: remove as credenciais salvas pela tela e desconecta a conta.
        group.MapDelete("/mercadolivre/credentials", (MercadoLivreCredentialsStore credentials, TokenStore tokens) =>
        {
            credentials.Delete();
            tokens.Delete();
            return Results.NoContent();
        });

        // POST /settings/mercadolivre/connect: devolve o endereço da tela de autorização do Mercado Livre.
        group.MapPost("/mercadolivre/connect", (MercadoLivreAuthService auth) =>
        {
            try
            {
                return Results.Ok(new { url = auth.BuildAuthorizationUrl() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // POST /settings/mercadolivre/disconnect: esquece o login (as credenciais do app continuam salvas).
        group.MapPost("/mercadolivre/disconnect", (TokenStore tokens) =>
        {
            tokens.Delete();
            return Results.NoContent();
        });
    }
}
