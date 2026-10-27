<#
Sobe o ambiente de desenvolvimento: API (http://localhost:5172) e painel (http://localhost:5173),
cada um em sua propria janela do PowerShell (feche a janela para parar o servidor).

  .\dev.ps1            sobe API e painel
  .\dev.ps1 -Tunnel    tambem sobe o tunel (so e necessario para fazer login no Mercado Livre)
  .\dev.ps1 -Tunnel -TunnelName meu-tunel   usa outro tunel do devtunnel (padrao: agentoffice)
  .\dev.ps1 -Stop      encerra o que estiver rodando nas portas da API e do painel
#>
param(
    [switch]$Tunnel,
    [switch]$Stop,
    [string]$TunnelName = 'agentoffice'
)

$root = $PSScriptRoot
$apiDir = Join-Path $root 'backend\src\AgentOffice.Api'
$webDir = Join-Path $root 'frontend'
$apiPort = 5172
$webPort = 5173

# Retorna true se alguem ja escuta nessa porta.
function Test-Port($port) {
    [bool](Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
}

# Encerra o processo (e os filhos dele) que escuta na porta.
function Stop-Port($port) {
    Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object {
            taskkill /T /F /PID $_ | Out-Null
            Write-Host "Encerrado o processo $_ (porta $port)"
        }
}

# Abre uma nova janela do PowerShell que entra na pasta e executa o comando.
function Start-Window($title, $dir, $command) {
    $safeDir = $dir.Replace("'", "''")
    $inner = "`$Host.UI.RawUI.WindowTitle = '$title'; Set-Location -LiteralPath '$safeDir'; $command"
    Start-Process powershell -ArgumentList "-NoExit -Command `"& { $inner }`""
}

if ($Stop) {
    Stop-Port $apiPort
    Stop-Port $webPort
    return
}

if (Test-Port $apiPort) {
    Write-Host "API ja esta rodando na porta $apiPort."
} else {
    Start-Window 'AgentOffice API' $apiDir 'dotnet run --launch-profile http'
    Write-Host "API iniciando em http://localhost:$apiPort"
}

if (Test-Port $webPort) {
    Write-Host "Painel ja esta rodando na porta $webPort."
} else {
    # Na primeira vez (ou apos clonar o repositorio) instala as dependencias do front-end.
    Start-Window 'AgentOffice Painel' $webDir 'if (-not (Test-Path node_modules)) { npm install }; npm run dev'
    Write-Host "Painel iniciando em http://localhost:$webPort"
}

if ($Tunnel) {
    # O PATH de terminais abertos antes da instalacao pode nao conhecer o devtunnel; entao recarregamos.
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

    if (Get-Command devtunnel -ErrorAction SilentlyContinue) {
        Start-Window 'AgentOffice Tunel' $root "devtunnel host $TunnelName"
        Write-Host "Tunel iniciando (devtunnel host $TunnelName)"
    } else {
        Write-Host 'devtunnel nao encontrado. Instale com: winget install Microsoft.devtunnel'
    }
}

Write-Host ''
Write-Host "Painel: http://localhost:$webPort"
