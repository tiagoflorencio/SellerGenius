using System.Security.Cryptography;
using System.Text.Json;
using AgentOffice.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AgentOffice.Api.MercadoLivre;

// Credenciais do app do Mercado Livre informadas na tela de Configurações.
public record MercadoLivreCredentials(string AppId, string ClientSecret, string RedirectUri);

// Credenciais em uso. Source: "ui" (tela de Configurações), "config" (user-secrets/appsettings) ou "none".
public record ResolvedCredentials(string AppId, string ClientSecret, string RedirectUri, string Source)
{
    public bool IsComplete => AppId.Length > 0 && ClientSecret.Length > 0 && RedirectUri.Length > 0;
}

// Guarda as credenciais do app em App_Data/ml-credentials.bin, criptografadas com o Data Protection (como os tokens).
// A tela tem prioridade; user-secrets/appsettings continuam funcionando como alternativa.
public class MercadoLivreCredentialsStore(
    IDataProtectionProvider dataProtection,
    IWebHostEnvironment env,
    IConfiguration config,
    IOptions<MercadoLivreOptions> options)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("MercadoLivre.Credentials");
    private readonly string _path = Path.Combine(AppPaths.DataDir(config, env), "ml-credentials.bin");

    public MercadoLivreCredentials? Load()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            return JsonSerializer.Deserialize<MercadoLivreCredentials>(_protector.Unprotect(File.ReadAllText(_path)));
        }
        catch (CryptographicException)
        {
            return null; // criptografado por outra instalação: informe de novo na tela
        }
    }

    public void Save(MercadoLivreCredentials credentials)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, _protector.Protect(JsonSerializer.Serialize(credentials)));
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    public ResolvedCredentials Resolve()
    {
        var saved = Load();
        if (saved is not null) return new ResolvedCredentials(saved.AppId, saved.ClientSecret, saved.RedirectUri, "ui");

        var o = options.Value;
        var fromConfig = new ResolvedCredentials(o.AppId.Trim(), o.ClientSecret.Trim(), o.RedirectUri.Trim(), "config");
        return fromConfig.AppId.Length + fromConfig.ClientSecret.Length + fromConfig.RedirectUri.Length > 0
            ? fromConfig
            : new ResolvedCredentials("", "", "", "none");
    }
}

// Monta a URL de redirecionamento sem o usuário precisar entender o processo. Retorna null quando não há endereço público HTTPS.
public static class RedirectUriSuggester
{
    public const string CallbackPath = "/auth/callback";

    // publicUrl: endereço público da API definido por configuração (App:PublicUrl), tem prioridade.
    // panelOrigin: endereço em que o usuário abriu o painel; o painel encaminha /api para a API.
    public static string? Suggest(string? publicUrl, string? panelOrigin)
    {
        var fromConfig = PublicBase(publicUrl);
        if (fromConfig is not null) return fromConfig + CallbackPath;

        var fromPanel = PublicBase(panelOrigin);
        return fromPanel is null ? null : fromPanel + "/api" + CallbackPath;
    }

    // Aceita só HTTPS com nome de domínio público (nada de localhost, IP ou nomes internos).
    private static string? PublicBase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;

        var host = uri.IdnHost.ToLowerInvariant();
        var internalName = host.EndsWith(".local") || host.EndsWith(".localhost") || host.EndsWith(".internal") || host.EndsWith(".lan");
        if (!host.Contains('.') || internalName || System.Net.IPAddress.TryParse(host, out _)) return null;

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}

public static class MercadoLivreCredentialsValidator
{
    // Retorna a mensagem do primeiro problema encontrado, ou null se os valores são aceitáveis.
    public static string? Validate(string appId, string clientSecret, string redirectUri)
    {
        if (appId.Length is < 5 or > 20 || !appId.All(char.IsAsciiDigit))
            return "O App ID deve ter apenas números (é o \"ID do aplicativo\" no painel de desenvolvedores do Mercado Livre).";

        if (clientSecret.Length is < 10 or > 200 || clientSecret.Any(char.IsWhiteSpace))
            return "A chave secreta parece inválida (copie o \"Secret Key\" do aplicativo, sem espaços).";

        // O Mercado Livre só aceita redirecionamento HTTPS.
        if (redirectUri.Length > 300 || !Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Fragment.Length > 0 || redirectUri.Any(char.IsWhiteSpace))
            return "A URL de redirecionamento deve ser um endereço HTTPS completo (ex.: https://seu-dominio.com/auth/callback).";

        return null;
    }
}
