# AgentOffice

Painel para vendedores do Mercado Livre com agentes de IA:

- **Anúncios**: diagnóstico de cada anúncio (conversão, fotos, título, preço, frete) e sugestões de melhoria geradas por IA.
- **Campanhas de Ads**: métricas de Product Ads, diagnóstico por regras, recomendações priorizadas pelo agente, diário de mudanças e avaliação antes/depois.
- **Configurações**: o próprio usuário informa as credenciais do seu aplicativo do Mercado Livre e conecta ou desconecta a conta.

O sistema é **somente leitura** no Mercado Livre: toda recomendação passa pela sua aprovação e é aplicada manualmente por você.

## Como funciona

O sistema tem duas partes:

- **Backend (API)**: roda no **seu computador**. Guarda seus dados (banco SQLite), as credenciais do Mercado Livre e conversa com o Mercado Livre e a IA.
- **Painel (site)**: a tela que você usa. Pode rodar também no seu computador, ou ficar online na Vercel conversando com o backend do seu PC por um túnel seguro.

Os seus dados nunca saem do seu computador, exceto o que a IA precisa para analisar.

## Tecnologias

- Backend: .NET 9 (minimal API), EF Core + SQLite (`backend/`)
- Frontend: Vue 3 + TypeScript + Vite (`frontend/`)
- IA: OpenAI (chave configurada no servidor)

## Baixar e rodar o backend no seu computador

### 1. Instale os pré-requisitos (Windows)

