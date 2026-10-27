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

    public string BaseUrl { get; set; } = "https://api.openai.com";
}

// Erro devolvido pela OpenAI (chave inválida, sem saldo, modelo inexistente...).
public class OpenAiException(string message) : Exception(message);

// Cliente mínimo da OpenAI (Chat Completions). Usa HttpClient direto, sem SDK, para ficar fácil de entender.
// É um HttpClient SEPARADO do Mercado Livre: os tokens do Mercado Livre nunca são enviados à OpenAI.
public class OpenAiClient(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> options)
{
    private readonly OpenAiOptions _options = options.Value;

    public string Model => _options.Model;

    // Envia uma instrução (system) e os dados (user) e devolve o texto da resposta, que será um JSON.
    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Chave da OpenAI não configurada (OpenAI:ApiKey).");

        var payload = new
        {
            model = _options.Model,
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
        return body?.Choices?.FirstOrDefault()?.Message?.Content
               ?? throw new OpenAiException("Resposta da OpenAI vazia.");
    }

    // DTOs privados do formato da OpenAI.
    private record ChatCompletionResponse([property: JsonPropertyName("choices")] List<Choice>? Choices);
    private record Choice([property: JsonPropertyName("message")] Message? Message);
    private record Message([property: JsonPropertyName("content")] string? Content);
    private record OpenAiErrorEnvelope([property: JsonPropertyName("error")] OpenAiError? Error);
    private record OpenAiError([property: JsonPropertyName("message")] string? Message);
}
