# Levanta el BACKEND .NET en modo ONLINE sin tocar los defaults LAN del repo.
#
# Mantiene ASPNETCORE_ENVIRONMENT=Development para que carguen los user-secrets
# (los 4 secretos reales), y aplica los overrides online como variables de entorno
# (que tienen prioridad sobre appsettings.json). Asi la demo LAN de otros (que usa
# los defaults committeados) no se rompe.
#
# Uso:  .\levantar-online.ps1
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Sesion__ModoLan        = 'false'
$env:Turn__Habilitado       = 'true'
$env:Turn__Urls__0          = 'turn:turn.comuna31.com.ar:3478?transport=udp'
$env:Turn__Urls__1          = 'turn:turn.comuna31.com.ar:3478?transport=tcp'
$env:Dominio                = 'https://app.comuna31.com.ar'
$env:Signaling__UrlPublica  = 'https://app.comuna31.com.ar'
$env:Cors__Origenes__0      = 'https://app.comuna31.com.ar'

Write-Host "Backend ONLINE (Development + user-secrets + overrides online). Ctrl+C para parar." -ForegroundColor Cyan
dotnet run
