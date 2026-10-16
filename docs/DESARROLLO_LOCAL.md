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

- Una **base SQL Server local, nueva y sin datos propios**, con el esquema de `docs/produccion/01-produccion.sql`: 44 tablas, con
  el resultado de las 15 migraciones de `migrations/` (no se corren aparte). Autenticación de Windows. El esquema solo siembra
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
de Windows: no hay contraseñas que guardar ni imprimir) y comprueba que la base tenga 44 tablas. El original no se toca: dice
`[yosoy]` fijo en tres lugares y, corrido tal cual en una instancia donde `yosoy` ya existe, apuntaría a la base de desarrollo.
Se niega a usar la base `yosoy` y a seguir si la base ya existe. Si PowerShell no te deja ejecutar scripts:
`powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\crear-base-de-pruebas.ps1`.

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

Fijate bien en el nombre: borra la base entera. Los tests limpian lo que crean, pero algunos dejan filas sueltas de prueba
(marcadas `[TEST-PR6]`); no molestan, y la suite se puede repetir sobre la misma base. Si querés una base limpia, borrala y
volvé a crearla.

### Si falla

| Síntoma | Causa |
|---|---|
| `Falta la variable de entorno ConnectionStrings__YoSoy` | La variable no está en el proceso que corre `dotnet test` (un secreto de usuario no cuenta). |
| `Cannot open database "..." requested by the login` | La base no existe o el nombre de la cadena no coincide: creala con el script. |
| `No encuentro sqlcmd` | Instalá las herramientas de línea de comandos de SQL Server (o SSMS). |
| "La ejecución de scripts está deshabilitada en este sistema" | Corré el script con `powershell -NoProfile -ExecutionPolicy Bypass -File ...` |
| El script dice que la base ya existe | Es a propósito: nunca toca una base existente. Borrala (arriba) o elegí otro `-Base`. |
