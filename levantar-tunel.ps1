$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot

$cloudflared = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source
if (-not $cloudflared) {
    foreach ($p in @(
        "C:\Program Files (x86)\cloudflared\cloudflared.exe",
        "C:\Program Files\cloudflared\cloudflared.exe",
        "$env:USERPROFILE\.cloudflared\cloudflared.exe"
    )) { if (Test-Path $p) { $cloudflared = $p; break } }
}
if (-not $cloudflared) {
    Write-Host "No se encontro cloudflared. Instalalo con: winget install --id Cloudflare.cloudflared" -ForegroundColor Red
    exit 1
}

Write-Host "Levantando signaling Node (puerto 3001, CORS abierto para el tunel)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$raiz\signaling-server'; `$Host.UI.RawUI.WindowTitle = 'YoSoy SIGNALING :3001';" +
    " `$env:MODO_LAN='true'; `$env:CORS_ORIGINS='*'; node server.js"
)

$logTunel = Join-Path $env:TEMP 'yosoy-cloudflared.log'
$errTunel = "$logTunel.err"
foreach ($f in @($logTunel, $errTunel)) { if (Test-Path $f) { Remove-Item $f -Force } }

Write-Host "Abriendo el quick tunnel de Cloudflare contra http://localhost:5000 ..." -ForegroundColor Cyan
Start-Process -FilePath $cloudflared `
    -ArgumentList @('tunnel', '--url', 'http://localhost:5000') `
    -RedirectStandardOutput $logTunel -RedirectStandardError $errTunel -WindowStyle Hidden

$urlPublica = $null
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 1
    foreach ($f in @($errTunel, $logTunel)) {
        if (Test-Path $f) {
            $matches = Select-String -Path $f -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -AllMatches -ErrorAction SilentlyContinue |
                ForEach-Object { $_.Matches } |
                ForEach-Object { $_.Value } |
                Where-Object { $_ -notmatch '://api\.trycloudflare\.com' } |
                Select-Object -First 1
            if ($matches) { $urlPublica = $matches; break }
        }
    }
    if ($urlPublica) { break }
}
if (-not $urlPublica) {
    Write-Host "No se pudo capturar la URL del tunel. Revisa $errTunel" -ForegroundColor Red
    exit 1
}

Write-Host "URL publica: $urlPublica" -ForegroundColor Green

Write-Host "Levantando backend .NET (puerto 5000) con la URL publica inyectada (mails + links de sesion)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$raiz'; `$Host.UI.RawUI.WindowTitle = 'YoSoy BACKEND :5000';" +
    " `$env:Dominio='$urlPublica'; `$env:Email__AppUrl='$urlPublica'; dotnet run"
)

Write-Host ""
Write-Host "Listo. Entra al portal y compartilo:" -ForegroundColor Green
Write-Host "  $urlPublica/pantallas/index.html" -ForegroundColor Yellow
Write-Host "Los mails y los links de sesion ya apuntan a esa URL publica." -ForegroundColor Yellow
Write-Host "Para frenar: cerra las ventanas de signaling y backend, y corre: taskkill /IM cloudflared.exe /F" -ForegroundColor DarkGray
