# Catálogo de scripts SQL del repo

Armado para preparar `docs/produccion/01-produccion.sql`. Lista todo lo que hay,
en qué orden se aplica cada cosa que sí es esquema, y por qué el resto no va a
producción. No es un inventario histórico completo del proyecto — es la base
para decidir qué entra en una base nueva y qué no.

## 1. Esquema — orden de ejecución

Esto es lo que `01-produccion.sql` consolida (misma forma final, un solo
archivo). El orden real, si alguien prefiriera correrlo en pasos sueltos en
vez del script consolidado, es:

| # | Origen | Qué agrega |
|---|---|---|
| 0 | Rama `DataBase`, archivo `Script de la Base de Datos` | Las 25 tablas base: `Agenda`, `Cobertura`, `Consentimiento`, `Derivacion`, `Especialidad`, `EstadoTurno`, `EventoCrisis`, `Evolucion`, `HistoriaClinica`, `InteraccionIA`, `LogAuditoria`, `ObraSocial`, `Paciente`, `PacienteProfesional`, `PacienteTutor`, `Profesional`, `RecuperacionPassword`, `Rol`, `TipoConsentimiento`, `Turno`, `Tutor`, `Usuario`, `UsuarioPaciente`, `UsuarioProfesional`, `UsuarioRol`, con sus PK/FK/UNIQUE/DEFAULT. |
| 1 | `migrations/add_columnas_faltantes.sql` | `Agenda.IntervaloMinutos`, `Turno.ConfirmadoPaciente`, `Turno.CodigoSala`, `PacienteProfesional.EstadoAsignacion` |
| 2 | `migrations/add_creadopor_turno.sql` | `Turno.CreadoPor` — ya estaba en el script base de `DataBase` (no agrega nada nuevo hoy, solo queda idempotente) |
| 3 | `migrations/add_token_profesional.sql` | `Profesional.Token` — idem, ya estaba en el script base |
| 4 | `migrations/add_token_recuperacion.sql` | `RecuperacionPassword.Token`/`TokenExpiracion` — idem, ya estaba en el script base |
| 5 | `migrations/add_mensajeria.sql` | Tablas `Conversacion`, `MensajeChat`, `Recordatorio`, `PreguntaFrecuente`, `NotificacionInterna` |
| 6 | `migrations/add_consentimiento_telemedicina.sql` | `Consentimiento.FechaFirma` de `DATE` a `DATETIME`; **el único dato obligatorio**: el `TipoConsentimiento` de telemedicina (RN-02, buscado por nombre exacto) |
| 7 | `migrations/add_lectura_conversacion.sql` | `Conversacion.IdUltimoMensajeLeido` |
| 8 | `migrations/alter_logauditoria_idusuario_nullable.sql` | `LogAuditoria.IdUsuario` admite NULL |
| 9 | `migrations/add_tablas_sin_migracion_previa.sql` | Tablas `InformeAdmision`, `Notificacion`, `SesionClinica` |
| 10 | `migrations/add_grupos_casos_derivacion.sql` | Tablas `Grupo`, `ProfesionalGrupo`, `Caso`, `CasoDerivacion`, `ReglaDerivacion`, `HistorialAsignacion` |
| 11 | `migrations/add_link_publico_profesional.sql` | `Profesional.Slug`, `LinkPublicoActivo` |
| 12 | `migrations/alter_casoderivacion_motivo_cerrado.sql` | `CasoDerivacion.Nota` |
| 13 | `migrations/add_chats_notas_etiquetas_respuestas.sql` | Tablas `NotaInternaConversacion`, `EtiquetaConversacion`, `ConversacionEtiqueta`, `RespuestaRapida` |
| 14 | `migrations/add_conversacion_asignacion.sql` | `Conversacion.IdProfesionalAsignado` |
| 15 | `migrations/add_consentimiento_video.sql` | Tabla `ConsentimientoVideo`; `SesionClinica.IdConsentimientoVideo`, `EmocionesAnuladasUtc`, `EmocionesAnuladasMotivo` (consentimiento de la lectura de emociones de la sala de video, PR 6) — requiere el 8 y el 9 |
| 16 | `migrations/add_consentimiento_whatsapp.sql` | Tabla `ConsentimientoWhatsApp` (consentimiento de los avisos de turnos por WhatsApp: una fila por aceptación, con la versión y el hash del texto, el número y las fechas en UTC) con su índice único filtrado y sus dos claves foráneas. Solo agrega: no toca ninguna tabla que ya exista. Requiere `Paciente` y `Usuario`. **Solo hace falta si se prende `Mensajeria__AvisosPorWhatsApp`** (apagada por defecto: con ella apagada la API no toca la tabla) |
| 17 | `migrations/add_chat_personal_entrantes.sql` | `MensajeChat.IdExterno` (el id del mensaje en WhatsApp, con un índice único filtrado `(IdConversacion, IdExterno)` para no guardar dos veces el mismo), `MensajeChat.Tipo` (`texto`, `audio`, `imagen`, `video`, `documento`, `sticker`, `ubicacion`, `contacto` u `otro`, con la restricción `CK_MensajeChat_Tipo` y por defecto `'texto'`) y `Conversacion.NombreContacto` (el nombre del perfil de WhatsApp): lo que el chat de WhatsApp para el personal necesita para guardar completos los mensajes que entran. Solo agrega, es idempotente y no toca la 16. **Hace falta solo con `Mensajeria__ModoAtencion=Personal`**: con `Asistente` (por defecto) la API no usa las columnas |
| 18 | `migrations/add_registro_errores_version_esquema.sql` | Tablas `VersionEsquema` (una fila por migración aplicada; registra las 1 a 17 como `Detectada` por un marcador) y `LogError` (cada error de la API con un código corto que ve el usuario: `SELECT * FROM LogError WHERE Codigo = '7F3A-91C2'`; nunca el cuerpo del pedido, y DNI, CUIL, teléfonos y correos enmascarados). Del trabajo de Alearmas01 (rama `nuevo-circuito1`), separado y renumerado. Solo agrega |
| 19 | `migrations/add_auditoria_inalterable.sql` | `LogAuditoria.Categoria` (`Administrativa` / `Clinica`; las filas viejas de acceso a datos clínicos pasan a `Clinica` ahora), `Espacio`, `Hash` y `HashAnterior`; triggers: cada registro nuevo se encadena con el hash del anterior (`TR_LogAuditoria_Encadenar`) y `LogAuditoria` y `LogError` no se pueden modificar ni borrar (`TR_LogAuditoria_SoloAgregar` y `TR_LogError_SoloAgregar`, errores 50018 y 50019); `sp_VerificarCadenaAuditoria` y `fn_HashAuditoria`. Requiere la 18. **Después de esta migración una fila de `LogAuditoria` ya no se puede corregir ni borrar, y un usuario con filas ahí no se puede borrar** |
| 20 | `migrations/add_permisos_roles_privilegios.sql` | Roles, permisos y privilegios (etapa 2 de la integración de Alearmas01): `Rol.Clave`, tablas `Permiso`, `Privilegio`, `PermisoPrivilegio`, `UsuarioPermiso`, `UsuarioPrivilegio`, `Organizacion` y `ConfiguracionOrganizacion`; `Usuario.Nombre/Apellido/Cuil`, `Profesional.JurisdiccionMatricula/MatriculaEspecialista`, `Especialidad.Disciplina/Tipo/RequiereMatriculaEspecialista` (las cinco especialidades que son servicios pasan a `Tipo = 'Servicio'`); el trigger `TR_UsuarioPermiso_RequiereProfesional` (RN-17) y la vista `vw_UsuarioAccesos`. Solo agrega; **no asigna permisos a nadie** (eso es la 23). Requiere la 18 y la 19 |
| 21 | `migrations/add_privilegio_agendas_auditoria_permisos.sql` | El privilegio `gestion_agendas` (solo para CRM) y tres triggers (`TR_UsuarioRol_Auditar`, `TR_UsuarioPermiso_Auditar`, `TR_UsuarioPrivilegio_Auditar`) que dejan en `LogAuditoria` todo cambio de roles, permisos y privilegios, se haga desde la pantalla o directo en la base. Requiere la 19 y la 20 |
| 22 | `migrations/add_alta_equipo_invitaciones.sql` | `Usuario.Telefono` y la tabla `InvitacionCuenta` (invitación por mail para activar la cuenta de alguien del equipo; solo se guarda el hash del token; vence a las 72 horas). Requiere la 20 y la 21 |
| 23 | `migrations/mapear_usuarios_existentes_a_permisos.sql` | **Datos:** da permisos a los usuarios que ya existen (profesional con matrícula y especialidad -> `profesional`; quien no es paciente ni profesional y está activo -> `configurador` y `crm`; pacientes, nada) y lista al que queda sin permiso. Corre **una sola vez** (queda registrada la versión 23). Requiere la 20, la 21 y la 22 |
| 24 | `migrations/add_debe_cambiar_password.sql` | `Usuario.DebeCambiarPassword` (`BIT NOT NULL`, por defecto 0; las cuentas que ya existen quedan en 0). La pone en 1 el Configurador al restablecer una contraseña (Configurador › Usuarios) y la baja el propio usuario al elegir su clave nueva: mientras vale 1 el ingreso no entrega sesión, solo un token de 5 minutos para elegir la clave. Aditiva e idempotente; registra la versión 24 con origen `Script`. Requiere la 18. Deshacer: `docs/produccion/deshacer-migracion-24.sql` |
| 25 | `migrations/add_conversacion_ia.sql` | `Conversacion.IaActiva` (`BIT NOT NULL`, por defecto 0): el interruptor de IA de cada conversación de Chats (Chats v2, Fase 1: se guarda y se muestra; todavía no hay IA que responda). **Opcional**: la API anda sin ella (el interruptor por conversación responde 409). Las conversaciones que ya existen quedan en 0. El interruptor global no necesita esquema (fila `Chats.IaActiva` de `ConfiguracionOrganizacion`). En una transacción; registra la versión 25 con origen `Script`. Requiere la 18. Deshacer: `docs/produccion/deshacer-migracion-25.sql` |
| 26 | `migrations/add_cierre_sesion.sql` | Cierre de sesión (el panel de después de la videollamada): `SesionClinica.ClaveCierre` (`UNIQUEIDENTIFIER NULL`, índice único filtrado por profesional y clave: repetir el mismo cierre no duplica nada), `SesionClinica.TurnoRealizadoUtc` (`DATETIME2(3) NULL`, cuándo el cierre pasó el turno a Realizado; solo trazabilidad), `SesionClinica.IdEvolucion` (`INT NULL`, FK a `Evolucion` e índice único filtrado: el vínculo manual sesión-evolución, que va de este lado para no tocar la tabla `Evolucion`) e `IX_SesionClinica_Turno` (filtrado, no único). No toca ninguna fila existente. **Opcional**: la API anda sin ella para los pedidos de siempre; con `claveCierre` o `confirmaRealizacion` responde 409 `falta-migracion-26`. En una transacción; registra la versión 26 con origen `Script`. Requiere la 18. Deshacer: `docs/produccion/deshacer-migracion-26.sql` |
| 27 | `migrations/add_alta_derivacion_cierre.sql` | Alta clínica y derivación interna asistidas desde el cierre de sesión: `Caso.Estado` admite **'En alta'** (el único estado nuevo; queda fuera de la carga del profesional), tabla **`AltaClinica`** (informe de alta, fecha y plan de control guardado — cuántos contactos, cada cuántos días y fecha del primero — una por caso) y columnas nuevas en **`Derivacion`** (`Origen` 'Solicitud'/'Cierre', `IdCaso`, `Observaciones`, `IdSesion` y quién/cuándo respondió el destino y resolvió Admisión) con un índice único filtrado: una derivación del cierre en curso por caso. No toca ninguna fila existente. **Opcional**: sin ella el alta y la derivación asistidas responden 409 `falta-migracion-27`. Requiere la 18 y la 26. En una transacción; registra la versión 27 con origen `Script`. Deshacer: `docs/produccion/deshacer-migracion-27.sql` (se niega a borrar altas o derivaciones reales) |
| 28 | `migrations/add_consentimientos_circuito.sql` | Consentimientos dentro del circuito: `Consentimiento.IdTutorFirmante` (quién firmó el consentimiento de tratamiento y telemedicina de una persona **menor** de edad o con tutor; las firmas que ya existen quedan en NULL) y la tabla **`AceptacionVideollamada`** (la aceptación, sesión por sesión, de usar la cámara y el micrófono: fecha/hora UTC, versión y hash del texto, revocable; un índice único filtrado deja una sola vigente por sesión y paciente). No toca ninguna fila existente ni el consentimiento de la lectura de emociones (`ConsentimientoVideo`). **Opcional**: sin ella la API anda como antes (no exige firma de tutor ni aceptación de la videollamada). Requiere la 18. En una transacción; registra la versión 28 con origen `Script`. Deshacer: `docs/produccion/deshacer-migracion-28.sql` (se niega a borrar aceptaciones o firmas de tutor guardadas) |
| 29 | `migrations/add_admision_agenda_entrevista.sql` | `Agenda.EsAdmision` (`BIT NOT NULL`, por defecto 0: franjas de "Mis horarios de trabajo" para entrevistas de admisión, que no se ofrecen como turnos) e `InformeAdmision.IdProfesionalEntrevista` (`INT NULL`, FK a `Profesional`: quién programó la entrevista). La API de esta versión la necesita: aplicarla antes de publicar. Deshacer: `docs/produccion/deshacer-migracion-29.sql` |
| 30 | `migrations/add_vigencia_agenda.sql` | `Agenda.FechaInicio` y `Agenda.FechaFin` (`DATE NULL`: desde y hasta qué día vale cada franja de "Mis horarios de trabajo"; NULL = sin límite) y `CK_Agenda_Vigencia` (el fin no puede ser anterior al inicio). La API de esta versión la necesita: aplicarla antes de publicar. Deshacer: `docs/produccion/deshacer-migracion-30.sql` |

