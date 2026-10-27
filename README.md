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

1. Crie um aplicativo em <https://developers.mercadolivre.com.br/devcenter/> e cole nele, em "URIs de redirect", a URL de redirecionamento exibida em **Configurações** (ela é gerada automaticamente a partir do endereço público HTTPS em que o painel foi aberto, terminando em `/api/auth/callback`; se a API tiver endereço próprio, defina `App:PublicUrl`).
2. No painel, abra **Configurações**, informe App ID e Secret Key e salve.
3. Clique em **Conectar conta** e autorize o acesso.

O Mercado Livre não aceita `http://localhost`. Em desenvolvimento local, suba um túnel (por exemplo `.\dev.ps1 -Tunnel`, que usa o devtunnel) e informe a URL dele em **Configurações → Avançado** (`https://seu-tunel/auth/callback`) ou defina `App:PublicUrl`.

As credenciais e os tokens ficam criptografados em `backend/src/AgentOffice.Api/App_Data` (ignorado pelo Git). A chave secreta nunca é devolvida pela API.

Se a API e o painel não estiverem em `http://localhost:5173`, defina `Frontend:Url` com o endereço do painel (para onde o login retorna).

## IA (OpenAI)

```powershell
cd backend/src/AgentOffice.Api
dotnet user-secrets set "OpenAI:ApiKey" "sua-chave"
```

Opcionalmente `OpenAI:Model` e `OpenAI:AdsModel` (modelo da análise de Ads).

## Publicar em produção (Vercel + Railway)

O painel (Vue) vai para a **Vercel** e a API (.NET + SQLite) para o **Railway** (ou Render/Fly.io), porque a API precisa de disco persistente.

**1. API no Railway**

1. Crie um projeto a partir do repositório do GitHub: o Railway usa o `Dockerfile` da raiz.
2. Adicione um **Volume** montado em `/data` (banco, credenciais, tokens e chaves).
3. Em *Variables*, defina:
   - `Auth__AccessPassword`: a senha de acesso ao sistema (use uma longa).
   - `OpenAI__ApiKey`: a chave da OpenAI.
   - `Frontend__Url`: o endereço do painel na Vercel (ex.: `https://seu-projeto.vercel.app`).
4. Gere o domínio público em *Settings → Networking* e anote a URL (ex.: `https://agentoffice-production.up.railway.app`). Teste `/health`.

**2. Painel na Vercel**

1. Edite `frontend/vercel.json` e troque `SEU-BACKEND.up.railway.app` pela URL da API.
2. Importe o repositório na Vercel com *Root Directory* = `frontend` (framework Vite).
3. Abra o endereço da Vercel, entre com a senha e vá em **Configurações**: a URL de redirecionamento aparece pronta (`https://seu-projeto.vercel.app/api/auth/callback`). Cole-a no aplicativo do Mercado Livre, informe App ID e Secret Key e conecte.

Sem `Auth__AccessPassword` a API só responde a requisições locais (nada é exposto por engano).

## Testes

```powershell
dotnet test backend
```

## Segurança

- Nunca versione chaves: `App_Data/`, bancos e `.env` estão no `.gitignore`; segredos ficam em user-secrets ou variáveis de ambiente.
- As rotas da API exigem sessão: com `Auth__AccessPassword` definida, é preciso entrar com a senha (cookie HttpOnly, 7 dias, bloqueio temporário após 8 erros); sem senha, só aceitam `localhost`. Apenas `/health`, `/auth/session` e o retorno do login do Mercado Livre (`/auth/callback`, protegido por `state`) são públicos.
- Este projeto é de um único vendedor (uma senha, uma conta do Mercado Livre). Para vender a vários clientes, é preciso adicionar contas de usuário e separar os dados por cliente.
