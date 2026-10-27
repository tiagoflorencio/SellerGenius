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
}
