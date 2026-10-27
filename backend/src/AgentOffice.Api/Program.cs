// ============================================================================
// Program.cs: ponto de entrada da API. O C# executa este arquivo de cima para
// baixo, e ele tem 3 fases: (1) configurar, (2) montar o pipeline, (3) rodar.
// "Top-level statements": não precisa de class Program nem método Main().
// ============================================================================

using AgentOffice.Api.Ai;
using AgentOffice.Api.Ads;
using AgentOffice.Api.Data;
using AgentOffice.Api.MercadoLivre;
using AgentOffice.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// FASE 1: CONFIGURAÇÃO
// O "builder" prepara tudo que a aplicação precisa antes de subir:
// servidor web (Kestrel), leitura de configurações, logs e injeção de dependência.
// "args" são os argumentos de linha de comando recebidos pelo programa.
var builder = WebApplication.CreateBuilder(args);

// builder.Services é o "contêiner de injeção de dependência" (DI).
// Tudo registrado aqui pode ser pedido automaticamente no construtor de outras
// classes ou nos parâmetros dos endpoints, sem usar "new".

// Gera o documento OpenAPI (descrição JSON dos endpoints), útil para testar e documentar.
builder.Services.AddOpenApi();

// Lê a seção "MercadoLivre" das configurações (appsettings.json, user-secrets,
// variáveis de ambiente) e preenche a classe MercadoLivreOptions (pasta MercadoLivre/).
// Depois, qualquer classe pode receber IOptions<MercadoLivreOptions> e ler esses valores.
builder.Services.Configure<MercadoLivreOptions>(builder.Configuration.GetSection("MercadoLivre"));

// Criptografia usada pelo TokenStore para proteger os tokens salvos em disco.
// O nome fixo evita que mover a pasta do projeto invalide os dados protegidos (por padrão o ASP.NET usa o caminho da pasta).
// Em produção (App:DataDir definido) as chaves ficam no disco persistente; senão, cada deploy invalidaria tokens e sessões.
var dataDir = AppPaths.DataDir(builder.Configuration, builder.Environment);
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("AgentOffice");
if (!string.IsNullOrEmpty(builder.Configuration["App:DataDir"]))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

// Controle de acesso: senha única em produção (Auth:AccessPassword) ou só localhost em desenvolvimento.
builder.Services.AddSingleton<AccessControl>();

// TRAVA DE SEGURANÇA: o HttpClient "MercadoLivre" é o ÚNICO canal para falar com o Mercado Livre,
// e todo tráfego dele passa pela ReadOnlyGuardHandler, que bloqueia qualquer escrita.
builder.Services.AddTransient<ReadOnlyGuardHandler>();
builder.Services
    .AddHttpClient("MercadoLivre", (sp, client) =>
        client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MercadoLivreOptions>>().Value.ApiBaseUrl))
    .AddHttpMessageHandler<ReadOnlyGuardHandler>();

// Singleton: uma única instância durante toda a vida da API (o AuthService guarda os "states" do login em memória).
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<MercadoLivreCredentialsStore>();
builder.Services.AddSingleton<MercadoLivreAuthService>();
builder.Services.AddSingleton<MercadoLivreClient>();

// Banco de dados local (SQLite): um arquivo em App_Data/, ignorado pelo Git.
// Scoped (padrão do AddDbContext): uma instância do banco por requisição HTTP.
var dbPath = Path.Combine(dataDir, "agentoffice.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<AgentOfficeDb>(o => o.UseSqlite($"Data Source={dbPath}"));

// Serviço que sincroniza os anúncios do Mercado Livre para o banco.
builder.Services.AddScoped<ListingSyncService>();

// IA (OpenAI): chave em user-secrets (OpenAI:ApiKey). O HttpClient "OpenAI" é separado do do Mercado Livre,
// então a trava de somente leitura não interfere e os tokens do Mercado Livre nunca vão para a OpenAI.
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.AddHttpClient("OpenAI", (sp, client) =>
    client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OpenAiOptions>>().Value.BaseUrl));
builder.Services.AddSingleton<OpenAiClient>();
builder.Services.AddScoped<ListingAdvisor>();

// Product Ads: cliente (somente leitura, via HttpClient "MercadoLivre") e sincronização para o banco local.
builder.Services.AddSingleton<AdsClient>();
builder.Services.AddScoped<AdsSyncService>();
builder.Services.AddScoped<AdsSettingsService>();

// Agente de Product Ads (IA): contexto/diagnóstico compartilhado, manual lido do arquivo e o analista.
builder.Services.AddScoped<AdsDiagnosisService>();
builder.Services.AddScoped<AdsDiaryService>();
builder.Services.AddSingleton<AdsManualProvider>();
builder.Services.AddScoped<AdsAdvisor>();

// FASE 2: PIPELINE
// Build() fecha o registro de serviços e cria a aplicação ("app").
// A partir daqui configuramos o pipeline: toda requisição HTTP passa pelos
// "middlewares" na ordem em que são declarados, até chegar no endpoint.
var app = builder.Build();

// Aplica as migrações pendentes: cria o arquivo do banco se não existir e atualiza as tabelas quando o modelo muda.
// As migrações ficam na pasta Migrations/ (geradas com "dotnet ef migrations add").
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AgentOfficeDb>().Database.Migrate();
}

// A rota do documento OpenAPI só existe em desenvolvimento, para não expor em produção.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Obs.: sem UseHttpsRedirection de propósito. O túnel (devtunnel) já entrega HTTPS ao Mercado Livre
// e repassa para a porta http 5172; redirecionar aqui quebraria o callback do login.

// ENDPOINTS (as rotas da API)
// MapGet liga o método HTTP GET e o caminho "/health" a uma função (lambda).
// Results.Ok(...) devolve status 200 e o objeto anônimo serializado como JSON: {"status":"ok"}.
// Serve para verificar rapidamente se a API está no ar.
// WithName dá um nome interno ao endpoint (usado por OpenAPI e para gerar links).
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

// Sessão de acesso (/auth/session): entrar com a senha, consultar e sair.
app.MapAccessEndpoints();

if (string.IsNullOrEmpty(app.Configuration["Auth:AccessPassword"]) && !app.Environment.IsDevelopment())
    app.Logger.LogWarning("Auth:AccessPassword não definida: a API só responde a requisições locais. Defina a senha para usar em produção.");

// Rotas do Mercado Livre (/auth/login, /auth/callback, /ml/me), definidas em MercadoLivre/MercadoLivreEndpoints.cs.
app.MapMercadoLivreEndpoints();

// Rotas da tela de Configurações (/settings/...), definidas em MercadoLivre/IntegrationEndpoints.cs.
app.MapIntegrationEndpoints();

// Rotas do agente de anúncios (/agent/...), definidas em Ai/AgentEndpoints.cs.
app.MapAgentEndpoints();

// Rotas de Product Ads (/ads/...), definidas em Ads/AdsEndpoints.cs.
app.MapAdsEndpoints();
app.MapAdsAgentEndpoints();
app.MapAdsDiaryEndpoints();

// FASE 3: EXECUÇÃO
// Inicia o servidor e bloqueia aqui, recebendo requisições até o processo ser encerrado.
app.Run();
