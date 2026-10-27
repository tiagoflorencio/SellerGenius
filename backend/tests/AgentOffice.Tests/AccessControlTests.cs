using AgentOffice.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AgentOffice.Tests;

public class AccessControlTests
{
    private static readonly IDataProtectionProvider Provider = DataProtectionProvider.Create("ao-tests");

    private static AccessControl Create(string? password) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:AccessPassword"] = password }).Build(),
        Provider);

    private static HttpRequest Request(string host = "localhost", string? cookie = null, bool forwarded = false)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        if (forwarded) ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.5";
        if (cookie is not null) ctx.Request.Headers.Cookie = $"{AccessControl.CookieName}={cookie}";
        return ctx.Request;
    }

    [Fact]
    public void Sem_senha_aceita_so_a_propria_maquina()
    {
        var access = Create(null);

        Assert.True(access.IsAllowed(Request("localhost")));
        Assert.False(access.IsAllowed(Request("app.exemplo.com")));
        Assert.False(access.IsAllowed(Request("localhost", forwarded: true)));
    }

    [Fact]
    public void Com_senha_exige_sessao_ate_em_localhost()
    {
        var access = Create("segredo-de-teste");

        Assert.False(access.IsAllowed(Request("localhost")));
        Assert.False(access.IsAllowed(Request("app.exemplo.com")));
    }

    [Fact]
    public void Login_correto_gera_sessao_valida()
    {
        var access = Create("segredo-de-teste");

        var token = access.TryLogin("segredo-de-teste", out var locked);

        Assert.NotNull(token);
        Assert.False(locked);
        Assert.True(access.IsAllowed(Request("app.exemplo.com", cookie: Uri.EscapeDataString(token!), forwarded: true)));
    }

    [Fact]
    public void Senha_errada_nao_gera_sessao_e_token_invalido_e_recusado()
    {
        var access = Create("segredo-de-teste");

        Assert.Null(access.TryLogin("errada", out _));
        Assert.False(access.IsAllowed(Request(cookie: "token-forjado")));
    }

    [Fact]
    public void Trocar_a_senha_encerra_as_sessoes_abertas()
    {
        var token = Create("senha-antiga-123").TryLogin("senha-antiga-123", out _)!;

        var depois = Create("senha-nova-456");

        Assert.False(depois.IsAllowed(Request(cookie: Uri.EscapeDataString(token))));
    }

    [Fact]
    public void Bloqueia_apos_muitas_tentativas_erradas_mesmo_com_a_senha_certa()
    {
        var access = Create("segredo-de-teste");

        for (var i = 0; i < 8; i++) Assert.Null(access.TryLogin("errada", out _));

        Assert.Null(access.TryLogin("segredo-de-teste", out var locked));
        Assert.True(locked);
    }
}
