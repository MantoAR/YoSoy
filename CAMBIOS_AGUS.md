# Cambios-Agus: consolidación de la app web

Seguimiento de la rama local `Cambios-Agus`. Cada paso deja anotado qué se hizo, qué se decidió y
qué queda pendiente. **La rama no se sube a GitHub**: queda local hasta revisarla juntos.

**Estado:** Pasos 0 a 6 terminados, más una tanda posterior (revisión XSS de mensajería, rama `fix-xss` y
script SQL) y un sexto tramo de `fix-xss` con los lugares de `main` que quedaban sin escapar. Son 65 commits
locales sobre `rediseno-visual`, y hay además dos ramas locales sobre `main`: `fix-xss` (16 commits) y
`fix-registro-publico` (2 commits, bug 18 de la tabla de bugs). No se hizo ningún push.
Al cierre:
- `dotnet build`: 0 errores y los mismos 167 warnings de nulabilidad que había al empezar;
- `dotnet test`: 96 de 96;
- el recorrido de todas las pantallas de los tres roles no muestra errores en la consola.

---

## Para revisar primero

1. **Seguridad: XSS almacenado** en el configurador, el profesional, el paciente y la sesión de video. Son 5 commits `fix`; el detalle está en el Paso 5 y en la tabla de bugs. Es lo más importante de la rama: un paciente podía ejecutar código en la sesión del profesional o del configurador.
   - La rama local `fix-xss` lleva esos 5 commits a `main`, más un sexto tramo de 11 commits (ver "Después del cierre") con los lugares que en esta rama se escaparon al reescribir pantallas, no en esos commits. Entre los dos tramos quedan 16 commits sobre `main`, verificados con una prueba de interfaz de 34 verificaciones (0 fallas). El más grave que cubre el sexto tramo era el listado de solicitudes de registro del configurador (público, sin sesión).
2. **El registro público de profesionales no funciona** (bug 18): siempre responde 500, incluso con datos válidos. Es un bug de `main`, no de esta rama, y esta rama lo hereda sin tocar. Ya está corregido en la rama local `fix-registro-publico` (2 commits sobre `main`); entra acá por merge, ver el bug 18 en la tabla de bugs.
3. **Decisiones que tocan comportamiento.** Ninguna se activó sola; todas están detalladas más abajo.
   - El cierre de sesión por inactividad nunca funcionó (bug 13). No lo activé porque cortaría las videollamadas largas.
   - La autoconfirmación de la agenda del profesional quedó apagada (Paso 4).
   - "Confirmar" desde el Inicio un turno que propuso el mismo profesional ahora pregunta antes. El botón sigue.
   - Asignar o cambiar de profesional en el configurador ya no preselecciona el primero de la lista.
   - Los avisos flotantes del paciente muestran como máximo tres. El resto queda en la campana.
4. **Migración:** `migrations/add_lectura_conversacion.sql` (Paso 3). Sin ella, la bandeja de chats da error.
5. **Merge con `videollamadas-seguras`:** va a tener conflictos en casi todo `PANTALLAS/`. Hay una guía en "Pendientes".
6. **Cómo probar:** ver la última sección. Las capturas de antes y después y las pruebas de interfaz quedaron en `docs/` como carpetas locales, sin versionar.

---

## Base de la rama

`Cambios-Agus` sale de **`rediseno-visual`** (d1200bf), no de `main`.

- `rediseno-visual` tiene 7 commits de rediseño probados (tema compartido, mensajería, agenda
  Día/Semana/Mes, Profesional, Configurador, Paciente y login) y está construida sobre `main`
  al día (4648edb, igual a `origin/main` al momento de crear la rama).
- Partir de `main` habría obligado a rehacer ese trabajo.

---

## Paso 0: estado de partida

### Lo que ya traía `rediseno-visual`

| Área | Estado |
|---|---|
| `PANTALLAS/shared/tema.css` | Variables de color, Inter, chips, botones, campos, tablas, toasts. Acento azul (no es el de YoSoy). |
| Mensajería (`Pantalla_ChatSimulado`) | Reestilizada, con barra lateral oscura de íconos y 3 columnas. Sigue siendo una pantalla aparte. |
| Profesional | Barra lateral de íconos; agenda con Día/Semana/Mes sobre los mismos turnos; 88 → 14 estilos incrustados. |
| Configurador | Barra lateral oscura con texto; todavía usa Bootstrap por CDN. |
| Paciente | Barra lateral de íconos; campanita flotante; consentimiento con el tema. |
| Login | Con el tema; todavía carga Bootstrap. |
| Sin tocar | Registro de paciente y profesional, consentimiento de registro, crear cuenta profesional, restablecer contraseña, las dos `sesion_video.html`, el aviso de inactividad de `js/sesion.js`. |

Cada pantalla tiene su propio `toast()`, `confirmar()`, `const API`, manejo de `fetch` y formateo
de fechas: el mismo código repetido tres o cuatro veces.

### Estilos incrustados que quedan (`style=`, `.style.`, `cssText`)

| Archivo | `main` | hoy |
|---|---|---|
| `Pantalla_Profesional/js/nuevo_dashboard.js` | 155 | 32 (muchos son la grilla semanal, con posiciones calculadas) |
| `Pantalla_Paciente/js/dashboard.js` | 113 | 10 |
| `Pantalla_Configurador/js/dashboard.js` | 71 | 2 |
| `js/sesion.js` | 15 | 15 |
| `Pantalla_Profesional/sesion_video.html` | 21 | 21 |
| `Pantalla_Usuario/Usuario/registro_profesional.html` | 6 | 6 |

Diálogos nativos que quedan: `confirm()` al cerrar una conversación del chat, `prompt()` al
derivar un paciente nuevo (Profesional), varios `alert()` en reportes, registro y sesión de video.

### Mapa de navegación actual

**Profesional** (`nuevo_dashboard.html`, pestañas con `activarTab`)

- Inicio: sesiones de hoy con sala, próximo turno, cita urgente, pacientes de hoy, turnos pendientes de confirmación.
- Mi agenda: calendario Día/Semana/Mes, panel del día (aceptar, rechazar, cancelar, reprogramar, solicitar turno) y "Mis horarios de trabajo".
- Mis pacientes: tabla, historia clínica en un modal (datos, evoluciones, sesiones clínicas), asignaciones nuevas (motivo, confirmar con primer turno, derivar).
- Sesión: `sesion_video.html` en un iframe (sala, video, emociones, transcripción, evolución).
- Reportes: descarga de "Mis atenciones" en CSV.
- Mi perfil.
- Avisos flotantes de "Turno confirmado → Generar sala".

**Paciente** (`dashboard.html`, `mostrarSeccion`)

- Inicio con el próximo turno.
- Mi agenda: calendario, filtro por profesional y panel del día (cancelar, reprogramar, solicitar turno).
- Mis profesionales, con "Solicitar cambio de profesional".
- Sesión: código de sala → `sesion_video.html`.
- Admisión: formulario o estado del informe.
- Consentimiento: página aparte, `consentimiento.html`.
- Mi perfil.
- Campanita de notificaciones y avisos flotantes.

**Configurador** (`dashboard.html`, `mostrarTab`)

- Cuentas: usuarios (pacientes y profesionales), solicitudes de registro, solicitudes de cambio, recuperación de contraseña.
- Auditoría: log de acciones.
- Clínico: admisiones, reportes, turnos, agenda, evoluciones, eventos de crisis.
- Sistema: roles, especialidades, coberturas, interacciones IA.
- No tiene Inicio: abre con "Seleccioná una sección del menú".

