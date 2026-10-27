using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentOffice.Api.MercadoLivre;

// ---- DTOs: formato do JSON devolvido pelo Mercado Livre (só os campos que usamos) ----

// Só os campos mínimos do usuário (evitamos trafegar dados pessoais desnecessários).
public record MercadoLivreUser(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("nickname")] string Nickname,
    [property: JsonPropertyName("site_id")] string SiteId);

// Resposta da busca de anúncios do vendedor: só ids + informação de paginação.
public record MlSearchResult(
    [property: JsonPropertyName("results")] List<string> Results,
    [property: JsonPropertyName("paging")] MlPaging Paging);

public record MlPaging([property: JsonPropertyName("total")] int Total);

// Detalhes de um anúncio.
public record MlItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("currency_id")] string? CurrencyId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("available_quantity")] int AvailableQuantity,
    [property: JsonPropertyName("sold_quantity")] int SoldQuantity,
    [property: JsonPropertyName("permalink")] string? Permalink,
    [property: JsonPropertyName("category_id")] string? CategoryId,
    [property: JsonPropertyName("listing_type_id")] string? ListingTypeId,
    [property: JsonPropertyName("health")] double? Health,
    [property: JsonPropertyName("pictures")] List<MlPicture>? Pictures,
    [property: JsonPropertyName("shipping")] MlShipping? Shipping);

public record MlPicture([property: JsonPropertyName("id")] string? Id);
public record MlShipping([property: JsonPropertyName("free_shipping")] bool FreeShipping);

// A consulta de vários anúncios devolve uma lista de { code, body } (um status HTTP por anúncio).
// O body fica como JsonElement (JSON bruto) para guardarmos o anúncio completo, sem perder campos.
public record MlMultiGetEntry(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("body")] JsonElement Body);

// Resposta de GET /items/{id}/description.
public record MlDescription([property: JsonPropertyName("plain_text")] string? PlainText);

public record MlVisits([property: JsonPropertyName("total_visits")] int TotalVisits);

// Pedidos: usamos só o status e, por item, a quantidade vendida.
public record MlOrdersResult(
    [property: JsonPropertyName("results")] List<MlOrder> Results,
    [property: JsonPropertyName("paging")] MlPaging Paging);

public record MlOrder(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("order_items")] List<MlOrderItem>? OrderItems);

public record MlOrderItem(
    [property: JsonPropertyName("item")] MlOrderItemRef? Item,
    [property: JsonPropertyName("quantity")] int Quantity);

public record MlOrderItemRef([property: JsonPropertyName("id")] string? Id);

// Cliente da API do Mercado Livre. Só expõe métodos de LEITURA (GET).
// O HttpClient "MercadoLivre" passa pela ReadOnlyGuardHandler, que bloqueia qualquer escrita.
public class MercadoLivreClient(IHttpClientFactory httpClientFactory, MercadoLivreAuthService auth)
{
    // GET /users/me: dados da conta logada. Serve para confirmar que o login funcionou.
    public Task<MercadoLivreUser> GetMeAsync(CancellationToken ct) =>
        GetAsync<MercadoLivreUser>("/users/me", ct);

    // Lista os ids de TODOS os anúncios do vendedor, página a página (50 por vez).
    // Obs.: esta busca vai até 1000 anúncios; acima disso seria preciso o modo "scan" da API.
    public async Task<List<string>> GetItemIdsAsync(long userId, CancellationToken ct)
    {
        var ids = new List<string>();
        var offset = 0;

        while (true)
        {
            var page = await GetAsync<MlSearchResult>(
                $"/users/{userId}/items/search?limit=50&offset={offset}", ct);

            ids.AddRange(page.Results);
            offset += page.Results.Count;

            // Para quando a página vier vazia ou já tivermos todos.
            if (page.Results.Count == 0 || offset >= page.Paging.Total) break;
        }

        return ids;
    }