- [Git](https://git-scm.com/downloads)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/) (para o painel local)

Confira no PowerShell: `git --version`, `dotnet --version` e `node --version`.

### 2. Baixe o projeto

```powershell
git clone https://github.com/tiagoflorencio/SellerGenius.git
cd SellerGenius
```

Sem Git: na página do repositório no GitHub use **Code → Download ZIP**, extraia e abra a pasta no PowerShell.

> **Pasta raiz do projeto:** é a pasta que contém `backend`, `frontend`, `docs` e o arquivo `dev.ps1` (aqui, `SellerGenius`; na sua máquina de desenvolvimento, `AgentOffice`). Todos os comandos abaixo dizem a partir de qual pasta executar. O `dev.ps1` **só funciona na pasta raiz**.

### 3. Configure (uma vez só)

Os segredos ficam fora do projeto, no seu perfil do Windows (*user-secrets*), nunca no Git. Execute **a partir da pasta raiz**:

```powershell
cd backend\src\AgentOffice.Api
dotnet user-secrets set "OpenAI:ApiKey" "sua-chave-da-openai"
dotnet user-secrets set "Auth:AccessPassword" "uma-senha-longa-para-entrar-no-sistema"
cd ..\..\..
```

O último `cd ..\..\..` volta para a pasta raiz. Para conferir o que está salvo: `dotnet user-secrets list` (na pasta `backend\src\AgentOffice.Api`).

- `OpenAI:ApiKey`: chave da OpenAI usada pelos agentes. Opcionalmente `OpenAI:Model` e `OpenAI:AdsModel`.
- `Auth:AccessPassword`: senha para entrar no sistema. **Defina sempre que usar o túnel** (passo 6), porque ele deixa a API acessível pela internet. Use uma senha longa e difícil de adivinhar. Sem senha, a API só responde ao próprio computador. Para trocar a senha, rode o mesmo comando de novo e reinicie.

### 4. Inicie a API e o painel

Execute **na pasta raiz do projeto** (onde está o `dev.ps1`):

```powershell
cd "C:\caminho\para\SellerGenius"   # pasta raiz
.\dev.ps1                            # sobe a API (http://localhost:5172) e o painel (http://localhost:5173)
```

Isso abre **duas janelas** do PowerShell (uma para a API, outra para o painel); deixe-as abertas enquanto usa o sistema. Na primeira vez o painel instala as dependências sozinho (leva alguns minutos). Depois abra <http://localhost:5173> no navegador.

Para encerrar, na pasta raiz: `.\dev.ps1 -Stop` (ou feche as janelas). Se alterar um segredo (passo 3), encerre e inicie de novo.

Se aparecer "scripts desabilitados" ao rodar o `dev.ps1`, libere na sessão atual: `Set-ExecutionPolicy -Scope Process Bypass` e rode de novo.

**Sem o `dev.ps1`** (Linux/macOS ou outro terminal), use dois terminais, cada um na pasta indicada:

| Terminal | Pasta | Comando |
|---|---|---|
| 1 (API) | `backend/src/AgentOffice.Api` | `dotnet run --launch-profile http` |
| 2 (painel) | `frontend` | `npm install` (só na primeira vez) e depois `npm run dev` |

Os dados ficam em `backend/src/AgentOffice.Api/App_Data` (banco, credenciais e tokens criptografados). Para fazer backup, copie essa pasta com a API parada. Para atualizar o sistema: `git pull` na pasta raiz e reinicie.

### 5. Conecte ao Mercado Livre

O Mercado Livre só aceita endereços **HTTPS públicos** no retorno do login (não aceita `http://localhost`), por isso é preciso um túnel (passo 6).

1. Crie um aplicativo em <https://developers.mercadolivre.com.br/devcenter/>.
2. No sistema, abra **Configurações**, informe o **App ID** e a **Secret Key** do aplicativo e salve.
3. Copie a **URL de redirecionamento** exibida (botão *Copiar*) e cole em *URIs de redirect* no aplicativo do Mercado Livre.
4. Clique em **Conectar conta** e autorize o acesso.

Com o painel local (`localhost`) a URL não pode ser gerada sozinha: use **Configurações → Avançado** e informe `https://SEU-TUNEL/auth/callback` (endereço do túnel do passo 6). Com o painel online na Vercel (passo 7) ela já aparece pronta.

### 6. Túnel HTTPS (Microsoft devtunnel, gratuito)

O túnel dá ao seu backend um endereço HTTPS público e estável.

```powershell
winget install Microsoft.devtunnel          # só na primeira vez; feche e reabra o PowerShell depois
devtunnel user login                        # só na primeira vez (abre o navegador)
devtunnel create agentoffice --allow-anonymous
devtunnel port create agentoffice -p 5172
```

Esses quatro comandos podem ser executados em qualquer pasta, uma única vez.

Daqui em diante, para subir tudo junto, **na pasta raiz do projeto**: `.\dev.ps1 -Tunnel` (inicia a API, o painel e o túnel, em três janelas). O endereço aparece na janela do túnel, no formato `https://xxxx-5172.brs.devtunnels.ms`; para consultá-lo depois: `devtunnel show agentoffice`. É ele que você usa no passo 5 (Avançado) ou no passo 7. O endereço permanece o mesmo enquanto o túnel `agentoffice` existir.

Se o login do devtunnel expirar, rode `devtunnel user login` de novo.

### 7. (Opcional) Painel online na Vercel

Para acessar de qualquer lugar sem abrir o painel local (com o backend e o túnel ligados no seu PC):

1. Defina o endereço do painel, para onde o login do Mercado Livre retorna:
   ```powershell
   cd backend/src/AgentOffice.Api
   dotnet user-secrets set "Frontend:Url" "https://seu-projeto.vercel.app"
   ```
2. Em `frontend/vercel.json`, troque `SEU-TUNEL.devtunnels.ms` pelo endereço do túnel, sem `https://` e sem `/` no final (ex.: `abc123-5172.brs.devtunnels.ms`), e faça commit e push.
3. Na [Vercel](https://vercel.com), importe o repositório com *Root Directory* = `frontend` (framework Vite).
4. Abra o endereço da Vercel, entre com a senha e use **Configurações**: a URL de redirecionamento já aparece pronta (`https://seu-projeto.vercel.app/api/auth/callback`).

O sistema só responde enquanto o computador, a API e o túnel estiverem ligados.

## Alternativa: API em um servidor na nuvem

O `Dockerfile` da raiz roda a API em qualquer hospedagem com Docker e disco persistente (Railway, Fly.io, uma VM). Monte um volume em `/data` e defina as variáveis `Auth__AccessPassword`, `OpenAI__ApiKey` e `Frontend__Url`. O restante (Vercel) é igual ao passo 7, apontando o `vercel.json` para o endereço do servidor.

## Testes

```powershell
dotnet test backend
```

## Segurança

- Nunca versione chaves: `App_Data/`, bancos e `.env` estão no `.gitignore`; segredos ficam em user-secrets ou variáveis de ambiente.
- A chave secreta do Mercado Livre é guardada criptografada e nunca é devolvida pela API.
- As rotas da API exigem sessão: com `Auth:AccessPassword` definida, é preciso entrar com a senha (cookie HttpOnly, 7 dias, bloqueio temporário após 8 erros); sem senha, só aceitam `localhost`. Apenas `/health`, `/auth/session` e o retorno do login do Mercado Livre (`/auth/callback`, protegido por `state`) são públicos.
- Este projeto é de um único vendedor (uma senha, uma conta do Mercado Livre). Para vender a vários clientes, é preciso adicionar contas de usuário e separar os dados por cliente.
