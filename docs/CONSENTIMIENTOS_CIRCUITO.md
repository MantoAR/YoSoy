# Consentimientos dentro del circuito (octubre 2026)

Qué se exige y cuándo, para que el consentimiento deje de ser un trámite suelto y pase a ser parte del recorrido del paciente. **Migración 28** (opcional): [`migrations/add_consentimientos_circuito.sql`](../migrations/add_consentimientos_circuito.sql).

> **Texto legal provisorio.** Ningún texto de acá lo validó el equipo legal ni los psicólogos. Las pantallas muestran la marca **«Texto provisorio, pendiente de validación legal»** (configurable: `Consentimiento:MarcaProvisoria`; para quitarla, dejarla vacía).

## Los tres consentimientos (no se mezclan)

| | Cuándo se firma | Quién | Se exige para | Código |
|---|---|---|---|---|
| **Tratamiento y telemedicina** | Una vez (se vuelve a firmar si cambia la versión del texto) | El paciente; si es menor, su tutor | **Asignar profesional** y confirmar un turno (RN-02) | `Services/ConsentimientoTratamiento.cs` |
| **Videollamada** | Antes de **cada** sesión | El paciente | Que el **paciente** entre a la sala | `Services/Video/AceptacionVideollamada.cs` |
| **Lectura de emociones** | Antes de cada sesión, **opcional** | El paciente adulto | Analizar y guardar emociones | `Services/Video/ConsentimientoVideoService.cs` (de antes) |

El consentimiento de **WhatsApp** sigue apagado y sin usar (`Mensajeria:AvisosPorWhatsApp`); nada de esto sale por WhatsApp.

## A. Tratamiento y telemedicina: «sin firma no se asigna profesional»

**Una sola definición de «vigente»**: `ConsentimientoTratamiento.Estado(...)` → `ConsentimientoDAO.EstadoDeTratamiento`. La usan RN-02 (`TurnoReglasService.ValidarPasoAConfirmado`), la pantalla de firma (`GET /pacientes/{id}/consentimiento-telemedicina`) y todos los puntos donde se asigna. Antes RN-02 solo miraba «hay alguna firma»; ahora es **vigente** si:

1. hay una firma del tipo exigido que no fue dada de baja, **y**
2. es de la **versión** del texto que está vigente (`Consentimiento:VersionTexto`); las firmas anteriores sin versión siguen valiendo, **y**
3. si la persona es **menor** (menos de 18 años o con un tutor cargado), la firmó un tutor (`Consentimiento.IdTutorFirmante`, migración 28). Sin la migración 28 esta tercera condición no se aplica.

`motivo` cuando no es vigente: `sin-firma`, `version-anterior`, `falta-tutor`.

### Dónde se bloquea la asignación (409 `consentimiento-pendiente`)

| Camino | Quién | Qué se frena |
|---|---|---|
| `POST /pacientes/{id}/profesionales` | Admisión | Asignar al equipo tratante |
| `PUT /informes-admision/{id}/derivar` | Admisión | **Derivar** un informe de admisión (se rechaza antes de tocar el informe) |
| `POST /usuarios/pacientes/{id}/profesionales` | Configurador | Asignar desde Usuarios (a un paciente inactivo o inexistente responde lo de siempre: 409 / 404) |
| `PUT /casosderivaciones/{id}/confirmar` y `/corregir` | CRM / Administración | Confirmar o corregir una derivación de caso **con** profesional |
| `PUT /turnos/{id}/generar-sala` | Profesional / Agendas | Habilitar la sala (ver «Cita urgente») |

Mensaje al operador: *«No se puede asignar profesional: <paciente> todavía no firmó el consentimiento de tratamiento. Pedile que lo firme y volvé a intentar.»* (variantes para versión anterior y para falta de tutor). La respuesta lleva `codigo`, `mensaje`, `motivo` y `urlConsentimiento`.

