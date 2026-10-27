using AgentOffice.Api.Data;

namespace AgentOffice.Api.Analysis;

// Resultado da análise de um anúncio: as métricas calculadas e os alertas (flags) encontrados.
public record ListingDiagnosis(
    string ItemId,
    string Title,
    string Status,
    int? Visits30d,
    int? Sales30d,
    double? Conversion,
    List<string> Flags);

// Análise por REGRAS, sem IA: rápida, gratuita e previsível.
// Ela aponta ONDE há problema; depois a IA sugere COMO melhorar (ver pasta Ai/).
// Os limites abaixo são pontos de partida e podem ser ajustados com o tempo.
public static class ListingAnalyzer
{
    // Nomes dos alertas (usados também no prompt enviado à IA).
    public const string Paused = "PAUSADO";
    public const string NoSalesWithTraffic = "VISITAS_SEM_VENDAS";
    public const string LowConversion = "CONVERSAO_BAIXA";
    public const string LowTraffic = "POUCAS_VISITAS";
    public const string FewPictures = "POUCAS_FOTOS";
    public const string ShortTitle = "TITULO_CURTO";
    public const string PriceAboveSimilar = "PRECO_ACIMA_DOS_SIMILARES";
    public const string NoFreeShipping = "SEM_FRETE_GRATIS";

    private const int MinVisitsToJudge = 30;        // com menos visitas que isso, a conversão não é confiável
    private const int MinPictures = 5;
    private const int MinTitleLength = 40;          // o ML aceita até 60 caracteres
    private const double PriceTolerance = 1.15;     // 15% acima da mediana dos similares
    private const double LowConversionFactor = 0.5; // metade da mediana da sua carteira
    private const double LowTrafficFactor = 0.25;   // um quarto da mediana de visitas da sua carteira
    private const int MinPeers = 2;                 // mínimo de anúncios similares para comparar

    public static List<ListingDiagnosis> Analyze(IReadOnlyList<Listing> listings)
    {
        var active = listings.Where(l => l.Status == "active").ToList();

        // Medianas da sua carteira: servem de "régua" para dizer o que é bom ou ruim PARA VOCÊ.
        var medianConversion = Median(active
            .Where(l => l.Visits30d >= MinVisitsToJudge && l.Sales30d is not null)
            .Select(l => ConversionOf(l)!.Value));
        var medianVisits = Median(active.Where(l => l.Visits30d is not null).Select(l => (double)l.Visits30d!.Value));

        return listings.Select(l => Diagnose(l, active, medianConversion, medianVisits)).ToList();
    }

    public static double? ConversionOf(Listing l) =>
        l.Visits30d is > 0 && l.Sales30d is not null ? (double)l.Sales30d.Value / l.Visits30d.Value : null;

    private static ListingDiagnosis Diagnose(
        Listing l, List<Listing> active, double? medianConversion, double? medianVisits)
    {
        var flags = new List<string>();
        var conversion = ConversionOf(l);

        // Anúncios pausados não são analisados: não geram tráfego.
        if (l.Status != "active")
        {
            flags.Add(Paused);
            return new ListingDiagnosis(l.ItemId, l.Title, l.Status, l.Visits30d, l.Sales30d, conversion, flags);
        }

        // Tráfego e conversão
        if (l.Visits30d >= MinVisitsToJudge && l.Sales30d == 0)
            flags.Add(NoSalesWithTraffic);
        else if (conversion is not null && medianConversion is > 0 && conversion < medianConversion * LowConversionFactor
                 && l.Visits30d >= MinVisitsToJudge)
            flags.Add(LowConversion);

        if (l.Visits30d is not null && medianVisits is not null && l.Visits30d < medianVisits * LowTrafficFactor)
            flags.Add(LowTraffic);

        // Qualidade do anúncio
        if (l.PicturesCount < MinPictures) flags.Add(FewPictures);
        if (l.Title.Length < MinTitleLength) flags.Add(ShortTitle);

        // Comparação com outros anúncios SEUS da mesma categoria (excluindo ele mesmo).
        var peers = active.Where(p => p.ItemId != l.ItemId && p.CategoryId is not null && p.CategoryId == l.CategoryId).ToList();
        if (peers.Count >= MinPeers)
        {
            var medianPrice = Median(peers.Select(p => (double)p.Price));
            if (medianPrice is not null && (double)l.Price > medianPrice * PriceTolerance)
                flags.Add(PriceAboveSimilar);

            if (!l.FreeShipping && peers.Count(p => p.FreeShipping) * 2 >= peers.Count)
                flags.Add(NoFreeShipping);
        }

        return new ListingDiagnosis(l.ItemId, l.Title, l.Status, l.Visits30d, l.Sales30d, conversion, flags);
    }

    // Mediana: valor do meio da lista ordenada (menos sensível a valores extremos que a média).
    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return null;

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
