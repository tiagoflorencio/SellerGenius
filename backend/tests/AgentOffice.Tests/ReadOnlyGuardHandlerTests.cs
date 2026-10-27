using AgentOffice.Api.MercadoLivre;

namespace AgentOffice.Tests;

// Testes da trava de somente leitura: garantem que nenhuma escrita chega ao Mercado Livre.
public class ReadOnlyGuardHandlerTests
{
    // Handler falso que "responde 200" e conta quantas requisições realmente "saíram".
    private class FakeNetworkHandler : HttpMessageHandler
    {
        public int SentCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SentCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static (HttpMessageInvoker Invoker, FakeNetworkHandler Network) CreateInvoker()
    {
        var network = new FakeNetworkHandler();
        var guard = new ReadOnlyGuardHandler { InnerHandler = network };
        return (new HttpMessageInvoker(guard), network);
    }

    [Theory]
    [InlineData("GET", "https://api.mercadolibre.com/users/me")]
    [InlineData("GET", "https://api.mercadolibre.com/items/MLB123")]
    [InlineData("HEAD", "https://api.mercadolibre.com/items/MLB123")]
    [InlineData("POST", "https://api.mercadolibre.com/oauth/token")]
    public async Task Allows_read_requests_and_oauth_token(string method, string url)
    {
        var (invoker, network) = CreateInvoker();

        using var response = await invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), default);

        Assert.Equal(1, network.SentCount);
    }

    [Theory]
    [InlineData("POST", "https://api.mercadolibre.com/items")]
    [InlineData("PUT", "https://api.mercadolibre.com/items/MLB123")]
    [InlineData("PATCH", "https://api.mercadolibre.com/items/MLB123")]
    [InlineData("DELETE", "https://api.mercadolibre.com/items/MLB123")]
    [InlineData("POST", "https://api.mercadolibre.com/items/MLB123/description")]
    [InlineData("POST", "https://api.mercadolibre.com/oauth/token/extra")]
    public async Task Blocks_any_write_before_it_leaves_the_server(string method, string url)
    {
        var (invoker, network) = CreateInvoker();

        await Assert.ThrowsAsync<WriteBlockedException>(() =>
            invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), default));

        Assert.Equal(0, network.SentCount);
    }
}
