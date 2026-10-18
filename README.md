# YoSoy — Plataforma de Telemedicina en Salud Mental

## Índice

- [Equipo](#equipo)
- [Descripción](#descripción)
- [Stack tecnológico](#stack-tecnológico)
- [Base de datos](#base-de-datos)
- [API REST](#api-rest)
- [Cómo ejecutar el proyecto](#cómo-ejecutar-el-proyecto)
- [Autenticación con JWT (rama Cambios-Agus-jwt)](#autenticación-con-jwt-rama-cambios-agus-jwt)
- [Circuito de atención](#circuito-de-atención-consentimientos-opcionales-ia-y-evolución-por-enfoque)
- [Roles, permisos y errores (etapa 1)](#roles-permisos-y-errores-etapa-1)
- [Acceso por módulo y auditoría (etapa 2)](#acceso-por-módulo-y-auditoría-etapa-2)
- [Reparto de funciones entre módulos (etapa 3)](#reparto-de-funciones-entre-módulos-etapa-3)
- [Alta de usuarios del equipo](#alta-de-usuarios-del-equipo)
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
   | 15 | `add_atencion_consentimientos_ia_evolucion.sql` | Consentimientos opcionales (IA de expresiones, transcripción, WhatsApp) y su revocación; `ConfiguracionProfesional`; `SalaSesion` y `SesionFuncion` (interruptores de la sala); evolución por enfoque (`Evolucion.Enfoque`, `ContenidoJson`, `Estado`, `FechaFirma`, `HashContenido`…), `EvolucionAddendum` y los triggers que impiden modificar o borrar una evolución firmada — requiere el 6 y el 9. Ver [Circuito de atención](#circuito-de-atención-consentimientos-opcionales-ia-y-evolución-por-enfoque) | Sí |
   | 16 | `add_permisos_especialidades_errores.sql` | Roles, permisos y privilegios (`Rol.Clave`, `Permiso`, `Privilegio`, `PermisoPrivilegio`, `UsuarioPermiso`, `UsuarioPrivilegio`, vista `vw_UsuarioAccesos`); `Usuario.Nombre`/`Apellido`/`Cuil`; matrícula de especialista y catálogo de especialidades; `Organizacion`, `ConfiguracionOrganizacion`; `LogError` y `VersionEsquema`. Carga los permisos de los usuarios existentes — requiere de la 1 a la 15. Ver [Roles, permisos y errores](#roles-permisos-y-errores-etapa-1) | Sí |
   | 17 | `add_auditoria_inalterable.sql` | `LogAuditoria.Categoria` (`Administrativa`/`Clinica`), `Hash` y `HashAnterior` (cadena de hashes), triggers que impiden modificar o borrar `LogAuditoria` y `LogError`, `sp_VerificarCadenaAuditoria` — requiere el 16. Ver [Acceso por módulo y auditoría](#acceso-por-módulo-y-auditoría-etapa-2) | Sí |
   | 18 | `add_privilegio_agendas_auditoria_permisos.sql` | Privilegio **Gestión de agendas** (solo para CRM) y triggers que registran en la auditoría administrativa cada alta o baja de rol, permiso o privilegio de un usuario, aunque se haga directo en la base — requiere el 16 y el 17. Ver [Reparto de funciones](#reparto-de-funciones-entre-módulos-etapa-3) | Sí |
   | 19 | `add_alta_equipo_invitaciones.sql` | `Usuario.Telefono` y tabla `InvitacionCuenta` (hash del token del mail de invitación, vence a las 72 horas, un uso) para el alta de usuarios del equipo — requiere el 16 al 18. Ver [Alta de usuarios del equipo](#alta-de-usuarios-del-equipo) | Sí |

   Las 17 son idempotentes: se pueden correr más de una vez (o sobre una base donde el script de la rama `DataBase` ya trajo alguna de estas columnas de fábrica) sin ningún error.

   Para cada uno:
   ```bash
   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -f 65001 -i migrations\<archivo>.sql
   ```
   (o abrirlos y ejecutarlos en SQL Server Management Studio). `-f 65001` hace falta para que los acentos se guarden bien.

3. Configurar la cadena de conexión. Lo recomendado es no tocar el código y guardarla en
   user-secrets (en el servidor, la variable de entorno `ConnectionStrings__YoSoy`):
```bash
dotnet user-secrets set "ConnectionStrings:YoSoy" "Server=TU_SERVIDOR;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;"
```
   Si no está configurada, se usa la cadena que trae `Conexion.cs`, como antes.

   Al arrancar, la consola de `dotnet run` dice si la base está al día o qué migración le
   falta (ver [Roles, permisos y errores](#roles-permisos-y-errores-etapa-1)).

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

- `signaling-server` (Node, WebRTC) **no autentica nada** — cualquiera que tenga o adivine un código de sala puede conectarse. Fuera de alcance de esta rama (lo trabaja `videollamadas-seguras`); solo queda anotado.
- Reconciliar con el JWT de `videollamadas-seguras` (`Services/JwtService.cs` ahí): son dos implementaciones distintas (claims, nombre de la clave de configuración, formato de token) hechas sin coordinarse — ver "Parte 2: JWT, Paso J0" en `CAMBIOS_AGUS.md` para el detalle.
- Cierre de sesión por inactividad: decisión de equipo pendiente, no se tocó.
- CSP (Content-Security-Policy): no se activa todavía — hay demasiado JS/CSS inline en `PANTALLAS/` y rompería pantallas. Ver `CAMBIOS_AGUS.md` para el detalle de por qué queda como mejora futura.

---

## Circuito de atención: consentimientos opcionales, IA y evolución por enfoque

> **Estado:** octubre 2026. Requiere la migración 15. Lo que sigue en el circuito (cobros y
> liquidación, rol de recepción con chat por IA) está documentado pero todavía no programado.

### Qué hace

- **Consentimientos opcionales** (`Pantalla_Paciente/consentimiento.html`, debajo del de telemedicina): análisis de expresiones por IA, transcripción de la sesión y avisos por WhatsApp. Se aceptan y se revocan de a uno; la revocación no borra la fila (queda `Estado = 'Revocado'` con fecha). Ninguno es obligatorio para atenderse.
- **Salas registradas:** cada código de sala (turno, entrevista de admisión o sesión directa) queda en `SalaSesion` con su paciente. Con solo el código, la API sabe qué consentimientos mirar y quién puede hacer qué. Las salas anteriores se siguen resolviendo por `Turno.CodigoSala` e `InformeAdmision.CodigoSala`.
- **Interruptores en la sala del profesional** (`Pantalla_Profesional/sesion_video.html` + `js/sesion_video.js`):
  - Sin consentimiento vigente, el interruptor aparece deshabilitado (RN-10 IA, RN-15 transcripción) y el servidor rechaza encenderlo con 409.
  - El valor con el que arranca sale de **Mi perfil → Preferencias de sesión**.
  - Cada encendido y apagado queda en `SesionFuncion`.
  - Con la IA apagada, `face-api` ni siquiera se carga.
- **Indicador para el paciente** (`Pantalla_Paciente/sesion_video.html`): "Análisis de expresiones activo" o "Transcripción activa", con un botón para pedir que se apague. El paciente puede apagar, nunca encender. Si revoca el consentimiento en medio de la sesión, la función figura apagada para los dos.
- **Evolución por enfoque** (`shared/evolucion.js`, plantillas servidas por `GET /plantillas-evolucion`):
  - TCC: situación, pensamiento, emoción y conducta, con PHQ-9 y GAD-7 calculadas en el servidor; alerta si el ítem 9 del PHQ-9 es positivo.
  - Psicoanálisis: nota libre; la plantilla avisa que no sugiere transcripción.
  - Sistémica: varios consultantes, con su vínculo y si estuvieron presentes.
  - Infanto-juvenil: qué se le devuelve al tutor, nivel de confidencialidad e indicadores de riesgo.
  - General: nota libre.
  - Psiquiatría queda afuera en esta etapa.
- **Modos de registro:** manual, dictado, transcripción de la sesión como borrador, o dictado + transcripción. Todo texto generado es borrador (RN-16).
- **Firma inmutable (RN-11):** borrador → firmada, con hash SHA-256 del contenido. Una evolución firmada no se modifica ni por la API (`PUT /evoluciones/{id}` responde 409) ni por la base (triggers `TR_Evolucion_Inmutable` y `TR_Evolucion_NoBorrarFirmada`). Las correcciones van como addendum desde la historia clínica, que además avisa si el hash no coincide.
- `POST /sesiones-clinicas` descarta las emociones o la transcripción si no hay consentimiento vigente. La transcripción completa no se guarda salvo `Atencion:ConservarTranscripcion = true`.

### Configuración (`appsettings.json` → `Atencion`)

| Clave | Por defecto | Para qué sirve |
|---|---|---|
| `VersionTextoOpcionales` | `sin-validar` | Versión del texto de los consentimientos opcionales; se guarda con cada firma. |
| `ConservarTranscripcion` | `false` | `true` guarda la transcripción completa en `SesionClinica` (solo con consentimiento). |
| `MotorTranscripcion` | `NavegadorDemo` | `NavegadorDemo` usa el reconocimiento de voz del navegador, **que envía el audio a un servicio externo: solo para la demo**. `Ninguno` apaga transcripción y dictado. El servicio propio (Whisper local) se agrega como otro valor. |

### Endpoints nuevos (`Endpoints/AtencionEndpoints.cs`)

| Método | Ruta | Quién |
|---|---|---|
| GET | `/pacientes/{id}/consentimientos-opcionales` | El paciente (propios), su profesional, Configurador |
| POST | `/pacientes/{id}/consentimientos-opcionales/{clave}` y `.../{clave}/revocar` | Solo el propio paciente |
| GET / PUT | `/profesionales/{id}/configuracion-sesion` | El propio profesional, Configurador |
| GET | `/plantillas-evolucion` | Profesional, Configurador |
| POST | `/salas` | Profesional asociado al paciente, Configurador |
| GET | `/salas/{codigo}` | Paciente de la sala, su profesional, Configurador |
| PUT | `/salas/{codigo}/funciones` | Encender: profesional de la sala o Configurador, con consentimiento. Apagar: también el paciente |
| POST | `/evoluciones/por-enfoque` | Profesional asociado, Configurador |
| PUT | `/evoluciones/{id}/borrador` | Quien escribió el borrador |
| POST | `/evoluciones/{id}/addenda` | Profesional asociado, Configurador |
| GET | `/historiasclinicas/{id}/addenda`, `/historiasclinicas/{id}/integridad` | Profesional asociado, Configurador |

Pruebas nuevas: `FuncionesSesionServiceTests` y `EvolucionEnfoquesTests` (sin base de datos).

## Roles, permisos y errores (etapa 1)

> **Estado:** octubre 2026. Requiere la migración 16. Es la etapa 1 de la propuesta de roles,
> permisos y módulos: deja las tablas y los datos listos **sin cambiar lo que ve nadie**. El
> login sigue decidiendo el rol como antes; el cambio de acceso llega en la etapa 2.

### Roles, permisos y privilegios
Tres niveles que asigna el Configurador:

| Nivel | Qué es | Valores iniciales (`Clave`) |
|---|---|---|
| Rol | El área de trabajo | `profesional`, `crm`, `facturacion`, `configurador` |
| Permiso | El nivel dentro del rol; cada uno es un espacio de trabajo al ingresar | `profesional` y `auditor` (rol profesional); `crm`; `facturacion`; `configurador` |
| Privilegio | Una capacidad que se suma a un permiso | `admision` (solo en `profesional` o `auditor`) |

La base garantiza la cadena con claves foráneas: no hay permiso sin su rol ni privilegio en
un permiso que no lo admite (`PermisoPrivilegio`). El trigger `TR_UsuarioPermiso_RequiereProfesional`
(RN-17) impide dar el rol Profesional a un usuario sin ficha con matrícula y especialidad.

La migración carga a los usuarios que ya existen con el mismo acceso que hoy: profesionales →
permiso `profesional`; usuarios que no son paciente ni profesional → `configurador`. Nadie
recibe `auditor` ni `admision` automáticamente. Para ver qué tiene cada usuario:
```sql
SELECT * FROM vw_UsuarioAccesos;
```

### Especialidades
`Especialidad` suma `Disciplina`, `Tipo` (`Especialidad` / `Servicio`) y
`RequiereMatriculaEspecialista`. Las filas que en realidad son servicios de derivación
("Admision", "Atencion Domiciliaria", etc.) pasan a `Servicio`: siguen sirviendo a las reglas
de derivación, pero no se ofrecen como especialidad de un profesional. Las especialidades de
psicología son las seis del Reglamento de Especialidades del Colegio de Psicólogos de Córdoba
(Res. Junta de Gobierno 083/16); las disciplinas, las de la Ley 26.657, art. 8. Al final, la migración lista los profesionales a completar (sin
matrícula o con un servicio como especialidad).

### Errores con código
Todo error de la API (excepción o respuesta 5xx) recibe un código como `7F3A-91C2`. La
pantalla lo muestra ("…Código: 7F3A-91C2.") y el detalle queda en:
- la tabla `LogError`: `SELECT * FROM LogError WHERE Codigo = '7F3A-91C2';`
- el archivo `logs/errores-AAAAMMDD.log` (una línea JSON por error), por si la base no responde;
- la consola de `dotnet run`.

No se guarda el cuerpo de los pedidos; DNI, CUIL, teléfonos y correos se enmascaran.
En Desarrollo, la respuesta incluye además un `detalle` corto para no tener que ir a la base.
Código: `Services/RegistroErrores.cs`.

### Versión de esquema
`VersionEsquema` registra cada migración aplicada (las 1 a 15 las detecta la 16 por los
objetos que crean). Además, la API revisa al arrancar y, si a la base le falta una migración,
responde **503** con un mensaje que dice cuál falta, en lugar de un error 500 suelto. Se
recupera sola a los 30 segundos de correr la migración, sin reiniciar. Si la base no responde,
no bloquea nada. Se puede apagar con `"Esquema": { "Verificar": false }`.
Código: `Services/VerificadorEsquema.cs`. **Cada migración nueva se agrega a esa lista con su
marcador** (la prueba `VerificadorEsquemaTests` falla si un archivo de `migrations/` no está).

Pruebas nuevas: `VerificadorEsquemaTests` y `RegistroErroresTests`. Guía paso a paso:
[`docs/PRUEBA_ETAPA1_PERMISOS.md`](docs/PRUEBA_ETAPA1_PERMISOS.md).

---

## Acceso por módulo y auditoría (etapa 2)

> **Estado:** octubre 2026. Requiere las migraciones 16 y 17. Es la etapa 2 de la propuesta de
> roles, permisos y módulos. El reparto de funciones del Configurador está en la
> [etapa 3](#reparto-de-funciones-entre-módulos-etapa-3).

### Ingreso
- La pantalla de ingreso tiene una pestaña por rol: **Paciente**, **Profesional**, **CRM**,
  **Facturación** (Administración de facturación y pagos) y **Configurador**. La pestaña viaja
  al servidor (`POST /login` con `modulo`): si el usuario tiene ese rol, entra directo; si no,
  recibe 403 con "Tu usuario no tiene el rol …".
- Para el equipo, el acceso sale de los roles, permisos y privilegios de la migración 16, ya no
  de "no es paciente ni profesional → configurador". Un usuario sin rol ni permiso recibe
  **403** con el aviso "Tu usuario todavía no tiene permisos asignados…" y no obtiene token.
- Cada rol es un **módulo**. Profesional y Profesional auditor son el mismo módulo (pestaña
  Profesional). Quien tiene más de un rol entra por la pestaña que elige y después cambia con
  **Cambiar de módulo**, en el menú del usuario, sin volver a iniciar sesión.
- `POST /login` devuelve lo de siempre (`rol`, `idUsuario`, `idProfesional`, `token`) más
  `modulo`, `modulos`, `permisos` y `privilegios`. Si se llama sin `modulo` y el usuario tiene
  más de uno, devuelve `requiereModulo: true` y un **token de selección** de 5 minutos, sin roles,
  que solo sirve para `POST /sesion/modulo { modulo }`. Ese endpoint es también el de
  **Cambiar de módulo**.
- El token de trabajo lleva el módulo, un claim de rol por permiso (el auditor lleva
  `profesional` y `auditor`) y uno `privilegio` por privilegio. `Identidad` suma `Modulo`,
  `Permisos`, `Privilegios`, `EsAuditor` y `TienePrivilegio(...)`.
- El módulo Facturación todavía no tiene pantalla: quien lo elige ve
  `Pantalla_Modulo/en_construccion.html` (CRM tiene la suya desde la etapa 3).

### Auditoría
- **Administrativa** (`Categoria = 'Administrativa'`): cada ingreso, ingreso fallido (con el
  motivo), bloqueo y cambio de módulo, además de las altas y bajas que ya se registraban.
- **Clínica** (`Categoria = 'Clinica'`): cada apertura o consulta de historia clínica,
  evoluciones, eventos de crisis, sesiones, interacciones de IA, informe de admisión y los
  listados completos, con **en qué calidad** se accedió (`como tratante`, `como auditor`,
  `como configurador`) y desde qué módulo. Nunca el contenido.
- **Inalterable** (migración 17): un trigger rechaza cualquier `UPDATE` o `DELETE` sobre
  `LogAuditoria` y `LogError` (RN-18), cada registro guarda el hash del anterior, y
  `EXEC sp_VerificarCadenaAuditoria;` dice si la cadena está íntegra o en qué registro se rompió.
- La tercera capa es un usuario de base sin permisos de administrador para la API:
  `Scripts/crear_usuario_aplicacion.sql`. **Obligatorio en producción.**

Pruebas nuevas: `AccesoPorModuloTests`. Las pruebas que antes borraban filas de `LogAuditoria`
al terminar ya no lo hacen (la base no lo permite). Guía paso a paso:
[`docs/PRUEBA_ETAPA2_ACCESO.md`](docs/PRUEBA_ETAPA2_ACCESO.md).

---

## Reparto de funciones entre módulos (etapa 3)

> **Estado:** octubre 2026. Requiere las migraciones 16, 17 y 18. Guía paso a paso:
> [`docs/PRUEBA_ETAPA3_REPARTO.md`](docs/PRUEBA_ETAPA3_REPARTO.md).

El Configurador tenía funciones que no le corresponden (historias clínicas, reportes, admisión,
chats). En esta etapa cada función pasa al módulo, permiso o privilegio que la definió la
organización, y **el servidor lo controla en cada endpoint** (`Services/Politicas.cs`): ocultar
un menú no protege nada.

| Quién | Qué hace | Qué ya no puede |
| --- | --- | --- |
| **Configurador** | Usuarios (Permisos, Cuentas, Solicitudes de registro, Recuperación de contraseña), Configuración (roles, especialidades, grupos, reglas de derivación, coberturas) y la **auditoría administrativa** (cuentas, ingresos, cambios de permisos) | Historias clínicas, evoluciones, eventos de crisis, interacciones de IA, reportes, admisión, asignaciones, turnos, agendas y chats |
| **Profesional** | Lo suyo: sus pacientes, su agenda, sus casos, la sala | Pacientes que no atiende |
| **Profesional + Admisión** (privilegio) | Admisiones, primera entrevista (con su propia sesión, ya no como "ADM-001"), solicitudes de cambio de profesional, asignaciones; lee la historia de los pacientes **mientras están en admisión** | — |
| **Profesional auditor** | Todo lo del profesional, más: lectura de **todas** las historias clínicas, evoluciones, eventos de crisis e interacciones de IA, reportes de la organización y la **auditoría clínica** | Escribir en historias de pacientes que no atiende |
| **CRM y atención al cliente** | Chats, escalamientos, etiquetas, casos y derivaciones de casos (antes, el grupo CRM del Configurador) | Historias clínicas (nunca) |
| **CRM + Gestión de agendas** (privilegio, migración 18) | Turnos y horarios de trabajo de todos los profesionales | — |

- **Panel de gestión:** `Pantalla_Configurador/dashboard.html` lo usan el Configurador, CRM y
  las secciones Admisión y Auditoría del módulo Profesional. Cada módulo ve solo lo suyo; en el
  panel profesional, los grupos Admisión y Auditoría aparecen si la sesión tiene el privilegio o
  el permiso.
- **Permisos por usuario** (Configurador › Usuarios › Permisos): módulo, permisos y
  privilegios de cada persona del equipo. `GET /usuarios/accesos`, `GET|PUT /usuarios/{id}/accesos`.
  Reglas (`Services/AccesosEdicion.cs`): Profesional y Profesional auditor exigen ficha con
  matrícula (RN-17); un privilegio solo va sobre un permiso que lo admite; nadie se quita su
  propio permiso de Configurador; Facturación todavía no se asigna desde la pantalla. Los
  cambios rigen desde el próximo ingreso de esa persona.
- **Auditoría en dos partes:** `GET /auditoria` devuelve la administrativa al Configurador y la
  clínica al auditor; la categoría la decide el servidor por el token. Cuentan como clínicos
  también los registros de asignaciones, derivaciones, admisión, reportes y turnos (así los
  registros viejos, que ya no se pueden corregir, quedan del lado correcto). Los cambios de
  permisos los registra la base con triggers (migración 18): con el usuario que los hizo desde
  la pantalla, o con el inicio de sesión de SQL Server si se hicieron a mano.
- **Agujero cerrado:** los seis endpoints `/reportes/profesional/...` y
  `/reportes/paciente/{id}/evolucion-emocional` **no pedían sesión** (cualquiera podía leer las
  emociones registradas de un paciente). Ahora: el propio profesional o el auditor.
- `/pacientes/{id}/informe-admision`: del equipo, solo Admisión, el auditor o quien atiende al
  paciente (antes, cualquier usuario del equipo).
- La barra superior saluda con `GET /sesion/yo` (sirve para cualquier módulo).

Pruebas nuevas: `RedistribucionEtapa3Tests` (políticas y reglas del editor, sin base) y casos
nuevos en `CierreSeguridadTests`, `PertenenciaConversacionTests` y `FuncionesSesionServiceTests`.
Las pruebas de chats usan ahora un token de CRM. Matriz completa de lo que cambió:
[`docs/MATRIZ_ENDPOINTS.md`](docs/MATRIZ_ENDPOINTS.md), sección "Etapa 3".

---

## Alta de usuarios del equipo

> **Estado:** octubre 2026. Requiere la migración 19. Guía: [`docs/PRUEBA_ALTA_EQUIPO.md`](docs/PRUEBA_ALTA_EQUIPO.md).

- **Configurador › Usuarios › Registrar usuario:** nombre, apellido, CUIL (con dígito
  verificador), teléfono, email, usuario y rol. Si es **Profesional**, se elige *Profesional*
  (ve solo sus pacientes) o *Profesional auditor* (todos los pacientes, reportes y registros),
  se le puede agregar **Admisión**, y se cargan matrícula, jurisdicción y especialidad principal
  (RN-17). CRM puede llevar **Gestión de agendas**. Cuenta, ficha, permisos e invitación se
  crean en una sola transacción (`Services/AltaEquipo.cs`, `POST /usuarios/equipo`).
- **Invitación por mail:** la cuenta queda *Pendiente* y no puede ingresar hasta que la persona
  abre el link (`PANTALLAS/activar-cuenta.html`), confirma y elige su contraseña (8 o más, con
  letras y números). El link sirve una vez y vence a las 72 horas; en la base solo queda el hash
  del token. *Reenviar invitación* (Usuarios › Permisos) anula el link anterior. Si el mail no
  sale y el entorno es Development, la respuesta muestra el link para probar; en producción no.
- **Configurador › Usuarios › Privilegios:** cada privilegio con quién lo tiene, y agregarlo o
  quitarlo a quien tiene un rol que lo admite (`GET /privilegios`,
  `POST|DELETE /usuarios/{id}/privilegios`). Mismas reglas y auditoría que el editor de permisos.
- Todo queda en la auditoría administrativa: alta, invitación reenviada, cuenta activada y cada
  permiso o privilegio (triggers de la migración 18).
- Pendiente: las contraseñas siguen con SHA-256 sin sal (igual que el resto de las cuentas);
  pasar todas a PBKDF2 antes de producción.

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
- Las notificaciones internas se ven solo en la campana del configurador; mostrarlas en el panel del profesional queda pendiente. Al administrador no se le manda mail (no hay un mail de administrador configurado).
- `DELETE /turnos/{id}` y el `POST /turnos` genérico no disparan avisos.
- La reprogramación y el modal de turno de la web arman horarios de 60 minutos fijos en lugar de usar `IntervaloMinutos`.
- Actualización por polling (incremental y adaptativo); sin WebSockets. No hay indicador de "escribiendo…": el canal no informa cuándo el paciente está escribiendo.
- La marca de leído es compartida por todo el equipo (como una bandeja de WhatsApp Business): si alguien abre la conversación, deja de figurar como no leída para todos.
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
