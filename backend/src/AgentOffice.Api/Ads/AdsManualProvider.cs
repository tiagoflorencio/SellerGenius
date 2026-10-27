using System.Security.Cryptography;
using System.Text;

namespace AgentOffice.Api.Ads;

public record AdsManual(string Text, string Hash, bool Found, string? Path);

// Localiza e lê o manual do agente (docs/SellerGenius_Manual_Agente_Mercado_Livre_Ads.md).
// O arquivo é lido a cada análise: se você editar o manual, a próxima análise já usa a versão nova.
// O hash identifica a versão usada em cada análise.
public class AdsManualProvider(IWebHostEnvironment env, IConfiguration config, ILogger<AdsManualProvider> logger)
{
    private const string FileName = "SellerGenius_Manual_Agente_Mercado_Livre_Ads.md";

    // Usado só se o arquivo não for encontrado, para o agente nunca rodar sem princípios.
    private const string Fallback = """
        Princípios mínimos: use apenas os dados fornecidos; nunca invente números; margem e metas do usuário têm
        precedência sobre benchmarks; só conclua com amostra suficiente; trate correlação como hipótese; proponha
        uma mudança por vez e nunca altere a conta sem autorização.
        """;

    public AdsManual Load()
    {
        var path = FindPath();
        if (path is null)
        {
            logger.LogWarning("Manual do agente de Ads não encontrado; usando princípios mínimos.");
            return new AdsManual(Fallback, Hash(Fallback), false, null);
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        return new AdsManual(text, Hash(text), true, path);
    }

    // Configuração Ads:ManualPath, ou procura a pasta docs/ subindo a partir da pasta do projeto.
    private string? FindPath()
    {
        var configured = config["Ads:ManualPath"];
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;

        var dir = new DirectoryInfo(env.ContentRootPath);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", FileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();
}