**Mensajería** (`Pantalla_ChatSimulado/index.html`): bandeja del administrador, más el modo
paciente para simular el WhatsApp. Por los avisos internos (destinatario "Administrador"),
corresponde al rol configurador.

### Línea base (antes de cambiar nada)

- `dotnet build --no-incremental`: 0 errores y **167 warnings**, todos de nulabilidad (CS8618, CS8601, CS8603, CS8600, CS8625, CS8604). Son previos; no se agregan nuevos.
- `dotnet test`: **89/89** correctas.
- La app levanta en `http://localhost:5000`. Recorrido con Edge headless por los tres roles:
  - Configurador y mensajería: sin errores en la consola.
  - Profesional: `GET /profesionales/1/turnos-confirmados` responde **500** (bug 1).
  - Paciente: `GET /pacientes/1/proximo-turno` y `GET /pacientes/1/informe-admision` responden 404. Es esperado: 404 quiere decir "no hay" y la pantalla lo maneja.

### Bugs encontrados (se corrigen en el Paso 4 o el 6, cada uno en su commit)

1. **`/profesionales/{id}/turnos-confirmados` da 500** cuando hay un turno confirmado. La causa es `TimeSpan.ToString(@"hh:mm")`: en un formato de `TimeSpan` los `:` van escapados (`hh\:mm`). Por eso nunca aparecen las "Sesiones de hoy" ni los avisos de "Generar sala".
2. **`PUT /turnos/{id}/generar-sala`** tiene el mismo formato: guarda el código de sala, pero se cae antes de crear la notificación al paciente y responde 500.
3. **`creadoPor` nunca llega al frontend.** `TurnoDAO` no lee la columna `CreadoPor` y `TurnoDetalle` no la incluye. Efectos:
   - En la agenda del profesional no se distingue "me lo pidieron" de "lo propuse yo", así que Aceptar/Rechazar del panel del día nunca aparece.
   - El paciente no tiene botón para confirmar un turno que le propuso el profesional. `confirmarTurnoPaciente()` existe, pero nada la llama.
4. **"Mis horarios de trabajo":**
   - No ordena por día, porque la base guarda `lunes` en minúscula y el orden compara contra `Lunes`.
   - Al editar, el `<select>` de día no reconoce el valor guardado.
   - (La tabla de 7 encabezados con 5 celdas ya se había corregido en `rediseno-visual`.)
5. **Panel del día del profesional:** con dos turnos activos el mismo día se repiten los ids `formReprogramarProf`, `reprogDetallesProf` y `btnReprogramarProf`, y reprogramar siempre actúa sobre el primero.
6. **Cerrar sesión en el Configurador** no limpia `localStorage`; las otras pantallas sí.
7. **`sesion_video.html` del paciente** ignora `?sala=` en la URL, así que el paciente tiene que volver a tipear el código que le llegó en la notificación.
8. **`consentimiento crear_cuenta.html`** tiene el HTML mal cerrado (un `</div>` de más y un `</p>` sin apertura).

**No se corrigen:** `crear-cuenta-profesional.html` y `reset-password.html` llaman a
`/profesionales/activar-cuenta` y `/recuperacion-password/reset`, que **no existen en `main`**.
Están en `videollamadas-seguras`, la rama del compañero. Esas pantallas se reestilizan, pero
recién funcionan con ese merge.

### Archivos sin uso (marcados acá, no se borran ni se mueven)

| Archivo | Por qué |
|---|---|
| `PANTALLAS/Pantalla_Profesional/dashboard.html`, `js/dashboard.js`, `css/dashboard.css` | Panel viejo; el login lleva a `nuevo_dashboard.html`. `videollamadas-seguras` los borra. |
| `PANTALLAS/Pantalla_Usuario/Usuario/consentimiento_crear_cuenta.html` | Copia idéntica de `consentimiento crear_cuenta.html` (con espacio), que es la que enlaza el login. |
| `PANTALLAS/Pantalla_Configurador/css/estilos.css` | Ninguna pantalla lo carga. |
| `crearTurnoProfesional()` y `renderTodosLosTurnos()` en `nuevo_dashboard.js` | Usan ids que no existen en ningún template (`nt-*`). |
| `abrirModalTurno()` y el modal "Solicitar turno" en el JS del paciente | Ningún botón lo abre. Se pide turno desde el panel del día. |

No se tocan para no generar conflictos "modificado vs. borrado" con `videollamadas-seguras`.

### Riesgo de merge con `videollamadas-seguras`

Esa rama reescribe casi las mismas pantallas: login en React, `sesion.js`, validaciones,
las dos `sesion_video.html` y los JS de Profesional y Paciente (unos 40 archivos de `PANTALLAS/`).
Cualquier rediseño choca con eso: el merge va a necesitar resolución a mano, siguiendo
`NOTAS_MERGE.md`.

---

## Plan

1. **Sistema de diseño.**
   - Paleta de YoSoy: violeta `#6A5ACD` como primario, degradé `#0D6EFD → #6A5ACD`, lavandas de fondo y bordes.
   - Inter como fuente única.
   - Componentes CSS y una capa JS compartida (`shared/ui.js`) con fetch, toasts, modales, fechas y validación.
   - Guía visual en `shared/guia-componentes.html`.
   - Decidir qué pasa con Bootstrap.
2. **Shell común** (`shared/shell.js`):
   - Barra superior con menús desplegables por rol, encabezado de página con migas y acciones, menú de usuario y campana.
   - Navegación por `#hash`.
   - Se saca la barra lateral.
3. **Chats en un cajón** que se abre desde la barra superior, se puede expandir y mantiene las mitigaciones:
   - Lista con filtros y no leídos.
   - Hilo con separadores, agrupación y carga hacia arriba.
   - Composer con plantillas y reintento.
   - Polling eficiente y panel de contexto.
4. **Agenda:** ajustes al shell y bugs de "Mis horarios de trabajo".
5. **Resto de pantallas**, en el orden pedido: login y registro, Inicio por rol, turnos, historia clínica y sesiones, consentimiento, Configurador y perfil.
6. **Consolidación:** una sola capa de `fetch`, cero duplicados de `toast`/`confirmar`, estilos incrustados a casi cero y bugs con su commit.

## Decisiones

### Paso 1: sistema de diseño

- **Paleta.** Se mantiene la de YoSoy. Es la que usaban todos los CSS de `main`:
  - violeta `#6A5ACD` como acento;
  - degradé `#0D6EFD → #6A5ACD` en los botones del login;
  - lavandas `#F0EAFF` y `#E0D4F5` para fondos y bordes.

  El azul del rediseño anterior se reemplaza por el violeta. El degradé queda solo para la marca
  (el nombre "YoSoy" en la barra), sin usarlo como fondo en todos lados.
- **Tipografía.** Inter con caída a `system-ui`, pesos 400/500/600 y base de 13 px.
- **Prefijo `ui-`** en todos los componentes, para no chocar con las clases propias de cada pantalla.
- **Capa JS compartida en `shared/ui.js`** (`window.YS`), sin dependencias.
  - `YS.fetch` devuelve el `Response` igual que `fetch`, así cada pantalla migra sin cambiar su lógica de status.
  - `YS.api` devuelve el JSON o tira un error con el `mensaje` del servidor.
  - Todos los pedidos mandan `X-Usuario-Id`: el backend ya lo usaba para auditar y para el alcance de reportes, aunque varias pantallas no lo mandaban.
