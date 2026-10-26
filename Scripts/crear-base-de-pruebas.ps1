<#
.SYNOPSIS
    Crea una base SQL Server NUEVA y vacía con el esquema de YoSoy, para correr los tests de integración.

.DESCRIPTION
    Arma una copia temporal de docs\produccion\01-produccion.sql con el nombre de la base reemplazado (el original no se
    toca), la ejecuta con sqlcmd contra la instancia indicada y comprueba que la base quede con 57 tablas (56 con
    -SinConsentimientoWhatsApp).

    Se niega a:
      - usar la base "yosoy" (la de desarrollo, con datos reales);
      - seguir si la base ya existe (nunca pisa ni completa una base existente).

    El esquema ya trae el resultado de las migraciones 1 a 25 de migrations\. La base queda sin datos propios (el esquema
    solo siembra el tipo de consentimiento y el contenido de arranque del chat) y así tiene que estar: los tests crean y
    borran sus filas, no hace falta migrations\seed_catalogos.sql.

    Usa la autenticación de Windows (sqlcmd -E): no hay usuario ni contraseña, así que no se guarda ni se imprime ningún
    secreto.

.PARAMETER Servidor
    Instancia de SQL Server. Por defecto, la instancia local .\SQLEXPRESS

.PARAMETER Base
    Nombre de la base a crear. Por defecto, yosoy_test. Letras, números y guion bajo; no puede ser "yosoy".

.PARAMETER SinConsentimientoWhatsApp
    Después de armar el esquema borra la tabla ConsentimientoWhatsApp (la de la migración 16): la base queda con 56 tablas,
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

.PARAMETER SinMigracion25
    Después de armar el esquema deshace la migración 25 (el interruptor de IA por conversación de Chats v2): borra la restricción por defecto y la columna
    Conversacion.IaActiva y la fila 25 de VersionEsquema. La base queda como producción mientras no se aplique esa migración (con -SinConsentimientoWhatsApp es la
    FORMA DE PRODUCCIÓN: 54 tablas, versión 24). Sirve para ensayar migrations\add_conversacion_ia.sql y su deshacer, y para comprobar que la API anda sin la columna:
        .\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_prod -SinConsentimientoWhatsApp -SinMigracion25
        .\Scripts\correr-tests.ps1 -Base yosoy_test_prod -Filtro 'Migracion!=25&Tabla!=ConsentimientoWhatsApp'
    Solo toca la base que este mismo comando acaba de crear.

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
    [switch]$SinMigracion17,
    [switch]$SinMigracion25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TablasEsperadas = if ($SinConsentimientoWhatsApp) { 56 } else { 57 }

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
    if ($LASTEXITCODE -ne 0) { throw "No pude borrar la tabla ConsentimientoWhatsApp de '$Base' (código $LASTEXITCODE). La base quedó con 57 tablas: borrala antes de repetir." }
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

# ── 4d) Sin la migración 25 (solo en la base que acabamos de crear) ──────────────────────────────────────────────────
# El orden es el de docs\produccion\deshacer-migracion-25.sql: la restricción por defecto, la columna y la fila de la versión.
if ($SinMigracion25) {
    Write-Host "3d/4 Deshaciendo la migración 25 en '$Base' (la base queda como producción sin ella)..."
    $deshacer25 = 'ALTER TABLE dbo.Conversacion DROP CONSTRAINT DF_Conversacion_IaActiva; ALTER TABLE dbo.Conversacion DROP COLUMN IaActiva; DELETE FROM dbo.VersionEsquema WHERE Numero = 25;'
    & sqlcmd -S $Servidor -d $Base -E -C -I -b -Q $deshacer25 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No pude deshacer la migración 25 en '$Base' (código $LASTEXITCODE). La base quedó con la columna nueva: borrala antes de repetir." }
}

# ── 5) Verificar: tiene que tener 57 tablas (y las columnas de la migración 17, salvo con -SinMigracion17) ───────────────
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

$columna25 = "$(Invoke-Consulta -EnBase $Base -Consulta "SET NOCOUNT ON; SELECT CASE WHEN COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NULL THEN 0 ELSE 1 END")".Trim()
$columna25Esperada = if ($SinMigracion25) { 0 } else { 1 }
if ($columna25 -ne "$columna25Esperada") {
    throw "La base '$Base' tiene Conversacion.IaActiva = $columna25 (1 = existe) y se esperaba $columna25Esperada. La dejo para que la mires; borrala antes de repetir (ver docs\DESARROLLO_LOCAL.md)."
}

$detalle = if ($SinMigracion17) { ' y sin las columnas de la migración 17' } else { '' }
if ($SinMigracion25) { $detalle += ' y sin la migración 25' }
Write-Host "Listo: '$Base' en '$Servidor', con $tablas tablas$detalle y sin datos propios (el esquema solo siembra el tipo de consentimiento y el contenido de arranque del chat)." -ForegroundColor Green
Write-Host "Correr los tests contra ella:  .\Scripts\correr-tests.ps1 -Servidor '$Servidor' -Base $Base"
