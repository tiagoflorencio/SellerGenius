FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY backend/src/AgentOffice.Api/AgentOffice.Api.csproj backend/src/AgentOffice.Api/
RUN dotnet restore backend/src/AgentOffice.Api/AgentOffice.Api.csproj
COPY backend/src/AgentOffice.Api backend/src/AgentOffice.Api
RUN dotnet publish backend/src/AgentOffice.Api/AgentOffice.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
# Manual do agente de Ads (lido pelo agente em tempo de execução).
COPY docs /app/docs

# /data deve ser um volume persistente: guarda o banco SQLite, as credenciais, os tokens e as chaves de criptografia.
ENV ASPNETCORE_ENVIRONMENT=Production \
    App__DataDir=/data \
    Ads__ManualPath=/app/docs/SellerGenius_Manual_Agente_Mercado_Livre_Ads.md
VOLUME /data
EXPOSE 8080

# Hospedagens como Railway/Render informam a porta na variável PORT.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet AgentOffice.Api.dll"]
