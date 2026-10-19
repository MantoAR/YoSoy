# Desarrollo local

Lo que cada máquina de desarrollo tiene que configurar y que **no se versiona**. Ningún valor real se escribe en un
archivo del repositorio: `appsettings.json` trae vacías las credenciales de correo (`Email:From` y `Email:Password`), y el
resto de los secretos (la clave de login, los de video y WhatsApp, la cadena de conexión) ni siquiera figuran ahí.

## Dónde van los secretos

En desarrollo se guardan con [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets): el
proyecto ya tiene su `UserSecretsId`, los valores quedan en tu perfil de usuario (fuera del repositorio) y la app los
lee sola. Se corren parados en la carpeta del proyecto (donde está `YoSoy.csproj`) y se leen cuando
`ASPNETCORE_ENVIRONMENT` es `Development` (lo que hace `dotnet run` con el perfil por defecto). En cualquier otro entorno
se usan variables de entorno: el mismo nombre con doble guion bajo (`__`) en lugar de los dos puntos.

Una variable de entorno gana sobre un secreto de usuario, que a su vez gana sobre `appsettings.json`.

`dotnet user-secrets list` muestra los valores **en claro**: no lo pegues en un chat, un ticket ni un commit.

## Todos los secretos

| Clave | En desarrollo | Variable de entorno (producción) | Hace falta | Si falta |
|---|---|---|---|---|
| `ConnectionStrings:YoSoy` | Solo variable de entorno: `Conexion.cs` la lee directo del entorno y un secreto de usuario no sirve | `ConnectionStrings__YoSoy` | Sí, para usar la base | La app arranca; falla al usar la base y el error dice qué variable falta |
| `Jwt:Key` | `dotnet user-secrets set "Jwt:Key" "<32 caracteres o más, al azar>"` | `Jwt__Key` | **Sí** | **La app no arranca** (a propósito: sin clave no hay con qué firmar un login) y el error explica este paso |
| `Email:From` | `dotnet user-secrets set "Email:From" "<cuenta-de-correo>"` | `Email__From` | Solo para mails reales | Arranca; al mandar un mail avisa `Correo no configurado` |
| `Email:Password` | `dotnet user-secrets set "Email:Password" "<contraseña-de-aplicación>"` | `Email__Password` | Solo para mails reales | Arranca; al mandar un mail avisa `Correo no configurado` |
| `Video:SignalingSecreto` | `dotnet user-secrets set "Video:SignalingSecreto" "<32 caracteres o más>"` | `Video__SignalingSecreto` | Solo para videollamadas | Arranca; `POST /video/acceso` contesta 503 y el log dice el motivo |
| `Video:TurnSecreto` | `dotnet user-secrets set "Video:TurnSecreto" "<32 caracteres o más>"` | `Video__TurnSecreto` | Solo para videollamadas que necesiten TURN | Arranca; la API entrega solo servidores STUN y el log lo avisa |
| `Video:Emociones:ClaveCifrado` | `dotnet user-secrets set "Video:Emociones:ClaveCifrado" "<32 bytes en base64>"` | `Video__Emociones__ClaveCifrado` | Solo para la lectura de emociones | Arranca; la lectura de emociones queda desactivada y el log dice por qué |
| `Mensajeria:WhatsApp:Secreto` | `dotnet user-secrets set "Mensajeria:WhatsApp:Secreto" "<secreto compartido>"` | `Mensajeria__WhatsApp__Secreto` | Solo para WhatsApp | Arranca; el webhook entrante contesta 404 a todos |

Reglas de los valores (el código las valida, y ningún mensaje ni log incluye el valor):

- Los dos secretos de video tienen que tener **32 caracteres o más** y ser **distintos de `Jwt:Key`** y entre sí. Se pueden
  generar con `openssl rand -hex 32`.
- `Video:Emociones:ClaveCifrado` es una clave AES-256: **32 bytes en base64** (`openssl rand -base64 32`), propia, distinta
  de cualquier otro secreto.
- `Video:SignalingSecreto` tiene que valer lo mismo que `SIGNALING_SECRETO` del servidor de señalización, y
  `Mensajeria:WhatsApp:Secreto` lo mismo que `Mensajeria__WhatsApp__Secreto` del servicio de WhatsApp: son otros procesos
  (Node), cada uno con su variable de entorno.

