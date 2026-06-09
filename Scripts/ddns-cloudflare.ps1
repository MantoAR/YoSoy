# ───────────────────────────────────────────────────────────────────────────
# DDNS para Cloudflare — actualiza los registros A de app y turn con tu IP pública.
#
# 1) Pegá tu API token de Cloudflare abajo (permiso: Zone > DNS > Edit sobre la zona).
#    Mejor aún: dejá el placeholder y exportá $env:CF_API_TOKEN antes de correrlo.
# 2) Los registros A deben EXISTIR en Cloudflare en modo DNS-only (nube gris).
# 3) Programar cada 5 min con el Programador de tareas de Windows (ver abajo).
#
#   schtasks /Create /SC MINUTE /MO 5 /TN "YoSoy DDNS" /RL LIMITED `
#     /TR "powershell -NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\ddns-cloudflare.ps1`""
#
#   (o Task Scheduler GUI: Crear tarea básica > cada 5 min > Iniciar programa >
#    powershell.exe con argumentos -NoProfile -ExecutionPolicy Bypass -File <ruta>)
# ───────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = 'Stop'

$CF_API_TOKEN = if ($env:CF_API_TOKEN) { $env:CF_API_TOKEN } else { 'PEGA_ACA_TU_API_TOKEN' }
$ZONE_NAME    = 'comuna31.com.ar'
$RECORDS      = @('app.comuna31.com.ar', 'turn.comuna31.com.ar')

if ($CF_API_TOKEN -eq 'PEGA_ACA_TU_API_TOKEN') {
    Write-Error "Falta el API token de Cloudflare. Pegalo en el script o exportá `$env:CF_API_TOKEN."
    exit 1
}

$headers = @{ Authorization = "Bearer $CF_API_TOKEN"; 'Content-Type' = 'application/json' }
$ip = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
Write-Host "IP pública actual: $ip"

$zoneResp = Invoke-RestMethod -Headers $headers -Uri "https://api.cloudflare.com/client/v4/zones?name=$ZONE_NAME"
$zone = $zoneResp.result | Select-Object -First 1
if (-not $zone) { Write-Error "Zona $ZONE_NAME no encontrada (¿token con permiso sobre esa zona?)."; exit 1 }

foreach ($name in $RECORDS) {
    $recResp = Invoke-RestMethod -Headers $headers -Uri "https://api.cloudflare.com/client/v4/zones/$($zone.id)/dns_records?type=A&name=$name"
    $rec = $recResp.result | Select-Object -First 1
    if (-not $rec) { Write-Warning "Registro A '$name' no existe. Crealo en Cloudflare (DNS-only)."; continue }
    if ($rec.content -eq $ip) { Write-Host "$name ya apunta a $ip (sin cambios)."; continue }

    $body = @{ type = 'A'; name = $name; content = $ip; ttl = 60; proxied = $false } | ConvertTo-Json
    Invoke-RestMethod -Method Put -Headers $headers `
        -Uri "https://api.cloudflare.com/client/v4/zones/$($zone.id)/dns_records/$($rec.id)" -Body $body | Out-Null
    Write-Host "$name -> $ip (ACTUALIZADO)"
}