**Dato mínimo obligatorio** (no esquema, pero sin él una funcionalidad entera
no tiene con qué andar): la fila de `TipoConsentimiento` para telemedicina
(paso 6 de la tabla). `01-produccion.sql` la incluye.

**Contenido de arranque, opcional** (no es dato de negocio ni de demo —
reemplaza textos que antes vivían fijos en el código/HTML — pero tampoco hace
falta para que la app funcione): las 4 preguntas frecuentes genéricas de
`add_mensajeria.sql` y las 6 respuestas rápidas globales de
`add_chats_notas_etiquetas_respuestas.sql`. `01-produccion.sql` las deja en
una sección aparte, bien separada y marcada, para decidir si se cargan o no.

## 2. Hallazgo: `Rol` nunca se siembra

Ningún script versionado (ni el de `DataBase` ni ninguna migración) inserta
los roles base (`Paciente`, `Profesional`, `Configurador`) en la tabla `Rol`.
**No es un bloqueante**: el login (`POST /login`) resuelve el rol mirando si
el usuario tiene una fila en `UsuarioPaciente` o `UsuarioProfesional` (si no
tiene ninguna, es Configurador) y ese rol viaja en el JWT — nunca consulta
`Rol`/`UsuarioRol`. Esa tabla solo importa si en algún momento se usa la
función administrativa de ver/asignar roles por usuario
(`GET /usuarios/{id}/roles` y afines). `01-produccion.sql` no la siembra;
si se necesita, son 3 INSERT de una línea cada uno.