**Aviso al paciente** (en el mismo rechazo): *«Para continuar tenés que firmar el consentimiento»* → campanita (con el botón «Firmar consentimiento») y mail si tiene; **nada por WhatsApp**. Es idempotente: no se repite mientras el anterior siga sin leer ni hayan pasado 24 horas. Sin aprobación de admisión como estado propio, el «gancho» es el **intento de derivar/asignar**: ahí se avisa.

**Admisión ve el estado**: `GET /informes-admision-detalle` agrega `consentimientoFirmado` y `consentimientoMotivo`; la tabla de admisiones del Configurador tiene la columna **Consentimiento** (Firmado / Pendiente) y el informe en entrevista avisa que no se puede derivar.

**No se bloquean** (por decisión, ver «A validar»): las **reasignaciones** de un paciente que ya tiene profesional (`POST /derivaciones/...` de traspaso, `PUT /casos/{id}/reasignar`, cambio directo), porque el paciente ya fue asignado.

### Firma del menor

`GET .../consentimiento-telemedicina` agrega `esMenor`, `tutores` (los de su ficha) y `marcaProvisoria`. `POST` de un menor exige `idTutor` (uno de sus tutores): 400 `falta-tutor` si no. **Provisorio:** hoy el tutor se «acredita» eligiéndolo desde la sesión del menor; cómo se comprueba que es el tutor lo tiene que definir el equipo legal.

## B. Antes de la videollamada: «Antes de empezar la sesión»

Pantalla del paciente (`sesion_video.html`) con dos pasos:

1. **Videollamada (obligatoria).** `GET/PUT /video/aceptacion` (ver abajo). Sin aceptar no se entra: **el servidor** responde 409 `aceptacion-video-pendiente` en `POST /video/acceso` (solo al rol paciente; el profesional entra igual).
2. **Análisis de emociones (opcional, apagado por defecto).** Explica qué se analiza (el análisis corre en el navegador del profesional; la imagen no se envía ni se guarda; al final se guarda cifrado solo un promedio) y que puede cambiar de idea. **Solo se puede aceptar si ya aceptó el video** (409 `aceptacion-video-pendiente` en `PUT /video/consentimiento`); rechazar no lo exige.

Cada aceptación guarda quién, **fecha y hora (UTC)**, **versión** y **SHA-256** del texto. No se vuelve a preguntar dentro de la misma sesión (la decisión queda en la base y la pantalla la lee). Una sola aceptación vigente por sesión y paciente (índice único filtrado).

### Revocar, en cualquier momento

- **Emociones** (`POST /video/consentimiento/revocar`, de antes): corta el análisis y **anula lo ya guardado** de esa sesión (las notas clínicas no).
- **Video** (`POST /video/aceptacion/revocar`): marca la aceptación como revocada, **revoca también las emociones** (y descarta lo acumulado), **avisa al profesional** (campanita, tipo `consentimiento-video-revocado`; en una admisión, a Administrador) y el siguiente `POST /video/acceso` del paciente se rechaza. La pantalla del paciente corta su video; la del profesional lo ve en su próxima consulta del permiso (cada ~3 s) y **cierra la sala de forma ordenada** (como «Finalizar sesión», dejando el cierre a mano).
- Desde la **sala** (botones «Dejar de usar el video» / «Dejar de compartir mis expresiones») y desde el **perfil** (`consentimiento.html` → «Mis sesiones: video y emociones», que lista `GET /video/aceptaciones-vigentes`: las sesiones de los últimos 30 días con el video aceptado).

### Lo que ve el profesional

Un renglón discreto: **«Paciente aceptó: video ✓ · emociones ✗»** (en rojo si el paciente dejó de usar el video). Sale de `GET /video/emociones/permiso`, que agrega `video: { exigido, estado: pendiente|acepta|revocada, revocadoUtc }`. El permiso de analizar exige **las dos** cosas, y el **servidor** (no solo la pantalla) las vuelve a exigir al guardar: `GuardiaEmocionesService` descarta con motivo `video-no-aceptado`.

