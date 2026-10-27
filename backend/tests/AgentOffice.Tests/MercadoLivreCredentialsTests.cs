using AgentOffice.Api.MercadoLivre;

namespace AgentOffice.Tests;

public class MercadoLivreCredentialsTests
{
    private const string Id = "1234567890123456";
    private const string Secret = "AbCdEfGhIjKlMnOpQrStUvWxYz012345";
    private const string Redirect = "https://meu-dominio.com/auth/callback";

    [Fact]
    public void Aceita_credenciais_validas() =>
        Assert.Null(MercadoLivreCredentialsValidator.Validate(Id, Secret, Redirect));

    [Theory]
    [InlineData("")]
    [InlineData("abc123")]        // não numérico
    [InlineData("1234")]          // curto demais
    public void Rejeita_app_id_invalido(string appId) =>
        Assert.NotNull(MercadoLivreCredentialsValidator.Validate(appId, Secret, Redirect));

    [Theory]
    [InlineData("")]
    [InlineData("curta")]
    [InlineData("tem espaco no meio 123456")]
    public void Rejeita_chave_secreta_invalida(string secret) =>
        Assert.NotNull(MercadoLivreCredentialsValidator.Validate(Id, secret, Redirect));

    [Theory]
    [InlineData("")]
    [InlineData("meu-dominio.com/auth/callback")]            // sem esquema
    [InlineData("http://meu-dominio.com/auth/callback")]     // o Mercado Livre exige HTTPS
    [InlineData("https://meu-dominio.com/auth/callback#x")]  // fragmento
    [InlineData("javascript:alert(1)")]
    public void Rejeita_redirecionamento_invalido(string redirect) =>
        Assert.NotNull(MercadoLivreCredentialsValidator.Validate(Id, Secret, redirect));

    [Fact]
    public void Sugere_a_url_a_partir_do_endereco_publico_do_painel() =>
        Assert.Equal("https://app.exemplo.com.br/api/auth/callback", RedirectUriSuggester.Suggest(null, "https://app.exemplo.com.br"));

    [Fact]
    public void Endereco_publico_configurado_tem_prioridade_e_ignora_caminho_extra() =>
        Assert.Equal("https://api.exemplo.com/auth/callback",
            RedirectUriSuggester.Suggest("https://api.exemplo.com/qualquer/coisa", "https://app.exemplo.com.br"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:7215")]
    [InlineData("https://192.168.0.10")]
    [InlineData("https://meu-pc")]
    [InlineData("https://servidor.local")]
    [InlineData("http://app.exemplo.com")]   // sem HTTPS
    [InlineData("nao-e-url")]
    public void Nao_sugere_quando_nao_ha_endereco_publico_https(string? origin) =>
        Assert.Null(RedirectUriSuggester.Suggest(null, origin));

    [Fact]
    public void Sugestao_gerada_passa_na_validacao() =>
        Assert.Null(MercadoLivreCredentialsValidator.Validate(Id, Secret, RedirectUriSuggester.Suggest(null, "https://app.exemplo.com.br")!));
}
