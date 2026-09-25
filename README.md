# YoSoy — Plataforma de Telemedicina en Salud Mental

## Índice

- [Equipo](#equipo)
- [Descripción](#descripción)
- [Stack tecnológico](#stack-tecnológico)
- [Base de datos](#base-de-datos)
- [API REST](#api-rest)
- [Cómo ejecutar el proyecto](#cómo-ejecutar-el-proyecto)
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

2. Restaurar la base de datos ejecutando el script de la rama `DataBase` en SQL Server Management Studio, y después los scripts de `migrations/` (`add_columnas_faltantes.sql` agrega columnas que el código usa y el script base no crea)

3. Configurar la cadena de conexión en `Conexion.cs`:
```csharp
private static string cadena = "Server=TU_SERVIDOR;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;";
```

4. Ejecutar el proyecto
```bash
dotnet run
```

5. Abrir `mockup.html` en el navegador para usar la interfaz

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
- **Pantalla "YoSoy · Mensajería"** (`PANTALLAS/Pantalla_ChatSimulado/`), estilo WhatsApp Web, para que la secretaría o el profesional sigan las conversaciones del bot:
  - Izquierda: buscador por nombre o teléfono y lista de conversaciones con estado en color (Urgente en rojo y siempre arriba, Derivada en naranja, Cerrada en gris).
  - Centro: el hilo, con el paciente a la izquierda y el bot, el equipo y los avisos automáticos a la derecha, cada uno con su ícono. Respuesta manual (deshabilitada si la conversación está cerrada).
  - Derecha: DNI, teléfono, profesionales y próximo turno del paciente, y el botón para cerrar la derivación o marcar la urgencia como resuelta.
  - Header con contadores de urgentes y derivadas, la campanita de avisos internos y el modo "Probar como paciente" para chatear con un teléfono de prueba.
  - Se actualiza por polling cada 2,5 s. Pensada para PC: las tres columnas quedan visibles desde 900 px de ancho.

### Cómo ponerlo en marcha

1. Correr las migraciones en este orden (son idempotentes, se pueden correr más de una vez):
   ```bash
   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -f 65001 -i migrations\add_columnas_faltantes.sql
   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -f 65001 -i migrations\add_mensajeria.sql
   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -f 65001 -i migrations\add_consentimiento_telemedicina.sql
   ```
   `-f 65001` hace falta para que los acentos de las preguntas frecuentes de ejemplo se guarden bien. También se pueden abrir y ejecutar en SQL Server Management Studio.
2. Revisar la sección `Mensajeria` de `appsettings.json`. **Antes de usarlo con pacientes reales** hay que completar y validar `LineasEmergencia` (hoy dicen `[COMPLETAR Y VALIDAR]`) y el texto legal del consentimiento (ver [RN-02](#rn-02-consentimiento-de-telemedicina)).
3. `dotnet run` y abrir `http://localhost:5000/pantallas/Pantalla_ChatSimulado/index.html`.

### Cómo probarlo

- **Modo paciente:** ingresar el teléfono de un paciente de prueba (por ejemplo el de un paciente asignado a un profesional con agenda) y escribir `hola`. Con un número que no existe se ve el flujo de DNI.
- **Modo administrador:** ver las conversaciones, responder, cerrar derivaciones y leer los avisos internos.
- **Recordatorios sin esperar:** `POST /mensajeria/recordatorios/ejecutar` corre una pasada del servicio.
- **Pruebas automáticas:** `dotnet test YoSoy.Tests` (89 pruebas del intérprete, las reglas de turnos, incluida RN-02 al confirmar, y la máquina de estados del bot, con dobles en memoria; no necesitan base de datos).

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
| `DiasBusquedaHorarios`, `MaxHorariosOfrecidos` | `14`, `5` | Búsqueda de horarios libres. |
| `PalabrasRiesgo` | lista | Frases que disparan el mensaje de emergencia. **Obligatoria:** la app no arranca si está vacía. |
| `PalabrasClinicas` | lista | Si el mensaje no se entiende y contiene alguna, el bot aclara que no responde consultas clínicas. |
| `LineasEmergencia` | placeholders | Nombre y teléfono de cada línea de ayuda. **Completar y validar con el equipo.** |

### Endpoints nuevos (`Endpoints/MensajeriaEndpoints.cs`)

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/mensajeria/simulado/entrante` | `{ telefono, texto }`: procesa el mensaje con el bot y devuelve las respuestas. |
| GET | `/mensajeria/simulado/conversacion?telefono=` | Conversación de ese teléfono (404 si no hay). |
| GET | `/mensajeria/conversaciones?estado=` | Listado con nombre del paciente, último mensaje y su hora; urgentes siempre arriba, el resto por última actividad. |
| GET | `/mensajeria/conversaciones/{id}` | Estado de una conversación. |
| GET | `/mensajeria/conversaciones/{id}/paciente` | Panel del administrador: nombre, DNI, teléfono, profesionales y próximo turno del paciente (`paciente: null` si el bot no lo identificó). |
| GET | `/mensajeria/conversaciones/{id}/mensajes?desde=` | Historial; `desde` = último id recibido (polling). |
| POST | `/mensajeria/conversaciones/{id}/responder` | `{ texto }`: respuesta de una persona del equipo. |
| PUT | `/mensajeria/conversaciones/{id}/cerrar` | Cierra la derivación o urgencia; el bot vuelve a atender. |
| GET / PUT | `/mensajeria/notificaciones-internas`, `.../{id}/leida` | Avisos para el administrador y los profesionales. |
| POST | `/mensajeria/recordatorios/ejecutar` | Corre una pasada del servicio de recordatorios. |
| GET / POST / PUT / DELETE | `/mensajeria/preguntas-frecuentes[/{id}]` | ABM de preguntas frecuentes (la baja es lógica). |

### Reglas de negocio de turnos

Las reglas del Encuentro 5 quedaron en el backend en `Services/TurnoReglasService.cs`, que es el único punto de verdad. Las usa el bot y, para RN-02, también los endpoints web que confirman turnos. RN-01, RN-05 y RN-07 todavía no se aplican en los endpoints web.

| Regla | Bot | Web (hoy) |
|---|---|---|
| RN-01 Disponibilidad real de agenda (franja, intervalo, futuro, horario libre) | ✅ Backend | Solo en el frontend (arma los horarios con la agenda y `/turnos/ocupados`) |
| RN-02 Consentimiento de telemedicina antes de confirmar | Avisa al solicitar con el link para firmar; modo estricto opcional | ✅ Backend al confirmar (ver abajo) |
| RN-05 Anticipación mínima de 24 h para pedir y cancelar | ✅ Backend | ❌ No se valida |
| RN-07 Un solo turno Pendiente/Confirmado por paciente, profesional y día | ✅ Backend, versión documentada | Solo en el frontend y distinta: bloquea el día entero con **cualquier** profesional |

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
- Reglas RN-01, RN-05 y RN-07 en los endpoints web: pendientes.
- **Consentimiento:** el texto legal es un placeholder (`[COMPLETAR CON TEXTO LEGAL VALIDADO]`) hasta que el equipo lo valide. La firma usa el `idPaciente` del navegador: sin autenticación real cualquiera podría firmar por otro paciente, y eso no tiene validez como consentimiento informado (`TODO(JWT)` en `Endpoints/ConsentimientoEndpoints.cs`). El "Acuerdo de Atención al Paciente" del registro sigue sin guardar ninguna fila, y no hay pantalla para revocar un consentimiento.
- **Versión del consentimiento:** cada firma guarda `Consentimiento:VersionTexto`, pero la vigencia (`ConsentimientoDAO.ObtenerVigente`, que usan RN-02 y la página de firma) **no compara versiones**: acepta cualquier fila del tipo requerido que no esté `Inactivo`. Por eso, el día que se cargue el texto legal validado y se cambie `VersionTexto`, los consentimientos firmados contra el placeholder (`sin-validar`) van a seguir contando como vigentes hasta que se agregue esa comparación.
- Las notificaciones internas se ven solo en el modo administrador del chat simulado; mostrarlas en el panel del profesional queda pendiente. Al administrador no se le manda mail (no hay un mail de administrador configurado).
- `DELETE /turnos/{id}` y el `POST /turnos` genérico no disparan avisos.
- La reprogramación y el modal de turno de la web arman horarios de 60 minutos fijos en lugar de usar `IntervaloMinutos`.
- Actualización por polling; sin WebSockets.
- Zona horaria: los turnos se guardan sin zona y se comparan con la hora local del servidor, que se asume en Argentina (UTC-3).
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