- **Bootstrap se saca.**
  - Nadie usa su JS: no hay `data-bs-*` ni `bootstrap.Modal`.
  - Del CSS solo se usaban unas pocas utilidades en el Configurador y el reboot en el login y el registro profesional.
  - El reset propio de `tema.css` cubre el reboot.
  - Se quita pantalla por pantalla en el Paso 5, cuando se reescribe cada template. Hasta entonces convive sin chocar, gracias al prefijo `ui-`.
- **Guía de componentes** en `PANTALLAS/shared/guia-componentes.html`
  (`http://localhost:5000/pantallas/shared/guia-componentes.html`).

### Paso 2: shell común

`shared/shell.js` arma en todas las pantallas con sesión la misma estructura:

- barra superior con logo, menú y submenús (se abren con clic, al pasar el mouse o con el teclado);
- campana de avisos y menú de usuario a la derecha;
- debajo, un encabezado con migas, título y acciones.

Cambia el contenido del menú según el rol, no el esqueleto.

| Rol | Menú | Menú de usuario | Campana |
|---|---|---|---|
| Profesional | Inicio · Agenda (Calendario, Mis horarios de trabajo) · Pacientes · Sala de video · Reportes | Mi perfil, Cerrar sesión | Turnos confirmados sin sala, con "Generar sala" |
| Paciente | Inicio · Turnos (Mis turnos, Mis profesionales) · Sala de video · Admisión · Consentimiento | Mis datos, Cerrar sesión | Sus notificaciones |
| Configurador | Inicio · Usuarios (4) · Clínico (5) · Reportes · Configuración (4) · Auditoría | Cerrar sesión | Avisos internos y Chats: se agregan en el Paso 3 |

- **Se saca la barra lateral de íconos y la oscura del Configurador.** Competía con el menú superior y no puede haber dos navegaciones.
- **Navegación por `#hash`** (`dashboard.html#agenda`).
  - El menú son links: funcionan el botón Atrás, abrir en otra pestaña y entrar directo a una sección.
  - Las funciones de cada panel (`activarTab`, `mostrarSeccion`, `mostrarTab`) siguen siendo las que dibujan cada sección.
- **"Mis horarios de trabajo" es una sección propia.** Antes era la segunda tarjeta del calendario. La agenda Día/Semana/Mes queda sola en "Calendario".
- **Títulos y acciones principales van al encabezado:**
  - "Cita urgente" en el Inicio del profesional;
  - "Agregar horario" en Mis horarios;
  - "Actualizar" y las cantidades en los listados del Configurador;
  - "Mostrar dados de baja" en Usuarios.
- **Campana:**
  - **Profesional:** los avisos flotantes de "Turno confirmado → Generar sala" pasan a la campana. Al entrar hay un aviso si quedan pendientes, una vez por sesión del navegador. Solo se muestran turnos de hoy en adelante.
  - **Paciente:** la campanita flotante pasa a la barra, con el mismo comportamiento: al abrirla, las notificaciones quedan leídas. Los avisos flotantes de notificaciones sin leer se mantienen y se van cuando se leen desde la campana.
- **Inicio del Configurador:** antes decía "Seleccioná una sección del menú". Ahora muestra:
  - tarjetas con datos reales: pendientes de registro, de cambio y de contraseña, admisiones, turnos de hoy, pacientes y profesionales activos;
  - la actividad reciente de auditoría.
- **Consentimiento sin sesión.** El link que le llega al paciente sigue mostrando el aviso de "tenés que ingresar" con el botón Ingresar, en vez de redirigir directo al login.
- **Sesión:**
  - El shell vuelve al login si falta la sesión del rol. Un profesional que abre la URL del Configurador vuelve al login.
  - Cerrar sesión pide confirmación con el modal propio y limpia `localStorage`.
  - `js/sesion.js` (aviso de inactividad) sigue cargado en las mismas pantallas que antes.
- **El Configurador deja de cargar `bootstrap.bundle.js`** (no se usaba). El CSS de Bootstrap se saca en el Paso 5.
- **La pantalla de mensajería** pasa al shell en el Paso 3, cuando se rehace el chat.
- **No quedaron pantallas huérfanas nuevas.**

**Adelantados del Paso 6** (la campana del profesional los necesitaba para funcionar):

- bugs 1 y 2, el formato de `TimeSpan` en `turnos-confirmados` y `generar-sala`, cada uno en su commit;
- bug 6, cerrar sesión en el Configurador, quedó resuelto por el shell.

### Paso 3: chats desde un botón

- **La bandeja es un cajón**, no una pantalla aparte. El botón **Chats** de la barra lo abre sobre la pantalla actual, sin recargar.
  - Ocupa unos 780 px a la derecha, con la lista y el hilo; se puede expandir a pantalla completa y se cierra con Esc.
  - Los datos del paciente se superponen en el cajón y son una tercera columna en pantalla completa.
  - `Pantalla_ChatSimulado/index.html` sigue existiendo: tiene la misma bandeja en modo página y el **simulador del paciente** (lo que antes era el "modo paciente"). Para chatear no hace falta ir ahí.
- **Quién la ve.** Solo el **configurador**: la bandeja muestra las conversaciones de todos los pacientes y no hay filtro por profesional. Mostrársela a un profesional expondría pacientes ajenos. Es el mismo destinatario ("Administrador") de los avisos internos.
- **Mitigaciones intactas** (verificado con el módulo apagado y con un pedido por proxy):
  - `RequiereMensajeriaHabilitada()` sigue siendo el único control. Lo usan el filtro y `GET /capacidades`, que dice si mostrar el botón.
  - Con el módulo apagado, o un pedido remoto con `SoloLocal`, `/capacidades` da `false`, el botón no aparece y `/mensajeria` responde 404.
  - El JS y el CSS de la bandeja viven en `Pantalla_ChatSimulado/`, que con el módulo apagado tampoco se sirve.
- **Backend, todo aditivo** (los pedidos de antes responden igual):
  - `desdeMensaje` en el listado (polling incremental);
  - `noLeidos`, `idUltimoMensaje` y `origenUltimoMensaje` en el resumen;
  - `PUT /conversaciones/{id}/leida`;
  - historial paginado con `antes` y `limite`;
  - 7 pruebas xUnit del control de acceso.
- **Migración nueva:** `migrations/add_lectura_conversacion.sql` agrega `Conversacion.IdUltimoMensajeLeido`.
  - Es idempotente y ya está aplicada en la base local.
  - Las conversaciones existentes arrancan leídas hasta su último mensaje.
  - **Sin esta migración, la bandeja da error.** Está en la lista del README.
- **No leídos.** Son los mensajes del paciente posteriores a la marca de lectura. La marca es **compartida por el equipo**, como en una bandeja de WhatsApp Business. Se mueve sola cuando la conversación está abierta y a la vista, scrolleada hasta abajo.
- **Polling.** Cada 3 s con la bandeja a la vista y cada 20 s cerrada, y se espacia hasta 12 s y 45 s si no hay novedades.
  - Con la pestaña oculta no se pide nada.
  - Pide solo las conversaciones con mensajes nuevos, y una vez por minuto la lista completa (para ver lo que leyeron otros).
  - No hay WebSocket.
- **"Escribiendo…" no se hizo:** el canal (WhatsApp o el simulado) no informa cuándo el paciente está escribiendo.
- **Respuestas rápidas** en `Pantalla_ChatSimulado/plantillas.json`. Son seis textos administrativos sin contenido clínico, para revisar con el equipo. Se insertan en la respuesta y no se mandan solas.
- **Avisos internos** (urgencias, derivaciones, avisos de turnos): pasan de la campanita de la pantalla de mensajería a la campana de la barra del configurador, con "Abrir chat" y "Marcar leído".
- **"Cerrar derivación"** pide confirmación con el modal propio, en vez del `confirm()` nativo.
- **Envío.** Los mensajes del equipo ahora mandan `X-Usuario-Id`, así que la "Respuesta manual en chat" queda en la auditoría. Antes no se registraba porque la pantalla no mandaba el header.
- **Bot.** No se tocó su lógica. No aparecieron bugs nuevos en el recorrido.

