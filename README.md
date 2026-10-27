# AgentOffice

Painel para vendedores do Mercado Livre com agentes de IA:

- **Anúncios**: diagnóstico de cada anúncio (conversão, fotos, título, preço, frete) e sugestões de melhoria geradas por IA.
- **Campanhas de Ads**: métricas de Product Ads, diagnóstico por regras, recomendações priorizadas pelo agente, diário de mudanças e avaliação antes/depois.
- **Configurações**: o próprio usuário informa as credenciais do seu aplicativo do Mercado Livre e conecta ou desconecta a conta.

O sistema é **somente leitura** no Mercado Livre: toda recomendação passa pela sua aprovação e é aplicada manualmente por você.

## Tecnologias

- Backend: .NET 9 (minimal API), EF Core + SQLite (`backend/`)
- Frontend: Vue 3 + TypeScript + Vite (`frontend/`)
- IA: OpenAI (chave configurada no servidor)

## Como rodar

Pré-requisitos: .NET 9 SDK e Node.js 20+.

```powershell
.\dev.ps1          # sobe a API (http://localhost:5172) e o painel (http://localhost:5173)
.\dev.ps1 -Stop    # encerra os dois
```

Sem PowerShell: `dotnet run --launch-profile http` em `backend/src/AgentOffice.Api` e `npm install && npm run dev` em `frontend`.

## Conectar ao Mercado Livre

1. Crie um aplicativo em <https://developers.mercadolivre.com.br/> e cadastre nele uma URL de redirecionamento **HTTPS** que termine em `/auth/callback` e encaminhe para a API (o Mercado Livre não aceita `http://localhost`; em desenvolvimento use um túnel, como o devtunnel: `.\dev.ps1 -Tunnel`).
2. No painel, abra **Configurações**, informe App ID, Secret Key e a mesma URL de redirecionamento e salve.
3. Clique em **Conectar conta** e autorize o acesso.

As credenciais e os tokens ficam criptografados em `backend/src/AgentOffice.Api/App_Data` (ignorado pelo Git). A chave secreta nunca é devolvida pela API.

Se a API e o painel não estiverem em `http://localhost:5173`, defina `Frontend:Url` com o endereço do painel (para onde o login retorna).

## IA (OpenAI)

```powershell
cd backend/src/AgentOffice.Api
dotnet user-secrets set "OpenAI:ApiKey" "sua-chave"
```

Opcionalmente `OpenAI:Model` e `OpenAI:AdsModel` (modelo da análise de Ads).

## Testes

```powershell
dotnet test backend
```

## Segurança

- Nunca versione chaves: `App_Data/`, bancos e `.env` estão no `.gitignore`; segredos ficam em user-secrets ou variáveis de ambiente.
- As rotas da API são aceitas apenas de `localhost`; só o retorno do login (`/auth/callback`) é público e protegido por `state`.
- Este projeto foi pensado para uso local por um único vendedor. Antes de hospedar para vários usuários, adicione autenticação e armazenamento seguro de segredos.
