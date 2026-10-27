using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AgentOffice.Api.MercadoLivre;

// Resposta do endpoint /oauth/token. [JsonPropertyName] liga o nome do JSON
// (snake_case) ao nome da propriedade em C# (PascalCase).
public record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("user_id")] long UserId);

// Cuida de todo o fluxo OAuth2 ("Authorization Code"):
//   1. BuildAuthorizationUrl: monta o link da tela de login do Mercado Livre.
//   2. ExchangeCodeAsync: troca o "code" recebido no callback por tokens.
//   3. GetAccessTokenAsync: entrega um token válido, renovando-o sozinho quando vence.
public class MercadoLivreAuthService(
    IHttpClientFactory httpClientFactory,
    IOptions<MercadoLivreOptions> options,
    MercadoLivreCredentialsStore credentials,
    TokenStore store)
{
    private readonly MercadoLivreOptions _options = options.Value;

    // "state" anti-CSRF: um código aleatório enviado no login e conferido na volta,
    // para garantir que o callback veio de um login que NÓS iniciamos.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingStates = new();

    // Garante que só uma renovação de token aconteça por vez (o refresh_token é de uso único).
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Passo 1: URL para onde o usuário é enviado para autorizar o app.
    public string BuildAuthorizationUrl()
    {
        var creds = RequireCredentials();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _pendingStates[state] = DateTimeOffset.UtcNow.AddMinutes(10);

        return $"{_options.AuthBaseUrl}/authorization" +
               $"?response_type=code" +
               $"&client_id={Uri.EscapeDataString(creds.AppId)}" +
               $"&redirect_uri={Uri.EscapeDataString(creds.RedirectUri)}" +
               $"&state={state}";
    }

    // As credenciais podem mudar na tela de Configurações, então são lidas a cada uso.
    private ResolvedCredentials RequireCredentials()
    {
        var creds = credentials.Resolve();
        if (!creds.IsComplete)
            throw new InvalidOperationException("Informe o App ID, a chave secreta e a URL de redirecionamento em Configurações.");
        return creds;
    }

    // Passo 2: chamado pelo /auth/callback. Valida o state e troca o code por tokens.
    public async Task ExchangeCodeAsync(string code, string state, CancellationToken ct)
    {
        // TryRemove: o state só vale uma vez e dentro de 10 minutos.
        if (!_pendingStates.TryRemove(state, out var expiresAt) || expiresAt < DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("State inválido ou expirado. Inicie o login novamente.");
        }

        var creds = RequireCredentials();
        var tokens = await RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = creds.AppId,
            ["client_secret"] = creds.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = creds.RedirectUri,
        }, ct);

        store.Save(tokens);
    }

    // Passo 3: retorna um access token válido, ou null se ainda não houve login.
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var tokens = store.Load();
            if (tokens is null) return null;

            // Ainda válido (com 1 minuto de folga)? Usa o atual.
            if (tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return tokens.AccessToken;

            // Vencido: usa o refresh_token para pegar um novo par de tokens.
            var creds = RequireCredentials();
            tokens = await RequestTokensAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = creds.AppId,
                ["client_secret"] = creds.ClientSecret,
                ["refresh_token"] = tokens.RefreshToken,
            }, ct);

            store.Save(tokens);
            return tokens.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // POST em /oauth/token (única chamada de escrita permitida pela trava, pois não altera a conta).
    private async Task<MercadoLivreTokens> RequestTokensAsync(
        Dictionary<string, string> form, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("MercadoLivre");
        using var response = await http.PostAsync("/oauth/token", new FormUrlEncodedContent(form), ct);

        if (!response.IsSuccessStatusCode)
        {
            // Não incluímos o corpo da resposta/requisição no erro para não vazar credenciais em logs.
            throw new InvalidOperationException(
                $"Falha ao obter token do Mercado Livre (HTTP {(int)response.StatusCode}).");
        }

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                   ?? throw new InvalidOperationException("Resposta de token vazia.");

        return new MercadoLivreTokens(
            body.AccessToken,
            body.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn),
            body.UserId);
    }
}