### Paso 4: agenda

La agenda Día/Semana/Mes ya venía de `rediseno-visual`. En este paso se corrigieron cuatro bugs, cada uno en su commit:

- **Bug 4: "Mis horarios de trabajo".**
  - Ordena por número de día (`YS.dias`) y no por el texto.
  - Al editar, el select toma bien el día guardado: con `miercoles` quedaba en "Lunes" y guardar movía el horario a lunes.
  - Los días se muestran con su forma prolija. No se migraron datos: el backend ya normaliza el día.
- **Bug 5: reprogramar con dos turnos activos el mismo día.** Los ids llevan el `idTurno`, así que cada turno tiene su formulario.
- **Bug 9 (nuevo): después de reprogramar, `null.getDate()`** tiraba un error y no se actualizaba el panel.
- **Bug 3: `creadoPor`.**
  - `Turno` y `TurnoDetalle` lo exponen, y el pedido web del paciente lo guarda como `paciente`, igual que el bot.
  - En la agenda del profesional aparecen Aceptar/Rechazar para los pedidos del paciente.
  - Los turnos que propuso el profesional se ven en violeta con la etiqueta "Propuesto".
  - El paciente tiene el botón **Confirmar turno** para las propuestas, en el panel del día y en el Inicio. RN-02 sigue aplicando: sin consentimiento, ofrece firmarlo.

**Colores de estado únicos en toda la app:** confirmado verde, pendiente amarillo, propuesto violeta, cancelado gris y realizado azul. En la agenda del paciente los días que atiende el profesional pasan a violeta claro (antes verde, que es "confirmado"). Hoy lleva una barra arriba y el día elegido, un borde.

> **Decisión para revisar juntos: autoconfirmación de la agenda del profesional.**
>
> - **Qué hace:** si el mismo día hay un pedido del paciente y una propuesta del profesional, confirma todos los pendientes de ese día.
> - **Por qué nunca corrió:** porque `creadoPor` no llegaba.
> - **Por qué la dejé apagada:** al arreglar `creadoPor` se hubiera activado sola, y tiene dos problemas.
>   - Confirmaría turnos de **pacientes distintos** del mismo día.
>   - Escribe en la base solo por abrir el calendario.
>
> Quedó apagada a propósito con `AUTOCONFIRMAR = false`, en `dibujarCalendarioProfesional`. Si se quiere, habría que limitarla al mismo paciente y pedir confirmación.

**Datos de prueba que quedaron en la base local:**

- el turno 1007 reprogramado al 5/10;
- la propuesta 2007, confirmada por el paciente 13;
- la firma del consentimiento de telemedicina del paciente 13;
- mensajes del simulador con el teléfono 1155550002.

### Paso 5: resto de las pantallas

**Acceso y registro.**
- Login, acuerdo, registro de paciente y de profesional, crear cuenta profesional y restablecer contraseña usan el sistema de diseño (`shared/acceso.css`) y ya no cargan Bootstrap.
- Mismos campos, ids, endpoints y datos enviados.
- Validación en cada campo, en lugar de `alert()` o de mandar el formulario incompleto.
- Los errores del servidor se muestran en la pantalla.
- El DNI del registro acepta solo números, que es como lo busca el asistente del chat.
- Bug 8 corregido: el acuerdo de atención tenía el HTML mal cerrado.
- Crear cuenta profesional y restablecer contraseña muestran el error hasta que se haga el merge de `videollamadas-seguras`, donde están sus endpoints.
- Los datos del tutor de un menor siguen sin guardarse: es un pendiente anterior y necesita cambios en el backend.

**Inicio de cada rol.**
- **Profesional:** tablero con turnos de hoy, pedidos para confirmar, pacientes asignados y próximo turno. Debajo, las sesiones y los pacientes de hoy y los pendientes.
  - Los turnos se piden una vez (antes, tres).
  - El próximo turno ahora considera la hora. Antes un turno de hoy más tarde nunca figuraba.
- **Paciente:** turnos próximos, turnos por confirmar y estado del consentimiento. Debajo, el próximo turno con "Confirmar turno" y la lista de los siguientes.
- **Configurador:** el tablero del Paso 2, con las tarjetas de chats cuando la mensajería está encendida.

**Listados del Configurador** con un componente nuevo, `YS.tabla`.
- Búsqueda, filtros con contador, orden por columna y paginación de 25, todo en el navegador sobre lo que ya trae la API.
- Estado vacío propio y "Sin resultados" cuando la búsqueda no encuentra nada.
- **Turnos:** vista propia.
  - Muestra nombres de paciente y profesional, día de la semana y estado como chip, incluido "Propuesto".
  - Filtros por estado y por profesional.
  - El link "Ver turnos del paciente" que sale de los chats filtra y muestra el nombre del paciente.
- **Catálogos** (agendas, evoluciones, eventos de crisis, roles, especialidades, coberturas, interacciones IA):
  - columnas con nombre legible;
  - ids de paciente y profesional reemplazados por nombres;
  - horas y fechas formateadas;
  - Sí/No y estados como chips.
- **Solicitudes de registro, de cambio de profesional y de recuperación, y admisiones:** filtros por estado. Las solicitudes de cambio arrancan en "Pendientes" y las admisiones en "Por resolver".
- **Auditoría:** botones para pasar a registros más antiguos. Antes se veía solo la primera página del servidor.
- **Recuperación:** la contraseña temporal se muestra en un modal con "Copiar". Antes era un toast de 8 segundos.
- **Usuarios:**
  - buscador en el encabezado;
  - tarjetas como botones, que se pueden usar con teclado;
  - acciones como botones del sistema;
  - la selección se mantiene al buscar.
- **Bootstrap:** ya no se carga. Los dos modales de la página usan la estructura del sistema de diseño:
  - se cierran con Esc, con la X, con Cancelar o con un clic afuera;
  - el foco vuelve al botón que los abrió;
  - el título y el botón cambian según la acción.
- **`toast()` y `confirmar()`** de la página usan los del sistema. Dar de baja, rechazar y eliminar piden confirmación con botón de peligro.
- **Modal de edición:**
  - etiquetas legibles;
  - ayuda con el nombre actual del paciente o profesional;
  - validación de ids, fechas y horas: solo lo que la API rechazaría igual.

> **Cambio de comportamiento menor:** asignar y cambiar de profesional ya no preseleccionan el primero de la lista; hay que elegirlo. Si el servidor falla, el modal queda abierto con su mensaje en lugar de cerrarse.

**Historia clínica y sesiones (profesional).**
- **Mis pacientes:** tabla con `YS.tabla`:
  - búsqueda;
  - filtro "Asignaciones nuevas";
  - estado vacío;
  - "Actualizar" en el encabezado.
- **"Sesión"** abre la sala de video con ese paciente ya elegido. Antes abría la sala sin paciente.
- **Historia clínica:** modal del sistema con tres pestañas accesibles (datos, evoluciones y sesiones clínicas), contadores y estados vacíos.
  - Se cierra con Esc o con un clic afuera.
  - Desde ahí se puede abrir la sesión de video.
