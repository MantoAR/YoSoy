<#
.SYNOPSIS
    Corre los tests de YoSoy.Tests contra la base de pruebas y deja las variables de entorno como estaban.

.DESCRIPTION
    Define, solo para este proceso:
      - ConnectionStrings__YoSoy: la cadena de conexión a -Base en -Servidor (autenticación de Windows, sin contraseña);
      - Email__Password con un espacio: así ningún test manda mails reales aunque tengas credenciales de correo en tus
        user-secrets (un espacio cuenta como "sin contraseña").
    Corre dotnet test del proyecto YoSoy.Tests y, al terminar (haya salido bien o mal), restaura las dos variables a como
    estaban. Devuelve el código de salida de dotnet test.

    Se niega a usar la base "yosoy" (la de desarrollo). Los tests de integración ESCRIBEN y BORRAN filas en la base a la que
    apuntan: usá siempre una base de pruebas (ver Scripts\crear-base-de-pruebas.ps1), nunca una con datos reales ni la de
    producción.

.PARAMETER Servidor
    Instancia de SQL Server. Por defecto, la instancia local .\SQLEXPRESS

.PARAMETER Base
    Base de pruebas. Por defecto, yosoy_test. Tiene que existir (salvo que el filtro deje afuera los tests de integración).

.PARAMETER Filtro
    Filtro de dotnet test. Por ejemplo 'Categoria!=Integracion' (solo los tests que no necesitan base; en ese caso no se
    exige que la base exista) o 'FullyQualifiedName~Login'.

.PARAMETER Argumentos
    Argumentos extra que se le pasan tal cual a dotnet test.

.EXAMPLE
    .\Scripts\correr-tests.ps1

.EXAMPLE
    .\Scripts\correr-tests.ps1 -Filtro 'Categoria!=Integracion'

.EXAMPLE
    .\Scripts\correr-tests.ps1 -Servidor 'MIPC\SQLEXPRESS' -Base yosoy_test2 -Filtro 'FullyQualifiedName~Turno'
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Servidor = '.\SQLEXPRESS',
    [string]$Base = 'yosoy_test',
    [string]$Filtro,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Argumentos
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Guardas: si alguna falla, no se corre nada ───────────────────────────────────────────────────────────────────────
if (-not $PSScriptRoot) { throw 'Ejecutá el archivo (por ejemplo: .\Scripts\correr-tests.ps1), no pegues su contenido en la consola.' }
if ($Base -ieq 'yosoy') { throw "Me niego a usar la base 'yosoy': es la de desarrollo, con datos, y los tests escriben y borran filas. Usá una base de pruebas (Scripts\crear-base-de-pruebas.ps1)." }
if ($Base -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') { throw "El nombre de la base ('$Base') tiene que empezar con una letra o _ y tener solo letras, números y _ (hasta 128 caracteres)." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'No encuentro dotnet. Instalá el SDK de .NET y volvé a probar.' }

$repo = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $repo 'YoSoy.Tests\YoSoy.Tests.csproj'
if (-not (Test-Path -LiteralPath $proyecto)) { throw "No encuentro $proyecto" }

# La base tiene que existir: si no, los tests de integración fallan todos con "Cannot open database". Si el filtro los deja
# afuera (Categoria!=Integracion) no hace falta base y no se comprueba.
$necesitaBase = -not ($Filtro -match 'Categoria\s*!=\s*Integracion')
if ($necesitaBase) {
    if (Get-Command sqlcmd -ErrorAction SilentlyContinue) {
        $salida = & sqlcmd -S $Servidor -d master -E -C -h -1 -W -b -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$Base') IS NULL THEN 'NO' ELSE 'SI' END"
        if ($LASTEXITCODE -ne 0) {
            throw "sqlcmd no pudo consultar '$Servidor' (código $LASTEXITCODE). Verificá que la instancia esté levantada y que el nombre sea correcto."
        }
        $existe = "$(@($salida | Where-Object { $_ -match '\S' }) | Select-Object -First 1)".Trim()
        if ($existe -ne 'SI') {
            throw "La base '$Base' no existe en '$Servidor'. Creala con:  .\Scripts\crear-base-de-pruebas.ps1 -Servidor '$Servidor' -Base $Base"
        }
    }
    else {
        Write-Warning 'No encuentro sqlcmd: no puedo comprobar que la base exista. Sigo igual.'
    }
}

# ── Variables solo para este proceso; se restauran siempre ───────────────────────────────────────────────────────────
$nombres = @('ConnectionStrings__YoSoy', 'Email__Password')
$previas = @{}
foreach ($nombre in $nombres) { $previas[$nombre] = [Environment]::GetEnvironmentVariable($nombre, 'Process') }

$codigo = 1
try {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__YoSoy', "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;", 'Process')
    [Environment]::SetEnvironmentVariable('Email__Password', ' ', 'Process')

    $argumentosDotnet = @('test', $proyecto)
    if ($Filtro) { $argumentosDotnet += @('--filter', $Filtro) }
    if ($Argumentos) { $argumentosDotnet += $Argumentos }

    Write-Host "Base de pruebas: '$Base' en '$Servidor' (los tests escriben y borran filas ahí)."
    Write-Host "dotnet $($argumentosDotnet -join ' ')"
    & dotnet @argumentosDotnet
    $codigo = $LASTEXITCODE
}
finally {
    foreach ($nombre in $nombres) { [Environment]::SetEnvironmentVariable($nombre, $previas[$nombre], 'Process') }
}

exit $codigo
