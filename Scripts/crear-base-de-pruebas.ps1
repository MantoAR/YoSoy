<#
.SYNOPSIS
    Crea una base SQL Server NUEVA y vacía con el esquema de YoSoy, para correr los tests de integración.

.DESCRIPTION
    Arma una copia temporal de docs\produccion\01-produccion.sql con el nombre de la base reemplazado (el original no se
    toca), la ejecuta con sqlcmd contra la instancia indicada y comprueba que la base quede con 47 tablas (46 con
    -SinConsentimientoWhatsApp).

    Se niega a:
      - usar la base "yosoy" (la de desarrollo, con datos reales);
      - seguir si la base ya existe (nunca pisa ni completa una base existente).

    El esquema ya trae el resultado de las 17 migraciones de migrations\. La base queda sin datos propios (el esquema
    solo siembra el tipo de consentimiento y el contenido de arranque del chat) y así tiene que estar: los tests crean y
    borran sus filas, no hace falta migrations\seed_catalogos.sql.

    Usa la autenticación de Windows (sqlcmd -E): no hay usuario ni contraseña, así que no se guarda ni se imprime ningún
    secreto.

.PARAMETER Servidor
    Instancia de SQL Server. Por defecto, la instancia local .\SQLEXPRESS

.PARAMETER Base
    Nombre de la base a crear. Por defecto, yosoy_test. Letras, números y guion bajo; no puede ser "yosoy".

.PARAMETER SinConsentimientoWhatsApp
    Después de armar el esquema borra la tabla ConsentimientoWhatsApp (la de la migración 16): la base queda con 46 tablas,
    como está producción mientras no se aplique esa migración. Sirve para comprobar que la API anda sin esa tabla con la
    llave Mensajeria:AvisosPorWhatsApp apagada (la de por defecto):
        .\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_44 -SinConsentimientoWhatsApp
        .\Scripts\correr-tests.ps1 -Base yosoy_test_44 -Filtro 'Tabla!=ConsentimientoWhatsApp'
    Solo toca la base que este mismo comando acaba de crear.

.PARAMETER SinMigracion17
    Después de armar el esquema deshace la migración 17 (el chat de WhatsApp para el personal): borra el índice, la restricción y las tres
    columnas que agrega (MensajeChat.IdExterno, MensajeChat.Tipo y Conversacion.NombreContacto). La base queda como producción mientras no
    se aplique esa migración. Sirve para comprobar que la API anda en modo Asistente (el de por defecto) sin esas columnas, y que con el
    modo Personal sin ellas sigue guardando los mensajes y lo avisa en el log:
        .\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_16 -SinMigracion17
        .\Scripts\correr-tests.ps1 -Base yosoy_test_16 -Filtro 'Migracion!=17'
    Se puede combinar con -SinConsentimientoWhatsApp. Solo toca la base que este mismo comando acaba de crear.

.EXAMPLE
    .\Scripts\crear-base-de-pruebas.ps1

.EXAMPLE
    .\Scripts\crear-base-de-pruebas.ps1 -Servidor 'MIPC\SQLEXPRESS' -Base yosoy_test2

.EXAMPLE
    .\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_44 -SinConsentimientoWhatsApp

.EXAMPLE
    .\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_16 -SinMigracion17
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Servidor = '.\SQLEXPRESS',
    [string]$Base = 'yosoy_test',
    [switch]$SinConsentimientoWhatsApp,
    [switch]$SinMigracion17
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TablasEsperadas = if ($SinConsentimientoWhatsApp) { 46 } else { 47 }

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

# ── 4b) Sin la tabla de la migración 16 (solo en la base que acabamos de crear) ──────────────────────────────────────
if ($SinConsentimientoWhatsApp) {
    Write-Host "3b/4 Borrando la tabla ConsentimientoWhatsApp de '$Base' (la base queda como producción sin la migración 16)..."
    & sqlcmd -S $Servidor -d $Base -E -C -I -b -Q 'DROP TABLE dbo.ConsentimientoWhatsApp' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No pude borrar la tabla ConsentimientoWhatsApp de '$Base' (código $LASTEXITCODE). La base quedó con 47 tablas: borrala antes de repetir." }
}

# ── 4c) Sin la migración 17 (solo en la base que acabamos de crear) ──────────────────────────────────────────────────
# El orden es el que dice la propia migración (migrations\add_chat_personal_entrantes.sql, "Deshacer"): primero el índice y las restricciones que
# dependen de las columnas, después las columnas.
if ($SinMigracion17) {
    Write-Host "3c/4 Deshaciendo la migración 17 en '$Base' (la base queda como producción sin ella)..."
    $deshacer = @(
        'DROP INDEX UQ_MensajeChat_IdExterno ON dbo.MensajeChat;',
        'ALTER TABLE dbo.MensajeChat DROP CONSTRAINT CK_MensajeChat_Tipo;',
        'ALTER TABLE dbo.MensajeChat DROP CONSTRAINT DF_MensajeChat_Tipo;',
        'ALTER TABLE dbo.MensajeChat DROP COLUMN IdExterno, Tipo;',
        'ALTER TABLE dbo.Conversacion DROP COLUMN NombreContacto;'
    ) -join ' '
    & sqlcmd -S $Servidor -d $Base -E -C -I -b -Q $deshacer | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No pude deshacer la migración 17 en '$Base' (código $LASTEXITCODE). La base quedó con las columnas nuevas: borrala antes de repetir." }
}

# ── 5) Verificar: tiene que tener 47 tablas (y las columnas de la migración 17, salvo con -SinMigracion17) ───────────────
Write-Host '4/4 Verificando el esquema...'
$cuenta = "$(Invoke-Consulta -EnBase $Base -Consulta 'SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables')".Trim()
if ($cuenta -notmatch '^\d+$') { throw "No pude contar las tablas de '$Base' (respuesta: '$cuenta')." }
$tablas = [int]$cuenta
if ($tablas -ne $TablasEsperadas) {
    throw "La base '$Base' quedó con $tablas tablas y se esperaban $TablasEsperadas. La dejo para que la mires; borrala antes de repetir (ver docs\DESARROLLO_LOCAL.md)."
}
$columnas17 = "$(Invoke-Consulta -EnBase $Base -Consulta "SET NOCOUNT ON; SELECT (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MensajeChat') AND name IN ('IdExterno', 'Tipo')) + (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Conversacion') AND name = 'NombreContacto')")".Trim()
$columnasEsperadas = if ($SinMigracion17) { 0 } else { 3 }
if ($columnas17 -ne "$columnasEsperadas") {
    throw "La base '$Base' quedó con $columnas17 de las 3 columnas de la migración 17 y se esperaban $columnasEsperadas. La dejo para que la mires; borrala antes de repetir (ver docs\DESARROLLO_LOCAL.md)."
}

$detalle = if ($SinMigracion17) { ' y sin las columnas de la migración 17' } else { '' }
Write-Host "Listo: '$Base' en '$Servidor', con $tablas tablas$detalle y sin datos propios (el esquema solo siembra el tipo de consentimiento y el contenido de arranque del chat)." -ForegroundColor Green
Write-Host "Correr los tests contra ella:  .\Scripts\correr-tests.ps1 -Servidor '$Servidor' -Base $Base"