- **Asignaciones nuevas:**
  - "Devolver a admisión" pide el motivo en un modal validado. Antes era un `prompt()`.
  - Proponer el primer turno valida fecha y hora en el campo y no deja elegir una fecha pasada.
- **Aviso de RN-02** (paciente sin consentimiento): modal del sistema con el link para copiar.
- **Reportes:** avisos del sistema en lugar de `alert()`. El CSV de "Mis atenciones" ahora sale con BOM (bug 15).
- **Mis horarios y cita urgente:**
  - mostrar y ocultar con clases, sin estilos inline;
  - validación en el campo (horas faltantes, fin antes que inicio);
  - el mensaje del servidor cuando algo falla;
  - la cita urgente avisa si el paciente no firmó el consentimiento, igual que al entrar a una sala desde el Inicio.

**Sesiones de video.**
- Bug 7: la sala del paciente toma el código de `?sala=`, que traen el panel, la campana y la notificación.
- Las dos páginas usan la tipografía, los colores y los botones del sistema, sin estilos inline estáticos. Se mantienen los mismos ids, el mismo WebRTC, la misma señalización y face-api.
- Los `alert()` de la sesión del profesional pasan al aviso de la página.

> **Rediseño liviano a propósito.** `videollamadas-seguras` trae su propia versión de las dos páginas, con links con token en lugar de códigos de sala. En esa rama son archivos nuevos, así que el merge va a chocar con cualquier versión de acá.
>
> Lo práctico es tomar la versión de esa rama y aplicarle el sistema de diseño (`tema.css`, `ui.js`, `shell.js`).

**Consentimiento.**
- Muestra un estado de carga mientras consulta. Antes quedaba en blanco.
- Si el servidor falla, ofrece "Reintentar". Antes cualquier error mostraba "tenés que ingresar con tu usuario"; eso ahora queda solo para 401/404 o cuando no hay sesión.
- Botones y alertas del sistema.
- La firma, la versión del texto y RN-02 no cambian. El texto legal sigue siendo el marcador `[COMPLETAR CON TEXTO LEGAL VALIDADO]`.

**Perfiles** ("Mis datos" del paciente y "Mi perfil" del profesional).
- Formularios del sistema, con etiquetas asociadas a sus campos y `autocomplete`.
- Validación en el campo con las mismas reglas que el registro: DNI de 7 a 9 números, mail válido, teléfono y fecha de nacimiento no futura. El mail es obligatorio para el profesional, porque ahí le llegan los avisos.
- Un aviso al guardar y el mensaje del servidor si falla.
- Bug 16: un campo opcional que estaba en NULL y se deja vacío se manda como `null`. Antes, guardar sin tocar nada cambiaba NULL por `""`.

**Paciente.**
- **Mis profesionales:** tarjetas con avatar. "Solicitar cambio" abre un modal del sistema; el motivo sigue siendo opcional.
- **Informe de admisión:**
  - formulario del sistema, con tildes y voseo;
  - "¿Cuál?" aparece solo si se contesta "Sí", y si se contesta "No" ya no se manda el detalle que había quedado escondido;
  - especialidad y motivo validados en el campo.
- **Avisos flotantes:** como máximo tres, primero los que tienen una acción, más "Tenés N avisos más · Ver todos", que abre la campana. Antes, con datos reales, se apilaban quince tarjetas.
- **Confirmaciones del sistema:** cancelar turno pide confirmar con botón de peligro y "Volver". El aviso de RN-02 ofrece "Firmar ahora" / "Más tarde".

**Seguridad: XSS almacenado.** Al seguir revisando aparecieron más lugares, en total 5 commits:
- `a9eeb16`: configurador. El informe de admisión, la historia clínica y los nombres en Usuarios y en los modales.
- `7e62c59`: profesional. El motivo del turno (lo escribe el paciente), el nombre y los datos del paciente en el panel del día, la vista semanal, Mis pacientes y la historia clínica. Además, el botón "Historia" pasaba el nombre en el `onclick` sin escapar `&quot;`.
- `9491bcb`: paciente. Nombre, especialidad, mail y teléfono del profesional, y motivo y modalidad del turno.
- `6d7d641`: sesión de video del profesional. Los nombres en el select de pacientes.
- `327d9b3`: paciente. Título y mensaje de las notificaciones flotantes y el estado del informe de admisión. El código de la sala viajaba dentro del `onclick`.

Cada uno se probó interceptando las respuestas de la API en el navegador, sin tocar la base. La versión anterior ejecuta el código inyectado y la nueva lo muestra como texto.

### Paso 6: consolidación

- **Una sola capa de `fetch`.** Los 90 `fetch()` directos de los paneles de paciente, profesional y configurador y de la página de consentimiento pasan a `YS.fetch`:
  - la misma base de la API en todas las pantallas;
  - `X-Usuario-Id` en todos los pedidos, así la auditoría registra quién hizo cada acción en lugar de 0;
  - un error con mensaje legible cuando no hay conexión;
  - un 401 lleva a "sesión vencida".

  Fue un reemplazo mecánico: mismas rutas, métodos y cuerpos. Las dos sesiones de video no cargan `ui.js` y siguen con `fetch`.
- **`toast()` y `confirmar()`.** Los de cada pantalla ahora delegan en `YS.toast` / `YS.confirmar`. No queda ningún `alert()`, `confirm()` ni `prompt()` en las pantallas en uso.
- **Estilos incrustados** (`style=`, `.style.`, `cssText`), al cierre:

  | Archivo | `main` | al cierre | Qué queda |
  |---|---|---|---|
  | `Pantalla_Profesional/js/nuevo_dashboard.js` | 155 | 10 | Geometría de la vista semanal (posiciones y alturas calculadas) |
  | `Pantalla_Paciente/js/dashboard.js` | 113 | 0 | |
  | `Pantalla_Configurador/js/dashboard.js` | 71 | 0 | |
  | `Pantalla_Profesional/sesion_video.html` | 21 | 2 | Ancho de las barras de emociones y color del punto del historial (cambian en vivo) |
  | `Pantalla_Usuario/Usuario/registro_profesional.html` | 6 | 0 | |
  | `Pantalla_ChatSimulado/js/bandeja.js` | nuevo | 2 | Alto automático del cuadro de texto |
  | `js/sesion.js` | 15 | 15 | No se tocó (ver bug 13) |

- **Código muerto quitado:**
  - **Profesional:** `responderTurno()`, `crearTurnoProfesional()` y `renderTodosLosTurnos()`, que no tenían llamadas. También las versiones viejas de `pacientes()` y `renderEvoluciones()`, pisadas por otra declaración con el mismo nombre, y `escHtml()`.
  - **Paciente:** el modal "Solicitar turno" (`abrirModalTurno()`, `actualizarHorasDisponibles()`, `cerrarModalTurno()`, `enviarSolicitudTurno()`), que ningún botón abría.
  - **CSS:** 89 selectores de componentes que ya no existen en paciente y profesional. El CSS del configurador pasó de 210 a 106 líneas: se fueron los ajustes a Bootstrap y los componentes propios que reemplazó el sistema.
  - Cada clase se verificó contra todo el HTML/JS, incluidas las páginas sin uso.
- **La guía de componentes** muestra `YS.tabla` con un ejemplo de uso.
- **"Victoria"** no aparece en el código, los textos ni los mensajes de commit.
- **Backend:** sin cambios en este paso.

### Después del cierre: XSS en mensajería, rama `fix-xss` y script SQL

