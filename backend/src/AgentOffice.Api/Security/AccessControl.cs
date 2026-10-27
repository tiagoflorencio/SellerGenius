using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace AgentOffice.Api.Security;

// Controla quem pode usar a API.
//  - Com "Auth:AccessPassword" definida (produção): é preciso entrar com a senha; a sessão fica num cookie assinado.
//  - Sem senha (desenvolvimento): só a própria máquina é aceita, como antes. De fora, a API se faz de inexistente (404).
public class AccessControl(IConfiguration config, IDataProtectionProvider dataProtection)
{
    public const string CookieName = "ao_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    // Limite de tentativas erradas (global: o sistema tem um único usuário) para frear tentativa e erro.
    private const int MaxFailures = 8;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("Access.Session").ToTimeLimitedDataProtector();
    private readonly object _lock = new();
    private readonly List<DateTimeOffset> _failures = [];

    private string Password => config["Auth:AccessPassword"] ?? "";

    public bool PasswordRequired => Password.Length > 0;

    public bool IsAllowed(HttpRequest request) =>
        PasswordRequired ? HasValidSession(request) : IsLocalRequest(request);

    // "Local" = Host de localhost e sem cabeçalhos de proxy (túnel/hospedagem colocam X-Forwarded-*).
    public static bool IsLocalRequest(HttpRequest request) =>
        request.Host.Host is "localhost" or "127.0.0.1" or "[::1]"
        && !request.Headers.ContainsKey("X-Forwarded-For")
        && !request.Headers.ContainsKey("X-Forwarded-Host");

    public bool HasValidSession(HttpRequest request)
    {
        if (!PasswordRequired || !request.Cookies.TryGetValue(CookieName, out var token)) return false;

        try
        {
            // O conteúdo é o hash da senha: trocar a senha encerra todas as sessões abertas.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(_protector.Unprotect(token)), Encoding.UTF8.GetBytes(PasswordFingerprint()));
        }
        catch (CryptographicException)
        {
            return false; // inválido, adulterado ou expirado
        }
    }

    // Confere a senha. Retorna o token da sessão, ou null se errada. "locked" indica bloqueio temporário por excesso de erros.
    public string? TryLogin(string? attempt, out bool locked)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            _failures.RemoveAll(f => now - f > FailureWindow);
            locked = _failures.Count >= MaxFailures;
            if (locked) return null;

            var ok = PasswordRequired && attempt is not null && FixedTimeEquals(attempt, Password);
            if (!ok)
            {
                _failures.Add(now);
                return null;
            }

            _failures.Clear();
        }

        return _protector.Protect(PasswordFingerprint(), SessionLifetime);
    }

    public static CookieOptions CookieOptionsFor(HttpRequest request) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = request.IsHttps || request.Headers["X-Forwarded-Proto"].ToString().StartsWith("https"),
        Expires = DateTimeOffset.UtcNow.Add(SessionLifetime),
        Path = "/",
    };

    private string PasswordFingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ao:" + Password)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

public record LoginRequest(string? Password);

// Rotas de sessão (públicas: é por elas que se entra). O painel consulta GET antes de mostrar qualquer dado.
public static class AccessEndpoints
{
    public static void MapAccessEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth/session");

        group.MapGet("", (AccessControl access, HttpRequest request) => Results.Ok(new
        {
            required = access.PasswordRequired,
            authenticated = access.IsAllowed(request),
        }));

        group.MapPost("", (LoginRequest body, AccessControl access, HttpRequest request, HttpResponse response) =>
        {
            if (!access.PasswordRequired)
                return Results.BadRequest(new { error = "Este ambiente não usa senha de acesso." });

            var token = access.TryLogin(body.Password, out var locked);
            if (locked)
                return Results.Json(new { error = "Muitas tentativas erradas. Aguarde alguns minutos e tente de novo." }, statusCode: StatusCodes.Status429TooManyRequests);
            if (token is null)
                return Results.Json(new { error = "Senha incorreta.", code = "bad_password" }, statusCode: StatusCodes.Status401Unauthorized);

            response.Cookies.Append(AccessControl.CookieName, token, AccessControl.CookieOptionsFor(request));
            return Results.Ok(new { authenticated = true });
        });

        group.MapDelete("", (HttpRequest request, HttpResponse response) =>
        {
            response.Cookies.Delete(AccessControl.CookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        });
    }
}
