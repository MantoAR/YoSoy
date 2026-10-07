# Matriz de endpoints — módulo de mensajería/Chats

Este archivo no existía antes de "Mejora de chats y centrado" (`Cambios-Agus-jwt` tampoco lo
tenía). Se crea acotado al módulo que tocó este trabajo (`Endpoints/MensajeriaEndpoints.cs`),
no a toda la API — extenderlo al resto de los endpoints de la app es un trabajo aparte, de
otro alcance.

Todos los endpoints del grupo `/mensajeria` (salvo los dos marcados "público") exigen sesión
de **Profesional o Configurador** (`RequireAuthorization` a nivel de grupo, en
`MapMensajeria`). Esa es la fila "Rol" por default; solo se repite cuando un endpoint agrega
algo *encima* de eso.

| Método | Ruta | Rol | Pertenencia | Notas |
|---|---|---|---|---|
| GET | `/capacidades` | Público | — | Fuera del grupo a propósito: lo consulta cualquier pantalla antes de saber quién sos. |
| POST | `/mensajeria/simulado/entrante` | Público | — | Simula un mensaje entrante (equivalente al webhook real). No tiene, ni tendría sentido, sesión del otro lado. |
| GET | `/mensajeria/simulado/conversacion` | Público | — | Idem — el simulador del lado paciente todavía no tiene sesión. |
| GET | `/mensajeria/conversaciones` | Profesional, Configurador | **Ninguna** | Bandeja compartida a propósito (mesa de entradas): cualquiera del equipo ve y atiende lo escalado. "Mías" (frontend) es un filtro de comodidad, no un límite de acceso — ver CAMBIOS_AGUS.md, Paso 0. |
| GET | `/mensajeria/conversaciones/{id}` | Profesional, Configurador | Ninguna | Igual que arriba. |
| GET | `/mensajeria/conversaciones/{id}/paciente` | Profesional, Configurador | Ninguna | Panel del lead: datos administrativos únicamente (sin domicilio, sin fecha de nacimiento, nada clínico). |
| GET | `/mensajeria/conversaciones/{id}/mensajes` | Profesional, Configurador | Ninguna | |
| PUT | `/mensajeria/conversaciones/{id}/leida` | Profesional, Configurador | Ninguna | |
| POST | `/mensajeria/conversaciones/{id}/responder` | Profesional, Configurador | Ninguna | Audita (acción + id, nunca el texto). |
| PUT | `/mensajeria/conversaciones/{id}/cerrar` | Profesional, Configurador | Ninguna | Audita. |
| PUT | `/mensajeria/conversaciones/{id}/reabrir` | Profesional, Configurador | Ninguna | Nuevo (A3). 400 si no estaba Cerrada. Audita. |
| PUT | `/mensajeria/conversaciones/{id}/asignar` | **Solo Configurador** | — | Nuevo (A3). 403 a Profesional. Resuelve el Caso abierto del paciente y delega en `AsignacionService.ReasignarManual` — mismo gate que `PUT /casos/{id}/reasignar`. 400 si el paciente no tiene caso abierto. |
| GET | `/mensajeria/conversaciones/{id}/notas` | Profesional, Configurador | Ninguna (lectura) | Nuevo (A3). Nunca al paciente; el texto nunca se audita. |
| POST | `/mensajeria/conversaciones/{id}/notas` | Profesional, Configurador | Ninguna | Nuevo (A3). |
| DELETE | `/mensajeria/conversaciones/{id}/notas/{idNota}` | Profesional, Configurador | **El autor, o Configurador** | Nuevo (A3). 403 para cualquier otro profesional. |
| GET | `/mensajeria/etiquetas` | Profesional, Configurador | Ninguna | Nuevo (A3). Catálogo compartido. |
| POST | `/mensajeria/etiquetas` | Profesional, Configurador | Ninguna | Nuevo (A3). Mismo criterio que `/preguntas-frecuentes` (ABM sin restricción extra). 409 si el nombre ya existe. |
| PUT | `/mensajeria/etiquetas/{id}` | Profesional, Configurador | Ninguna | Nuevo (A3). |
| DELETE | `/mensajeria/etiquetas/{id}` | Profesional, Configurador | Ninguna | Nuevo (A3). Baja lógica (`Activa=0`): no borra las ya puestas en conversaciones. |
| POST | `/mensajeria/conversaciones/{id}/etiquetas/{idEtiqueta}` | Profesional, Configurador | Ninguna | Nuevo (A3). Idempotente. |
| DELETE | `/mensajeria/conversaciones/{id}/etiquetas/{idEtiqueta}` | Profesional, Configurador | Ninguna | Nuevo (A3). |
| GET | `/mensajeria/respuestas-rapidas` | Profesional, Configurador | — | Nuevo (A3). Cada quien ve las globales + las propias (nunca las de otro profesional: lo filtra el `WHERE` del DAO, no hace falta chequeo aparte). |
| POST | `/mensajeria/respuestas-rapidas` | Profesional, Configurador | **Dueño fijo según quién crea** | Nuevo (A3). Configurador → global (`IdProfesional=NULL`); Profesional → propia. Ninguno elige el dueño. |
| PUT | `/mensajeria/respuestas-rapidas/{id}` | Profesional, Configurador | **Dueño de la fila** | Nuevo (A3). Global → solo Configurador (403 a Profesional); propia → solo ese profesional (403 a Configurador y a cualquier otro). |
| DELETE | `/mensajeria/respuestas-rapidas/{id}` | Profesional, Configurador | Dueño de la fila | Nuevo (A3). Mismo criterio que PUT. Baja lógica. |
| GET | `/mensajeria/notificaciones-internas` | Profesional, Configurador | Ninguna | Preexistente. |
| PUT | `/mensajeria/notificaciones-internas/{id}/leida` | Profesional, Configurador | Ninguna | Preexistente. |
| POST | `/mensajeria/recordatorios/ejecutar` | Profesional, Configurador | — | Preexistente (dispara el servicio de recordatorios fuera de su intervalo; pensado para pruebas/demo). |
| GET | `/mensajeria/preguntas-frecuentes` | Profesional, Configurador | Ninguna | Preexistente. |
| POST/PUT/DELETE | `/mensajeria/preguntas-frecuentes[/{id}]` | Profesional, Configurador | Ninguna | Preexistente. |

**Por qué varios endpoints dicen "Pertenencia: Ninguna" a propósito:** el módulo de Chats es
una bandeja compartida entre todo el equipo (como una mesa de entradas) — así funcionaba antes
de este trabajo y seguir así es una decisión consciente, no un descuido (se consideró agregar
un filtro duro por profesional asignado y se descartó: ver CAMBIOS_AGUS.md, Paso 0 y Frente
A1 — cortaría la posibilidad de que cualquiera del equipo atienda un escalamiento urgente sin
asignar). Donde sí hay pertenencia (asignar, borrar una nota, tocar una respuesta rápida) es
porque la acción es de alguien en particular (el administrador, el autor, el dueño), no
porque haga falta limitar quién puede *ver* una conversación.

**Probado:** matriz de rol completa (401 sin token, 403 rol equivocado, 403 recurso ajeno, 404
inexistente, 200/201/204 camino correcto) en `docs/pruebas-ui-cambios-agus/p10_chats_a3.py`
(no versionado, 27/27) y permanente en `YoSoy.Tests/ChatsGestionTests.cs` (5 tests, los puntos
más sensibles: asignar, reabrir, notas ajenas, respuestas rápidas cruzadas).
