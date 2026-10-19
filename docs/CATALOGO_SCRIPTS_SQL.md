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
