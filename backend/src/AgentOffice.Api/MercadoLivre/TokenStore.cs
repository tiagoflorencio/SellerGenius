using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AgentOffice.Api.MercadoLivre;

// "record": tipo imutável feito para carregar dados.
// Guarda o resultado do login: os dois tokens e quando o de acesso expira.
public record MercadoLivreTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    long UserId);

// Guarda os tokens em disco (App_Data/ml-tokens.bin) para você não precisar
// logar de novo a cada vez que reiniciar a API.
// O conteúdo é CRIPTOGRAFADO com o Data Protection do ASP.NET: o arquivo sozinho não revela os tokens.
// (O construtor logo após o nome da classe é um "primary constructor" do C# 12.)
public class TokenStore(IDataProtectionProvider dataProtection, IWebHostEnvironment env)
{
    // "Protector" é o objeto que criptografa/descriptografa; o texto é o "propósito" (isola as chaves).
    private readonly IDataProtector _protector = dataProtection.CreateProtector("MercadoLivre.Tokens");

    private readonly string _path = Path.Combine(env.ContentRootPath, "App_Data", "ml-tokens.bin");

    // Lê e descriptografa os tokens. Retorna null se ainda não houve login ou se o arquivo não pôde ser lido
    // (ex.: foi criptografado por outra instalação); nos dois casos basta fazer o login de novo.
    public MercadoLivreTokens? Load()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            var json = _protector.Unprotect(File.ReadAllText(_path));
            return JsonSerializer.Deserialize<MercadoLivreTokens>(json);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // Criptografa e grava os tokens (criando a pasta App_Data se necessário).
    public void Save(MercadoLivreTokens tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, _protector.Protect(JsonSerializer.Serialize(tokens)));
    }

    // Esquece o login (desconectar).
    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
