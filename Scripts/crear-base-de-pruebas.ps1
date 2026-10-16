<#
.SYNOPSIS
    Crea una base SQL Server NUEVA y vacía con el esquema de YoSoy, para correr los tests de integración.

.DESCRIPTION
    Arma una copia temporal de docs\produccion\01-produccion.sql con el nombre de la base reemplazado (el original no se
    toca), la ejecuta con sqlcmd contra la instancia indicada y comprueba que la base quede con 44 tablas.

    Se niega a:
      - usar la base "yosoy" (la de desarrollo, con datos reales);
      - seguir si la base ya existe (nunca pisa ni completa una base existente).

    El esquema ya trae el resultado de las 15 migraciones de migrations\. La base queda sin datos propios (el esquema
    solo siembra el tipo de consentimiento y el contenido de arranque del chat) y así tiene que estar: los tests crean y
    borran sus filas, no hace falta migrations\seed_catalogos.sql.

    Usa la autenticación de Windows (sqlcmd -E): no hay usuario ni contraseña, así que no se guarda ni se imprime ningún
    secreto.

.PARAMETER Servidor
    Instancia de SQL Server. Por defecto, la instancia local .\SQLEXPRESS

.PARAMETER Base
    Nombre de la base a crear. Por defecto, yosoy_test. Letras, números y guion bajo; no puede ser "yosoy".

.EXAMPLE
    .\Scripts\crear-base-de-pruebas.ps1

.EXAMPLE
    .\Scripts\crear-base-de-pruebas.ps1 -Servidor 'MIPC\SQLEXPRESS' -Base yosoy_test2
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Servidor = '.\SQLEXPRESS',
    [string]$Base = 'yosoy_test'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TablasEsperadas = 44

# ── 1) Guardas que no necesitan SQL Server: si alguna falla, no se toca nada ─────────────────────────────────────────
if (-not $PSScriptRoot) { throw 'Ejecutá el archivo (por ejemplo: .\Scripts\crear-base-de-pruebas.ps1), no pegues su contenido en la consola.' }
if ($Base -ieq 'yosoy') { throw "Me niego a usar la base 'yosoy': es la de desarrollo, con datos. Elegí otro nombre para la base de pruebas." }
if ($Base -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') { throw "El nombre de la base ('$Base') tiene que empezar con una letra o _ y tener solo letras, números y _ (hasta 128 caracteres)." }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw 'No encuentro sqlcmd. Instalá las herramientas de línea de comandos de SQL Server (o SSMS) y volvé a probar.' }

$repo = Split-Path -Parent $PSScriptRoot
$origen = Join-Path $repo 'docs\produccion\01-produccion.sql'
if (-not (Test-Path -LiteralPath $origen)) { throw "No encuentro $origen" }

# Corre una consulta de solo lectura con sqlcmd y devuelve la primera línea de la respuesta.
function Invoke-Consulta {
    param([string]$EnBase, [string]$Consulta)
    $salida = & sqlcmd -S $Servidor -d $EnBase -E -C -h -1 -W -b -Q $Consulta
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd no pudo consultar '$Servidor' (código $LASTEXITCODE). Verificá que la instancia esté levantada y que el nombre sea correcto. $(@($salida | Select-Object -First 3) -join ' ')"
    }
    return (@($salida | Where-Object { $_ -match '\S' }) | Select-Object -First 1)
}

# ── 2) La base no puede existir (solo lee sys.databases) ─────────────────────────────────────────────────────────────
Write-Host "1/4 Comprobando que '$Base' no exista en '$Servidor'..."
$existe = "$(Invoke-Consulta -EnBase 'master' -Consulta "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$Base') IS NULL THEN 'NO' ELSE 'SI' END")".Trim()
switch ($existe) {
    'NO' { }
    'SI' { throw "La base '$Base' ya existe en '$Servidor'. No la toco. Para empezar de cero, borrala vos (ver docs\DESARROLLO_LOCAL.md) o elegí otro nombre con -Base." }
    default { throw "No pude comprobar si '$Base' existe (respuesta inesperada: '$existe'). No hago nada." }
}

# ── 3) Copia temporal del script de esquema con el nombre de base reemplazado ───────────────────────────────────────
# El original dice [yosoy] en tres lugares (IF DB_ID, CREATE DATABASE y USE) y no se parametriza: corrido tal cual en una
# instancia donde 'yosoy' ya existe, apuntaría a la base de desarrollo. Por eso se trabaja sobre una copia.
$copia = Join-Path ([System.IO.Path]::GetTempPath()) ("01-produccion-$Base-" + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.sql')
try {
    Write-Host '2/4 Armando la copia temporal del script de esquema...'
    $texto = [System.IO.File]::ReadAllText($origen, [System.Text.Encoding]::UTF8)
    $texto = $texto.Replace("N'yosoy'", "N'$Base'").Replace('[yosoy]', "[$Base]")      # .Replace distingue mayúsculas
    if ($texto -match '(?i)\bUSE\s+\[yosoy\]|DB_ID\(N''yosoy''\)|DATABASE\s+\[yosoy\]') {
        throw 'Quedó una referencia a la base yosoy en la copia del script. No sigo.'
    }
    foreach ($esperado in @("CREATE DATABASE [$Base]", "USE [$Base]", "DB_ID(N'$Base')")) {
        if (-not $texto.Contains($esperado)) {
            throw "El script de esquema ya no contiene '$esperado': cambió su estructura y hay que actualizar este script."
        }
    }
    [System.IO.File]::WriteAllText($copia, $texto, (New-Object System.Text.UTF8Encoding($false)))

    # ── 4) Crear la base y las tablas ────────────────────────────────────────────────────────────────────────────────
    Write-Host "3/4 Creando la base '$Base' y sus tablas (unos segundos)..."
    $salida = & sqlcmd -S $Servidor -d master -E -C -I -b -f 65001 -i $copia
    if ($LASTEXITCODE -ne 0) {
        $cola = @($salida | Select-Object -Last 12) -join [Environment]::NewLine
        throw "sqlcmd terminó con error (código $LASTEXITCODE). Últimas líneas:$([Environment]::NewLine)$cola$([Environment]::NewLine)La base '$Base' pudo quedar a medias: borrala (ver docs\DESARROLLO_LOCAL.md) antes de repetir."
    }
}
finally {
    if (Test-Path -LiteralPath $copia) { Remove-Item -LiteralPath $copia -Force }
}

# ── 5) Verificar: tiene que tener 44 tablas ──────────────────────────────────────────────────────────────────────────
Write-Host '4/4 Verificando el esquema...'
$cuenta = "$(Invoke-Consulta -EnBase $Base -Consulta 'SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables')".Trim()
if ($cuenta -notmatch '^\d+$') { throw "No pude contar las tablas de '$Base' (respuesta: '$cuenta')." }
$tablas = [int]$cuenta
if ($tablas -ne $TablasEsperadas) {
    throw "La base '$Base' quedó con $tablas tablas y se esperaban $TablasEsperadas. La dejo para que la mires; borrala antes de repetir (ver docs\DESARROLLO_LOCAL.md)."
}

Write-Host "Listo: '$Base' en '$Servidor', con $tablas tablas y sin datos propios (el esquema solo siembra el tipo de consentimiento y el contenido de arranque del chat)." -ForegroundColor Green
Write-Host "Correr los tests contra ella:  .\Scripts\correr-tests.ps1 -Servidor '$Servidor' -Base $Base"
