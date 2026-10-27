namespace AgentOffice.Api.Data;

// Pasta dos dados locais (banco SQLite, tokens, credenciais e chaves de criptografia).
// Em produção aponte "App:DataDir" para um disco persistente; sem isso, usa App_Data ao lado da API.
public static class AppPaths
{
    public static string DataDir(IConfiguration config, IHostEnvironment env) =>
        config["App:DataDir"] is { Length: > 0 } dir ? dir : Path.Combine(env.ContentRootPath, "App_Data");
}
