namespace AgentOffice.Api.MercadoLivre;

// Exceção lançada quando o código tenta fazer uma chamada que ALTERARIA algo
// na conta do Mercado Livre sem estar liberada.
public class WriteBlockedException(string message) : Exception(message);

// ============================================================================
// TRAVA DE SEGURANÇA: modo somente leitura.
//
// Um DelegatingHandler fica "no meio do caminho" de toda chamada HTTP feita pelo
// HttpClient do Mercado Livre. Como TODAS as chamadas passam por aqui, nenhum
// outro código do projeto consegue alterar anúncios, preços, estoque, etc.
//
// Regras:
//   - GET / HEAD / OPTIONS: só leem dados, são liberados.
//   - POST em /oauth/token: é só a troca do código de login por um token e não altera a conta.
//   - Qualquer outra coisa (POST, PUT, PATCH, DELETE...): BLOQUEADA com exceção.
//
// Alterações reais só serão possíveis no futuro por um fluxo separado, que exige
// a sua aprovação explícita antes de cada operação.
// ============================================================================
public class ReadOnlyGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsAllowed(request))
        {
            // Lançamos a exceção ANTES de enviar: a requisição nunca sai do nosso servidor.
            throw new WriteBlockedException(
                $"Bloqueado: {request.Method} {request.RequestUri?.AbsolutePath}. " +
                "O sistema está em modo somente leitura no Mercado Livre; alterações exigem aprovação.");
        }

        // Permitida: segue para o próximo handler (o que realmente envia pela rede).
        return base.SendAsync(request, cancellationToken);
    }

    // Decide se a requisição é segura (não altera dados na conta).
    private static bool IsAllowed(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get ||
            request.Method == HttpMethod.Head ||
            request.Method == HttpMethod.Options)
        {
            return true;
        }

        // Única exceção: a obtenção/renovação de token do OAuth.
        return request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == "/oauth/token";
    }
}