## 3. Históricos, ya cubiertos por lo de arriba (no aportan nada nuevo)

Archivos sueltos en la raíz del repo, versionados (commiteados, no son
basura local), de antes de que existiera la carpeta `migrations/`:

| Archivo | Por qué no hace falta |
|---|---|
| `SQLQuery2.sql` (650 líneas) | Una foto vieja (21/5) del esquema completo — el script de `DataBase` (2/6) es más nuevo y ya lo reemplaza. |
| `Scripts/nuevas_tablas.sql` | `LogAuditoria` y las columnas de `Usuario` que agrega ya están en el script base de `DataBase`. |
| `ATENCION CONFIGURADOR.sql` | `InformeAdmision.CodigoSala` y la tabla `Notificacion` que crea ya están en `migrations/add_tablas_sin_migracion_previa.sql` (paso 9). Su `INSERT` de un profesional sintético "Equipo Admisión" (matrícula `ADM-001`) es aparte — ver sección 5. |
| `consentimientos consultas.sql` | La tabla `InformeAdmision` que crea ya está en el paso 9. Los dos `TipoConsentimiento` que inserta ("Uso del Sistema", "Tratamiento de Datos de Salud") **no son** el que usa el código hoy (`Consentimiento:TipoRequerido` pide el de "Atención por Telemedicina", paso 6) — parecen de un diseño anterior, abandonado. |

