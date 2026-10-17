# YoSoy — Plataforma de Telemedicina en Salud Mental

## Índice

- [Equipo](#equipo)
- [Descripción](#descripción)
- [Stack tecnológico](#stack-tecnológico)
- [Base de datos](#base-de-datos)
- [API REST](#api-rest)
- [Cómo ejecutar el proyecto](#cómo-ejecutar-el-proyecto)
- [Autenticación con JWT (rama Cambios-Agus-jwt)](#autenticación-con-jwt-rama-cambios-agus-jwt)
- [Mensajería: chatbot de reservas y recordatorios](#mensajería-chatbot-de-reservas-y-recordatorios)
- [Estructura del proyecto](#estructura-del-proyecto)
- [Como usar git desde CMD](#como-usar-git-desde-cmd)
- [Materia](#materia)

---

Sistema de gestión clínica y telemedicina orientado a la atención en salud mental, desarrollado como proyecto académico para la **Tecnicatura Superior en Análisis de Sistemas de Computación** en el Instituto Cervantes.

---

## Equipo

| Rol | Integrante |
|---|---|
| Product Owner | Santiago Mantovani |
| Scrum Master | Agustín Herrera |
| Dev Team | Araceli Armas |
| Dev Team | Mariana Gallo |

---

## Descripción

YoSoy es una plataforma integral que combina gestión clínica, turnos y asistencia por inteligencia artificial para profesionales y pacientes de salud mental. El sistema busca eliminar barreras geográficas y optimizar el acceso a la atención psicológica y psiquiátrica.

### Funcionalidades principales
- Gestión de pacientes y profesionales
- Agenda y turnos online
- Historia clínica digital
- Registro de evoluciones por sesión
- Derivaciones entre profesionales
- Protocolo de eventos de crisis
- Consentimiento informado digital
- Cobertura de obra social
- Interacciones con IA de apoyo emocional

---

## Stack tecnológico

| Capa | Tecnología |
|---|---|
| Backend | C# — ASP.NET Minimal API (.NET 9) |
| Frontend | HTML + JavaScript (Fetch API) |
| Acceso a datos | ADO.NET |
| Base de datos | SQL Server Express |
| Control de versiones | Git / GitHub |

---

## Base de datos

El script de creación se encuentra en la rama `DataBase` del repositorio.

La base de datos `yosoy` contiene las siguientes entidades:

**Referencia:** `Especialidad`, `Rol`, `EstadoTurno`, `TipoConsentimiento`

**Principales:** `Usuario`, `Paciente`, `Profesional`, `Tutor`

**Operativas:** `Agenda`, `Turno`, `Consentimiento`, `Cobertura`

**Clínicas:** `HistoriaClinica`, `Evolucion`, `Derivacion`, `EventoCrisis`, `InteraccionIA`

**Relacionales:** `PacienteTutor`, `UsuarioRol`, `UsuarioPaciente`, `UsuarioProfesional`

---

## API REST

El proyecto expone una API REST construida con ASP.NET Minimal API. Todos los endpoints devuelven y reciben JSON.

| Recurso | Endpoints disponibles |
|---|---|
| Especialidad | GET, POST, PUT, DELETE |
| Rol | GET, POST, PUT, DELETE |
| EstadoTurno | GET, POST, PUT, DELETE |
| TipoConsentimiento | GET, POST, PUT, DELETE |
| ObraSocial | GET, POST, PUT |
| Tutor | GET, POST, PUT |
| Usuario | GET, POST, PUT, DELETE |
| Paciente | GET, POST, PUT, DELETE |
| Profesional | GET, POST, PUT, DELETE |
| Agenda | GET, POST, PUT, DELETE |
| Turno | GET, POST, PUT, DELETE |
| Consentimiento | GET, POST, PUT, DELETE |
| HistoriaClinica | GET, POST |
| Evolucion | GET, POST, PUT |
| Derivacion | GET, POST, PUT |
| EventoCrisis | GET, POST, PUT |
| InteraccionIA | GET, POST |
| Cobertura | GET, POST, PUT, DELETE |

---

## Cómo ejecutar el proyecto

### Requisitos
- .NET 9
- SQL Server Express
- SQL Server Management Studio

### Pasos

1. Clonar el repositorio
```bash
git clone https://github.com/MantoAR/YoSoy.git
cd YoSoy
```

2. Restaurar la base de datos ejecutando el script de la rama `DataBase` en SQL Server Management Studio, y después **todos** los scripts de `migrations/`, en este orden (cada uno depende de que el anterior ya esté aplicado):

   | # | Script | Qué agrega | ¿Se puede correr dos veces? |
   |---|---|---|---|
   | 1 | `add_columnas_faltantes.sql` | Columnas que el código ya usa y el script base de `DataBase` no crea: `Agenda.IntervaloMinutos`, `Turno.ConfirmadoPaciente`, `Turno.CodigoSala`, `PacienteProfesional.EstadoAsignacion` | Sí |
   | 2 | `add_creadopor_turno.sql` | `Turno.CreadoPor` | Sí |
   | 3 | `add_token_profesional.sql` | `Profesional.Token` | Sí |
   | 4 | `add_token_recuperacion.sql` | `RecuperacionPassword.Token`, `TokenExpiracion` | Sí |
   | 5 | `add_mensajeria.sql` | Tablas `Conversacion`, `MensajeChat`, `Recordatorio`, `PreguntaFrecuente`, `NotificacionInterna` (chatbot, ver [Mensajería](#mensajería-chatbot-de-reservas-y-recordatorios)) | Sí |
   | 6 | `add_consentimiento_telemedicina.sql` | `Consentimiento.FechaFirma` pasa a `DATETIME`; crea el `TipoConsentimiento` de telemedicina (RN-02) | Sí |
   | 7 | `add_lectura_conversacion.sql` | `Conversacion.IdUltimoMensajeLeido` (contador de no leídos de la bandeja) — requiere el 5 | Sí |
   | 8 | `alter_logauditoria_idusuario_nullable.sql` | `LogAuditoria.IdUsuario` admite `NULL` (acciones sin sesión, como un registro público) | Sí |
   | 9 | `add_tablas_sin_migracion_previa.sql` | Tablas `InformeAdmision`, `Notificacion`, `SesionClinica` — existían en la base real y el código las usa desde hace rato, pero nunca tuvieron una migración (se crearon a mano en algún momento). Encontrado al probar la cadena completa contra una base nueva (integración a la rama principal) | Sí |
   | 10 | `add_grupos_casos_derivacion.sql` | Tablas `Grupo`, `ProfesionalGrupo`, `Caso`, `CasoDerivacion`, `ReglaDerivacion`, `HistorialAsignacion` (Grupos, Casos y derivación en cascada) — requiere el 9 (`Caso.IdInforme` referencia `InformeAdmision`) | Sí |
   | 11 | `add_link_publico_profesional.sql` | `Profesional.Slug`, `LinkPublicoActivo` (link público de reserva) | Sí |
   | 12 | `alter_casoderivacion_motivo_cerrado.sql` | `CasoDerivacion.Nota` — requiere el 10 | Sí |
   | 13 | `add_chats_notas_etiquetas_respuestas.sql` | Tablas `NotaInternaConversacion`, `EtiquetaConversacion`, `ConversacionEtiqueta`, `RespuestaRapida` (notas internas, etiquetas y respuestas rápidas de la bandeja de chats) — requiere el 5 | Sí |
   | 14 | `add_conversacion_asignacion.sql` | `Conversacion.IdProfesionalAsignado` (pertenencia de conversaciones: quién puede ver/tomar/devolver un chat escalado — cierre de seguridad) — requiere el 5 | Sí |
   | 15 | `add_consentimiento_video.sql` | Tabla `ConsentimientoVideo` y `SesionClinica.IdConsentimientoVideo`, `EmocionesAnuladasUtc`, `EmocionesAnuladasMotivo` (consentimiento de la lectura de emociones de la sala de video, PR 6) — requiere el 8 y el 9 | Sí |
   | 16 | `add_consentimiento_whatsapp.sql` | Tabla `ConsentimientoWhatsApp` (el paciente acepta o revoca recibir avisos de sus turnos por WhatsApp; una fila por aceptación, con la versión y el hash del texto, el número y las fechas en UTC). Solo agrega — requiere `Paciente` y `Usuario` | Sí |

   Las 16 son idempotentes: se pueden correr más de una vez (o sobre una base donde el script de la rama `DataBase` ya trajo alguna de estas columnas de fábrica) sin ningún error.

   Para cada uno:
   ```bash
   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -f 65001 -i migrations\<archivo>.sql
   ```
   (o abrirlos y ejecutarlos en SQL Server Management Studio). `-f 65001` hace falta para que los acentos se guarden bien.
   El 15 (`add_consentimiento_video.sql`) y el 16 (`add_consentimiento_whatsapp.sql`) se probaron con `-I` (`QUOTED_IDENTIFIER ON`, como lo abre la aplicación): agregalo.

3. Configurar la cadena de conexión en `Conexion.cs`:
```csharp
private static string cadena = "Server=TU_SERVIDOR;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;";
```

4. Ejecutar el proyecto
```bash
dotnet run
```

5. Abrir `http://localhost:5000/pantallas/index.html` en el navegador — es el login real,
   de los tres roles (ver [Autenticación con JWT](#autenticación-con-jwt-rama-cambios-agus-jwt)
   más abajo: hace falta configurar `Jwt:Key` antes de este paso, o la app no arranca).
   `mockup.html`, en la raíz del proyecto, es un ABM de antes de que existiera el login
   por roles — quedó como referencia histórica, pero ya no refleja cómo se usa la app hoy
   (no manda ningún token, así que la mayoría de los endpoints protegidos le van a dar
   401/403).

---

## Autenticación con JWT (rama `Cambios-Agus-jwt`)

> **Estado:** hecho sobre `Cambios-Agus-jwt`, no sobre `main`. Reemplaza la identidad
> autodeclarada por `X-Usuario-Id` (que sigue existiendo como modo de transición, ver
> abajo) por un JWT real que emite `POST /login`.

### Configurar la clave de firma (obligatorio para arrancar)

La clave **no está en ningún archivo versionado**. Hace falta configurarla una vez por máquina:

```bash
dotnet user-secrets set "Jwt:Key" "una-clave-aleatoria-de-32-caracteres-o-mas"
```

(o la variable de entorno `Jwt__Key`, si no es un entorno de desarrollo). Sin esto, la
app **no arranca**: tira un error claro explicando este mismo paso al intentar resolver
`JwtEmisorService` apenas levanta, antes de aceptar ningún pedido.

### Configurar la contraseña de correo (opcional para desarrollar, obligatoria para mails reales)

`appsettings.json` → `Email:Password` queda **vacío** a propósito: no se versiona ninguna
contraseña real. Con el valor vacío (o cualquier valor que no autentique, como un espacio),
`EmailService` intenta conectarse igual pero el envío falla y queda registrado como
`Omitido` — así se puede desarrollar y correr la batería de pruebas sin mandar mails de
verdad. Para que los recordatorios y avisos salgan por correo real, configurar, una vez
por máquina:

```bash
dotnet user-secrets set "Email:Password" "la-contraseña-de-aplicación-real"
```

(o la variable de entorno `Email__Password`, si no es un entorno de desarrollo). La
rotación de una contraseña que se haya expuesto alguna vez se hace en el proveedor de
correo (Gmail u otro) — nunca alcanza con cambiar el archivo.

### Qué cambia

- Los 3 logins (`POST /login`) devuelven, además de lo que ya devolvían, un campo
  `"token"` (expira a las 8 horas, sin refresh token). El frontend lo guarda en
  `localStorage` y `YS.fetch` lo manda como `Authorization: Bearer <token>` en todos los
  llamados.
- `Services/IdentidadActualService.cs` es la única manera de resolver "quién es" en el
  backend (reemplaza a `AutorizacionCasos`/`AutorizacionReporte` y a cada
  `X-Usuario-Id` suelto que se leía directo del header).
- **Modo de transición:** `Auth:PermitirEncabezadoLegacy` (`appsettings.json`). En
  `false` (el valor por defecto, una vez migrado y verificado lo de abajo): sin un JWT
  válido, cualquier pedido a un endpoint protegido da 401, aunque mande
  `X-Usuario-Id`. En `true`: si no hay JWT, se acepta igual el `X-Usuario-Id` de
  siempre (con un `warning` en el log indicando qué ruta todavía no migró) — es el
  interruptor de emergencia si algo del frontend quedó sin actualizar.

### Qué quedó protegido (y con qué alcance)

> **Actualizado en `cierre-seguridad`** (ver `CAMBIOS_AGUS.md`, sección "Cierre de
> seguridad"): esta tabla listaba varios huecos reales como "pendiente"; esta vuelta los
> cierra. `Auth:PermitirEncabezadoLegacy` queda en **`false`** (ver más abajo) y
> `GuardiaEndpointsTests` (reflexión sobre `EndpointDataSource`, no una lista a mano)
> falla si aparece un endpoint nuevo sin decidir.

| Grupo | Alcance |
|---|---|
| Mensajería (`/mensajeria/*`) | Rol Profesional o Configurador, con pertenencia de conversación (quién ve qué chat — ver `docs/MATRIZ_ENDPOINTS.md`). Las dos rutas que simulan un mensaje entrante del paciente quedan abiertas (son el equivalente a un webhook), cerradas aparte por `ChatSimuladoHabilitado`/`SoloLocal`. |
| Consentimiento (firmar y leer estado) | Rol Paciente **y** que el id de la URL sea el del propio token (o el equipo, para leer). |
| Administración (usuarios, auditoría, catálogos al escribir, solicitudes, "Solicitudes de cambio"/`Derivacion`) | Solo Configurador. |
| Casos, Grupos, Reglas de derivación, Derivaciones (Fase 2) | `AutorizacionCasos` ya filtraba por dueño; ahora todo el grupo exige sesión a nivel de ruta (antes, varios — `/grupos`, `/reglasderivacion`, `/pacientes/{id}/caso` — no tenían ningún chequeo, ni de sesión ni de rol). |
| Turnos | La identidad sale siempre del token, nunca de un id en el cuerpo o la URL: un paciente solo pide/ve/cancela/confirma los propios; un profesional solo administra los propios. El `POST /turnos` crudo (sin reglas de negocio, sin uso real) queda apagado (410). `/profesionales/{id}/agenda` sigue abierto a propósito: cualquier paciente necesita ver el horario de **cualquier** profesional al pedir turno. |
| Agendas (franjas de horario) | El profesional solo administra las propias; el Configurador, cualquiera. |
| Historia clínica, evoluciones, eventos de crisis, sesiones clínicas, interacciones con la IA | Solo el profesional **asociado a ese paciente** (mismo criterio que Casos y que los chats: equipo tratante vigente, `PacienteProfesionalDAO`) o el Configurador — ya no "cualquier profesional autenticado". Queda auditado (solo ids, nunca contenido) quién abre qué paciente y cuándo. |
| Reportes de atenciones (CB-04) | Corregido: usaba `X-Usuario-Id` sin firmar, al margen del JWT. Ahora el alcance (propio/total) sale de `IdentidadActualService`, igual que el resto de la app. |
| Catálogos de referencia (Especialidad, Rol, EstadoTurno, TipoConsentimiento, ObraSocial) | Lectura abierta (`/especialidades` la necesita además el registro público de profesional); escritura, solo Configurador. |
| Pacientes/Profesionales (ficha, "Mi perfil") | Lectura y edición exigen sesión; el paciente solo la propia, el profesional solo la propia (antes, sin ningún chequeo, cualquiera editaba la ficha de cualquiera). El volcado completo (todos los pacientes/profesionales) queda Configurador-only. |
| Login, registro de paciente, registro de profesional, recuperación de contraseña, `/reservar/{slug}` y su disponibilidad/reserva pública, `/capacidades`, archivos estáticos | Sin token, a propósito — lista completa y el por qué de cada uno en `GuardiaEndpointsTests.Publicos`. |

### Pendiente

- `signaling-server` (Node, WebRTC): desde el PR 2 exige un token de sala firmado que emite la API (`POST /video/acceso`, PR 3) y que las pantallas piden y mandan (PR 4); ver `docs/VIDEOLLAMADAS.md`. El despliegue en el servidor (unit de systemd, secretos, coturn y Caddy) está preparado en `deploy/` y se hace a mano con `docs/DESPLIEGUE_VIDEO.md` (PR 5; todavía no se ejecutó en el servidor). El consentimiento y el manejo de los datos de la lectura de emociones (PR 6) están implementados, pero **el texto legal es provisorio y lo tiene que validar un profesional legal antes de usarse con pacientes reales**; antes de desplegar la API hay que aplicar `migrations/add_consentimiento_video.sql` y generar la clave de cifrado (`Video__Emociones__ClaveCifrado`; ver `docs/DESPLIEGUE_VIDEO.md`, paso f0).
- Reconciliar con el JWT de `videollamadas-seguras` (`Services/JwtService.cs` ahí): son dos implementaciones distintas (claims, nombre de la clave de configuración, formato de token) hechas sin coordinarse — ver "Parte 2: JWT, Paso J0" en `CAMBIOS_AGUS.md` para el detalle.
- Cierre de sesión por inactividad: decisión de equipo pendiente, no se tocó.
- CSP (Content-Security-Policy): no se activa todavía — hay demasiado JS/CSS inline en `PANTALLAS/` y rompería pantallas. Ver `CAMBIOS_AGUS.md` para el detalle de por qué queda como mejora futura.

---

## Mensajería: chatbot de reservas y recordatorios

> **Estado:** primera etapa. El chatbot funciona sobre un **chat simulado** tipo WhatsApp Web dentro de YoSoy. **No hay conexión con WhatsApp, Instagram ni Facebook**; el diseño deja preparado el lugar donde se enchufan (ver [Etapa siguiente](#etapa-siguiente)).

### Qué hace

- **Chatbot de reservas** con menú guiado: 1) pedir turno, 2) ver mis turnos, 3) cancelar un turno, 4) preguntas frecuentes, 5) hablar con una persona. `0` o `menu` vuelven al menú desde cualquier paso.
  - Identifica al paciente por teléfono (últimos 10 dígitos, así `+54 9 11 5555-0001` coincide con `11 5555-0001`). Si no coincide, pide el DNI (máximo 3 intentos). **No crea pacientes:** si no está registrado lo manda a la web.
  - Pedir turno: elige entre "mis profesionales" o, si no tiene, por especialidad; ofrece hasta 5 horarios libres de los próximos 14 días y crea el turno en estado **Pendiente**, igual que `POST /turnos/solicitar`.
  - Si el paciente no tiene consentimiento firmado, el turno se crea igual y se le avisa que debe firmarlo antes de la primera sesión (RN-02 se exige al confirmar; ver más abajo).
  - "Hablar con una persona" pasa la conversación a `DerivadaHumano`: el bot deja de responder hasta que un administrador la cierre.
  - Después de 30 minutos sin actividad (configurable), el siguiente mensaje vuelve al menú.
- **Seguridad clínica** (criterio validado con una psicóloga): antes de interpretar cualquier mensaje se buscan frases de riesgo (lista configurable). Si hay coincidencia, el bot corta el flujo, responde un mensaje fijo que indica ir a la guardia o llamar a emergencias con las líneas configuradas, marca la conversación como `Urgente` y crea un aviso para el administrador. El bot **nunca** da consejos clínicos, diagnósticos ni contención: ante una consulta clínica responde que eso lo trabaja el profesional.
- **Recordatorio 24 h:** un servicio en segundo plano revisa cada 5 minutos los turnos **Confirmados** que empiezan dentro de las próximas 24 h y avisa por chat, por mail (si el paciente tiene mail y el mail está configurado) y por la campanita. Nunca manda el mismo aviso dos veces. El paciente puede responder:
  - `1`: queda registrada la confirmación de asistencia.
  - `2`: si faltan más de 24 h se cancela; si faltan menos, **no** se cancela (RN-05) y se le avisa al profesional.
- **Avisos por evento** (chat + campanita): solicitud recibida, turno propuesto por el profesional, turno confirmado y turno cancelado.
- **Bandeja de chats** para el configurador (la secretaría), estilo WhatsApp Web. Se abre con el botón **Chats** de la barra superior desde cualquier pantalla de su panel. Es un cajón que se superpone sin recargar; se puede expandir a pantalla completa y se cierra con Esc. Código en `PANTALLAS/Pantalla_ChatSimulado/js/bandeja.js`:
  - **Lista:**
    - buscador por nombre o teléfono;
    - filtros Todas, No leídas, Derivadas (incluye urgentes), Del bot y Cerradas;
    - avatar, último mensaje, hora relativa, contador de no leídos y estado (Urgente siempre arriba).
  - **Hilo:**
    - paciente a la izquierda; asistente, equipo y avisos automáticos a la derecha;
    - separadores de día y mensajes seguidos agrupados;
    - historial hacia arriba de a 50 mensajes;
    - botón para ir al último mensaje con la cantidad de nuevos.
  - **Respuesta:**
    - Enter envía y Shift+Enter hace salto de línea;
    - estado enviando / enviado / error con "Reintentar";
    - respuestas rápidas editables en `PANTALLAS/Pantalla_ChatSimulado/plantillas.json`;
    - deshabilitada si la conversación está cerrada.
  - **Datos del paciente** (panel derecho, colapsable): DNI, teléfono, profesionales, próximo turno, estado del consentimiento, cerrar la derivación y accesos a sus turnos y a su ficha.
  - **Avisos:**
    - contador en el botón y `(n)` en el título de la pestaña;
    - toast cuando llega un mensaje con la bandeja cerrada;
    - sonido opcional, desactivado por defecto;
    - los avisos internos (urgencias y derivaciones) en la campana de la barra.
  - **Polling eficiente:**
    - pide solo las conversaciones con mensajes nuevos (`?desdeMensaje=`);
    - cada 3 s con la bandeja abierta, cada 20 s cerrada, más espaciado si no hay novedades;
    - nada con la pestaña oculta.
  - **Atajos:** `?` muestra la ayuda, `/` busca, Alt+↑/↓ cambia de conversación y Esc cierra.
  - Solo aparece si la mensajería está disponible para ese pedido (`GET /capacidades`, mismo control que el 404 de `/mensajeria`).
- **Pantalla de chats** (`PANTALLAS/Pantalla_ChatSimulado/index.html`): la misma bandeja en modo página y el **simulador del paciente**, para chatear con el asistente como si fuera un paciente con un teléfono de prueba.

### Cómo ponerlo en marcha

1. Las migraciones que necesita puntualmente la mensajería son la 1, 5, 6 y 7 de la tabla en [Cómo ejecutar el proyecto](#cómo-ejecutar-el-proyecto) (son idempotentes, se pueden correr más de una vez) — pero para tener la base al día con esta rama hay que correr la lista completa de esa tabla, en orden, no solo estas 4.
2. Revisar la sección `Mensajeria` de `appsettings.json`. **Antes de usarlo con pacientes reales** hay que completar y validar `LineasEmergencia` (hoy dicen `[COMPLETAR Y VALIDAR]`) y el texto legal del consentimiento (ver [RN-02](#rn-02-consentimiento-de-telemedicina)).
3. `dotnet run`, ingresar como configurador y abrir **Chats** en la barra superior. El simulador del paciente está en `http://localhost:5000/pantallas/Pantalla_ChatSimulado/index.html#simulador`.

### Cómo probarlo

- **Simulador del paciente:** ingresar el teléfono de un paciente de prueba (por ejemplo el de un paciente asignado a un profesional con agenda) y escribir `hola`. Con un número que no existe se ve el flujo de DNI.
- **Bandeja (configurador):** ver las conversaciones, responder, cerrar derivaciones y leer los avisos internos en la campana.
- **Recordatorios sin esperar:** `POST /mensajeria/recordatorios/ejecutar` corre una pasada del servicio.
- **Pruebas automáticas:** `dotnet test YoSoy.Tests` (96 pruebas del intérprete, las reglas de turnos, incluida RN-02 al confirmar, la máquina de estados del bot y el control de acceso de `/mensajeria`, con dobles en memoria; no necesitan base de datos).

> ⚠️ **Mails reales:** si la sección `Email` de la configuración está completa, los recordatorios y el aviso al profesional **se envían de verdad**. Para probar sin mandar mails, levantar la app con la contraseña en blanco: en PowerShell `$env:Email__Password = " "; dotnet run`. En ese caso el envío queda registrado como `Omitido`.

### Configuración (`appsettings.json` → `Mensajeria`)

Solo valores no secretos. Cualquier token futuro (Meta, etc.) va por `dotnet user-secrets` o variables de entorno, nunca en este archivo.

| Clave | Por defecto | Para qué sirve |
|---|---|---|
| `CanalActivo` | `Simulado` | Canal por el que salen los mensajes. Hoy es el único disponible. |
| `RecordatorioHorasAntes` | `24` | Ventana del recordatorio. |
| `IntervaloChequeoMinutos` | `5` | Cada cuánto corre el servicio de recordatorios. |
| `TimeoutConversacionMinutos` | `30` | Inactividad tras la cual el bot vuelve al menú. |
| `MaxIntentosEnvio` | `3` | Reintentos de un aviso que dio error. |
| `ChatSimuladoHabilitado` | `true` | Apaga todos los endpoints `/mensajeria` (responden 404). |
| `SoloLocal` | `true` | `/mensajeria` solo acepta pedidos desde la propia máquina. |
| `UrlRegistroWeb`, `UrlIngresoTurno` | | Links que manda el bot. El de firma del consentimiento sale de `Consentimiento:UrlFirma`. |
| `ExigirConsentimientoParaSolicitar` | `false` | `true` = el bot no crea el turno si falta el consentimiento. |
| `ConsentimientoWhatsApp:VersionTexto` | `v0-provisorio` | Versión del texto del consentimiento de los avisos por WhatsApp (`Services/Mensajeria/Consentimiento/<versión>.txt`, embebido). Una versión publicada no se edita; **al cambiarla, los avisos por WhatsApp se detienen hasta que cada paciente vuelva a aceptar.** Variable de entorno: `Mensajeria__ConsentimientoWhatsApp__VersionTexto`. |
| `DiasBusquedaHorarios`, `MaxHorariosOfrecidos` | `14`, `5` | Búsqueda de horarios libres. |
| `PalabrasRiesgo` | lista | Frases que disparan el mensaje de emergencia. **Obligatoria:** la app no arranca si está vacía. |
| `PalabrasClinicas` | lista | Si el mensaje no se entiende y contiene alguna, el bot aclara que no responde consultas clínicas. |
| `LineasEmergencia` | placeholders | Nombre y teléfono de cada línea de ayuda. **Completar y validar con el equipo.** |

### Endpoints nuevos (`Endpoints/MensajeriaEndpoints.cs`)

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/mensajeria/simulado/entrante` | `{ telefono, texto }`: procesa el mensaje con el bot y devuelve las respuestas. |
| GET | `/mensajeria/simulado/conversacion?telefono=` | Conversación de ese teléfono (404 si no hay). |
| GET | `/capacidades` | `{ mensajeria }`: si la interfaz muestra el botón Chats. Mismo control que el filtro de `/mensajeria`: apagado o pedido remoto con `SoloLocal` da `false`. |
| GET | `/mensajeria/conversaciones?estado=&desdeMensaje=` | Listado con nombre del paciente, último mensaje (id, texto, origen y hora) y `noLeidos`; urgentes siempre arriba, el resto por última actividad. Con `desdeMensaje` trae solo las conversaciones con mensajes posteriores (polling incremental). |
| PUT | `/mensajeria/conversaciones/{id}/leida` | `{ hasta }`: el equipo vio la conversación hasta ese mensaje (contador de no leídos; requiere `add_lectura_conversacion.sql`). |
| GET | `/mensajeria/conversaciones/{id}` | Estado de una conversación. |
| GET | `/mensajeria/conversaciones/{id}/paciente` | Panel del administrador: nombre, DNI, teléfono, profesionales y próximo turno del paciente (`paciente: null` si el bot no lo identificó). |
| GET | `/mensajeria/conversaciones/{id}/mensajes?desde=&antes=&limite=` | Historial; `desde` = último id recibido (polling). Con `antes`/`limite`, los mensajes anteriores a ese id de a páginas (sin `antes`, los últimos). |
| POST | `/mensajeria/conversaciones/{id}/responder` | `{ texto }`: respuesta de una persona del equipo. |
| PUT | `/mensajeria/conversaciones/{id}/cerrar` | Cierra la derivación o urgencia; el bot vuelve a atender. |
| GET / PUT | `/mensajeria/notificaciones-internas`, `.../{id}/leida` | Avisos para el administrador y los profesionales. |
| POST | `/mensajeria/recordatorios/ejecutar` | Corre una pasada del servicio de recordatorios. |
| GET / POST / PUT / DELETE | `/mensajeria/preguntas-frecuentes[/{id}]` | ABM de preguntas frecuentes (la baja es lógica). |

### Reglas de negocio de turnos

Las reglas del Encuentro 5 quedaron en el backend en `Services/TurnoReglasService.cs`, que es el único punto de verdad. Las usa el bot y también los endpoints web de turnos (`POST /turnos/solicitar`, la reserva por link público, `POST /turnos/profesional-solicitar` y la cancelación del paciente); RN-02 además corre en los endpoints que confirman turnos.

| Regla | Bot | Web (hoy) |
|---|---|---|
| RN-01 Disponibilidad real de agenda (franja, intervalo, futuro —posterior a "ahora" en la zona horaria de la clínica—, horario libre) | ✅ Backend | ✅ Backend (el frontend además arma los horarios con la agenda y `/turnos/ocupados`) |
| RN-02 Consentimiento de telemedicina antes de confirmar | Avisa al solicitar con el link para firmar; modo estricto opcional | ✅ Backend al confirmar (ver abajo) |
| RN-05 Anticipación mínima de 24 h **para cancelar**. Para pedir un turno ya no hay anticipación mínima: alcanza con que el horario sea posterior a "ahora" (RN-01) | ✅ Backend | ✅ Backend (cancelación del paciente) |
| RN-07 Un solo turno Pendiente/Confirmado por paciente, profesional y día | ✅ Backend, versión documentada | ✅ Backend (por profesional); el frontend además bloquea el día entero con **cualquier** profesional |

### RN-02: consentimiento de telemedicina

- **Qué se exige:** una fila en `Consentimiento` del tipo `Consentimiento Informado - Atención por Telemedicina` (buscado por descripción) que no esté `Inactivo`. El modelo no tiene vencimiento.
- **Dónde:** `PUT /turnos/{id}` cuando el turno *pasa* a Confirmado (así confirma el profesional) y `PUT /turnos/{id}/confirmar-paciente` (el paciente acepta un turno propuesto). Si falta, **409** `{ codigo: "RN-02", mensaje, urlConsentimiento }` y el turno no cambia.
- **Hacia adelante:** los turnos que ya estaban Confirmados no se tocan, y rechazar o cancelar no se frena.
- **Sesiones:** la falta de consentimiento nunca bloquea una sala. En "Ir a sala" de una sesión común el turno queda sin confirmar y el profesional ve un aviso persistente de que el paciente no firmó. La sesión urgente no se toca.
- **Firma:** `PANTALLAS/Pantalla_Paciente/consentimiento.html` (pestaña "Consentimiento" del panel del paciente). Muestra el texto, se firma con un botón explícito y guarda fecha y hora. Si ya está firmado lo muestra y no deja firmar de nuevo.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/pacientes/{id}/consentimiento-telemedicina` | `{ firmado, fechaFirma, versionFirmada, tipo, versionTexto }`. |
| POST | `/pacientes/{id}/consentimiento-telemedicina` | `{ acepto: true, version }`. 201 si firmó; 409 si ya estaba firmado o si la versión del texto cambió; 400 sin aceptación explícita. |

Configuración (`appsettings.json` → `Consentimiento`):

| Clave | Por defecto | Para qué sirve |
|---|---|---|
| `TipoRequerido` | `Consentimiento Informado - Atención por Telemedicina` | Tipo que exige RN-02 (lo crea `migrations/add_consentimiento_telemedicina.sql`). |
| `VersionTexto` | `sin-validar` | Versión del texto legal; se guarda con cada firma. Cambiarla al cargar el texto validado. |
| `UrlFirma` | `.../Pantalla_Paciente/consentimiento.html` | Link de firma que usan el bot y los mensajes de error. |

### Archivos existentes modificados

- `Program.cs`: registra `EmailService` y `AddMensajeria`, agrega `app.MapMensajeria()` y una llamada al notificador en `POST /turnos/solicitar`, `PUT /turnos/{id}` (lee el estado anterior para detectar confirmación o rechazo), `PUT /turnos/{id}/cancelar`, `PUT /turnos/{id}/confirmar-paciente` y `POST /turnos/profesional-solicitar`. Un error al notificar nunca hace fallar al endpoint.
- `Services/EmailService.cs`: nuevo método `EnviarAviso`.
- `appsettings.json`: nuevas secciones `Mensajeria` y `Consentimiento`.
- `Program.cs` (RN-02): `PUT /turnos/{id}` y `PUT /turnos/{id}/confirmar-paciente` validan el consentimiento antes de guardar (dos líneas cada uno) y se agrega `app.MapConsentimiento()`.
- `DAOs/ConsentimientoDAO.cs`: métodos nuevos `ObtenerVigente` y `FirmarSiNoExiste`; lo existente no cambia.
- `PANTALLAS/Pantalla_Paciente/dashboard.html` y `js/dashboard.js`: pestaña "Consentimiento" y ofrecimiento de firmar al aceptar un turno propuesto sin consentimiento.
- `PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js`: aviso con el link al confirmar sin consentimiento, y aviso persistente en "Ir a sala".
- `YoSoy.csproj` y `YoSoy.sln`: excluyen y agregan el proyecto de pruebas.

### Limitaciones conocidas y pendientes

- **Sin autenticación real.** Los endpoints `/mensajeria` exponen conversaciones de pacientes. Mitigación actual: `ChatSimuladoHabilitado` y `SoloLocal`, que no alcanza detrás de un proxy o túnel local. Pendiente: exigir JWT y rol de administrador al mergear `videollamadas-seguras` (`TODO(JWT)` en el código). No publicar en una URL pública.
- **`/mensajeria/conversaciones/{id}/paciente` expone datos sensibles del paciente (nombre, DNI, teléfono, profesionales y próximo turno) sin autenticación real**: cualquiera que llegue al servidor con el módulo encendido puede recorrer ids y armarse una lista de pacientes. Mitigado hoy con `ChatSimuladoHabilitado` (con `false`, todo `/mensajeria` responde 404, igual que un pedido no local con `SoloLocal`); pendiente de JWT.
- **Exposición al mergear `videollamadas-seguras`:** el `Caddyfile` de esa rama publica todo lo que atiende `127.0.0.1:5000`, así que `/mensajeria/*` y `/pantallas/Pantalla_ChatSimulado/` quedarían accesibles desde afuera, y como Caddy se conecta desde 127.0.0.1, la IP sola no alcanza. Por eso un pedido que trae `X-Forwarded-For` o `CF-Connecting-IP` se trata como remoto (404), y con `ChatSimuladoHabilitado` en `false` también la pantalla estática responde 404. Hasta tener JWT, en cualquier despliegue público `ChatSimuladoHabilitado` tiene que estar en `false`. La verificación a hacer al mergear está en [`NOTAS_MERGE.md`](NOTAS_MERGE.md).
- **Pensado para PC.** La pantalla de mensajería se diseña para PC/notebook; el uso desde el celular lo va a cubrir la app mobile aparte. Lo que ya existe para pantallas chicas quedó como está, sin más trabajo.
- **Identificación por DNI:** quien conozca el DNI de un paciente puede vincular otro teléfono y ver o cancelar sus turnos. El tope de 3 intentos solo evita probar DNIs al azar. Pendiente: verificación adicional (por ejemplo, un código por mail) antes de vincular un teléfono nuevo.
- Un celular cargado con el `15` delante del número no se reconoce por teléfono y cae en el flujo de DNI.
- **Consentimiento:** el texto legal es un placeholder (`[COMPLETAR CON TEXTO LEGAL VALIDADO]`) hasta que el equipo lo valide. La firma usa el `idPaciente` del navegador: sin autenticación real cualquiera podría firmar por otro paciente, y eso no tiene validez como consentimiento informado (`TODO(JWT)` en `Endpoints/ConsentimientoEndpoints.cs`). El "Acuerdo de Atención al Paciente" del registro sigue sin guardar ninguna fila, y no hay pantalla para revocar un consentimiento.
- **Versión del consentimiento:** cada firma guarda `Consentimiento:VersionTexto`, pero la vigencia (`ConsentimientoDAO.ObtenerVigente`, que usan RN-02 y la página de firma) **no compara versiones**: acepta cualquier fila del tipo requerido que no esté `Inactivo`. Por eso, el día que se cargue el texto legal validado y se cambie `VersionTexto`, los consentimientos firmados contra el placeholder (`sin-validar`) van a seguir contando como vigentes hasta que se agregue esa comparación.
- Las notificaciones internas se ven solo en la campana del configurador; mostrarlas en el panel del profesional queda pendiente. Al administrador no se le manda mail (no hay un mail de administrador configurado).
- `DELETE /turnos/{id}` y el `POST /turnos` genérico no disparan avisos.
- La reprogramación y el modal de turno de la web arman horarios de 60 minutos fijos en lugar de usar `IntervaloMinutos`.
- Actualización por polling (incremental y adaptativo); sin WebSockets. No hay indicador de "escribiendo…": el canal no informa cuándo el paciente está escribiendo.
- La marca de leído es compartida por todo el equipo (como una bandeja de WhatsApp Business): si alguien abre la conversación, deja de figurar como no leída para todos.
- Zona horaria: los turnos se guardan sin zona y se comparan con la hora de la clínica, que es una zona **explícita** (`Turnos__ZonaHoraria`; por defecto `America/Argentina/Buenos_Aires`) y no depende de la zona del sistema operativo del servidor (`Services/RelojDeLaClinica.cs`). Ver `docs/VIDEOLLAMADAS.md`, "La zona horaria". Las pantallas filtran los horarios de hoy que ya pasaron con la hora de Argentina fija; si algún día se configura otra zona, hay que cambiarla también en `PANTALLAS/Pantalla_Paciente/js/dashboard.js` y `PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js` (`ahoraEnArgentina`).
- Fechas y horas de auditoría (`DateTime.Now` / `GETDATE()` en altas, derivaciones, recuperación de contraseña y bloqueo de cuenta): todavía usan la hora del servidor (o de la base), no el reloj de la clínica. No cambian ninguna decisión de turnos; quedaron anotadas para unificarlas.
- Recordatorios de pago: fuera de alcance (no existe una entidad de pagos). El tipo `PagoPendiente` quedó reservado en `Recordatorio`.

### Etapa siguiente

- **WhatsApp Cloud API:** implementar `CanalWhatsAppCloud` (hoy lanza `NotImplementedException` y no está registrado): envío por la Graph API con el token en user-secrets, webhook con verificación (`hub.challenge`) y validación de la firma `X-Hub-Signature-256`, y estados de entrega.
- **Plantillas de Meta:** los mensajes que inicia el negocio (recordatorios y avisos) fuera de la ventana de 24 h desde el último mensaje del paciente solo se pueden mandar con plantillas aprobadas.
- **Instagram y Facebook Messenger:** nuevas implementaciones de `IMessageChannel`; el bot no cambia.
- **IA conversacional:** una `InterpreteIA` que implemente `IInterpreteIntencion` reemplaza al menú registrándola en DI. El filtro de seguridad clínica sigue corriendo antes que cualquier interpretación.
- **Recordatorios de pago**, cuando exista la entidad de pagos.

---

## Estructura del proyecto

```
YoSoy/
├── Conexion.cs
├── Program.cs
├── mockup.html
├── YoSoy.csproj
├── Models/
│   ├── Especialidad.cs
│   ├── Rol.cs
│   ├── EstadoTurno.cs
│   ├── TipoConsentimiento.cs
│   ├── ObraSocial.cs
│   ├── Tutor.cs
│   ├── Usuario.cs
│   ├── Paciente.cs
│   ├── Profesional.cs
│   ├── Agenda.cs
│   ├── Turno.cs
│   ├── Consentimiento.cs
│   ├── Cobertura.cs
│   ├── HistoriaClinica.cs
│   ├── Evolucion.cs
│   ├── Derivacion.cs
│   ├── EventoCrisis.cs
│   ├── InteraccionIA.cs
│   ├── PacienteTutor.cs
│   ├── UsuarioRol.cs
│   ├── UsuarioPaciente.cs
│   └── UsuarioProfesional.cs
└── DAOs/
    ├── EspecialidadDAO.cs
    ├── RolDAO.cs
    ├── EstadoTurnoDAO.cs
    ├── TipoConsentimientoDAO.cs
    ├── ObraSocialDAO.cs
    ├── TutorDAO.cs
    ├── UsuarioDAO.cs
    ├── PacienteDAO.cs
    ├── ProfesionalDAO.cs
    ├── AgendaDAO.cs
    ├── TurnoDAO.cs
    ├── ConsentimientoDAO.cs
    ├── CoberturaDAO.cs
    ├── HistoriaClinicaDAO.cs
    ├── EvolucionDAO.cs
    ├── DerivacionDAO.cs
    ├── EventoCrisisDAO.cs
    ├── InteraccionIADAO.cs
    ├── PacienteTutorDAO.cs
    ├── UsuarioRolDAO.cs
    ├── UsuarioPacienteDAO.cs
    └── UsuarioProfesionalDAO.cs
```

---

## Como usar git desde CMD

### Bajar los últimos cambios del repositorio (pull)
```bash
cd "ruta\a\tu\carpeta\YoSoy"
git pull
```

### Subir cambios al repositorio (push)
```bash
cd "ruta\a\tu\carpeta\YoSoy"

# 1. Agregar los archivos modificados
git add .

# 2. Crear el commit con una descripción
git commit -m "descripción de lo que cambiaste"

# 3. Subir al repositorio
git push
```

### Agregar un archivo o carpeta específica
```bash
# Un archivo
git add nombre_archivo.html

# Una carpeta completa
git add nombre_carpeta/
```

> **Recomendación:** hacer `git pull` siempre antes de empezar a trabajar para tener la última versión del proyecto.

---

## Materia

**Práctica Profesionalizante I**
Docente: Fernando Loza
Carrera: Tecnicatura Superior en Análisis de Sistemas de Computación — 42NMS (AS) 2-25
