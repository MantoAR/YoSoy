# Levanta TODO lo necesario para la videollamada en modo LAN/demo (misma PC o misma red).
#
# Abre dos ventanas persistentes (una por servicio) para que NO se cierren al terminar
# el script ni al cerrar esta consola:
#   1) Backend .NET   -> http://localhost:5000   (sirve el portal en /pantallas)
#   2) Signaling Node -> http://localhost:3001   (coordina el WebRTC)
#
# El signaling se fuerza a modo LAN por variable de entorno (sin tocar su .env, que
# guarda la config online): CORS hacia localhost y MODO_LAN=true.
#
# Uso:  .\levantar-lan.ps1
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot

Write-Host "Levantando backend .NET (puerto 5000)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$raiz'; `$Host.UI.RawUI.WindowTitle = 'YoSoy BACKEND :5000'; dotnet run"
)

Write-Host "Levantando signaling Node (puerto 3001, modo LAN)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$raiz\signaling-server'; `$Host.UI.RawUI.WindowTitle = 'YoSoy SIGNALING :3001';" +
    " `$env:MODO_LAN='true'; `$env:CORS_ORIGINS='http://localhost:5000,http://localhost:5500'; node server.js"
)

Write-Host ""
Write-Host "Dos ventanas abiertas. Esperá ~10s y entrá al portal:" -ForegroundColor Green
Write-Host "  http://localhost:5000/pantallas/index.html" -ForegroundColor Yellow
Write-Host "Para frenar todo: cerrá las dos ventanas (o Ctrl+C en cada una)." -ForegroundColor DarkGray