### Cita urgente (el hueco que se cerró)

`PUT /turnos/{id}/generar-sala` no miraba el consentimiento y avisaba «sala lista» al paciente. Ahora, sin el consentimiento de tratamiento vigente: 409, **no** se genera la sala, el paciente recibe el aviso para firmar, y al profesional le queda un **bloqueo** en su campanita (`consentimiento-bloqueo`: «La sesión no se pudo habilitar»). Igual si el paciente intenta entrar sin aceptar la videollamada. **La regla no se saltea**; una sola notificación por turno mientras no se lea. *A validar con los psicólogos* si, ante una urgencia clínica, tiene que existir un camino de excepción (hoy no hay).

## Compatibilidad y degradación

Sin la migración 28 la API anda como antes: no se exige la firma del tutor, no se exige ni se guarda la aceptación de la videollamada (`exigida = false`; la pantalla no muestra el bloque), `permiso.video.exigido = false` (el profesional no ve el indicador). El resto (versión del texto, bloqueo de asignación, aviso, generar-sala) funciona sin ella.

## Endpoints nuevos o cambiados

| Método | Ruta | Quién | Qué |
|---|---|---|---|
| GET | `/video/aceptacion?origen=&id=` | Paciente de la sesión (404 si no) | Estado + texto + versión + marca provisoria |
| PUT | `/video/aceptacion` | Paciente de la sesión | `{ origen, id, version }`; 409 `version-desactualizada` si el texto cambió |
| POST | `/video/aceptacion/revocar` | Paciente de la sesión | `{ origen, id }`; corta emociones y avisa al profesional |
| GET | `/video/aceptaciones-vigentes` | Paciente | Sus sesiones recientes con el video aceptado (para «Mis sesiones») |
| POST | `/video/acceso` | (cambia) | 409 `aceptacion-video-pendiente` para el paciente sin aceptar |
| PUT | `/video/consentimiento` | (cambia) | Aceptar emociones exige haber aceptado el video |
| GET | `/video/emociones/permiso` | (cambia) | Agrega `video` |
| GET | `/informes-admision-detalle` | (cambia) | Agrega `consentimientoFirmado` / `consentimientoMotivo` |
| GET/POST | `/pacientes/{id}/consentimiento-telemedicina` | (cambia) | `esMenor`, `tutores`, `motivo`, `marcaProvisoria`; `idTutor` para un menor |
| varios | ver tabla de A | (cambian) | 409 `consentimiento-pendiente` |

## Despliegue

Requiere la **migración 28** si se quiere la firma del tutor y la aceptación de la videollamada; es opcional (sin ella todo sigue como estaba). Se aplica con `sqlcmd` (idempotente, transaccional); deshacer: [`produccion/deshacer-migracion-28.sql`](produccion/deshacer-migracion-28.sql) (se niega a borrar aceptaciones o firmas de tutor guardadas). Publicación de la API: `dotnet publish .\YoSoy.csproj -c Release -r linux-x64 --self-contained -o D:\Claude\Publish-YoSoy-<fecha>-consentimientos`.

## A validar (equipo legal y psicólogos)

- El **texto** de «Antes de empezar la sesión» (versión `video-v0-provisorio`, en `TextoAceptacionVideollamada`) y el del consentimiento de tratamiento. Todos provisorios.
- **Menores**: cómo se acredita al tutor; hoy lo elige el menor desde su sesión. Y si un menor puede aceptar él mismo la videollamada (hoy sí; la lectura de emociones sigue sin ofrecérsele).
- **Cita urgente**: si debe existir una excepción clínica a la regla (hoy no; queda el bloqueo registrado).
- **Reasignaciones** de un paciente ya asignado: hoy no exigen firma.
- **Versionado**: cambiar `Consentimiento:VersionTexto` obliga a todos a volver a firmar antes de la próxima asignación o confirmación de turno.
- **Plazo de conservación** y datos de contacto de la institución (siguen como `[A DEFINIR]` en el texto de emociones).
