# Matriz de endpoints — módulo de mensajería/Chats

Este archivo no existía antes de "Mejora de chats y centrado" (`Cambios-Agus-jwt` tampoco lo
tenía). Se crea acotado al módulo que tocó este trabajo (`Endpoints/MensajeriaEndpoints.cs`),
no a toda la API — extenderlo al resto de los endpoints de la app es un trabajo aparte, de
otro alcance.

Todos los endpoints del grupo `/mensajeria` (salvo los dos marcados "público") exigen sesión
de **Profesional o Configurador** (`RequireAuthorization` a nivel de grupo, en
`MapMensajeria`). Esa es la fila "Rol" por default; solo se repite cuando un endpoint agrega
algo *encima* de eso.

## Pertenencia de conversaciones (actualizado — cierre del alcance abierto en A1)

La bandeja **dejó de ser compartida sin filtro**: los chats pueden contener información de
salud mental, así que quién ve qué conversación ahora lo decide el servidor en cada pedido,
nunca la interfaz. Un único punto de decisión, `Services/VisibilidadConversacion.cs`, resuelve
la regla (mismo patrón que `AutorizacionCasos`):

- **Escalada (`DerivadaHumano`/`Urgente`) y sin asignar** (`Conversacion.IdProfesionalAsignado
  IS NULL`): la ve cualquier Profesional + Configurador — para que cualquiera pueda tomarla.
- **Escalada y asignada**: la ve **solo quien la tiene asignada** + Configurador. Para
  cualquier otro profesional, **404** (nunca 403: no confirma que existe).
- **Todo lo demás** (`Activa`, `Cerrada` — nunca escaló o ya se resolvió el escalamiento): la ve
  el **equipo tratante vigente del paciente** (`PacienteProfesional`) + Configurador. Sin
  paciente identificado todavía, solo Configurador.
- Paciente (rol) nunca pasa por este módulo: sus propias conversaciones las resuelve
  `IServicioMensajes`/`BotReservasService` por teléfono.

`Conversacion.IdProfesionalAsignado` es **propia de la conversación** — quién atiende *este
hilo*, no el profesional tratante del paciente (`PacienteProfesional`, sin relación) ni el
`Caso` de Fase 2. Se mueve con **Tomar** (atómico, cualquier profesional, solo si está sin
asignar), **Devolver** (quien la tiene, o Configurador, vuelve a sin asignar) y **Asignar**
(Configurador, directo a cualquiera). Las tres acciones solo tienen efecto mientras la
conversación está escalada — es el único estado donde esta columna decide algo; no se ofrecen
para `Activa`/`Cerrada`. El detalle completo (por qué esta columna y no `Caso`/
`PacienteProfesional`, qué pasa al reabrir/cerrar, por qué el catálogo de etiquetas pasó a ser
solo Configurador) está en `CAMBIOS_AGUS.md`, sección "Pertenencia de conversaciones".