**1. Revisión XSS de mensajería, registro y recuperación.** No apareció nada para corregir en esta rama.
- **Bandeja de chats** (cajón y pantalla), datos del paciente, respuestas rápidas y avisos internos de la campana: todo lo que viene del servidor pasa por `YS.esc` o se arma con nodos de texto.
- **Simulador del paciente** (`chat.js`): las burbujas son nodos de texto y solo se arman links `http(s)`. Un `javascript:` queda como texto.
- **Login, recuperación, registro de paciente y de profesional, crear cuenta y restablecer contraseña:** los mensajes del servidor van con `textContent` y la contraseña temporal se escapa.
- **Mensajería en `main`:** también es segura. Arma todo con `textContent`, y el código tiene un comentario explícito sobre no usar `innerHTML`.
- **Prueba:** `p7_xss_mensajeria`, con 29 verificaciones, en la carpeta local de pruebas. Empieza con un control: el payload `<img src=x onerror=...>` sí se ejecuta si se inserta como HTML, así que la prueba detecta un XSS real.
- **Bajo riesgo en `main`:** en esas páginas quedan dos casos que en esta rama ya no existen, porque se reescribieron.
  - La contraseña temporal del login entra sin escapar, pero la genera el servidor.
  - El `toast()` del registro de paciente arma HTML con el mensaje; como mucho sería self-XSS.

**2. Rama `fix-xss`** (local, sin push). Sale de `main` (4648edb, igual a `origin/main` en el último fetch) y tiene solo los cinco commits de XSS, aplicados con `cherry-pick -x`. Mensajería no necesitó commit.

| En `fix-xss` | Original | Qué arregla | Conflictos |
|---|---|---|---|
| `38fef0f` | `a9eeb16` | Configurador | 7 |
| `346de0b` | `7e62c59` | Profesional | 7 |
| `314a296` | `9491bcb` | Paciente | 1 |
| `b49fcc3` | `6d7d641` | Sesión de video | ninguno |
| `a2f1db7` | `327d9b3` | Notificaciones del paciente | 2 |

- **Cómo se resolvieron los conflictos:**
  - `main` no tiene `shared/ui.js`. Cada archivo afectado define al principio una versión mínima de `YS.esc` (y `YS.nombreCompleto` donde hace falta), que no pisa la de `ui.js` si algún día se carga.
  - En cada conflicto se conservó el marcado de `main`, con sus estilos, y solo se escaparon los mismos datos. Así las líneas arregladas quedan iguales a las de esta rama.
  - La vista semanal de la agenda no existe en `main` (llegó con `rediseno-visual`), así que ese arreglo no aplica.
  - Cada commit explica en su mensaje cómo se adaptó.
- **Verificación:**
  - compila con 0 errores (los mismos 167 warnings de `main`) y pasan las 89 pruebas de `main`;
  - `p8_fixxss_main` ataca los lugares del parche sobre la interfaz de `main`, con una etiqueta por campo: `main` falla en 15 verificaciones y `fix-xss` en 2.
- **Lo que los cinco commits no cubren.** Las 2 fallas que quedan, más otros lugares que encontré revisando el código de `main`, siguen vulnerables en `fix-xss`. En esta rama se escaparon al reescribir esas pantallas (`0e7ba80`, `164a34f` y otros), no en un commit de XSS.
  - **Configurador:**
    - el listado de **solicitudes de registro**, que arma las celdas con `${row[c]}`. Es el más grave: el registro profesional es público, así que cualquiera, sin cuenta, puede dejar HTML que se ejecuta cuando el configurador abre la lista;
    - solicitudes de cambio (nombres y motivo);
    - recuperación de contraseña (usuario y mail);
    - auditoría (acción, entidad y detalle);
    - listado de admisiones (nombre y DNI);
    - la tabla genérica de catálogos (por ejemplo, la consulta de interacciones IA);
    - las opciones de profesional al derivar una admisión.
  - **Profesional:**
    - el Inicio (sesiones, pendientes y pacientes de hoy: nombre y motivo);
    - las opciones de la cita urgente;
    - los avisos de "turno confirmado sin sala".
  - **Paciente:**
    - el próximo turno del Inicio (profesional, especialidad, modalidad y estado);
    - la lista de notificaciones de la campanita.

  Esto se cubrió después con el sexto tramo de `fix-xss` (punto 4).

**3. Script para revertir los datos de prueba del turno 1 / paciente 1:** `docs/revertir-datos-prueba-turno1.sql`. No se ejecutó. Hace esto:
- borra el mensaje 1190, los registros de envío 1018 y 1019 y la notificación 1010;
- vuelve a no leídas las otras 14 notificaciones del paciente 1;
- si la marca de lectura de la conversación 1 sigue en 1190, la devuelve al último mensaje que queda.

Todo va en una transacción que por defecto termina con ROLLBACK. Cada paso corta si no toca exactamente las filas esperadas.

**4. Sexto tramo de `fix-xss`: los lugares de `main` que quedaban sin escapar.** 11 commits más sobre `fix-xss` (local, sin push), uno por pantalla o grupo, con el de solicitudes de registro profesional primero por ser el más grave:

| Commit | Pantalla / grupo | Qué escapa |
|---|---|---|
| `a1b18f8` | Configurador: solicitudes de registro | las celdas del listado público (nombre, apellido, dni, mail, matrícula, provincia) |
| `e4367ba` | Configurador: solicitudes de cambio | paciente, profesional origen/destino, especialidad, motivo |
| `c04faa4` | Configurador: recuperación de contraseña | usuario y mail del listado; la contraseña temporal del aviso |
| `8437d77` | Configurador: auditoría | acción, entidad y detalle |
| `9989838` | Configurador: listado de admisiones | nombre, dni y especialidad de cada fila; el detalle ya estaba escapado desde `38fef0f` |
| `ab28cf2` | Configurador: catálogos y reportes | las celdas de la tabla genérica (roles, especialidades, coberturas, interacciones IA, turnos, agendas) y el `nombre` del select de Reportes; además, el botón "editar" pasaba la fila entera como JSON dentro de un atributo `onclick` con comillas simples — una comilla en un valor rompía el atributo e inyectaba HTML en el botón sin pasar por `innerHTML`. Ahora ese JSON también pasa por `YS.esc` |
| `6f17d7a` | Profesional: Inicio y cita urgente | sesiones de hoy, próximo turno, saludo con el nombre del profesional, select de pacientes de la cita urgente, turnos pendientes y pacientes de hoy |
| `4b29529` | Profesional: aviso de turno sin sala | nombre del paciente en el panel flotante |
| `28ce341` | Paciente: próximo turno del Inicio | nombre del profesional, especialidad, modalidad y estado |
| `54f5258` | Paciente: campanita (historial completo) | título y mensaje de cada notificación; el botón "Unirse / Ver sala" pasaba el link dentro del `onclick` entre comillas simples (mismo problema que el de catálogos) — ahora pasa solo el id, igual que ya se hacía con "Unirse a la entrevista" |
| `4d541f8` | Bajo riesgo | la contraseña temporal del login y el `toast()` del registro de paciente, los dos casos de bajo riesgo que quedaban |