    // Busca os anúncios COMPLETOS (JSON bruto). A API aceita até 20 ids por chamada, então dividimos em lotes.
    public async Task<List<JsonElement>> GetItemsAsync(IReadOnlyList<string> itemIds, CancellationToken ct)
    {
        var items = new List<JsonElement>();

        foreach (var batch in itemIds.Chunk(20))
        {
            var entries = await GetAsync<List<MlMultiGetEntry>>($"/items?ids={string.Join(',', batch)}", ct);

            // Só aproveitamos os anúncios cuja consulta individual deu certo (code 200).
            items.AddRange(entries.Where(e => e.Code == 200 && e.Body.ValueKind == JsonValueKind.Object).Select(e => e.Body));
        }

        return items;
    }

    // Descrição do anúncio em texto puro. Retorna null se o anúncio não tem descrição (a API responde 404).
    public async Task<string?> GetItemDescriptionAsync(string itemId, CancellationToken ct)
    {
        try
        {
            var description = await GetAsync<MlDescription>($"/items/{itemId}/description", ct);
            return description.PlainText;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // Dados da categoria (nome, caminho e regras como limite de título/descrição/fotos).
    public Task<JsonElement> GetCategoryAsync(string categoryId, CancellationToken ct) =>
        GetAsync<JsonElement>($"/categories/{categoryId}", ct);

    // Atributos da categoria; os marcados como obrigatórios têm a tag "required".
    public Task<JsonElement> GetCategoryAttributesAsync(string categoryId, CancellationToken ct) =>
        GetAsync<JsonElement>($"/categories/{categoryId}/attributes", ct);

    // Termos de venda aceitos pela categoria (garantia etc.).
    public Task<JsonElement> GetCategorySaleTermsAsync(string categoryId, CancellationToken ct) =>
        GetAsync<JsonElement>($"/categories/{categoryId}/sale_terms", ct);

    // Total de visitas de um anúncio nos últimos 30 dias.
    public async Task<int> GetVisitsLast30DaysAsync(string itemId, CancellationToken ct)
    {
        var visits = await GetAsync<MlVisits>($"/items/{itemId}/visits/time_window?last=30&unit=day", ct);
        return visits.TotalVisits;
    }

    // Unidades vendidas por anuncio nos ultimos 30 dias, somando os pedidos (exceto cancelados).
    // Retorna um dicionario { id do anuncio -> quantidade }. Anuncios sem venda nao aparecem.
    public async Task<Dictionary<string, int>> GetSalesLast30DaysAsync(long userId, CancellationToken ct)
    {
        var sales = new Dictionary<string, int>();
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
        var offset = 0;

        while (true)
        {
            var page = await GetAsync<MlOrdersResult>(
                $"/orders/search?seller={userId}&order.date_created.from={from}&limit=50&offset={offset}", ct);

            foreach (var order in page.Results.Where(o => o.Status != "cancelled"))
            {
                foreach (var line in order.OrderItems ?? [])
                {
                    if (line.Item?.Id is null) continue;
                    sales[line.Item.Id] = sales.GetValueOrDefault(line.Item.Id) + line.Quantity;
                }
            }

            offset += page.Results.Count;
            if (page.Results.Count == 0 || offset >= page.Paging.Total) break;
        }

        return sales;
    }

    // Leitura genérica devolvendo JSON bruto. As APIs de publicidade exigem o cabeçalho api-version.
    public Task<JsonElement> GetJsonAsync(string path, string? apiVersion, CancellationToken ct) =>
        GetAsync<JsonElement>(path, ct, apiVersion);

    // Método genérico de leitura: pega o token, envia o GET e converte o JSON em T.
    private async Task<T> GetAsync<T>(string path, CancellationToken ct, string? apiVersion = null)
    {
        var token = await auth.GetAccessTokenAsync(ct)
                    ?? throw new InvalidOperationException("Não autenticado. Acesse /auth/login primeiro.");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", token);
        if (apiVersion is not null) request.Headers.Add("api-version", apiVersion);

        var http = httpClientFactory.CreateClient("MercadoLivre");
        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // Inclui o motivo informado pelo Mercado Livre (ex.: "No permissions found for user_id") para facilitar o diagnóstico.
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = body.Length > 300 ? body[..300] : body;
            throw new HttpRequestException(
                $"Mercado Livre respondeu {(int)response.StatusCode} em {path.Split('?')[0]}: {detail}",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(ct)
               ?? throw new InvalidOperationException("Resposta vazia do Mercado Livre.");
    }
}