**Límite conocido, por el diseño sin WebSocket del módulo** ("Tiempo real por polling
eficiente... Sin WebSocket", ver cabecera de `bandeja.js`): una sesión que ya tenía una
conversación visible puede tardar hasta la próxima vuelta completa del polling (20 s) en
notar que otra sesión se la llevó (Tomar) o que el Configurador la reasignó — el servidor
nunca se la vuelve a servir, pero el *caché local* de esa sesión no se entera hasta esa vuelta.
`YS.bandeja.actualizar()` (refresco manual) fuerza esa vuelta completa en el acto.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/capacidades` | Público | — | Fuera del grupo a propósito: lo consulta cualquier pantalla antes de saber quién sos. |
| POST | `/mensajeria/simulado/entrante` | Público | — | Simula un mensaje entrante (equivalente al webhook real). No tiene, ni tendría sentido, sesión del otro lado. |
| GET | `/mensajeria/simulado/conversacion` | Público | — | Idem — el simulador del lado paciente todavía no tiene sesión. |
| GET | `/mensajeria/conversaciones` | Profesional, Configurador | **Sí — filtra la lista** | `VisibilidadConversacion.Filtrar`. `idEtiqueta` filtra encima de lo ya visible (Paso 5). El buscador del frontend filtra client-side sobre esta misma lista, así que queda cubierto gratis. |
| GET | `/mensajeria/conversaciones/{id}` | Profesional, Configurador | **Sí — 404** | |
| GET | `/mensajeria/conversaciones/{id}/paciente` | Profesional, Configurador | **Sí — 404** | Panel del lead: datos administrativos únicamente. Devuelve también `idProfesionalAsignado`/`nombreProfesionalAsignado` (de la conversación, no del paciente). Único endpoint llamado solo al abrir/refrescar el panel (nunca por el polling de la lista) — ahí se audita la apertura (Paso 4: quién, conversación y paciente por id, cuándo; nunca el nombre ni contenido). |
| GET | `/mensajeria/conversaciones/{id}/mensajes` | Profesional, Configurador | **Sí — 404** | |
| PUT | `/mensajeria/conversaciones/{id}/leida` | Profesional, Configurador | **Sí — 404** | |
| POST | `/mensajeria/conversaciones/{id}/responder` | Profesional, Configurador | **Sí — 404** | Audita (acción + ids, nunca el texto). |
| PUT | `/mensajeria/conversaciones/{id}/cerrar` | Profesional, Configurador | **Sí — 404** | Audita. |
| PUT | `/mensajeria/conversaciones/{id}/reabrir` | Profesional, Configurador | **Sí — 404** | 400 si no estaba Cerrada. Audita. |
| PUT | `/mensajeria/conversaciones/{id}/asignar` | **Solo Configurador** | — (Configurador ve todo) | Reescrito: ya no pasa por `Caso`/`AsignacionService` — asigna directo `Conversacion.IdProfesionalAsignado`, sin que haga falta un caso abierto. 400 si la conversación no está escalada. Audita (ids). |
| PUT | `/mensajeria/conversaciones/{id}/tomar` | Profesional | **Sí — 404** (más 400/409 propios) | **Nuevo (Paso 3).** Atómico: `UPDATE ... WHERE IdProfesionalAsignado IS NULL` — si dos la toman a la vez, una gana (204) y la otra pierde la carrera (**409**, "ya la tomó otra persona"), nunca dos dueños. 400 si no está escalada o si la pide un Configurador (usa `/asignar`). Ya asignada a quien la pide: 204 idempotente. Audita. |
| PUT | `/mensajeria/conversaciones/{id}/devolver` | **Quien la tiene asignada, o Configurador** | **Sí — 404**, más 403 si no es el dueño ni Configurador | **Nuevo (Paso 3).** Vuelve a sin asignar. 400 si ya estaba sin asignar. Motivo de la misma lista cerrada que `/asignar` (`MotivoReasignacion.Categorias`). Audita. |
| GET | `/mensajeria/conversaciones/{id}/notas` | Profesional, Configurador | **Sí — 404** | Nunca al paciente; el texto nunca se audita. |
| POST | `/mensajeria/conversaciones/{id}/notas` | Profesional, Configurador | **Sí — 404** | |
| DELETE | `/mensajeria/conversaciones/{id}/notas/{idNota}` | **El autor, o Configurador** | **Sí — 404** (de la conversación, antes de mirar quién escribió la nota) | 403 para cualquier otro profesional que sí puede ver la conversación pero no escribió la nota. |
| GET | `/mensajeria/etiquetas` | Profesional, Configurador | — | Catálogo: lectura abierta (cualquiera que vea una conversación puede necesitar mostrarle sus chips). |
| POST | `/mensajeria/etiquetas` | **Solo Configurador** | — | **Cambiado (Paso 5):** antes tan abierto como `/preguntas-frecuentes`; el catálogo de etiquetas de chats con información de salud mental pasa a administrarlo solo Configurador (pantalla propia, `#etiquetas` en el panel). 409 si el nombre ya existe. |
| PUT | `/mensajeria/etiquetas/{id}` | **Solo Configurador** | — | Cambiado (Paso 5). |
| DELETE | `/mensajeria/etiquetas/{id}` | **Solo Configurador** | — | Cambiado (Paso 5). Baja lógica (`Activa=0`): no borra las ya puestas en conversaciones. |
| POST | `/mensajeria/conversaciones/{id}/etiquetas/{idEtiqueta}` | Profesional, Configurador | **Sí — 404** | Asignar una etiqueta a una conversación puntual es pertenencia de la *conversación*, no del catálogo — sigue abierto a quien pueda verla. Idempotente. |
| DELETE | `/mensajeria/conversaciones/{id}/etiquetas/{idEtiqueta}` | Profesional, Configurador | **Sí — 404** | |
| GET | `/mensajeria/respuestas-rapidas` | Profesional, Configurador | — | Cada quien ve las globales + las propias (nunca las de otro profesional: lo filtra el `WHERE` del DAO). No es pertenencia de conversación — revisado, no necesita filtro adicional. |
| POST | `/mensajeria/respuestas-rapidas` | Profesional, Configurador | **Dueño fijo según quién crea** | Configurador → global (`IdProfesional=NULL`); Profesional → propia. Ninguno elige el dueño. |
| PUT | `/mensajeria/respuestas-rapidas/{id}` | Profesional, Configurador | **Dueño de la fila** | Global → solo Configurador (403 a Profesional); propia → solo ese profesional (403 a Configurador y a cualquier otro). |
| DELETE | `/mensajeria/respuestas-rapidas/{id}` | Profesional, Configurador | Dueño de la fila | Mismo criterio que PUT. Baja lógica. |
| GET | `/mensajeria/notificaciones-internas` | Profesional, Configurador | **Sí — filtra la lista** | **Reescrito (Paso 2):** las que referencian una conversación puntual (`IdConversacion` no nulo) se filtran re-resolviendo su pertenencia **actual** — no un dato grabado cuando se creó el aviso. Si ya la tomó otra persona, deja de aparecer aunque el aviso sea de antes. Las de turnos (sin conversación) no cambian: ya venían con su propio `IdProfesional`. |
| PUT | `/mensajeria/notificaciones-internas/{id}/leida` | Profesional, Configurador | **Sí — 404** si referencia una conversación no visible | Defensa en profundidad: en el flujo normal la lista ya viene filtrada, así que no debería llegarse a pedir el id de un aviso ajeno. |
| POST | `/mensajeria/recordatorios/ejecutar` | Profesional, Configurador | — | Preexistente (dispara el servicio de recordatorios fuera de su intervalo; pensado para pruebas/demo). |
| GET | `/mensajeria/preguntas-frecuentes` | Profesional, Configurador | Ninguna | Preexistente — sin relación con conversaciones puntuales. |
| POST/PUT/DELETE | `/mensajeria/preguntas-frecuentes[/{id}]` | Profesional, Configurador | Ninguna | Preexistente. |

**Probado:**
- Matriz de rol A3 (401/403/404/200/201/204) en `docs/pruebas-ui-cambios-agus/p10_chats_a3.py`
  (no versionado) y permanente en `YoSoy.Tests/ChatsGestionTests.cs`.
- Pertenencia (este trabajo): matriz con dos profesionales reales + Configurador + Paciente,
  toma simultánea (test concurrente: gana uno, el otro 409, dueño único en la base),
  reasignación, notificaciones filtradas, auditoría de apertura solo con ids — permanente en
  `YoSoy.Tests/PertenenciaConversacionTests.cs` (11 tests). Con dos sesiones de Profesional de
  verdad en el navegador (aislamiento de lista/contador/toast, 404 con gracia, Devolver,
  reasignación, pantalla de etiquetas) en
  `docs/pruebas-ui-cambios-agus/p14_pertenencia.py` (no versionado, 21/21).