## 4. Demo, prueba o correcciones puntuales — nunca a producción

| Archivo | Qué es |
|---|---|
| `docs/credenciales-demo.sql` | Contraseña de demo para 3 usuarios — "NO EJECUTADO", explícito en el propio archivo. |
| `docs/datos-demo.sql` | Paciente/caso ficticios para el guion de `docs/DEMO.md` — "NO EJECUTADO". |
| `docs/reset-demo.sql` | Revierte lo que agregaría `datos-demo.sql`. |
| `docs/datos-prueba-grupos-fase2.sql` | Grupos de ejemplo sobre profesionales de prueba que ya existían en la base local. |
| `docs/corregir-especialidad-martin-sosa.sql` | Corrección de un dato mal cargado en la base LOCAL de desarrollo; no aplica a una base nueva, que no va a tener ese error porque no va a tener ese dato. |
| `docs/revertir-datos-prueba-turno1.sql` | Revierte una confirmación de turno hecha por error durante una prueba de interfaz en la base local. |
| `"Consultas armado de estadisticas.sql"` | 208 líneas acumuladas de INSERT/SELECT ad-hoc: usuarios, roles, profesionales, pacientes y **sesiones clínicas** de prueba. |
| `"consultas para insertar pacientes de prueba.sql"` | 3 pacientes y usuarios ficticios, contraseña `1234`. |
| `"creacion de usuarios de prueba.sql"` | Un profesional y un usuario ficticios, contraseña `1234`. |

## 5. Caso aparte, a decidir (no es ni esquema ni claramente descartable)

`ATENCION CONFIGURADOR.sql` inserta un profesional sintético "Equipo Admisión"
(`Nombre='Equipo'`, `Apellido='Admision'`, `Matricula='ADM-001'`,
`Mail='admision@yosoy.com'`) — una cuenta de triage, no un dato de prueba ni
parte del esquema. `01-produccion.sql` no la incluye; es una decisión de
producto (¿la plataforma real quiere esa cuenta por defecto?), no algo para
decidir en este catálogo.