Para que la app arranque con todo vacío alcanza con `Jwt:Key` y, para tocar la base, `ConnectionStrings__YoSoy`. Para
correr los tests de integración hace falta además una base propia: ver
[Correr los tests de integración](#correr-los-tests-de-integración).

## Correo

| Clave | Variable de entorno equivalente | Para qué | Hace falta |
|---|---|---|---|
| `Email:From` | `Email__From` | La cuenta de correo desde la que se manda. **También es el usuario** con el que se autentica en el SMTP. | Solo para mails reales |
| `Email:Password` | `Email__Password` | La contraseña de esa cuenta (en Gmail, una *contraseña de aplicación*, no la de la cuenta). | Solo para mails reales |
| `Email:Host`, `Email:Port` | `Email__Host`, `Email__Port` | Servidor SMTP y puerto. No son secretos: ya vienen en `appsettings.json`. | No |
| `Email:AppUrl` | `Email__AppUrl` | Base de los links que llevan los mails (recuperación de contraseña). En `appsettings.json` apunta a la máquina local. | No |

Configurarlas, una vez por máquina:

```bash
dotnet user-secrets set "Email:From" "<cuenta-de-correo>"
dotnet user-secrets set "Email:Password" "<contraseña-de-aplicación>"
```

### Con y sin credenciales

- **Sin credenciales (el estado por defecto):** la app arranca y anda normal; solo los mails no salen. Cada vez que
  intenta mandar uno (recuperación de contraseña, recordatorios y avisos de turno) deja en el log un aviso como
  `Correo no configurado: no se envió un aviso de turno. Falta Email:From, Email:Password. ...`, sin abrir ninguna
  conexión. Los avisos por mail de los recordatorios quedan registrados como `Omitido`.
- **Con `Email:Host`, `Email:From` y `Email:Password`:** los mails salen **de verdad**. Usalo solo cuando lo necesites
  y con destinatarios de prueba.
- **Para apagar el correo aunque tengas los secretos guardados:** una variable de entorno gana sobre el secreto, así que
  alcanza con `Email__Password` vacía o con un espacio. En PowerShell: `$env:Email__Password = " "; dotnet run` (un espacio
  cuenta como vacío).

Para comprobar cómo quedó, pedí un "Olvidé mi contraseña" desde la pantalla de ingreso con un usuario que tenga mail
cargado y mirá la consola: si dice `Correo no configurado`, falta algo (el aviso nombra qué).

## Si una credencial se expuso

Borrarla del archivo no alcanza: queda en el historial de git. Hay que **revocarla en el proveedor** (en Gmail, revocar la
contraseña de aplicación y crear otra) y cargar la nueva solo como secreto de usuario o variable de entorno. Para
producción, ver [`DEPLOY.md`](DEPLOY.md).

## Correr los tests de integración

La mayoría de los tests de `YoSoy.Tests` no necesitan nada. Los de categoría `Integracion` (la API real contra SQL Server)
necesitan una base propia, y ninguno la crea. Sin ella fallan más de cien tests, con `Falta la variable de entorno
ConnectionStrings__YoSoy` o con `Cannot open database "..." requested by the login`.

> **Nunca apuntes los tests a una base con datos reales ni a producción.** Los tests de integración **escriben y borran filas**
> en la base a la que apuntan. Usá siempre una base de pruebas propia (`yosoy_test`), nunca `yosoy` (la de desarrollo) ni una
> copia de datos reales.

### Qué necesitan

- Una **base SQL Server local, nueva y sin datos propios**, con el esquema de `docs/produccion/01-produccion.sql`: 45 tablas, con
  el resultado de las 17 migraciones de `migrations/` (no se corren aparte). Autenticación de Windows. El esquema solo siembra
  el tipo de consentimiento y el contenido de arranque del chat.
- **Nada más sembrado:** cada test crea las filas que necesita (`YoSoy.Tests/DatosDePrueba.cs`) y las borra al terminar. No hace
  falta `migrations/seed_catalogos.sql`: ese es para servidores reales (ver [`DEPLOY.md`](DEPLOY.md)).
- La cadena de conexión en la variable de entorno **`ConnectionStrings__YoSoy`**, en el proceso que corre `dotnet test`.
  **Un secreto de usuario no sirve para esto:** `Conexion.cs` lee la cadena directo del entorno (primera fila de la tabla de
  secretos de arriba).

### Crear la base de pruebas (una vez)

```powershell
.\Scripts\crear-base-de-pruebas.ps1                    # crea yosoy_test en la instancia local .\SQLEXPRESS
.\Scripts\crear-base-de-pruebas.ps1 -Servidor 'MIPC\SQLEXPRESS' -Base yosoy_test2     # otra instancia u otro nombre
```

El script arma una copia temporal de `01-produccion.sql` con el nombre de la base cambiado, la ejecuta con `sqlcmd` (autenticación
de Windows: no hay contraseñas que guardar ni imprimir) y comprueba que la base tenga 45 tablas. El original no se toca: dice
`[yosoy]` fijo en tres lugares y, corrido tal cual en una instancia donde `yosoy` ya existe, apuntaría a la base de desarrollo.
Se niega a usar la base `yosoy` y a seguir si la base ya existe. Si PowerShell no te deja ejecutar scripts:
`powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\crear-base-de-pruebas.ps1`.

**Si ya tenías `yosoy_test` de antes de la migración 17** (los tests marcados `Migracion=17` y el chat para el personal necesitan sus columnas), aplicale la migración: es idempotente, solo agrega y no toca
ningún dato (también se puede borrar la base y crearla de nuevo, abajo):

```powershell
sqlcmd -S .\SQLEXPRESS -d yosoy_test -E -C -I -f 65001 -b -i .\migrations\add_chat_personal_entrantes.sql
```

### Probar contra una base sin la migración 17

Producción tampoco tiene la migración 17 (las columnas `MensajeChat.IdExterno`, `MensajeChat.Tipo` y `Conversacion.NombreContacto`). Con `Mensajeria:ModoAtencion=Asistente` (la de por defecto) la API no las
toca; para comprobarlo con la suite entera, armá una base **sin** ellas y corré todo lo que no sea de la migración misma (los tests marcados `[Trait("Migracion", "17")]`):

```powershell
.\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_16 -SinMigracion17                         # 45 tablas, sin las columnas de la 17
.\Scripts\correr-tests.ps1 -Base yosoy_test_16 -Filtro 'Migracion!=17'

# lo más parecido a producción hoy: sin la 16 (44 tablas) y sin la 17
.\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_prod -SinMigracion17 -SinConsentimientoWhatsApp
.\Scripts\correr-tests.ps1 -Base yosoy_test_prod -Filtro 'Migracion!=17&Tabla!=ConsentimientoWhatsApp'
```

Tienen que pasar todos. En esas bases el modo `Personal` también anda (guarda los mensajes sin id, tipo ni nombre y el log avisa que falta la migración). Son bases más de pruebas: borralas igual que `yosoy_test` (abajo).

### Probar contra una base sin la tabla de la migración 16

Producción tiene hoy 44 tablas (sin `ConsentimientoWhatsApp`, la de la migración 16). Con la llave `Mensajeria:AvisosPorWhatsApp` apagada (la de por defecto) la API no toca esa tabla; para comprobarlo
con la suite entera, armá una base **sin** ella y corré todo lo que no sea de la tabla misma (los tests marcados `[Trait("Tabla", "ConsentimientoWhatsApp")]`):

```powershell
.\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_44 -SinConsentimientoWhatsApp          # 44 tablas
.\Scripts\correr-tests.ps1 -Base yosoy_test_44 -Filtro 'Tabla!=ConsentimientoWhatsApp'
```

Tienen que pasar todos. (Con `-SinConsentimientoWhatsApp` el script crea la base como siempre y borra esa tabla; solo toca la base que él mismo acaba de crear.) Es una base más de pruebas: borrala igual que `yosoy_test` (abajo).

### Correr los tests

```powershell
.\Scripts\correr-tests.ps1                                    # todos, contra yosoy_test
.\Scripts\correr-tests.ps1 -Filtro 'Categoria!=Integracion'   # solo los que no necesitan base (no hace falta crearla)
.\Scripts\correr-tests.ps1 -Base yosoy_test2 -Filtro 'FullyQualifiedName~Turno'
```

El script define `ConnectionStrings__YoSoy` y `Email__Password` con un espacio (así ningún test manda mails reales, aunque tengas
credenciales en tus user-secrets), corre `dotnet test` y, al terminar, deja las dos variables como estaban. Se niega a usar la
base `yosoy`. La suite completa tarda unos 2 minutos. Los argumentos que sobren se le pasan a `dotnet test`
(por ejemplo `--logger trx`). Hecho a mano sería:

```powershell
$env:ConnectionStrings__YoSoy = 'Server=.\SQLEXPRESS;Database=yosoy_test;Trusted_Connection=True;TrustServerCertificate=True;'
$env:Email__Password = ' '
dotnet test YoSoy.Tests\YoSoy.Tests.csproj
```

Esas variables quedan en esa ventana de PowerShell: cerrala o devolvelas a como estaban, para que la API no siga apuntando a la
base de pruebas.

### Borrar la base de pruebas

```powershell
sqlcmd -S .\SQLEXPRESS -d master -E -C -Q "ALTER DATABASE [yosoy_test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [yosoy_test];"
```

Fijate bien en el nombre: borra la base entera. Los tests limpian lo que crean; lo único que puede quedar es alguna fila de
catálogo (por ejemplo un `EstadoTurno`), que no molesta y que la suite reutiliza. Si querés una base limpia, borrala y volvé a
crearla.

### Si falla

| Síntoma | Causa |
|---|---|
| `Falta la variable de entorno ConnectionStrings__YoSoy` | La variable no está en el proceso que corre `dotnet test` (un secreto de usuario no cuenta). |
| `Cannot open database "..." requested by the login` | La base no existe o el nombre de la cadena no coincide: creala con el script. |
| `No encuentro sqlcmd` | Instalá las herramientas de línea de comandos de SQL Server (o SSMS). |
| "La ejecución de scripts está deshabilitada en este sistema" | Corré el script con `powershell -NoProfile -ExecutionPolicy Bypass -File ...` |
| El script dice que la base ya existe | Es a propósito: nunca toca una base existente. Borrala (arriba) o elegí otro `-Base`. |

## Probar el chat de WhatsApp para el personal en tu PC

Para ver el chat de punta a punta con tu teléfono de prueba: el paciente le escribe al número dedicado y el **Configurador** (que hace de recepción) lo ve y contesta desde CRM → Chats. La
API y `whatsapp-service` corren en tu máquina, contra una base de pruebas (`yosoy_test`). **Nada de esto toca `yosoy` ni el servidor.**

- **No uses tu número personal** para el número dedicado: Baileys es un cliente no oficial de WhatsApp y un uso brusco puede hacer que bloqueen el número. Por eso los topes bajos de abajo.
- Hace falta **Node 22 o más**, **.NET** y **dos ventanas de PowerShell**. Las variables `$env:` viven solo en la ventana donde las definís: cada ventana necesita las suyas.
- La carpeta de la sesión de WhatsApp y el secreto compartido van **fuera del repositorio** (`%LOCALAPPDATA%\YoSoy\whatsapp`), y no dentro de Google Drive, OneDrive ni Dropbox: la sesión es una credencial
  de la cuenta. El servicio se niega a usar una carpeta dentro del repo o de la app.
- El QR lo escaneás vos, con el teléfono del número dedicado: nada ni nadie más lo hace.

### Una sola vez

```powershell
# 1) Comprobaciones (solo lectura)
node --version                                   # 22 o más
dotnet --version
cd D:\Claude\YoSoy\YoSoy\whatsapp-service
npm ls --omit=dev --depth=0                      # tiene que listar @whiskeysockets/baileys
# Solo si faltaran las dependencias:  npm ci --omit=dev

# 2) Secreto compartido entre la API y el servicio: al azar, se guarda en un archivo y NO se imprime
$base = Join-Path $env:LOCALAPPDATA 'YoSoy\whatsapp'
New-Item -ItemType Directory -Force $base | Out-Null
$b = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
(($b | ForEach-Object { $_.ToString('x2') }) -join '') | Set-Content (Join-Path $base 'secreto.txt') -NoNewline -Encoding ascii
```

**Crear un usuario Configurador en `yosoy_test`** (una base nueva no tiene usuarios; el script del repo apunta a `[yosoy]`, así que se usa una copia con el nombre de la base cambiado):

```powershell
cd D:\Claude\YoSoy\YoSoy
$sql = (Get-Content .\docs\produccion\03-usuario-admin.sql -Raw -Encoding UTF8).Replace('USE [yosoy];', 'USE [yosoy_test];')
if ($sql -notmatch 'USE \[yosoy_test\];') { throw 'No se pudo cambiar el nombre de la base en el script.' }
$tmp = Join-Path $env:TEMP 'usuario-admin-yosoy_test.sql'
[IO.File]::WriteAllText($tmp, $sql, (New-Object Text.UTF8Encoding($true)))
sqlcmd -S .\SQLEXPRESS -d yosoy_test -E -C -f 65001 -v AdminPassword="UnaClaveSoloParaPruebas1" -i $tmp
Remove-Item $tmp
```

El usuario es `admin`. La contraseña tiene que ser **ASCII puro y sin comillas simples** (cambiala por la que quieras: es la clave de una base de pruebas local). El script es idempotente: volver a correrlo
solo actualiza la contraseña. Si preferís no mezclar tus pruebas manuales (con números reales) con las de la suite, creá otra base con `.\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_wa` y usala en la
cadena de conexión de abajo.

### Cada vez: dos ventanas

**Ventana A, la API** (queda en `http://localhost:5000`):

```powershell
cd D:\Claude\YoSoy\YoSoy
$env:ConnectionStrings__YoSoy = 'Server=.\SQLEXPRESS;Database=yosoy_test;Trusted_Connection=True;TrustServerCertificate=True;'
$env:Email__Password = ' '                       # un espacio: no sale ningún mail
$env:Mensajeria__CanalActivo = 'Baileys'
$env:Mensajeria__ModoAtencion = 'Personal'       # el asistente no contesta: lo atiende el Configurador
$env:Mensajeria__WhatsApp__Secreto = (Get-Content "$env:LOCALAPPDATA\YoSoy\whatsapp\secreto.txt" -Raw).Trim()
dotnet run
```

Si la API no arranca por falta de `Jwt:Key`: `dotnet user-secrets set "Jwt:Key" "<32 caracteres o más, al azar>"` (parado en `D:\Claude\YoSoy\YoSoy`).

**Ventana B, `whatsapp-service`** (queda en `127.0.0.1:3002`; no cambies `WHATSAPP_SERVICE_PORT`: la API tiene el 3002 fijo):

```powershell
cd D:\Claude\YoSoy\YoSoy\whatsapp-service
$env:API_BASE_URL = 'http://127.0.0.1:5000'
$env:Mensajeria__WhatsApp__Secreto = (Get-Content "$env:LOCALAPPDATA\YoSoy\whatsapp\secreto.txt" -Raw).Trim()
$env:WHATSAPP_BAILEYS_SESSION_PATH = "$env:LOCALAPPDATA\YoSoy\whatsapp\sesion-baileys"
$env:WHATSAPP_TOPE_DIARIO = '30'                 # número nuevo: empezar bajo
node index.js
```

Levantá primero la API: si el servicio recibe un mensaje antes de que la API esté arriba, ese mensaje se pierde. En el log de la ventana B tenés que ver, en este orden, `Carpeta de la sesión de WhatsApp lista`,
`whatsapp-service escuchando en 127.0.0.1:3002 (Baileys)`, `estado iniciando` y después `hay un código QR para vincular el número`.

### Vincular el número dedicado (lo hacés vos)

1. Abrí `http://localhost:5000/pantallas/index.html` e ingresá con `admin` y la contraseña del paso de arriba.
2. En el menú, **WhatsApp**: con el servicio corriendo y sin sesión aparece el QR (se renueva solo; si no sirve, "¿El código no funciona? Generar uno nuevo").
3. En el teléfono del número dedicado: WhatsApp, **Ajustes, Dispositivos vinculados, Vincular un dispositivo**, y escaneá el código. La pantalla pasa a "Conectado" y, desde ahí, reiniciar el
   servicio **no vuelve a pedir el QR**: la sesión queda en la carpeta.

Alternativa por la terminal, con el servicio **parado** (en la ventana B, con `WHATSAPP_BAILEYS_SESSION_PATH` definida como arriba): `chcp 65001` y `npm run vincular`.

### Probar

```powershell
curl.exe -s http://localhost:5000/capacidades    # {"mensajeria":true,"modoAtencion":"personal"}
curl.exe -s http://127.0.0.1:3002/salud          # 200 si está conectado; 503 si falta vincular (sin teléfonos ni textos)
```

1. Desde **otro** teléfono, escribile al número dedicado. **No tiene que llegar ninguna respuesta automática.**
2. En el navegador, ingresado como `admin`: **CRM → Chats**. La conversación aparece como **"Para atender"**, con un aviso en la campanita ("Escribió una persona por WhatsApp").
3. Elegí la conversación y respondé desde el composer: tiene que llegar al otro teléfono. Cerrarla y reabrirla no le manda ningún texto.
4. Un profesional (ingresando con su usuario) **no ve el botón Chats**, y el servidor le contesta 403 en `/mensajeria`.

5. Mandá una **foto**, un **audio** y un texto: aparecen en el hilo como `[Imagen]` (con la leyenda, si la pusiste) y `[Audio]`, con el aviso "El archivo no se guarda en YoSoy: mirá el teléfono del número", y la
   conversación muestra **"Contacto: <el nombre de tu perfil de WhatsApp>"** (hace falta la migración 17 en la base: si no, el chat anda igual pero sin tipo ni nombre y el log de la API lo avisa).
6. Si la API estaba apagada cuando escribiste, el mensaje se reenvía solo en cuanto vuelve (hasta unos 2 minutos y medio; después se pierde y el log de `whatsapp-service` lo dice). `curl.exe -s http://127.0.0.1:3002/salud`
   muestra en `entrantes` cuántos se reenviaron, reintentaron, fallaron o se perdieron (solo números).

Lo que todavía **no** pasa (llega con las ramas siguientes del chat para el personal): la conversación sale "sin identificar" aunque el teléfono sea de un paciente (no hay vínculo con el paciente todavía). Si respondés desde el teléfono
del número, **eso no queda en el sistema**: respondé siempre desde YoSoy.

### Parar y empezar de cero

- **Parar:** `Ctrl+C` en cada ventana (el servicio cierra ordenado sin cerrar la sesión de WhatsApp).
- **Olvidar la sesión** (y el registro de quién le escribió al número, que vive en la misma carpeta), con el servicio parado:

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\YoSoy\whatsapp\sesion-baileys"
```

Después, en el teléfono: Dispositivos vinculados, y cerrá esa sesión.

### Si algo falla

| Síntoma | Causa probable |
|---|---|
| `Falta API_BASE_URL` / `Falta Mensajeria__WhatsApp__Secreto` / `Falta WHATSAPP_BAILEYS_SESSION_PATH` | La variable no está definida **en esa ventana** |
| `La carpeta de la sesión ... está dentro de un repositorio git` | La ruta quedó dentro de `D:\Claude\YoSoy\YoSoy` (incluida `whatsapp-service`) |
| El Configurador dice "Servicio no disponible" | El servicio no corre, o los secretos de las dos ventanas no coinciden (la API recibe 401), o cambiaste el puerto |
| `/salud` da 503 `requiere_vincular` | Falta escanear el QR (o venció: pedir otro) |
| No aparece el botón Chats ni el grupo CRM | `/capacidades` da `"mensajeria":false`: `ChatSimuladoHabilitado` apagado, o el pedido no es local (proxy); o entraste con un usuario que no es Configurador |
| El mensaje llega al teléfono del número pero no a Chats | Mirá la ventana A y el log de la B: `La API respondió 404` es un secreto distinto |
| `npm : No se puede cargar el archivo npm.ps1` | Usá `npm.cmd` (por ejemplo `npm.cmd run vincular`) |
