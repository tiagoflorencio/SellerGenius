using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AgentOffice.Api.Analysis;
using AgentOffice.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ai;

// Formato que pedimos à IA para responder.
public record AdvisorOutput(
    [property: JsonPropertyName("diagnostico")] string Diagnostico,
    [property: JsonPropertyName("titulos")] List<string> Titulos,
    [property: JsonPropertyName("descricao")] string? Descricao,
    [property: JsonPropertyName("melhorias_descricao")] List<string> MelhoriasDescricao,
    [property: JsonPropertyName("outras_acoes")] List<string> OutrasAcoes);

// O "agente de anúncios": junta as métricas e alertas de um anúncio, pede à IA sugestões
// de melhoria e salva o resultado como PENDENTE de aprovação.
// IMPORTANTE: este serviço NUNCA publica nada no Mercado Livre. Ele só grava sugestões no banco local.
public class ListingAdvisor(AgentOfficeDb db, OpenAiClient ai)
{
    private const int DefaultMaxTitleLength = 60; // usado quando a categoria ainda não foi sincronizada
    private const int MaxDescriptionCharsSent = 4000; // limita o texto enviado à IA (custo)

    private const string SystemPrompt = """
        Você é um especialista em anúncios do Mercado Livre Brasil (SEO de marketplace e conversão).
        Receberá os dados de UM anúncio do vendedor: métricas dos últimos 30 dias, alertas calculados
        por regras, ficha técnica (atributos), descrição atual, limites da categoria e anúncios
        similares do mesmo vendedor.

        Regras:
        - Responda SOMENTE em JSON, em português do Brasil, neste formato:
          {"diagnostico": string, "titulos": [string, string, string], "descricao": string,
           "melhorias_descricao": [string], "outras_acoes": [string]}
        - "diagnostico": 2 a 4 frases explicando o provável motivo do desempenho, com base nos dados.
        - "titulos": 3 variações com NO MÁXIMO "limites.max_titulo" caracteres, começando pelo produto e com
          palavras-chave que compradores pesquisam. Mantenha marca, modelo, medidas e características do
          título original. Não cite frete grátis, parcelamento, estoque nem condição (novo/usado) no título.
        - "descricao": nova descrição em TEXTO PURO (sem HTML, sem markdown), com NO MÁXIMO
          "limites.max_descricao" caracteres (se for 0, no máximo 3000). Estruture em parágrafos curtos e
          listas com hífen: o que é, para quem serve, benefícios, o que acompanha e especificações.
          Use SOMENTE informações presentes na ficha técnica, no título e na descrição atual.
          Proibido: links, telefone, e-mail, WhatsApp, redes sociais, preços, promoções e prazos de frete.
        - Não invente características, certificações, garantias ou marcas que não estejam nos dados.
        - Se os dados forem insuficientes para uma conclusão, diga isso no diagnóstico.
        - "melhorias_descricao": o que foi melhorado na descrição em relação à atual.
        - "outras_acoes": ações práticas (fotos, preço, frete, ficha técnica), sem prometer resultados.
        """;

    public async Task<ListingSuggestion?> SuggestAsync(string itemId, CancellationToken ct)
    {
        var listings = await db.Listings.ToListAsync(ct);
        var listing = listings.FirstOrDefault(l => l.ItemId == itemId);
        if (listing is null) return null;

        // Alertas por regras (gratuito) calculados para toda a carteira.
        var diagnoses = ListingAnalyzer.Analyze(listings);
        var diagnosis = diagnoses.First(d => d.ItemId == itemId);

        // Dados completos do anúncio e regras da categoria (limites de título e descrição).
        var detail = await db.ListingDetails.FindAsync([itemId], ct);
        var category = listing.CategoryId is null ? null : await db.Categories.FindAsync([listing.CategoryId], ct);
        var maxTitle = category?.MaxTitleLength ?? DefaultMaxTitleLength;
        var maxDescription = category?.MaxDescriptionLength ?? 0;

        // Até 5 anúncios similares (mesma categoria) para a IA comparar, os de mais vendas primeiro.
        var similar = listings
            .Where(l => l.ItemId != itemId && l.Status == "active" && l.CategoryId == listing.CategoryId)
            .OrderByDescending(l => l.Sales30d ?? 0)
            .Take(5)
            .Select(Summarize)
            .ToList();

        var userMessage = JsonSerializer.Serialize(new
        {
            anuncio = Summarize(listing),
            alertas = diagnosis.Flags,
            conversao_30d = diagnosis.Conversion,
            ficha_tecnica = detail is null ? new List<object>() : AttributesOf(detail),
            descricao_atual = Truncate(detail?.Description, MaxDescriptionCharsSent),
            limites = new { max_titulo = maxTitle, max_descricao = maxDescription },
            similares_do_vendedor = similar,
        });

        var raw = await ai.CompleteJsonAsync(SystemPrompt, userMessage, ct);

        var output = JsonSerializer.Deserialize<AdvisorOutput>(raw)
                     ?? throw new OpenAiException("A IA devolveu um JSON inesperado.");

        // A IA pode errar os limites de caracteres, então corrigimos antes de salvar:
        // títulos acima do limite são descartados e a descrição é cortada no último parágrafo que cabe.
        output = output with
        {
            Titulos = output.Titulos.Where(t => t.Length <= maxTitle).ToList(),
            Descricao = maxDescription > 0 ? Truncate(output.Descricao, maxDescription) : output.Descricao,
        };

        var suggestion = new ListingSuggestion
        {
            ItemId = itemId,
            CreatedAt = DateTimeOffset.UtcNow,
            Model = ai.Model,
            Status = "Pending", // só muda quando VOCÊ aprovar ou rejeitar
            Flags = string.Join(",", diagnosis.Flags),
            ContentJson = JsonSerializer.Serialize(output),
        };

        db.ListingSuggestions.Add(suggestion);
        await db.SaveChangesAsync(ct);
        return suggestion;
    }

    // Ficha técnica do anúncio (marca, modelo, medidas...) para a IA se basear em fatos e não inventar.
    private static List<object> AttributesOf(ListingDetail detail)
    {
        var result = new List<object>();
        if (JsonNode.Parse(detail.RawItemJson)?["attributes"] is not JsonArray attributes) return result;

        foreach (var attribute in attributes.Take(60))
        {
            var value = (string?)attribute?["value_name"];
            if (value is null) continue;
            result.Add(new { atributo = (string?)attribute?["name"] ?? (string?)attribute?["id"], valor = value });
        }

        return result;
    }

    // Corta o texto no limite, preferindo terminar no fim de um parágrafo.
    private static string? Truncate(string? text, int max)
    {
        if (text is null || text.Length <= max) return text;

        var cut = text[..max];
        var lastBreak = cut.LastIndexOf('\n');
        return (lastBreak > max / 2 ? cut[..lastBreak] : cut).TrimEnd();
    }

    // Resumo enxuto do anúncio: só o necessário (menos tokens = menor custo).
    private static object Summarize(Listing l) => new
    {
        titulo = l.Title,
        preco = l.Price,
        visitas_30d = l.Visits30d,
        vendas_30d = l.Sales30d,
        fotos = l.PicturesCount,
        frete_gratis = l.FreeShipping,
        tipo_anuncio = l.ListingTypeId,
        categoria = l.CategoryId,
    };
}
