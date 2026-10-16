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

Para que la app arranque con todo vacío alcanza con `Jwt:Key` y, para tocar la base, `ConnectionStrings__YoSoy`.

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
