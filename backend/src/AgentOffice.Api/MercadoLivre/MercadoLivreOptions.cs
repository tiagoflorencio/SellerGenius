namespace AgentOffice.Api.MercadoLivre;

// Classe que representa a seção "MercadoLivre" das configurações.
// Os nomes das propriedades precisam bater com as chaves em appsettings/user-secrets:
//   "MercadoLivre": { "AppId": "...", "ClientSecret": "...", "RedirectUri": "..." }
// O "= """ evita valor null (o projeto usa <Nullable>enable</Nullable>).
public class MercadoLivreOptions
{
    // ID público do app, gerado no painel de desenvolvedores do Mercado Livre.
    public string AppId { get; set; } = "";

    // Senha do app. É segredo: nunca vai no código nem no Git, só em user-secrets.
    public string ClientSecret { get; set; } = "";

    // URL para onde o Mercado Livre devolve o usuário após o login.
    // Deve ser idêntica à cadastrada no painel.
    public string RedirectUri { get; set; } = "";

    // Tela de login/autorização do Mercado Livre Brasil (aberta no navegador do usuário).
    public string AuthBaseUrl { get; set; } = "https://auth.mercadolivre.com.br";

    // Base da API REST (chamada pelo nosso servidor, nunca pelo navegador).
    public string ApiBaseUrl { get; set; } = "https://api.mercadolibre.com";
}
