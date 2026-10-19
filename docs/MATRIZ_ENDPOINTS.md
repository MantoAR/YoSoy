# Matriz de endpoints — toda la API

Reemplaza a la versión anterior, acotada solo a `/mensajeria` (de "Mejora de chats y
centrado"). Esta vuelta (`cierre-seguridad`) cubre **todos** los endpoints del servidor, y
la columna "Rol" de cada tabla sale **por reflexión** sobre la tabla de rutas real
(`EndpointDataSource`, ver `YoSoy.Tests/ReflexionEndpoints.cs`), no de leer el código a
mano — así esta tabla no se puede desactualizar en silencio sin que algo la contradiga. La
columna "Pertenencia" sí es juicio: la reflexión solo puede ver "autenticado" y "con qué
rol", nunca "solo el dueño del recurso" — esa parte la decide el código de cada endpoint.

## Guardia automática (Cierre de seguridad, Paso 1)

`YoSoy.Tests/GuardiaEndpointsTests.cs` enumera la tabla de rutas real por reflexión y
**falla** si aparece un endpoint sin `RequireAuthorization` que no esté en su lista
explícita de públicos (`Publicos`, con el motivo de cada uno al lado). Un endpoint nuevo
sin decidir rompe la batería en vez de quedar abierto en silencio. La lista hoy:

| Método | Ruta | Por qué es público |
|---|---|---|
| GET | `/` | Redirige a `/pantallas/index.html`; no expone nada. |
| POST | `/login` | Los 3 roles entran por la misma ruta; todavía no hay sesión. |
| POST | `/pacientes` | Registro público de paciente (con acceso: ver abajo); rate-limited ("registro"). |
| POST | `/profesionales` | Registro público de profesional. |
| POST | `/recuperacion-password` | Pedido de recuperación: todavía no hay sesión. |
| POST | `/recuperacion-password/reset` | Confirmación con el token del mail: idem. |
| GET | `/reservar/{slug}` | Página pública de reserva (Fase 3a). |
| GET | `/public/profesionales/{slug}/disponibilidad` | Disponibilidad pública, misma superficie. |
| POST | `/public/profesionales/{slug}/reservar` | Reserva pública, misma superficie (rate-limited). |
| GET | `/capacidades` | Lo consulta cualquier pantalla antes de saber quién sos; sin datos sensibles (solo flags). |
| GET | `/consentimiento-whatsapp/texto` | El texto del consentimiento de los avisos por WhatsApp (versión, título, párrafos): la pantalla de registro lo muestra antes de que haya sesión. Es texto legal, sin datos de nadie; rate-limited (`consentimiento-whatsapp`). |
| POST | `/mensajeria/simulado/entrante` | Equivalente a un webhook: no hay sesión del otro lado. |
| GET | `/mensajeria/simulado/conversacion` | Idem; ambos ya cierran con `Disponible()`/`SoloLocal`, no con JWT. |
| POST | `/mensajeria/whatsapp/entrante` | Webhook real de `whatsapp-service`. Sin JWT a propósito (lo llama una máquina) — cierra con `EsLocal()` + secreto compartido, no con `Disponible()`/`SoloLocal` (candado propio, no depende de `ChatSimuladoHabilitado`). |

Archivos estáticos (`/pantallas/*`) no aparecen en ninguna tabla: los sirve
`UseStaticFiles`, un middleware, nunca un endpoint — `EndpointDataSource` no los ve, y por
eso tampoco pasan por la guardia. Quedan fuera del alcance de "autenticación por
endpoint" a propósito (son HTML/JS/CSS; los datos que importan están detrás de la API).

## Chats: solo del Configurador (D2) y la pertenencia anterior

**D2 del chat para el personal: los chats son SOLO del Configurador, que hace de recepción.** Los profesionales atienden, no contestan mensajes: no ven ningún chat
ni reciben sus notificaciones, y un Profesional (o un paciente) recibe **403** en toda ruta de `/mensajeria`, sin distinguir qué se pida ni a quién pertenezca la
conversación (la decisión es por el rol y no un 404). Sin sesión, 401. Lo prueba `ChatsSoloConfiguradorTests`, que **lee la tabla de rutas de la aplicación** (no una
lista escrita a mano) y falla si una ruta nueva de `/mensajeria` no exige solo el rol Configurador. Las únicas rutas de `/mensajeria` sin sesión son las que no tienen una
persona del otro lado: el simulador y el webhook de `whatsapp-service` (arriba). **No cambia** la campanita de turnos del profesional (`/profesionales/{id}/turnos-confirmados`):
no es de los chats.

Lo que sigue describe la **pertenencia por profesional que existía antes** (Tomar/Devolver/Asignar y la visibilidad por paciente). Con la D2 **queda inalcanzable**: el
código de `VisibilidadConversacion` y de esas tres rutas sigue, pero ningún profesional pasa de la puerta, y la columna "Pertenencia" de la tabla de abajo describe lo que
decidiría ese código. Se retirará en una rama aparte (ver `docs/PROGRESO_CHAT_PERSONAL_WHATSAPP.md`, 5.9). La pantalla ya no ofrece Tomar, Devolver, Asignar ni "Mías".

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
| POST | `/mensajeria/simulado/entrante` | Público | — | Simula un mensaje entrante (equivalente al webhook real). No tiene, ni tendría sentido, sesión del otro lado. |
| GET | `/mensajeria/simulado/conversacion` | Público | — | Idem — el simulador del lado paciente todavía no tiene sesión. |
| POST | `/mensajeria/whatsapp/entrante` | Público (candado propio) | — | Webhook real: lo llama `whatsapp-service`, nunca una persona. `EsLocal()` + secreto compartido (`X-Shared-Secret`, comparación en tiempo constante); un pedido que llega por Caddy desde internet da 404 aunque el secreto sea correcto. |
| GET | `/mensajeria/whatsapp/estado` | **Solo Configurador** | — | Pantalla de WhatsApp del Configurador: estado + QR (data-URL) en la misma respuesta. `servicioDisponible:false` si no se pudo contactar a `whatsapp-service`, distinto de `conectado:false`. `Cache-Control: no-store`; el cuerpo (con el QR) nunca se loguea. Rate limit propio por IP (`whatsapp-estado`, más laxo — es polling). |
| POST | `/mensajeria/whatsapp/desvincular` | **Solo Configurador** | — | Cierra la sesión de WhatsApp y fuerza un QR nuevo (también lo usa el botón "¿no funciona? generar uno nuevo" durante el escaneo). Audita qué número se desvinculó. Rate limit propio por IP (`whatsapp-admin`, más estricto — acción deliberada). |
| POST | `/mensajeria/whatsapp/confirmar-vinculacion` | **Solo Configurador** | — | Lo llama el frontend al notar (por su propio polling) que el estado pasó a conectado — el endpoint vuelve a pedirle el estado real a `whatsapp-service` antes de auditar, no confía en el aviso a ciegas. Límite conocido: si se cierra la pestaña antes de que el polling note el cambio, esa vinculación puntual no queda auditada. Mismo rate limit que `/desvincular`. |
| GET | `/mensajeria/conversaciones` | **Solo Configurador** | **Sí — filtra la lista** | `VisibilidadConversacion.Filtrar`. `idEtiqueta` filtra encima de lo ya visible (Paso 5 de la vuelta anterior). El buscador del frontend filtra client-side sobre esta misma lista, así que queda cubierto gratis. |
| GET | `/mensajeria/conversaciones/{id}` | **Solo Configurador** | **Sí — 404** | |
| GET | `/mensajeria/conversaciones/{id}/paciente` | **Solo Configurador** | **Sí — 404** | Panel del lead: datos administrativos únicamente. Devuelve también `idProfesionalAsignado`/`nombreProfesionalAsignado` (de la conversación, no del paciente). Único endpoint llamado solo al abrir/refrescar el panel (nunca por el polling de la lista) — ahí se audita la apertura (quién, conversación y paciente por id, cuándo; nunca el nombre ni contenido). |
| GET | `/mensajeria/conversaciones/{id}/mensajes` | **Solo Configurador** | **Sí — 404** | |
| PUT | `/mensajeria/conversaciones/{id}/leida` | **Solo Configurador** | **Sí — 404** | |
| POST | `/mensajeria/conversaciones/{id}/responder` | **Solo Configurador** | **Sí — 404** | Audita (acción + ids, nunca el texto). |
| PUT | `/mensajeria/conversaciones/{id}/cerrar` | **Solo Configurador** | **Sí — 404** | Audita. Le manda el aviso de cierre a la persona **solo si ella escribió en esa conversación** (una respuesta automática no empieza una conversación); si no, cierra igual y no manda nada. |
| PUT | `/mensajeria/conversaciones/{id}/reabrir` | **Solo Configurador** | **Sí — 404** | 400 si no estaba Cerrada. Audita. Igual que `cerrar`: el aviso a la persona sale solo si ella escribió en esa conversación. |
| PUT | `/mensajeria/conversaciones/{id}/asignar` | **Solo Configurador** | — (Configurador ve todo) | Asigna directo `Conversacion.IdProfesionalAsignado`, sin que haga falta un caso abierto. 400 si la conversación no está escalada. Audita (ids). |
| PUT | `/mensajeria/conversaciones/{id}/tomar` | **Solo Configurador** | **Sí — 404** (más 400/409 propios) | Atómico: `UPDATE ... WHERE IdProfesionalAsignado IS NULL` — si dos la toman a la vez, una gana (204) y la otra pierde la carrera (**409**, "ya la tomó otra persona"), nunca dos dueños. 400 si no está escalada o si la pide un Configurador (usa `/asignar`): **desde la D2 siempre da 400 o 403, ya no hay profesionales en los chats**. Ya asignada a quien la pide: 204 idempotente. Audita. |
| PUT | `/mensajeria/conversaciones/{id}/devolver` | **Solo Configurador** | **Sí — 404**, más 403 si no es el dueño ni Configurador | Vuelve a sin asignar. 400 si ya estaba sin asignar. Motivo de la misma lista cerrada que `/asignar` (`MotivoReasignacion.Categorias`). Audita. |
| GET | `/mensajeria/conversaciones/{id}/notas` | **Solo Configurador** | **Sí — 404** | Nunca al paciente; el texto nunca se audita. |
| POST | `/mensajeria/conversaciones/{id}/notas` | **Solo Configurador** | **Sí — 404** | |
| DELETE | `/mensajeria/conversaciones/{id}/notas/{idNota}` | **Solo Configurador** | **Sí — 404** (de la conversación, antes de mirar quién escribió la nota) | 403 para cualquier otro profesional que sí puede ver la conversación pero no escribió la nota. |
| GET | `/mensajeria/etiquetas` | **Solo Configurador** | — | Catálogo: lectura abierta (cualquiera que vea una conversación puede necesitar mostrarle sus chips). |
| POST/PUT/DELETE | `/mensajeria/etiquetas[/{id}]` | **Solo Configurador** | — | El catálogo de etiquetas de chats con información de salud mental lo administra solo Configurador (pantalla propia, `#etiquetas` en el panel). 409 si el nombre ya existe. Baja lógica. |
| POST/DELETE | `/mensajeria/conversaciones/{id}/etiquetas/{idEtiqueta}` | **Solo Configurador** | **Sí — 404** | Asignar una etiqueta a una conversación puntual es pertenencia de la *conversación*, no del catálogo — sigue abierto a quien pueda verla. Idempotente. |
| GET | `/mensajeria/respuestas-rapidas` | **Solo Configurador** | — | Se ven las globales; las propias de un profesional ya no se usan (nunca las de otro profesional: lo filtra el `WHERE` del DAO). |
| POST/PUT/DELETE | `/mensajeria/respuestas-rapidas[/{id}]` | **Solo Configurador** | Siempre globales (sin dueño) | Global (Configurador) → 403 a Profesional; propia → 403 a Configurador y a cualquier otro. Baja lógica. |
| GET | `/mensajeria/notificaciones-internas` | **Solo Configurador** | **Sí — filtra la lista** | Las que referencian una conversación puntual se filtran re-resolviendo su pertenencia **actual**, no un dato grabado cuando se creó el aviso. |
| PUT | `/mensajeria/notificaciones-internas/{id}/leida` | **Solo Configurador** | **Sí — 404** si referencia una conversación no visible | Defensa en profundidad: en el flujo normal la lista ya viene filtrada. |
| POST | `/mensajeria/recordatorios/ejecutar` | **Solo Configurador** | — | Dispara el servicio de recordatorios fuera de su intervalo; pensado para pruebas/demo. |
| GET/POST/PUT/DELETE | `/mensajeria/preguntas-frecuentes[/{id}]` | **Solo Configurador** | Ninguna | Sin relación con conversaciones puntuales. |

## Historia clínica y datos clínicos (Cierre de seguridad, Paso 2)

Lo más sensible de toda la app (Ley 25.326). Antes: "cualquier profesional autenticado"
podía ver la historia clínica, evoluciones, eventos de crisis o sesiones de **cualquier**
paciente — un alcance más ancho que Casos, que sí filtraba por asignación. Ahora: el mismo
criterio que chats y Casos, "equipo tratante vigente" (`Services/AutorizacionClinica.cs`,
vía `PacienteProfesionalDAO`) — **solo el profesional asociado a ese paciente**, o el
Configurador. `InteraccionIA` (interacciones con la IA de apoyo emocional) se suma al mismo
criterio: es contenido tan sensible como una evolución, aunque hoy sin uso real (solo lo
llamaba `mockup.html`, de antes de `PANTALLAS/`).

Los volcados sin filtro (`ObtenerTodos`, sin paciente) — `/historiasclinicas`,
`/evoluciones`, `/eventoscrisis`, `/interaccionesia` sin id — quedan **Configurador-only**
en vez de un "todos los historiales de mis pacientes" que ninguna pantalla pide hoy (se
confirmó buscando en todo `PANTALLAS/`: solo la tabla genérica del Configurador los usa).

Se audita la apertura (Paso 6, ver más abajo): quién, qué tipo de recurso e id del
paciente, cuándo — nunca contenido clínico. Una fila por apertura real; no hay polling que
la repita (a diferencia de chats, esta parte de la app no tiene actualización en vivo).

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/historiasclinicas` | Configurador | — | Volcado completo; solo la tabla admin lo usa. |
| GET | `/historiasclinicas/{id}` | Profesional, Configurador | **Sí — 404** | Por id de la propia historia (no del paciente). |
| GET | `/pacientes/{id}/historiaclinica` | Profesional, Configurador | **Sí — 404** | La ficha real que abre el profesional. **Auditada.** |
| POST | `/historiasclinicas` | Profesional, Configurador | **Sí — 403** | 403 (no 404: nada que ocultar, es una creación) si el paciente no es de su equipo. |
| GET | `/evoluciones` | Configurador | — | Volcado completo. |
| GET | `/evoluciones/{id}` | Profesional, Configurador | **Sí — 404** | Pertenencia resuelta vía el paciente de la Historia (Evolucion no tiene `IdPaciente` directo). |
| GET | `/historiasclinicas/{id}/evoluciones` | Profesional, Configurador | **Sí — 404** | La lista que abre la ficha real. **Auditada.** |
| POST/PUT | `/evoluciones[/{id}]` | Profesional, Configurador | **Sí — 403/404** | POST 403, PUT 404 (mismo criterio que arriba). |
| GET | `/eventoscrisis` | Configurador | — | Volcado completo. |
| GET | `/eventoscrisis/{id}` | Profesional, Configurador | **Sí — 404** | |
| GET | `/pacientes/{id}/eventoscrisis` | Profesional, Configurador | **Sí — 404** | **Auditada.** |
| POST/PUT | `/eventoscrisis[/{id}]` | Profesional, Configurador | **Sí — 403/404** | |
| POST | `/sesiones-clinicas` | Profesional, Configurador | **Sí — 403** | El `IdProfesional` sale del token si quien crea es Profesional (Paso 3, mismo criterio que turnos) — nunca del cuerpo. **PR 6:** las emociones llegan en `emociones` (ya no en `historialEmociones`, que se ignora) y solo se guardan, **cifradas**, con una aceptación vigente del paciente de esa sesión de video y si el servidor las valida; si no, la sesión se guarda igual sin emociones y la respuesta dice por qué (`emociones.estado`/`motivo`). Ver `docs/VIDEOLLAMADAS.md`. |
| GET | `/pacientes/{id}/sesiones-clinicas` | Profesional, Configurador | **Sí — 404** | **Auditada.** **PR 6:** al profesional se le devuelven las emociones descifradas; **al Configurador no** (`historialEmociones` y `emociones` en `null`). |
| GET | `/profesionales/{id}/sesiones-clinicas` | Profesional, Configurador | **Propio o Configurador — 403** | No es por paciente: es "mis propias sesiones". **PR 6:** igual que la anterior, el Configurador no recibe emociones. |
| GET | `/interaccionesia` | Configurador | — | Volcado completo. Sin uso real (ver arriba). |
| GET | `/interaccionesia/{id}` | Profesional, Configurador | **Sí — 404** | |
| GET | `/pacientes/{id}/interaccionesia` | Profesional, Configurador | **Sí — 404** | |
| POST | `/interaccionesia` | Profesional, Configurador | **Sí — 403** | |

## Agendas y turnos (Cierre de seguridad, Paso 3)

**Agendas** (franjas de horario de un profesional): antes, alta/baja/edición sin ningún
chequeo — cualquiera, con cualquier id, podía crear o borrar la franja de cualquier
profesional. Ahora, el profesional solo administra las propias (`IdProfesional` se fuerza
desde el token al crear; PUT/DELETE verifican contra la fila existente); el Configurador,
cualquiera. `/agenda` (alias sin "s", para compatibilidad con una pantalla anterior) tiene
el mismo tratamiento. La lectura (`GET /agendas[/{id}]`) exige sesión pero no pertenencia:
no es información sensible (solo franjas de horario) y no hay evidencia de que ninguna
pantalla pre-login la necesite en bruto — a diferencia de `/profesionales/{id}/agenda`
(ver abajo), que sí.

**Turnos**: la identidad sale siempre del token, **nunca** de un id en el cuerpo o la URL
sin contrastar — el hueco real que tenía esta parte de la app. `POST /turnos` (crudo, sin
las reglas de `TurnoReglasService`, sin uso por ningún frontend) queda **apagado (410)** en
vez de protegido: no tenía ningún uso legítimo y solo preservaba un atajo que ignora RN-01/
05/07 y la inserción a prueba de doble reserva.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/agendas`, `/agendas/{id}`, `/agenda/{id}` | Cualquier rol autenticado | No | Solo franjas de horario, sin PII. |
| POST | `/agendas`, `/agenda` | Profesional, Configurador | **Sí** | `IdProfesional` se fuerza al propio si quien crea es Profesional. |
| PUT/DELETE | `/agendas/{id}`, `/agenda/{id}` | Profesional, Configurador | **Sí — 404/403** | 404 si no existe; 403 si es de otro profesional. |
| GET | `/profesionales/{id}/agenda` | **Público** | — | Cualquier paciente necesita ver el horario de **cualquier** profesional al pedir turno (decisión preexistente, sin cambios). |
| GET | `/profesionales/{id}/horarios-libres` | Cualquier rol autenticado | **Propio o Configurador — 403** | Vista previa "así te ven los pacientes"; la versión pública real es `/public/profesionales/{slug}/disponibilidad`. |
| POST | `/turnos` | — | — | **Apagado (410 Gone).** Sin uso, bypaseaba todas las reglas de negocio. |
| POST | `/turnos/solicitar` | Paciente | **Sí** | `IdPaciente` se fuerza al del token — antes se confiaba en el del cuerpo. |
| POST | `/turnos/profesional-solicitar` | Profesional, Configurador | **Sí** | `IdProfesional` se fuerza al propio si quien pide es Profesional. |
| GET | `/turnos/{id}` | Cualquier rol autenticado | **Sí — 403** | Ya tenía el chequeo (paciente/profesional dueño) desde la Fase JWT; sin cambios. |
| PUT | `/turnos/{id}` | Profesional, Configurador | **Sí — 404/403** | Antes sin ningún chequeo. 404 si no existe el turno; 403 si es de otro profesional. Nunca el paciente por acá: su camino es `/cancelar` y `/confirmar-paciente`. |
| DELETE | `/turnos/{id}` | Configurador | — | Sin uso real (el cancelar real es `/cancelar`); antes sin ningún chequeo. |
| PUT | `/turnos/{id}/cancelar` | Cualquier rol autenticado | **Sí — 403** | Ya filtraba por dueño (Fase JWT); sin cambios. |
| PUT | `/turnos/{id}/confirmar-paciente` | Cualquier rol autenticado | **Sí — 404/403** | Lo confirma el propio paciente, o el profesional en una cita urgente (mismo flujo que ya usa `nuevo_dashboard.js`) — antes sin ningún chequeo. |
| PUT | `/turnos/{id}/generar-sala` | Profesional, Configurador | **Sí — 404/403** | Antes sin ningún chequeo. |
| GET | `/turnos/ocupados` | Cualquier rol autenticado | No | Solo horarios ocupados, sin identidad del paciente. |
| GET | `/turnos` | Configurador | — | Volcado completo. |
| GET | `/pacientes/{id}/turnos[-detalle]`, `/proximo-turno` | Cualquier rol autenticado | **Sí (paciente) — 403** | El paciente solo los propios; profesional/Configurador, de cualquiera (parte de su atención). |
| GET | `/profesionales/{id}/turnos[-confirmados]` | Cualquier rol autenticado | **Propio o Configurador — 403** | |

## Casos, Grupos, Reglas de derivación, Derivaciones de casos (`Endpoints/CasosEndpoints.cs`)

Ya filtraban por dueño desde la Fase JWT (`AutorizacionCasos`), pero el grupo entero no
exigía sesión a nivel de ruta — varios devolvían 403 igual gracias al `if (!esAdmin)`
interno (un pedido anónimo resuelve `(false, null)`), pero otros (`/grupos`,
`/reglasderivacion`, `/grupos/{id}/miembros`, `/casos/motivos-reasignacion`,
`/pacientes/{id}/caso`) no tenían **ningún** chequeo y quedaban abiertos a cualquiera.
`MapCasos()` ahora agrupa todo con `MapGroup("").RequireAuthorization()` (sin tocar las
rutas, ningún prefijo nuevo) y exige sesión pareja; la pertenencia específica de cada uno
sigue decidida adentro, sin cambios de comportamiento para quien ya tenía sesión.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET/POST/PUT | `/grupos[/{id}]`, `/grupos/{id}/activar`, `/desactivar` | Cualquier rol autenticado, escritura solo Configurador | — | Lectura abierta a cualquier autenticado; escritura 403 adentro si no es Configurador. |
| GET/POST/DELETE | `/grupos/{id}/miembros[/{idProfesional}]` | Cualquier rol autenticado, escritura solo Configurador | — | |
| GET/POST/PUT/DELETE | `/reglasderivacion[/{id}]`, `/activar`, `/desactivar` | Cualquier rol autenticado, escritura solo Configurador | — | |
| GET | `/casos` | Cualquier rol autenticado | **Sí — filtra** | Administrador ve todos (con los filtros que pida); profesional, solo los suyos, sin importar qué filtro de profesional haya pedido. |
| GET | `/casos/{id}`, `/derivaciones`, `/historial` | Cualquier rol autenticado | **Sí — 403** | "Ese caso no es tuyo." (no 404: no hay necesidad de ocultar la existencia acá, es un recurso interno del equipo, no algo que un paciente pudiera intentar). |
| PUT | `/casos/{id}/reasignar` | Configurador | — | |
| GET | `/casos/motivos-reasignacion` | Cualquier rol autenticado | No | Catálogo cerrado para el `<select>`. |
| GET | `/pacientes/{id}/caso` | **Profesional, Configurador** | No (más allá del rol) | Panel "Información del lead" de CRM > Chats. Antes sin ningún chequeo de rol; ahora el mismo que ese panel. |
| GET | `/profesionales/{id}/casos` | Cualquier rol autenticado | **Propio o Configurador — 403** | |
| GET/PUT | `/casosderivaciones/pendientes`, `/{id}/confirmar`, `/corregir`, `/rechazar` | Configurador | — | |

## Derivación ("Solicitudes de cambio", mecanismo anterior a Casos — sin cambios esta vuelta)

Ya era Configurador-only de punta a punta desde la Fase JWT (se acepta/rechaza desde el
panel de administrador, nunca el profesional directo). No requirió cambios.

| Método | Ruta | Rol | Pertenencia |
|---|---|---|---|
| GET/POST/PUT | `/derivaciones[/{id}]`, `/pendientes`, `/detalle` | Configurador | — |
| GET | `/pacientes/{id}/derivaciones` | Configurador | — |
| PUT | `/derivaciones/{id}/aceptar`, `/rechazar` | Configurador | — |
| POST | `/derivaciones/cambio-directo` | Configurador | — |

## Reportes (Cierre de seguridad, Paso 4 — CB-04 cerrado)

**CB-04, el hueco marcado en el propio código:** `AutorizacionReporte.ResolverAlcance`
leía `X-Usuario-Id` directo del header, **al margen del pipeline de JWT** — ni ese archivo
ni los dos endpoints que lo llaman exigían sesión a nivel de ruta, así que cualquiera, sin
ningún token, podía mandar cualquier `X-Usuario-Id` (o ninguno, cayendo en "Configurador",
alcance total) y bajar nombre, DNI y NHC de pacientes. Corregido: ahora lee
`IdentidadActualService`, igual que el resto de la app desde la Fase JWT, y ambos
endpoints exigen autenticación.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/reportes/atenciones` | Cualquier rol autenticado | **Sí — 403** | Paciente, sin acceso (403). Profesional, solo su propio alcance (403 si pide el de otro). Configurador, total o filtrado. |
| GET | `/reportes/atenciones/html` | Cualquier rol autenticado | **Sí — 403** | Mismo `AutorizacionReporte`, descarga en HTML. |

## Catálogos de referencia (Especialidad, Rol, EstadoTurno, TipoConsentimiento, ObraSocial)

Lectura abierta (son datos de referencia, sin PII) — `/especialidades` además la necesita
el registro público de profesional antes de loguearse (confirmado en
`registro_profesional.html`); los otros 4 no están confirmados pre-login, así que su
lectura pasó a exigir sesión (cualquier rol) por las dudas, sin romper nada (ningún
screen pre-login los llama). Escritura, solo Configurador en los 5 — antes,
EstadoTurno/TipoConsentimiento/ObraSocial no tenían **ningún** chequeo en sus altas/bajas/
modificaciones (a diferencia de Especialidad/Rol, que ya eran Configurador-only).

| Método | Ruta | Rol | Notas |
|---|---|---|---|
| GET | `/especialidades[/{id}]` | **Público** | Pre-login: registro de profesional. |
| POST/PUT/DELETE | `/especialidades[/{id}]` | Configurador | Sin cambios esta vuelta. |
| GET | `/roles[/{id}]`, `/estadosturnos[/{id}]`, `/tiposconsentimiento[/{id}]`, `/obrassociales[/{id}]` | Cualquier rol autenticado | Antes, sin ningún chequeo. |
| POST/PUT/DELETE | `/roles[/{id}]` | Configurador | Ya lo era. |
| POST/PUT/DELETE | `/estadosturnos[/{id}]`, `/tiposconsentimiento[/{id}]` | Configurador | Antes, sin ningún chequeo. |
| POST/PUT | `/obrassociales[/{id}]` | Configurador | Antes, sin ningún chequeo (no hay DELETE). |

## Tutor, PacienteTutor, Cobertura, Consentimiento (genérico)

`Tutor`/`PacienteTutor` (datos personales de un guardián) y `Cobertura` (cobertura de obra
social de un paciente): confirmado que **ninguna pantalla real de `PANTALLAS/`** los llama
hoy — solo `mockup.html`, el scaffold anterior a esta rama (y que, al no mandar ningún
token, ya era incompatible con el resto de los endpoints protegidos desde la Fase JWT). Se
cierran igual, sin riesgo de romper nada: Profesional o Configurador. `Consentimiento`
(genérico, distinto del RN-02 de telemedicina en `ConsentimientoEndpoints.cs`): volcado y
escritura Configurador-only (mismo criterio que historia clínica); la lectura por paciente,
para su propio dueño o el equipo.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET/POST/PUT | `/tutores[/{id}]` | Profesional, Configurador | No | Antes, sin ningún chequeo. |
| GET/POST/DELETE | `/pacientes/{id}/tutores`, `/pacientetutores[/...]` | Profesional, Configurador | No | Antes, sin ningún chequeo. |
| GET | `/coberturas[/{id}]`, `/pacientes/{id}/coberturas` | Profesional, Configurador | No | Antes, sin ningún chequeo (las escrituras ya eran Configurador-only). |
| POST/PUT/DELETE | `/coberturas[/{id}]` | Configurador | — | Sin cambios esta vuelta. |
| GET | `/consentimientos[/{id}]` | Configurador | — | Volcado completo; antes, sin ningún chequeo. |
| GET | `/pacientes/{id}/consentimientos` | Cualquier rol autenticado | **Sí (paciente) — 403** | Antes, sin ningún chequeo. |
| POST/PUT/DELETE | `/consentimientos[/{id}]` | Configurador | — | Antes, sin ningún chequeo. |
| GET | `/pacientes/{id}/consentimiento-telemedicina` | Cualquier rol autenticado | **Sí (paciente) — 403** | El de RN-02 (`ConsentimientoEndpoints.cs`). Antes, sin ningún chequeo. |
| POST | `/pacientes/{id}/consentimiento-telemedicina` | Paciente | **Sí — 403** | Firma; ya exigía que el id fuera el propio (Fase JWT). |

## Pacientes y Profesionales (ficha, "Mi perfil", alta/baja)

Antes: el volcado completo (todos los pacientes/profesionales, con mail y teléfono), la
ficha de cualquiera por id, y hasta editar el perfil de cualquiera — todo sin **ningún**
chequeo, ni de sesión ni de dueño. Confirmado en `PANTALLAS/`: el volcado completo solo lo
usa la tabla admin del Configurador; la ficha y "Mi perfil" (PUT), cualquier pantalla con
sesión del propio dueño. POST sigue público (es el registro).

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/pacientes` | Configurador | — | Volcado completo; antes, sin ningún chequeo. |
| GET | `/pacientes/{id}` | Cualquier rol autenticado | **Sí (paciente) — 403** | Antes, sin ningún chequeo. |
| POST | `/pacientes` | **Público** (rate-limited, política "registro") | — | Registro — crea Paciente + Usuario + UsuarioPaciente juntos, en una transacción; la persona elige usuario y contraseña en el mismo alta. |
| PUT | `/pacientes/{id}` | Cualquier rol autenticado | **Sí (paciente) — 403** | Antes, sin ningún chequeo: cualquiera editaba la ficha de cualquiera. |
| DELETE | `/pacientes/{id}`, `PUT /alta` | Configurador | — | Antes, sin ningún chequeo. |
| POST/DELETE | `/pacientes/{id}/profesionales[/{idProf}]` | Configurador | — | Asignar/desasignar el equipo tratante es administrativo (mismo criterio que `/derivaciones/*`). Antes, sin ningún chequeo. |
| GET | `/pacientes/{id}/mis-profesionales` | Cualquier rol autenticado | **Sí (paciente) — 403** | Sin cambios (ya lo tenía). |
| GET | `/profesionales` | Configurador | — | Volcado completo; antes, sin ningún chequeo. |
| GET | `/profesionales/{id}` | Cualquier rol autenticado | No | Antes, sin ningún chequeo (sin pertenencia: es información pública de un profesional, igual que su ficha en el registro). |
| POST | `/profesionales` | **Público** | — | Registro. |
| PUT | `/profesionales/{id}` | Cualquier rol autenticado | **Sí (propio) — 403** | "Mi perfil": antes, sin ningún chequeo, cualquiera reescribía el perfil de cualquier profesional. |
| DELETE/PUT | `/profesionales/{id}`, `/alta`, `/aprobar`, `/rechazar` | Configurador | — | Sin cambios (ya lo tenían). |
| GET | `/profesionales/solicitudes` | Configurador | — | Sin cambios. |
| POST | `/profesionales/{id}/usuario` | Configurador | — | **Nuevo** (Tarea 3 de `docs/PROGRESO_NOCTURNO.md`): crea el `Usuario` de un profesional Activo y su vínculo en UNA transacción (un usuario sin vínculo entra como configurador); valida usuario, mail y contraseña (mínimo 8); si se pide, devuelve una contraseña temporal UNA sola vez (`Cache-Control: no-store`). Audita "Alta Usuario" y "Vinculación Usuario-Profesional". |
| GET | `/profesionales/sin-usuario` | Configurador | — | **Nuevo**: ids de los profesionales Activos que todavía no tienen usuario. |
| GET | `/profesionales/activos/especialidad/{idEsp}` | Cualquier rol autenticado | No | Antes, sin ningún chequeo. |
| PUT | `/profesionales/{id}/link-publico` | Cualquier rol autenticado | **Sí (propio o Configurador) — 403** | Ya tenía el chequeo interno; se agrega `RequireAuthorization()` a nivel de ruta por consistencia (antes, un pedido anónimo ya caía en 403 por el fallback interno). |
| GET | `/profesionales/{id}/pacientes[-detalle]` | Cualquier rol autenticado | **Sí (propio o Configurador) — 403** | Sin cambios (ya lo tenían). |

## Usuarios, asociaciones Usuario↔Rol/Paciente/Profesional, Auditoría, Recuperación de contraseña

Plomería administrativa — ninguna pantalla de paciente/profesional la llama directo. Ya
era Configurador-only de punta a punta desde la Fase JWT; sin cambios esta vuelta (salvo
`/recuperacion-password` y `/recuperacion-password/reset`, públicos a propósito: todavía
no hay sesión cuando se piden).

Recuperación de contraseña pasó a ser autoservicio (antes, un Configurador aprobaba cada
pedido a mano desde una pestaña propia): el usuario pide el reset, recibe un link con un
token de un solo uso por mail (vence a los 30 minutos) y entra a `reset-password.html`
a poner la contraseña nueva. Los endpoints de aprobación manual (`/pendientes`,
`/{id}/aceptar`, `/{id}/rechazar`, `/estado/{username}`) se borraron — ver
CAMBIOS_AGUS.md.

| Método | Ruta | Rol |
|---|---|---|
| GET/POST/PUT/DELETE | `/usuarios[/{id}]`, `/{id}/roles`, `/{id}/paciente`, `/{id}/profesional` | Configurador |
| POST/DELETE | `/usuarioroles[/...]`, `/usuariopacientes[/...]`, `/usuarioprofesionales[/...]` | Configurador |
| GET | `/auditoria` | Configurador |
| POST | `/recuperacion-password` | **Público** (rate-limited, respuesta genérica) |
| POST | `/recuperacion-password/reset` | **Público** (rate-limited) |

## Informes de admisión, notificaciones, asignaciones de equipo tratante

`POST /informes-admision` lo envía el propio paciente desde su panel (confirmado en
`Pantalla_Paciente/js/dashboard.js`): el `IdPaciente` se fuerza al del token, nunca el del
cuerpo; el Configurador puede completarlo en nombre de alguien (admisión asistida). El
resto del flujo de admisión (entrevista, derivar, detalle, estado) es Configurador —
confirmado que solo esas pantallas lo llaman. `/informes-admision/rechazar` (el profesional
rechaza un paciente derivado) valida que el `IdProfesional` del cuerpo sea el propio, igual
que turnos. `/pacientes/{id}/notificaciones` y `/notificaciones/{id}/leida`: antes sin
ningún chequeo — cualquiera podía leer o marcar como leído el aviso de cualquier paciente,
con título y mensaje completos.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| POST | `/informes-admision` | Paciente, Configurador | **Sí** | `IdPaciente` forzado al del token si no es Configurador. |
| GET | `/informes-admision`, `/{id}`, `-detalle` | Configurador | — | Sin uso directo confirmado por ninguna pantalla; Configurador-only por las dudas. |
| GET | `/pacientes/{id}/informe-admision` | Cualquier rol autenticado | **Sí (paciente) — 403** | |
| PUT | `/informes-admision/{id}/estado` | Configurador | — | Sin uso confirmado. |
| PUT | `/informes-admision/{id}/entrevista`, `/derivar` | Configurador | — | |
| PUT | `/informes-admision/rechazar` | Profesional, Configurador | **Sí — 403** | `IdProfesional` del cuerpo debe ser el propio si no es Configurador. |
| PUT | `/asignaciones/estado` | Profesional, Configurador | **Sí — 403** | Idem. |
| GET | `/pacientes/{id}/notificaciones` | Cualquier rol autenticado | **Sí (paciente) — 403** | Antes, sin ningún chequeo. |
| PUT | `/notificaciones/{id}/leida` | Cualquier rol autenticado | **Sí (paciente) — 403/404** | Antes, sin ningún chequeo. |

## Video: acceso a las salas, token de sala y servidores ICE (`Endpoints/VideoEndpoints.cs`)

`POST /video/acceso` es el endpoint del PR 3; el PR 6 sumó los cuatro del consentimiento de la lectura de emociones (más abajo).
El cliente dice `origen` (`turno` | `admision`) + `id`; la sala y el
rol los decide el servidor con los datos de la base, nunca el cuerpo. A cambio recibe un token de sala de vida corta
para el signaling y los servidores ICE (STUN y, si está configurado, TURN con credenciales efímeras). Reglas, ventana
horaria y variables de configuración: `docs/VIDEOLLAMADAS.md`.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| POST | `/video/acceso` | Cualquier rol autenticado | **Sí — 404** | Turno: el paciente o el profesional del turno, o un Configurador (misma regla que `PUT /turnos/{id}/generar-sala`). Admisión: el paciente del informe o un Configurador. 404 si no existe **o si existe y no sos parte: la misma respuesta, con el mismo status y el mismo cuerpo, para que no se pueda sondear qué ids existen** (el rechazo por pertenencia igual se audita); 409 si el estado, el horario (de 10 min antes a 30 min después del fin) o la sala no lo permiten. La pertenencia se evalúa **antes** que el estado y el horario, así un ajeno no se entera de ninguno de los dos. No crea la sala. Rate limit propio por IP real (política `video`). `Cache-Control: no-store`; el token y las credenciales no se loguean; los accesos y los rechazos van a `LogAuditoria`. 503 si falta un secreto de señalización válido (la API arranca igual). |

### Consentimiento de la lectura de emociones (PR 6, `Endpoints/ConsentimientoVideoEndpoints.cs`)

Mismo criterio que `/video/acceso`: el cliente solo dice **de qué sesión** habla (`origen` + `id`) y el servidor decide quién es
parte. Los cuatro piden el JWT de la sesión de YoSoy (el token de sala no sirve), nunca se cachean (`Cache-Control: no-store`) y
tienen su propia política de rate limit por IP (`video-consentimiento`, `Video__Emociones__LimitePorMinuto`). Contrato, estados,
auditoría y lo que hay que validar legalmente: `docs/VIDEOLLAMADAS.md`, "Lectura de emociones: consentimiento y datos sensibles".

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/video/consentimiento` | Cualquier rol autenticado | **Solo el paciente de la sesión — 404** | Su estado (`sin-decision`, `acepta`, `rechaza`, `revocado`, `no-disponible`) y, si se le puede ofrecer, el texto vigente y su versión. A un menor, a quien tiene tutor o no tiene fecha de nacimiento, o sin clave de cifrado en el servidor, `no-disponible`. Misma respuesta 404 para "no existe" y "no sos parte". |
| PUT | `/video/consentimiento` | Cualquier rol autenticado | **Solo el paciente de la sesión — 404** | `{ origen, id, decision: acepta\|rechaza, version }`. Registra una fila de `ConsentimientoVideo` con la versión y el hash del texto y la hora UTC. 409: `version-desactualizada`, `usar-revocar` (ya aceptó), `menor-de-edad`, `edad-desconocida`, `no-configurado` y los de la sala. **Auditada.** |
| POST | `/video/consentimiento/revocar` | Cualquier rol autenticado | **Solo el paciente de la sesión — 404** | `{ origen, id }`. Revoca la aceptación vigente y **anula las emociones ya guardadas de esa sesión** (nunca las notas). No depende de la ventana de la sala; se puede repetir. **Auditada**, con la cantidad anulada. |
| GET | `/video/emociones/permiso` | Cualquier rol autenticado | **Solo el profesional del turno o un Configurador — 404** | `permitido` solo con una aceptación vigente de alguien a quien se le puede ofrecer; con `consultarCadaSegundos` y `vigenciaSegundos`. Es lo que la pantalla del profesional pregunta cada pocos segundos y obedece (falla cerrada). Un profesional que no es del turno, o que quiere una entrevista de admisión, recibe el 404 uniforme. |

## Consentimiento de los avisos por WhatsApp (`Endpoints/ConsentimientoWhatsAppEndpoints.cs`)

El paciente acepta (y puede revocar) recibir avisos de sus turnos por WhatsApp. **Quién es sale siempre del JWT**: no hay un id en la URL ni
en el cuerpo, así que nadie acepta ni revoca por otro; un profesional o el Configurador reciben 403 (`solo-pacientes`). Nada viene aceptado por
defecto. Nunca se cachean (`Cache-Control: no-store`) y comparten una política de rate limit por IP (`consentimiento-whatsapp`, 30 por minuto).
El registro de un paciente nuevo (`POST /pacientes`, público) también acepta, con `aceptaWhatsApp` y `versionTextoWhatsApp`: se valida **antes**
de crear la cuenta (celular argentino válido, mayor de edad, texto vigente) y la aceptación queda a nombre del usuario recién creado. Modelo,
reglas y decisiones: `docs/PROGRESO_CONSENTIMIENTO_WHATSAPP.md`.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/consentimiento-whatsapp/texto` | Público | — | La versión y el texto vigentes. Si la versión configurada no existe, `disponible: false`. |
| GET | `/consentimiento-whatsapp` | Paciente | **Solo el paciente de la sesión — 403 al resto** | Su estado (`sin-decision`, `acepta`, `revocado`, `telefono-cambiado`, `texto-desactualizado`, `no-disponible`), si puede aceptar ahora (`disponible` y `motivo`: `menor-de-edad`, `telefono-invalido`, `no-configurado`) y el texto que tiene que leer. También `contacto`: `{ "enlace": "https://wa.me/…" }` para que el paciente le **escriba primero** a la clínica (lo arma la API con `Mensajeria:WhatsAppNumeroClinica`), o `null` si no hay número configurado; lo traen también las respuestas de `aceptar` y `revocar`. |
| POST | `/consentimiento-whatsapp/aceptar` | Paciente | **Solo el paciente de la sesión — 403 al resto** | `{ version }` (la del texto que leyó). Registra una fila de `ConsentimientoWhatsApp` con la versión y el hash del texto, su usuario, **el número nacional de 10 dígitos de su ficha** y la hora UTC; si tenía una abierta (otro número u otro texto) la cierra en el mismo paso. Repetirlo no agrega nada. 409: `version-desactualizada`, `menor-de-edad`, `telefono-invalido`, `no-configurado`. **Auditada.** |
| POST | `/consentimiento-whatsapp/revocar` | Paciente | **Solo el paciente de la sesión — 403 al resto** | Cierra la aceptación abierta con la hora UTC y el motivo `paciente`. Se puede repetir. **Auditada.** |

## Superficie pública (reserva por link, sin sesión)

Sin cambios esta vuelta — ya estaba bien decidida desde la Fase 3a. 404 idéntico si el
slug no existe o si el link está apagado (nunca se revela cuál de las dos cosas pasó).

| Método | Ruta | Rol |
|---|---|---|
| GET | `/reservar/{slug}` | Público (rate-limited) |
| GET | `/public/profesionales/{slug}/disponibilidad` | Público (rate-limited) |
| POST | `/public/profesionales/{slug}/reservar` | Público (rate-limited) |
| PUT | `/profesionales/{id}/link-publico` | Ver arriba (Pacientes y Profesionales) |

## Probado

- **Guardia de endpoints (Paso 1):** `YoSoy.Tests/GuardiaEndpointsTests.cs` — reflexión
  real sobre `EndpointDataSource`, falla si aparece algo sin decidir.
- **Matriz de pertenencia de chats:** `YoSoy.Tests/PertenenciaConversacionTests.cs` (11
  tests) y, en el navegador, `docs/pruebas-ui-cambios-agus/p14_pertenencia.py` (no
  versionado, 21/21) — de la vuelta anterior, sin cambios.
- **Matriz de rol A3 de chats:** `YoSoy.Tests/ChatsGestionTests.cs`.
- **Cierre de seguridad (este trabajo):** `YoSoy.Tests/CierreSeguridadTests.cs` —
  historia clínica por pertenencia (404 al profesional ajeno, nunca 403; auditoría sin
  contenido), agenda ajena (403), identidad de turno desde el token (nunca del cuerpo),
  CB-04 de reportes, cabeceras de seguridad globales.
- **Modo legacy (Paso 5):** `YoSoy.Tests/ModoLegacyTests.cs` — con el flag en `false`,
  `X-Usuario-Id` solo no identifica a nadie (401); un JWT real sigue andando igual.
- **Consentimiento de la lectura de emociones (PR 6):** `YoSoy.Tests/ConsentimientoVideoEndpointsTests.cs` (los cuatro
  endpoints y `POST /sesiones-clinicas` por HTTP: 401, 404 idéntico, 409 con su código, 429, `no-store`, el Configurador sin
  emociones), `ConsentimientoVideoServiceTests`, `GuardiaEmocionesServiceTests`, `EmocionesCifradoServiceTests`,
  `PurgaEmocionesTests`, `RecifrarEmocionesTests` y, contra una base real con la migración, `EmocionesBaseRealTests`.
- **Consentimiento de los avisos por WhatsApp:** `YoSoy.Tests/ConsentimientoWhatsAppEndpointsTests.cs` (401, 403 a los demás roles, el cuerpo
  que no puede decir de quién es, 409 con su código, 429, `no-store`), `ConsentimientoWhatsAppServiceTests` (las reglas), `TelefonoArgentinoTests`,
  `ComandoWhatsAppTests` (la BAJA, comparada con el código real del servicio de WhatsApp), `TextoConsentimientoWhatsAppTests` y, contra una base
  real, `ConsentimientoWhatsAppBaseRealTests` y `ConsentimientoWhatsAppEsquemaTests`.
- **Videollamadas (PR 3):** `YoSoy.Tests/VideoAccesoEndpointsTests.cs` (matriz 401/404/409, respuesta idéntica para "no existe" y "no sos parte", bordes de la
  ventana, rol y `exp` del token desde el servidor, 503, ICE, auditoría, cabeceras, rate limit),
  `AccesoSalaServiceTests`, `TurnCredentialsServiceTests` (vectores HMAC-SHA1 calculados aparte), `TokenSalaServiceTests`,
  `VideoInteropNodeTests` (el token de la API verificado con `signaling-server/token.js`) y `VideoBaseRealTests`
  (base real). Detalle en `docs/VIDEOLLAMADAS.md`.
