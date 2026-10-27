using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AgentOffice.Api.Ai;

// Seção "OpenAI" das configurações. A ApiKey fica só em user-secrets.
public class OpenAiOptions
{
    public string ApiKey { get; set; } = "";

    // Modelo pequeno e barato é suficiente para reescrever anúncios. Dá para trocar sem mexer no código.
    public string Model { get; set; } = "gpt-4.1-mini";

    // Modelo da análise de Product Ads. Vazio = usa o mesmo de Model.
    public string AdsModel { get; set; } = "";

    public string BaseUrl { get; set; } = "https://api.openai.com";
}

// Erro devolvido pela OpenAI (chave inválida, sem saldo, modelo inexistente...).
public class OpenAiException(string message) : Exception(message);

// Resposta da OpenAI: o texto (JSON), o modelo usado e os tokens consumidos (null se a API não informou).
public record OpenAiResult(string Content, string Model, int? PromptTokens, int? CompletionTokens);

// Cliente mínimo da OpenAI (Chat Completions). Usa HttpClient direto, sem SDK, para ficar fácil de entender.
// É um HttpClient SEPARADO do Mercado Livre: os tokens do Mercado Livre nunca são enviados à OpenAI.
public class OpenAiClient(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options)
{
    private readonly OpenAiOptions _options = options.Value;

    public string Model => _options.Model;

    // Modelo da análise de Ads (mais capaz que o dos anúncios, se configurado em OpenAI:AdsModel).
    public string AdsModel => string.IsNullOrWhiteSpace(_options.AdsModel) ? _options.Model : _options.AdsModel;

    // Envia uma instrução (system) e os dados (user) e devolve o texto da resposta, que será um JSON.
    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct) =>
        (await CompleteJsonDetailedAsync(system, user, null, ct)).Content;

    // Igual ao anterior, mas permite escolher o modelo e devolve o consumo de tokens (para acompanhar custo).
    public async Task<OpenAiResult> CompleteJsonDetailedAsync(string system, string user, string? model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Chave da OpenAI não configurada (OpenAI:ApiKey).");

        var usedModel = model ?? _options.Model;
        var payload = new
        {
            model = usedModel,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            // Obriga a resposta a ser um JSON válido, facilitando o processamento.
            response_format = new { type = "json_object" },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new("Bearer", _options.ApiKey);

        var http = httpClientFactory.CreateClient("OpenAI");
        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<OpenAiErrorEnvelope>(ct);
            throw new OpenAiException(
                $"OpenAI respondeu HTTP {(int)response.StatusCode}: {error?.Error?.Message ?? "sem detalhes"}");
        }

        var body = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(ct);
        var content = body?.Choices?.FirstOrDefault()?.Message?.Content
                      ?? throw new OpenAiException("Resposta da OpenAI vazia.");

        return new OpenAiResult(content, usedModel, body?.Usage?.PromptTokens, body?.Usage?.CompletionTokens);
    }

    // DTOs privados do formato da OpenAI.
    private record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] Usage? Usage);
    private record Usage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
    private record Choice([property: JsonPropertyName("message")] Message? Message);
    private record Message([property: JsonPropertyName("content")] string? Content);
    private record OpenAiErrorEnvelope([property: JsonPropertyName("error")] OpenAiError? Error);
    private record OpenAiError([property: JsonPropertyName("message")] string? Message);
}