- **Cómo se resolvieron:** mismo criterio que los cinco commits originales — el `YS.esc` mínimo que ya define cada archivo, escapando sobre el marcado de `main` tal cual está.
- **Revisión con grep antes de cerrar** (`innerHTML`, `insertAdjacentHTML` y plantillas con datos de servidor en todo `PANTALLAS/`), para no dejar lugares sin cubrir. Lo que queda sin escapar y por qué es seguro:
  - **Código muerto, no alcanzable:** `Pantalla_Profesional/dashboard.html` + `js/dashboard.js` + `css/dashboard.css` (el login nunca redirige ahí, solo a `nuevo_dashboard.html`; ese `.css` ni siquiera son reglas CSS, son JS de una versión vieja que quedó mal guardado, pero se carga con `<link>`, así que el navegador lo descarta como CSS inválido sin ejecutarlo). Dentro de `nuevo_dashboard.js`, dos funciones quedaron declaradas dos veces (`pacientes()` y `renderEvoluciones()`); la primera versión de cada una nunca corre, porque JavaScript se queda con la última declaración. `renderTodosLosTurnos()` está definida pero no la llama nadie en todo `PANTALLAS/`.
  - **Valores fijos o numéricos:** `codigoSala` (lo genera el servidor como `"SALA-" + id + "-" + hex`, sin datos de usuario), ids, fechas, y el estado de las derivaciones (`Pendiente`/`Aceptada`/`Rechazada`, fijado por el backend, no por texto libre) cuando se usa como clase CSS.
- **Un bug ajeno al XSS, encontrado al armar la prueba con un POST real:** el registro público de profesionales (`POST /profesionales`) siempre devuelve 500, incluso con datos válidos, porque intenta guardar un registro de auditoría con `IdUsuario = 0` (no hay sesión todavía) y esa columna tiene clave foránea a `Usuario`. El profesional queda creado igual (el error pasa después), así que quien se registra ve "Error al enviar la solicitud" aunque su solicitud sí quedó guardada. No lo corregí acá: no es XSS y no estaba roto por este parche, es un bug previo de `main`. Se corrigió después en la rama `fix-registro-publico` (bug 18, ver la tabla de bugs).
- **Verificación:** compila con 0 errores (los mismos 167 warnings) y pasan las 89 pruebas de `main`. `p8_fixxss_main`, ampliada con estos lugares (incluye un POST real al endpoint público de solicitudes, con su fila de prueba borrada después a mano por SQL), pasa 34 de 34 verificaciones sobre `fix-xss`.

---

## Bugs encontrados y corregidos

| # | Bug | Commit |
|---|---|---|
| 1 | `/profesionales/{id}/turnos-confirmados` respondía 500 (formato de `TimeSpan`) | `8fc5973` |
| 2 | `generar-sala` respondía 500 y no avisaba al paciente | `7ee939d` |
| 3 | `creadoPor` no llegaba al frontend: Aceptar/Rechazar nunca aparecía y el paciente no podía confirmar una propuesta | `5e9f89f`, `832d1ed` |
| 4 | "Mis horarios" no ordenaba por día y al editar perdía el día | `aeacefa` |
| 5 | Con dos turnos el mismo día, reprogramar actuaba sobre el primero | `256dc37` |
| 6 | Cerrar sesión en el configurador no limpiaba `localStorage` | `0124128` (shell) |
| 7 | La sala del paciente ignoraba `?sala=` | `eb90f3a` |
| 8 | HTML mal cerrado en el acuerdo de atención | `d5bb4b9` |
| 9 | Después de reprogramar, `null.getDate()` | `81e07df` |
| 10 | "Ver todos los turnos" no hacía nada: el router ignoraba los parámetros | `1d5224e` |
| 11 | Chats: al abrir desde el aviso una conversación ya elegida, no se veía el mensaje nuevo ni quedaba leída | `4217d45` |
| 12 | Editar una agenda desde el configurador siempre daba 400 (`disponible` iba como texto) | `5d23c16` |
| 13 | El aviso y el cierre de sesión por inactividad (`js/sesion.js`) nunca se muestran | **No se corrigió** (ver abajo) |
| 14 | La cita urgente pedida después de las 21 quedaba para el día siguiente (fecha en UTC) | `359ead2` |
| 15 | El CSV de "Mis atenciones" mostraba mal los acentos en Excel (faltaba el BOM) | `9ae94b4` |
| 16 | Guardar un perfil sin tocar nada cambiaba NULL por `""` | `318b604` |
| 17 | "Confirmar" desde el Inicio confirmaba una propuesta propia sin preguntar | `00d874e` |
| 18 | El registro público de profesionales (`POST /profesionales`) respondía 500 por la auditoría sin usuario | **No se corrigió acá** (ver abajo) |
| — | XSS almacenado (5 lugares) | `a9eeb16`, `7e62c59`, `9491bcb`, `6d7d641`, `327d9b3` |

Menores, corregidos dentro de commits de estilo:
- editar desde la tabla del configurador se rompía con un apóstrofo en los datos (`0e7ba80`);
- cancelar un turno desde la agenda del profesional no revisaba la respuesta (`aa849fd`);
- el calendario del profesional mostraba la vista Mes un instante al abrirse (`76e7c8a`).

> **Bug 13, para decidir con el equipo: cierre por inactividad.**
>
> - **Qué pasa:** `js/sesion.js` crea un contenedor con el mismo id que el overlay que tiene adentro. `getElementById` devuelve el contenedor y muestra ese, mientras el overlay sigue con `display:none`. Ni el aviso de "1 minuto" ni el de "sesión expirada" aparecieron nunca: en la práctica nadie pierde la sesión por inactividad. Se comprobó adelantando el reloj del navegador.
> - **Por qué no lo arreglé:** la sala de video del profesional va en un iframe, y lo que pasa adentro (hablar, mirar el video) no cuenta como actividad de la página que lo contiene. Activarlo cortaría con "sesión expirada" cualquier videollamada de más de 10 minutos en la que no se toque el panel.
> - **Para activarlo:** pausar el contador mientras la sala está abierta, o contar la actividad del iframe con `postMessage`. `videollamadas-seguras` conserva la misma estructura con ids duplicados.

> **Bug 18, para decidir con el equipo: registro público de profesionales (500).**
>
> - **Qué pasa:** `POST /profesionales` (lo llama `registro_profesional.html`, sin sesión) da de alta el
>   profesional y después intenta guardar un `LogAuditoria` con `IdUsuario = 0` (no hay `X-Usuario-Id`
>   todavía). Esa columna tiene clave foránea a `Usuario`, `0` no existe ahí, y la fila de auditoría nunca
>   se inserta: el pedido entero responde 500. El profesional queda creado igual, porque el alta ya se
>   confirmó antes de ese paso; quien se registra ve "Error al enviar la solicitud" sin saber que su
>   solicitud sí quedó guardada. Es un bug de `main`, no de esta rama ni del XSS (se encontró revisando ese
>   parche, ver "Después del cierre").
> - **Por qué no se corrigió en `Cambios-Agus`:** esta rama parte de `rediseno-visual` (sobre `main` en
>   4648edb) y tiene el mismo `Program.cs` sin tocar en esa parte, así que hereda el bug tal cual. El
>   arreglo ya está hecho en la rama local `fix-registro-publico` (2 commits sobre `main`, sin push):
>   `LogAuditoria.IdUsuario` pasa a admitir NULL para las acciones sin sesión (migración idempotente en
>   `migrations/alter_logauditoria_idusuario_nullable.sql`), y se revisaron los demás endpoints sin sesión
>   (registro de pacientes y toda la recuperación de contraseña no escriben auditoría, así que no tenían
>   este problema). Tiene un test de integración (`WebApplicationFactory<Program>`) que hace el POST real
>   sin sesión y verifica 2xx y la auditoría con `IdUsuario` NULL; borra sus propias filas al final.
> - **Cómo entra a esta rama:** por merge desde `fix-registro-publico` cuando se decida subir esa rama, no
>   escribiendo el mismo cambio dos veces. Mientras tanto, el registro público de profesionales sigue roto
>   también en `Cambios-Agus`.

## Pantallas: estado final

| Pantalla | Estado |
|---|---|
| Login, acuerdo, registro de paciente y de profesional, crear cuenta profesional, restablecer contraseña | Rediseñadas, sin Bootstrap |
| Inicio de los tres roles | Rediseñados, como tableros con datos reales |
| Agenda Día/Semana/Mes (profesional y paciente) | Venía de `rediseno-visual`; bugs y colores corregidos en el Paso 4 |
| Mis horarios, cita urgente | Rediseñados |
| Mis pacientes e historia clínica | Rediseñados |
| Configurador: 16 secciones con listados, Usuarios y modales | Rediseñados con `YS.tabla`, sin Bootstrap |
| Chats | Rehechos como cajón desde la barra (Paso 3) |
| Consentimiento | Rediseñado (Paso 2) y pulido (Paso 5) |
| Perfiles, informe de admisión, Mis profesionales | Rediseñados |
| Reportes | Botones y avisos del sistema; el gráfico del configurador no se tocó |
| Sesiones de video (las dos) | Rediseño liviano a propósito (merge) |
| Aviso de inactividad (`js/sesion.js`) | Sin tocar (bug 13) |
| Panel viejo del profesional (`Pantalla_Profesional/dashboard.html`) | Sin uso; no se toca |

## Archivos y código sin uso (estado final)

Siguen sin tocarse, para no generar conflictos "modificado vs. borrado" con `videollamadas-seguras`:
- `Pantalla_Profesional/dashboard.html`, `js/dashboard.js` y `css/dashboard.css`;
- `Pantalla_Usuario/Usuario/consentimiento_crear_cuenta.html`;
- `Pantalla_Configurador/css/estilos.css`.

Las funciones sin uso que estaban dentro de archivos que igual se reescribieron sí se quitaron (Paso 6).

## Migraciones

- `migrations/add_lectura_conversacion.sql` (Paso 3): agrega `Conversacion.IdUltimoMensajeLeido`. Es idempotente y está aplicada en la base local. La bandeja de chats la necesita.
- No hay otros cambios de esquema en esta rama.
- **Aparte, en la base local (no en esta rama):** `fix-registro-publico` trae `migrations/alter_logauditoria_idusuario_nullable.sql`, que vuelve nullable `LogAuditoria.IdUsuario` (bug 18, más arriba). Ya está aplicada en la base local compartida, porque todas las ramas usan la misma base (`Conexion.cs` apunta siempre a `DESKTOP-76GUM00\SQLEXPRESS`/`yosoy`). El script no está en el `migrations/` de esta rama todavía: entra por el mismo merge que el resto del bug 18.

## Datos de prueba que quedaron en la base local

- **Paso 3:**
  - mensajes del simulador con el teléfono 1155550002 (conversación 4), que crecen con cada corrida de `p3_chats`;
  - la conversación 4 se cerró y la reabrió el bot.
- **Paso 4:**
  - el turno 1007 reprogramado al 5/10;
  - la propuesta 2007, confirmada por el paciente 13;
  - la firma del consentimiento de telemedicina del paciente 13;
  - un código de sala en el turno 1006.
- **Pasos 5 y 6:**
  - `PUT` con los mismos valores a `/agendas/1`, `/pacientes/1` y `/profesionales/1`, para comprobar guardados (los datos no cambian).
  - El `segundoNombre` del profesional 1 pasó de NULL a `""` en una prueba (el bug 16) y se volvió a NULL con la API.
  - **Una prueba confirmó por error el turno 1** (11/08, paciente 1). Se volvió a Pendiente con la API. Quedaron los avisos que había generado: la notificación 1010 "Turno confirmado" del paciente 1, los registros de envío 1018 (Chat) y 1019 (App) y el mensaje 1190 en la conversación 1. No los borré porque no se borran datos.
  - Las notificaciones del paciente 1 quedaron leídas, porque una prueba abrió su campana. `p2_paciente` y `p2_profesional` también las marcan al abrirla.
  - La marca de lectura de la conversación 1 quedó en el mensaje 1190, porque el recorrido final de capturas abrió esa conversación.
  - **Para revertir esos tres puntos:** `docs/revertir-datos-prueba-turno1.sql`. No se ejecutó; corre en una transacción con ROLLBACK por defecto.

## Pendientes

- **Decidir con el equipo:**
  - el cierre por inactividad (bug 13);
  - la autoconfirmación (Paso 4);
  - si se quita el "Confirmar" de las propuestas propias en el Inicio del profesional (bug 17);
  - las respuestas rápidas de `plantillas.json`;
  - el texto legal del consentimiento.
- **Merge con `videollamadas-seguras`.** Esa rama toca unos 40 archivos de `PANTALLAS/`: login en React, `js/sesion.js`, las dos `sesion_video.html` y los JS de profesional y paciente. Sugerencia:
  - **paneles** (`nuevo_dashboard.js`, `Pantalla_Paciente/js/dashboard.js`, configurador): quedarse con los de esta rama y portar las validaciones y el uso del token de la otra;
  - **sesiones de video:** tomar las de esa rama y aplicarles el sistema de diseño;
  - **login:** decidir entre la versión en React y el HTML de esta rama, que mantiene los mismos ids y flujos;
  - **`js/sesion.js`:** tomar el de esa rama y resolver el bug 13 de una vez;
  - **endpoints** `/profesionales/activar-cuenta` y `/recuperacion-password/reset`: vienen con ese merge. Hasta entonces, crear cuenta profesional y restablecer contraseña muestran el error del servidor.
- **Backend:**
  - Los datos del tutor de un menor no se guardan en el registro (pendiente anterior).
  - Usuarios del configurador pide los profesionales de cada paciente por separado (un pedido por paciente). Con muchos pacientes conviene un endpoint que los traiga juntos.
- **Identidad:** `X-Usuario-Id` lo declara el navegador y no está firmado. Lo reemplaza el JWT de `videollamadas-seguras` (ver `Services/AutorizacionReporte.cs`).
- **Merge del bug 18** (registro público de profesionales, 500 por la auditoría sin usuario): ya está corregido en la rama local `fix-registro-publico` (2 commits sobre `main`, sin push). Falta traerlo a esta rama por merge; mientras tanto sigue roto acá igual que en `main`. Ver el bug 18 en la tabla de bugs y "Para revisar primero".

## Ideas futuras (no se construyen)

- Sincronizar la agenda con Google Calendar.
- Links públicos para reservar turno.
- Compartir la agenda.
- ABM de preguntas frecuentes del bot: el backend ya existe (`/mensajeria/preguntas-frecuentes`), pero no tiene pantalla.

## Cómo correr y probar

```
dotnet build
dotnet test YoSoy.Tests/YoSoy.Tests.csproj
# sin mandar mails reales (en PowerShell, un espacio: "" borra la variable)
$env:Email__Password = " "; dotnet run
```

- La app queda en `http://localhost:5000/pantallas/index.html` y la guía de componentes en `/pantallas/shared/guia-componentes.html`.
- La bandeja de chats necesita la migración `add_lectura_conversacion.sql` y la mensajería simulada encendida (`Mensajeria:ChatSimuladoHabilitado`, solo local).
- **Pruebas de interfaz:** `docs/pruebas-ui-cambios-agus/`. Son scripts de Playwright con Edge; su `README.md` explica cómo correrlas y cuáles escriben en la base.
- **Capturas:** `docs/capturas-cambios-agus/`. En `antes/` está el recorrido del Paso 0, previo a los cambios; en `despues/`, las mismas pantallas al cierre, más modales y detalles.
- Las dos carpetas están excluidas en `.git/info/exclude`: quedan solo en esta máquina.
