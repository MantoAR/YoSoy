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
- **Fase 1, Paso 5 (bug 2, auditoría):** para comprobar que una respuesta manual nueva queda con usuario y detalle legibles (los mensajes viejos no los tienen, se guardaron antes del arreglo), se mandó un mensaje real de prueba — "prueba auditoria bug2" — a la conversación 6 (paciente Camila Vega) desde el cajón del configurador. Quedó el mensaje 1192 (saliente, humano, 5/10 23:09) en esa conversación. No se revirtió: es un mensaje de texto suelto, sin notificaciones ni recordatorios asociados (a diferencia del caso del turno 1 de arriba), así que no arrastra nada más.

## Pendientes

- **Decidir con el equipo:**
  - el cierre por inactividad (bug 13);
  - la autoconfirmación (Paso 4);
  - si se quita el "Confirmar" de las propuestas propias en el Inicio del profesional (bug 17);
  - las respuestas rápidas de `plantillas.json`;
  - el texto legal del consentimiento;
  - la especialidad real de Martín Sosa (Fase 1, bug 6) — script listo en `docs/corregir-especialidad-martin-sosa.sql`, sin ejecutar, falta que alguien confirme el valor correcto y complete la variable;
  - si conviene el vocabulario de chips que pedía el Paso 3 para las tarjetas de CRM > Chats ("Respondido por el bot", "Nueva", "Sin responder") en vez del chip de estado que ya existía (Bot/Escalada a persona/Urgente/Cerrada), que es el que quedó.
- **Merge con `videollamadas-seguras`.** Esa rama toca unos 40 archivos de `PANTALLAS/`: login en React, `js/sesion.js`, las dos `sesion_video.html` y los JS de profesional y paciente — la Fase 1 agregó textos y un `::placeholder` nuevo a las dos `sesion_video.html` (bug 4) y a los dos `dashboard.js` (CRM > Chats, bug 4, bug 5), así que ese merge va a tener más líneas en conflicto ahí que antes. Sugerencia:
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

---

## Fase 1: CRM > Chats y layout de ancho completo

Plan más grande, en pasos. Arranca después de todo lo anterior; sigue en `Cambios-Agus`, local, sin push.

### Paso 0: reconocimiento

- **Árbol limpio**, sin cambios pendientes al arrancar.
- **El ancho limitado es una sola variable:** `--contenido-max: 1600px` en `shared/tema.css:79`, usada en `.ui-encabezado-interior` y `.ui-contenido` (las dos con `margin: 0 auto`). A 2560 px deja ~480 px de gris de cada lado; a 1920 px, ~160 px. Las pantallas angostas (`consentimiento.html`) ya tienen su propio mecanismo: `body.ui-pagina-angosta` pisa la variable a 900px — ese patrón ya es el que pide el pedido, no hace falta inventarlo. Login y registro no usan `shared/tema.css` (tienen su propio HTML suelto): no les toca este cambio.
- **La bandeja de chats ya se construyó pensando en esto.** `Pantalla_ChatSimulado/js/bandeja.js` tiene un modo `"pagina"` además del modo `"cajon"` (lo usa hoy `Pantalla_ChatSimulado/index.html`, una pantalla de prueba que además simula al paciente por teléfono, sin autenticación real — esa pantalla sigue para probar, no es la que hay que reemplazar). En modo página, el mismo JS dibuja las tres secciones (`.chats-lista`, `.chats-hilo`, `.chats-contexto`) dentro de cualquier contenedor, con los filtros, el buscador, el polling adaptativo, las respuestas rápidas con "/" y el panel de contexto ya funcionando. El `grid-template-columns` de `.chats-cuerpo` ya arma tres columnas (330px / flexible / 310px) cuando hay contexto — el orden hoy es lista · hilo · contexto; el pedido quiere lista · contexto · hilo, así que hay que reordenar (con `order` en CSS, para que cambiarlo sea una línea, tal como pide el pedido) sin tocar la estructura del JS.
- **El panel de contexto ("Datos del paciente") ya tiene casi todos los campos** que pide "Información del lead": nombre, estado con chip, DNI, teléfono, profesional(es) asignado(s), próximo turno, consentimiento (con chip y el botón que inserta el link de firma en el composer), "Ver turnos" y "Ver ficha". Faltan mostrar como campo propio: **ID de la conversación**, **Creado** (fecha) y **Origen (canal)**. El modelo `Conversacion` ya tiene `FechaCreacion` y `Canal` ("Simulado"); el endpoint `GET /mensajeria/conversaciones/{id}/paciente` (`Endpoints/MensajeriaEndpoints.cs:59`) no los devuelve todavía. Agregar esos dos campos a la respuesta es aditivo (no rompe el contrato existente, regla 4 del pedido) y no necesita ni migración ni lógica nueva.
- **Los textos a renombrar** ("Derivada"/"Derivadas" → "Escalada a persona"/"Escalamientos") están en `bandeja.js`: el mapa `ESTADO.DerivadaHumano.texto` y el filtro `FILTROS` con `id: "derivadas"`. El `id` interno y el estado `DerivadaHumano` de la base **no se tocan**, solo el texto que se muestra.
- **Chats hoy es solo del configurador.** `shared/shell.js` dice explícitamente "Chats: solo para el configurador" y carga la bandeja únicamente para ese rol. Profesional y paciente no tienen acceso a la bandeja hoy, así que el Paso 2 no les agrega el ítem (el pedido lo condicionaba a que ya lo tuvieran).
- **Menú actual del configurador** (`shell.js`, `MENUS.configurador`): Inicio · Usuarios ▾ (Pacientes y profesionales, Solicitudes de registro, Solicitudes de cambio, Recuperación de contraseña) · Clínico ▾ (Admisiones, Turnos, Agendas, Evoluciones, Eventos de crisis) · Reportes · Configuración ▾ (Roles, Especialidades, Coberturas, Interacciones IA) · Auditoría. El nuevo grupo **CRM ▾** (Chats · Escalamientos) va primero, antes de Usuarios, reemplazando conceptualmente al botón de burbuja de la barra superior.

### La captura de referencia que no llegó

El pedido avisaba que adjuntaba una captura de referencia de la pantalla de chats "para imitar en estructura y sensación" — **no llegó ninguna imagen adjunta**. Se lo señalé al usuario antes de construir el Paso 3; contestó "seguir sin la imagen". El Paso 3 se hizo entero a partir de la descripción escrita del pedido, con el diseño propio de YoSoy (componentes y colores de `shared/tema.css`) y los íconos originales de `shared/fondo-chat.svg` — nada copiado de ninguna app existente.

### Paso 1: ancho completo

- `shared/tema.css`: `--contenido-max` pasó de `1600px` a `none`. Esa única variable gobernaba `.ui-encabezado-interior` y `.ui-contenido` (las dos con `margin:0 auto`), así que alcanzó con el cambio ahí; no hubo que tocar cada pantalla. Las angostas (`consentimiento.html`, login, registro) ya tenían su propio mecanismo (`body.ui-pagina-angosta`, o HTML suelto sin `shared/tema.css`) y no les tocó este paso.
- `Pantalla_Configurador/css/dashboard.css`: dos grillas que estaban en 2 columnas fijas (`.grilla-2`, admisiones) o con `margin-bottom` manual entre tarjetas (`.split-list`, Usuarios) pasaron a `repeat(auto-fit, minmax(240px,1fr))` / `repeat(auto-fill, minmax(260px,1fr))`, para que el ancho extra sume columnas en vez de dejar aire. Se sacó el media-query de 900px que ya no hacía falta (el `auto-fit` resuelve solo el caso angosto).
- Capturas antes/después a 1366/1920/2560px: `docs/capturas-cambios-agus/fase1/`.

### Paso 2: menú CRM

- `shared/shell.js`: nuevo grupo **CRM ▾** (Chats · Escalamientos) al principio del menú del configurador, antes de Usuarios — es el único rol con acceso a Chats hoy (confirmado en el Paso 0), así que Profesional y Paciente no lo reciben, tal como pedía el pedido ("solo si ya lo tienen").
- El botón de burbuja de la barra superior ya no abre el cajón: ahora es un acceso directo a CRM > Chats (`#chats`), y conserva el contador de no leídos tal cual estaba.
- `Pantalla_Configurador/dashboard.html`/`dashboard.js`: nueva sección `#vistaChatsCRM`, con dos ids de menú (`chats`, `escalamientos`) que apuntan a la misma vista — `escalamientos` abre con el filtro "Escaladas a persona" ya aplicado. `bandeja.js` se inicializa una sola vez en modo `"pagina"` (es un singleton) y la sección solo la muestra/oculta con CSS.

### Paso 3: CRM > Chats en 3 columnas

Reemplaza el cajón por una página dentro del shell, ancho completo, sin scroll de página (cada columna scrollea por separado). Reusa `bandeja.js` entero — el mismo JS que ya dibujaba el cajón, con su polling, filtros, buscador y respuestas rápidas — así que el cambio real fue de layout (CSS) y de un panel nuevo (el central), no una reescritura.

- **Orden de columnas** (lista · lead · chat) con la propiedad CSS `order` (`bandeja.css`: `.chats-lista{order:1}`, `.chats-contexto{order:2}`, `.chats-hilo{order:3}`), sin tocar el orden real del HTML/JS — cambiarlo es tocar esas tres líneas, como pedía el pedido. `grid-template-columns: 340px 330px minmax(0,1fr)` en el mismo orden visual (lista 340px · lead 330px · chat flexible). Bajo 1500px la columna del lead se colapsa a un panel superpuesto (`@media (max-width:1499px)`) en vez de competir por espacio con las otras dos.
- **Columna izquierda** (Conversaciones): sin cambios funcionales — ya tenía buscador, contador "N chats coinciden", filtros y tarjetas. Se le agregó una insignia de canal en la esquina del avatar (`.chats-conv-canal`, ícono de chat) para que el origen del mensaje se vea de un vistazo.
- **Columna central** ("Información del lead", antes "Datos del paciente"): header con "Configurar vista" visible pero deshabilitado (`aria-disabled`, fase posterior). Dos secciones:
  - **PARTICIPANTES**: paciente + profesional(es)/admin asignados, cada uno con `YS.avatar` de iniciales, y un "+" sin construir (también fase posterior).
  - **GENERAL**: campos de solo lectura con `.ui-dato`/`.ui-datos` (ya eran del sistema de diseño — label arriba, fondo claro). Nombre, DNI, Teléfono, Profesional asignado, ID de la conversación, Creado, Origen/canal, Estado (chip), Próximo turno, Consentimiento RN-02 (chip + link de firma si falta), "Ver turnos"/"Ver ficha". Todo dato existente — no se inventó ningún campo. **ID de la conversación**, **Creado** y **Origen** no venían en la respuesta de `GET /mensajeria/conversaciones/{id}/paciente`; se agregaron ahí (`Endpoints/MensajeriaEndpoints.cs`), devolviendo `Canal` y `FechaCreacion` del modelo `Conversacion`, que ya los tenía. Es aditivo: no cambia ningún campo existente de la respuesta.
- **Columna derecha** (chat): fondo propio con íconos lineales originales a baja opacidad (`shared/fondo-chat.svg`: burbuja, corazón, estrella, nube, calendario, hoja, cruz de salud, reloj, carita — 9 íconos, mosaico de 240×240, trazo `#6A5ACD` al 9% de opacidad, un solo archivo, nada copiado). Composer reordenado en dos filas: "Enviar por" con un selector de canal (hoy solo "Chat simulado", sin otras opciones) arriba, y la fila de ícono/texto/enviar que ya existía abajo. El resto (burbujas con cola, separadores de día, mensajes de sistema, agrupación, "/" de respuestas rápidas, scroll inteligente, Enter/Shift+Enter) ya estaba y no se tocó.
- **Textos renombrados** (sin tocar el campo ni el estado interno — siguen siendo `id:"derivadas"` y `DerivadaHumano` en el modelo): "Derivada" → "Escalada a persona" (chip/estado), "Derivadas" → "Escaladas a persona" (filtro, título del filtro), "No hay conversaciones derivadas..." → "No hay conversaciones escaladas a una persona." (estado vacío), "Cerrar derivación" → "Cerrar escalamiento" (botón, en las 4 veces que aparece), "Derivación cerrada." → "Escalamiento cerrado." (aviso), más 2 comentarios de código. Se verificó aparte que esto **no tocó** el "Derivado"/"Derivadas" de admisiones (`Pantalla_Configurador/js/dashboard.js`, filtro y chip de `informes-admision`): es la "Derivación" de la Fase 2 reservada, un concepto distinto, y sigue diciendo "Derivado" tal cual.
- **Vocabulario de chips sugerido por el pedido** ("Respondido por el bot", "Nueva", "Sin responder") **no se implementó tal cual**: las tarjetas siguen mostrando el chip de estado que ya existía (Bot/Escalada a persona/Urgente/Cerrada), que es el que tiene datos reales detrás. Decisión de alcance, no un olvido — queda anotado para que se revise si hace falta ese vocabulario específico.
- **El cajón no se borró.** `Pantalla_ChatSimulado/index.html` (y su `chat.js`) simulan además al paciente por teléfono sin autenticación real, y siguen siendo la forma de probar esa punta — no quedó reemplazada por la página nueva, así que no aplica "si quedó completamente reemplazada" del pedido. El modo `"cajon"` de `bandeja.js` (el que usa el configurador para ver un chat puntual desde otras pantallas, ej. un turno) tampoco se borró: sigue en uso aparte del modo `"pagina"`.

### Paso 4: estados vacíos

- Calendario del profesional sin horarios cargados: `Pantalla_Profesional/js/nuevo_dashboard.js`, función `agenda()` — con `YS.vacio()` (ícono, título, texto y el botón "Mis horarios de trabajo" que lleva a esa pestaña), arriba de la grilla, que se mantiene.
- El resto de las pantallas "0 registros" de la app ya usaban `YS.vacio()` desde el Paso 5 de la consolidación original (antes de esta Fase 1) — no se encontró otra pantalla con tabla/lista cruda sin estado vacío al revisar Usuarios, Admisiones, Auditoría, Turnos y la bandeja de chats. No se hizo una auditoría exhaustiva de cada pantalla de la app.

### Paso 5: bugs corregidos

Cada uno en su commit, con build y batería verificados después:

1. **Avisos duplicados** (`02d8fd2`): la campana del configurador pedía todas las notificaciones internas sin filtrar por destinatario y las listaba una por una — con varias generadas para la misma conversación (confirmado con una consulta de solo lectura a `NotificacionInterna`: eran datos reales acumulados de pruebas repetidas, no un bug de inserción duplicada en el backend) el contador y la lista se inflaban. `instalarAvisosInternos()` ahora pide solo las del destinatario `Administrador` y agrupa por conversación: un ítem por conversación, con la cantidad de avisos nuevos y "Marcar leídos (N)".
2. **Auditoría ilegible** (`860df58`): "Respuesta manual en chat" y "Cierre de conversación" mostraban el usuario como `#1` y el detalle vacío. `Auditar()` (`MensajeriaEndpoints.cs`) ahora arma `Detalle = "Conversación #{id} (paciente)"` cuando hay paciente identificado; el front resuelve el nombre de usuario cruzando `/auditoria` con `/usuarios` en vez de mostrar el id crudo. Cambio aditivo: los registros viejos (sin Detalle) no se tocaron, solo se ven distinto los nuevos.
3. **Consentimiento con placeholder** (`c51a1d6`): `Consentimiento:VersionTexto` sigue en `"sin-validar"` (el default de `ConsentimientoOptions`) hasta que se cargue el texto legal definitivo. El paciente veía esa palabra tal cual, tanto al ir a firmar como, si llegaba a firmar así, en el detalle de la firma ya hecha. Ahora se oculta la línea "Versión del texto" cuando no hay una versión real; se sigue mandando el valor al backend al firmar, la validación de versión no cambió.
4. **Sala de video** (`09dc06a`): solo texto y aspecto, sin tocar WebRTC/señalización. El placeholder `"SALA-X-XXXXXX"` del input para unirse (dashboard del paciente y `sesion_video.html`) pasó a `"Ingresá el código de tu sala"`; en `sesion_video.html` el input ya tenía mayúsculas/mono/letter-spacing pensado para el código tipeado, así que ese estilo quedó limitado a `::placeholder` para que la frase no se vea gritada. Del lado del profesional, `"Modelos listos. Podés crear la sala."` (jerga de IA interna) pasó a `"Listo. Podés crear la sala."`.
5. **"Actividad reciente" duplicaba Auditoría** (`69efea1`): el Inicio del configurador ya tenía tarjetas de resumen reales (`tarjeta()`, "Para revisar"/"Hoy en YoSoy") de una consolidación anterior — cubrían lo que pedía el bug. Se sacó la tabla cruda de "Actividad reciente" (y el `fetch` a `/auditoria` que ya no se usa) y quedó un acceso directo chico a "Ver auditoría completa".
6. **Dato, no código — especialidad de Martín Sosa** (`7d2cf66`): el profesional 3 figura con Psiquiatría, fuera de alcance. No se tocan datos sin OK: queda `docs/corregir-especialidad-martin-sosa.sql`, sin ejecutar, con ROLLBACK por defecto y una variable sin completar (`@IdEspecialidadNueva`) que corta el script hasta que alguien confirme la especialidad real. No tiene turnos cargados, así que el cambio quedaría aislado a esa fila.

Byproducto encontrado al hacer el Paso 2/3: la tarjeta de chats escalados del Inicio llamaba a `YS.bandeja?.abrir(null, filtro)`, que abría el cajón — ahora que Chats es una página escondida (no un cajón), eso cambiaba el filtro sin mostrar nada. Se agregó `irAChatsFiltrados()`, que aplica el filtro y navega a `#chats`/`#escalamientos` por hash.

## Fase 2: Grupos, Casos y derivación en cascada

Plan más grande, Fase 2. Sigue en `Cambios-Agus`, local, sin push. A diferencia de la Fase 1, esta toca la base: tiene su propio Paso 0 de diseño antes de escribir código, como pedía el pedido.

### Paso 0: el modelo real, hoy

Árbol limpio al arrancar (`ae53cec..2b6ffb8` es toda la Fase 1, 16 commits).

**Especialidad / Profesional.** `Especialidad` es una tabla chica y plana (`IdEspecialidad, Nombre, Estado`): hoy tiene 4 filas activas (Psicología, Trabajo Social, Terapia Ocupacional) más Psiquiatría, fuera de alcance del proyecto (bug 6 de la Fase 1: Martín Sosa la tiene cargada, sin corregir todavía, script en `docs/corregir-especialidad-martin-sosa.sql`). `Profesional.IdEspecialidad` es **una sola FK, obligatoria**: hoy un profesional tiene exactamente una especialidad. Esto **no se toca** — `Grupo` es una clasificación nueva y más fina que convive al lado, no un reemplazo.

**PacienteProfesional.** Es la asignación "en vivo": qué profesional(es) atienden a cada paciente **ahora mismo**. Es **N:M de verdad** (`ObtenerProfesionalesDePaciente` devuelve una lista — un paciente puede tener psicólogo y trabajador social al mismo tiempo). La clase `Models/PacienteProfesional.cs` está desactualizada: el `ALTER` que agregó `EstadoAsignacion` (`'Nueva' | 'Confirmada' | 'Rechazada'`) y le dio otro uso a `FechaAsignacion` no se reflejó en el modelo, así que esas columnas solo se tocan con SQL crudo en `Program.cs`. `EstadoAsignacion = 'Nueva'` es el profesional todavía no confirmó que acepta al paciente (lo confirma con `PUT /asignaciones/estado`, o lo rechaza y `InformeAdmision` vuelve a `Rechazado` con `PUT /informes-admision/rechazar`).

**InformeAdmision.** El modelo `Models/InformeAdmision.cs` también está incompleto: la tabla real tiene además `FechaEntrevista, ObservacionesEntrevista, IdProfesionalDerivado, CodigoSala, MotivoRechazo`, usadas directo con `SqlCommand` en `Program.cs` (no hay `DerivarRequest`/etc. en el DAO tipado). Estados: `Pendiente → En entrevista → Derivado | Rechazado` (el default al insertar es `Pendiente`, confirmado en `InformeAdmisionDAO`). `PUT /informes-admision/{id}/derivar` (Program.cs:1138) hace **tres cosas** en una sola llamada: (1) pone `Estado = 'Derivado'` y graba `IdProfesionalDerivado`; (2) hace upsert en `PacienteProfesional` con `EstadoAsignacion = 'Nueva'`; (3) agrega una `Evolucion` en la historia clínica con `TipoCierre = 'Derivación'` y texto libre (`'Derivado a: ' + nombre`) — eso es historia clínica de verdad, no un log de auditoría, así que no choca con la regla 7 del pedido. Ninguno de los tres pasos pasa hoy por `LogAuditoria`.

**Agenda.** Son franjas (`IdProfesional, Dia, HoraInicio, HoraFin, Disponible, IntervaloMinutos`), cada una con su propio `Disponible` — un profesional puede tener franjas cargadas y alguna desactivada. "Horarios de trabajo" (el link que agregó el Paso 4 de la Fase 1) son filas de esta misma tabla.

**Derivacion / DerivacionDetalle — esto ya existe, y con otro significado.** Hay una tabla `Derivacion` (`IdDerivacion, IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado`), con endpoints `/derivaciones*` completos (listar, pendientes, detalle, crear, aceptar, rechazar) más `/derivaciones/cambio-directo`. **Esto es el cambio de profesional de un paciente que ya está en tratamiento** — lo que la interfaz llama "Solicitudes de cambio" (`shell.js`: `{ id: "derivaciones", texto: "Solicitudes de cambio" }`; el paciente las pide desde "Mis profesionales"). `/derivaciones/{id}/aceptar` y `/cambio-directo` reasignan con `PacienteProfesionalDAO.Desasignar(origen) + Asignar(destino)`, y sí quedan en `LogAuditoria` — **con el motivo de puño y letra del pedido de cambio en el campo `Detalle`** (`Program.cs:693`, `Accion = "Cambio Directo Profesional", Detalle = req.Motivo`). Es un patrón previo a esta fase, no se toca en esta rama, pero es exactamente lo que la regla 7 de la Fase 2 pide evitar en lo nuevo — queda anotado en Pendientes.

### El choque de nombres: "Derivación" ya significa otra cosa (y dos, en realidad)

El pedido avisaba de esto ("probablemente ya significa algo") y pedía no esperar respuesta. Con el modelo de arriba, la palabra "derivación"/"derivado" ya se usa para **dos** cosas distintas en el código, ninguna de las dos es lo que pide la Fase 2:

1. La tabla `Derivacion` = cambio de profesional de un paciente en tratamiento (arriba).
2. `InformeAdmision.Estado = 'Derivado'` = la admisión terminó asignada a un profesional (no usa la tabla `Derivacion` para nada — es un estado propio con su propia columna `IdProfesionalDerivado`).

Ninguna de las dos tiene hoy el sentido de "caso que entra y se deriva a un **grupo**", que es el corazón de la Fase 2. Reusar el nombre `Derivacion` para la tabla nueva del Paso 0 del pedido **no es viable**: ya existe una clase `Derivacion` en el proyecto (colisión de compilación) y, aunque se pudiera, mezclaría dos procesos de negocio independientes (cambiar de profesional a un paciente ya asignado vs. rutear un caso nuevo) bajo la misma tabla — justo lo que la regla 8 del pedido pide evitar.

**Decisión (sin esperar respuesta, como indicaba el pedido):** la tabla nueva se llama **`CasoDerivacion`**, no `Derivacion`. Mismo patrón que ya usa el proyecto para esto (`Turno` → `TurnoDetalle`, `TurnoAviso`; acá `Caso` → `CasoDerivacion`). Las rutas nuevas van bajo `/casos` y `/casosderivaciones`, nunca `/derivaciones` (ver Paso 3 más abajo). La tabla y los endpoints `Derivacion`/`/derivaciones/*` existentes **no se tocan**: siguen siendo "Solicitudes de cambio", tal cual están.

En los textos de la interfaz nueva, "derivar" se usa siempre con objeto explícito — "derivar a un grupo" / "caso derivado" — para no sumar un tercer sentido suelto a una palabra que ya tiene dos (más el "escalar a una persona" del chat, que la Fase 1 ya renombró para sacarlo de esta familia de palabras).

### Modelo nuevo propuesto

Mínimo, como pedía el pedido — nada más que esto:

- **`Grupo`**: `IdGrupo, Nombre, Descripcion (nullable), Activo (bit)`.
- **`ProfesionalGrupo`**: `IdProfesional, IdGrupo` (PK compuesta). N:M — no agrega columnas de más; la fecha de alta a un grupo no la pidió el pedido y no hace falta todavía.
- **`Caso`**: `IdCaso, IdPaciente, Estado, IdGrupoActual (nullable), IdProfesionalAsignado (nullable), Origen, FechaCreacion, IdInforme (nullable, FK a InformeAdmision.IdInforme)`. Estados: `Nuevo, Derivado, Asignado, En atención, Cerrado`. `Origen` es cómo entró el caso (`Admision` hoy; `Canal`/`Bot` reservados, no se usan todavía). Un caso por admisión como máximo: `IdInforme` tiene un índice único, pero **filtrado** (`WHERE IdInforme IS NOT NULL`), no una `UNIQUE CONSTRAINT` de tabla — a diferencia de otros motores, SQL Server solo deja **un** NULL por columna en una UNIQUE normal, y el test de integración lo encontró en la práctica (el segundo caso sin admisión rompía). Con el índice filtrado, cualquier cantidad de casos sin admisión conviven sin chocar.
- **`CasoDerivacion`**: `IdCasoDerivacion, IdCaso, IdGrupo, IdProfesionalSugerido (nullable), IdProfesionalAsignado (nullable), Origen, Estado, Motivo, IdCreadoPor (nullable), FechaCreacion, FechaResolucion (nullable)`. `Origen`: `Regla | Manual | Bot` (reservado). Estado: `Pendiente de confirmar, Confirmada, Corregida, Rechazada`. `Motivo` es corto — vocabulario cerrado o texto breve, nunca el detalle clínico (regla 7).
- **`ReglaDerivacion`**: `IdRegla, IdEspecialidad, IdGrupoDestino, Prioridad, Activa`. Criterio mínimo viable tal cual lo pedía el pedido: especialidad solicitada en la admisión → grupo. Nada de palabras clave ni IA.
- **`HistorialAsignacion`**: `IdHistorial, IdCaso, IdProfesional (nullable, para "quedó sin asignar"), Motivo, IdRealizadoPor (nullable), FechaHora`. `Motivo` con vocabulario cerrado: `AutomaticaPorCarga | AutomaticaAleatoria | Manual | SolicitudCambio | SinDisponibles` (este último para cuando el caso queda sin asignar — `IdProfesional` NULL — y conviene que quede igual de explicado que el resto). Esta tabla es la que permite explicar cada asignación (punto 5 del Paso 0 del pedido).

Todas las FK hacia `Profesional`/`Paciente`/`Especialidad`/`Usuario` existentes, sin tocarlas. Índices en `Caso.Estado`, `Caso.IdGrupoActual`, `Caso.IdProfesionalAsignado`, `CasoDerivacion.Estado` y `CasoDerivacion.IdCaso` (las columnas de búsqueda, Paso 1 del pedido).

### Cómo convive con lo existente (nada se reemplaza, se engancha al lado)

La idea es que `Caso`/`CasoDerivacion` sea una capa de **seguimiento nueva, aditiva**, enganchada a los puntos donde hoy ya se decide "quién atiende a este paciente" — sin cambiar lo que esos endpoints devuelven ni lo que ve el administrador hoy (regla 2 y 3):

- `POST /informes-admision` (al enviarse la admisión): además de lo que ya hace, crea un `Caso` (`Estado = Nuevo`, `Origen = Admision`, `IdInformeAdmision` = el nuevo informe). Aplica `ReglaDerivacion` por la especialidad solicitada; si hay regla, genera una `CasoDerivacion` sugerida (`Origen = Regla`, `Estado = Pendiente de confirmar`) pasando los candidatos del grupo por `AsignacionService`. Si no hay regla, el caso queda `Nuevo` sin grupo, en la cola del administrador. **La respuesta del POST no cambia.**
- `PUT /informes-admision/{id}/derivar` (el admin asigna profesional desde la pantalla de admisión que ya existe): además de lo que ya hace (los tres pasos de arriba), sincroniza el `Caso` de ese informe — `IdProfesionalAsignado`, `Estado = Asignado` — y resuelve la `CasoDerivacion` pendiente (`Confirmada` si coincide con lo sugerido, `Corregida` si el admin eligió otro profesional o grupo), con su fila en `HistorialAsignacion` (`Motivo = Manual`). **La pantalla de admisión sigue igual**; lo nuevo se ve en Casos/Derivaciones.
- `PUT /derivaciones/{id}/aceptar` y `POST /derivaciones/cambio-directo` (solicitud de cambio, la de siempre): si el paciente tiene un `Caso` abierto (`Estado` no `Cerrado`), además de lo que ya hacen, actualizan `Caso.IdProfesionalAsignado` y agregan una fila a `HistorialAsignacion` (`Motivo = SolicitudCambio`) — así el historial del caso cuenta también estos cambios. Si el paciente no tiene un `Caso` abierto (pacientes de antes de esta fase), no hacen nada nuevo: no se reconstruye historial viejo. **El comportamiento visible de "Solicitudes de cambio" no cambia.**

Nada de esto reemplaza `PacienteProfesional`: sigue siendo la tabla operativa de "quién atiende a quién ahora" que ya usan turnos y agenda. `Caso.IdProfesionalAsignado` es la misma información vista desde el seguimiento nuevo, mantenida en sync por los mismos puntos de código que ya tocan `PacienteProfesional`.

### "Disponible" y "carga"

- **Disponible** = `Profesional.Estado = 'Activo'` **y** tiene al menos una fila en `Agenda` con `Disponible = 1`. No hay hoy ningún dato de vacaciones/licencias en el modelo — no se inventa; si hace falta más adelante, es una columna nueva en otra fase.
- **Carga** = cantidad de `Caso` con `IdProfesionalAsignado` = ese profesional y `Estado` en (`Asignado`, `En atención`). Casos `Cerrado` no cuentan.
- **Desempate**: menor carga → más antigua `HistorialAsignacion` del profesional (el que hace más tiempo no recibe un caso nuevo) → aleatorio con el generador inyectado.

### Paso 3 (adelanto): nombres de endpoint, para no chocar con `/derivaciones`

`/grupos` (+ `/grupos/{id}/miembros`), `/reglasderivacion`, `/casos` (+ `/casos/{id}`, `/casos/{id}/derivaciones`, `/casos/{id}/reasignar`), `/casosderivaciones/pendientes` (+ `/casosderivaciones/{id}/confirmar|corregir|rechazar`), `/profesionales/{id}/casos` ("Mis casos"). Todo nuevo, nada pisa una ruta que ya existe.

### Decisiones tomadas sin esperar respuesta (regla del Paso 0)

- Tabla `Derivacion`/rutas `/derivaciones/*` existentes: **no se tocan**. La palabra "Derivación" sola, sin calificar, sigue siendo "solicitud de cambio de profesional" en la interfaz existente; lo nuevo de esta fase se llama siempre "caso derivado"/`CasoDerivacion` en código y "Derivaciones" (de casos a grupos) solo dentro del menú **CRM** nuevo, donde el contexto ya lo deja claro.
- `Models/InformeAdmision.cs` y `Models/PacienteProfesional.cs` quedan desactualizados respecto de la tabla real (no es de esta fase arreglarlo); el Paso 1 no los toca, sigue sumando SQL crudo donde hace falta leer las columnas que el modelo no tiene, igual que ya hace el resto de `Program.cs`.
- El motivo de `CasoDerivacion`/`HistorialAsignacion` usa vocabulario cerrado (enum-string), nunca el texto libre de `InformeAdmision.MotivoConsulta` ni de la historia clínica — ni siquiera por referencia desde la auditoría.

### Paso 1: migración y DAOs

`migrations/add_grupos_casos_derivacion.sql` — las 6 tablas del Paso 0, con índices en `Caso.Estado/IdGrupoActual/IdProfesionalAsignado` y `CasoDerivacion.Estado/IdCaso`. Aplicada dos veces contra la base local: la segunda corrida no hizo nada (idempotente, confirmado). Un DAO por entidad (`GrupoDAO`, `CasoDAO`, `CasoDerivacionDAO`, `ReglaDerivacionDAO`, `HistorialAsignacionDAO`), más `CasoDetalle`/`CasoDerivacionDetalle` con nombres ya resueltos para las pantallas.

**Dos bugs reales que encontró el test de integración del Paso 2** (y que quedaron arreglados en el propio Paso 1, antes de seguir):
1. `Caso.IdInforme` como `UNIQUE CONSTRAINT` de tabla rompía al segundo `Caso` sin admisión: a diferencia de otros motores, SQL Server solo deja **un** NULL por columna en una UNIQUE normal. Arreglado con un índice único **filtrado** (`WHERE IdInforme IS NOT NULL`), que si deja cualquier cantidad de NULL.
2. Los índices/scripts filtrados piden `SET QUOTED_IDENTIFIER ON` — sin eso, tanto crear el índice como después tocar filas de `Caso` desde `sqlcmd -Q` suelto falla con el error 1934. Se agregó al principio de la migración y hay que acordarse de ponerlo también en cualquier script nuevo que toque `Caso` fuera de la app (los DAOs no necesitan nada especial: ADO.NET ya lo deja en `ON` por conexión).

### Paso 2: AsignacionService

Único servicio que decide asignaciones (`Services/AsignacionService.cs`). `Elegir()` es la cascada en estado puro — sin acceso a datos, recibe la lista de candidatos ya resuelta y un `Random` inyectado — menor carga → desempate por antigüedad de la última asignación → aleatorio → `SinDisponibles` si no hay nadie. El resto del servicio (`SugerirDerivacion`, `ConfirmarDerivacion`, `CorregirDerivacion`, `RechazarDerivacion`, `ReasignarManual`) orquesta esa decisión contra `IAsignacionRepositorio`, dejando rastro en `HistorialAsignacion` con el motivo real (el de la cascada si fue automática, `Manual` si no, `SolicitudCambio` si vino del mecanismo viejo).

`CasoDerivacion.Motivo` cumple doble función a propósito: para una derivación por regla guarda el mismo vocabulario cerrado que `HistorialAsignacion.Motivo` (`AutomaticaPorCarga`/`AutomaticaAleatoria`/`SinDisponibles`) — así la pantalla de Derivaciones puede mostrar "cómo se sugirió" sin otra columna — y para una manual puede ser un texto corto que escribe el administrador (ver el punto de privacidad más abajo).

16 tests en `YoSoy.Tests/AsignacionServiceTests.cs` (con `FakeAsignacionRepositorio` en `Fakes.cs`, sin base de datos) cubren los 7 escenarios que pedía el pedido (menor carga, desempate, aleatorio con semilla fija y reproducible, grupo sin profesionales, nadie disponible, reasignación manual, un profesional en dos grupos) más las operaciones de orquestación. `AsignacionIntegrationTests.cs` es el test de integración contra la base real: crea dos grupos y un caso propios, confirma que `ObtenerCandidatos` ve la carga de un caso aunque haya llegado por otro grupo, corre la cascada de punta a punta, y borra todo en el `finally` (verificado: 0 filas propias al final, sin tocar nada que ya existiera).

**Bug encontrado después, al preparar la verificación de "solicitud de cambio" por la interfaz** (commit `ed27937`): `ConfirmarDerivacion`/`CorregirDerivacion`/`ReasignarManual` nunca tocaban `PacienteProfesional` — a pesar de que el Paso 0 ya decía que esa tabla debía quedar en sync. Un caso asignado solo por el camino nuevo no dejaba rastro ahí: el paciente no aparecía en "Mis Pacientes" del profesional ni el profesional en "Mis profesionales" del paciente, aunque "Mis casos" sí lo mostrara. Arreglado agregando `IAsignacionRepositorio.SincronizarPacienteProfesional` (upsert nada más — nunca desasigna al profesional anterior; sacar a alguien sigue siendo una decisión de "Solicitudes de cambio"), llamado desde las tres operaciones que asignan de verdad, nunca desde `SugerirDerivacion` (una sugerencia no es una asignación) ni `RechazarDerivacion`. Confirmado a mano contra la base real: antes de confirmar un caso la paciente no aparecía en `/profesionales/1/pacientes-detalle`, después sí.

### Paso 3: endpoints

`Endpoints/CasosEndpoints.cs` — grupos (+ miembros), reglas de derivación, casos (listar con filtros/detalle/historial/reasignar), derivaciones de casos (pendientes/confirmar/corregir/rechazar), "Mis casos" del profesional, y el caso de un paciente (para el panel del lead). Verificación de rol igual que `AutorizacionReporte` (CB-04): `Services/AutorizacionCasos.cs` resuelve por `X-Usuario-Id` contra `UsuarioProfesional` — con vínculo es profesional (alcance: solo sus casos), sin vínculo es administrador (todo). Mismo `TODO(JWT)` que el resto de la app.

Integración aditiva con lo existente, verificada a mano de punta a punta contra la base real (y revertida después, sin dejar nada):
- `POST /informes-admision` crea el `Caso` y, si hay una regla activa para la especialidad solicitada, sugiere la derivación — sin cambiar la respuesta del POST.
- `PUT /informes-admision/{id}/derivar` (la pantalla de admisión de siempre) sincroniza ese `Caso` con lo que elige el administrador ahí — confirma o corrige la derivación sugerida, o asigna directo si ningún regla había sugerido nada — sin cambiar su propio comportamiento visible.
- `PUT /derivaciones/{id}/aceptar` y `POST /derivaciones/cambio-directo` (el mecanismo viejo, "Solicitudes de cambio") actualizan el `Caso` abierto del paciente si tiene uno, con su propia fila en `HistorialAsignacion` (`SolicitudCambio`); si no tiene un caso abierto (pacientes de antes de esta fase), no hacen nada nuevo.

### Paso 4: pantallas

`Pantalla_Configurador`: **Grupos** (Configuración ▾) en un split-view grupo↔miembros como el de Usuarios; **Reglas de derivación** (Configuración ▾), tabla editable con alta; **Casos** (CRM ▾), tabla con filtros (activos/todos/sin asignar/cerrados) + panel lateral con detalle, historial y reasignar, con aviso cuando hay casos sin asignar; **Derivaciones** (CRM ▾, `id` interno `casosderivaciones` para no chocar con el `derivaciones` que ya usaba Usuarios ▾ > Solicitudes de cambio), cola de pendientes con confirmar/corregir/rechazar. Modal compartido nuevo (`modalCaso` en `dashboard.html`/`.js`) para las cuatro.

`Pantalla_Profesional/js/nuevo_dashboard.js`: **Mis casos**, solo los propios, reusando las acciones que ya tenía Mis Pacientes (historia clínica, sesión).

`Pantalla_ChatSimulado/js/bandeja.js`: el panel "Información del lead" de CRM > Chats suma un renglón **Caso** (estado · grupo · profesional, con link al caso) solo si el paciente ya tiene uno — de solo lectura, sin tocar el resto del panel.

Verificado por Playwright contra la interfaz real, en varias corridas (todas sin errores de consola, todos los datos de prueba revertidos después): crear grupo, agregar/quitar miembro, crear regla, admisión real → derivación sugerida → confirmar, ver detalle y reasignar, Mis casos del profesional, panel del lead con el link, grupo sin nadie disponible → cola "Sin asignar" visible → corregir una → rechazar la otra, y que lo existente (turnos, agendas, Solicitudes de cambio, auditoría, usuarios, consentimiento, chats) siga andando igual.

## Fase 2: datos de prueba

`docs/datos-prueba-grupos-fase2.sql`, sin ejecutar: 4 grupos de ejemplo basados en las especialidades y los profesionales de prueba que ya existen (Psicología clínica con Santiago Manto y Laura Giménez, Psicología educativa con Ángeles Muñoz, Trabajo Social con Sebastián O'Brien, Terapia Ocupacional con Carolina Ferreyra) y 3 reglas de derivación — nada de Psiquiatría. "Psicología educativa" queda sin regla propia a propósito: la admisión hoy no tiene un campo más fino que "especialidad" para elegir clínica vs. educativa, así que la derivación automática va siempre al grupo más grande y el otro sirve de ejemplo de derivación a mano. Probado: corre limpio y confirma 0 filas propias después del `ROLLBACK`.

## Fase 2: privacidad de datos de salud mental

- **Vocabulario cerrado por defecto:** `CasoDerivacion.Origen/Estado` y `HistorialAsignacion.Motivo` son siempre un enum-string fijo, nunca texto libre — ni la auditoría ni el historial de asignación referencian `MotivoConsulta` ni ningún campo clínico.
- **Un campo sigue siendo texto libre a propósito, y hay que tenerlo presente:** `CasoDerivacion.Motivo` cuando el origen es `Manual` (al reasignar un caso o corregir una derivación, el campo "Motivo" del modal es opcional y lo escribe quien lo usa). Es información operativa ("pidió otro horario", "se fue de licencia"), pero nada impide que alguien escriba ahí algo clínico — no hay validación de contenido, como tampoco la hay hoy en `Derivacion.Motivo` (el mecanismo viejo de "Solicitudes de cambio", que además lo manda tal cual al `Detalle` de `LogAuditoria` — ver la Fase 1, bug 2, donde quedó anotado que ese patrón viejo no se tocó). Si esto preocupa, la corrección más chica sería no guardar ese texto libre en ningún lado que vaya a auditoría o reportes — hoy no se guarda ahí, solo en `CasoDerivacion.Motivo`, pero conviene decidirlo a propósito y no por omisión.
- **`TODO(JWT)` en los endpoints nuevos:** `Services/AutorizacionCasos.cs`, igual que `AutorizacionReporte` — la identidad hoy es `X-Usuario-Id`, un header que pone el navegador sin firmar. Mientras tanto, cualquiera que conozca o adivine un id puede pedir `/casos`, `/profesionales/{id}/casos`, etc. en nombre de otro. Se cierra solo al mergear `videollamadas-seguras`.
- **Grupos y especialidades no son, en sí, un dato más sensible** que lo que ya exponía `Especialidad` — pero `Caso`/`CasoDerivacion` sí nuevo: ahora hay una tabla más con el recorrido completo de a qué profesional llegó cada paciente y por qué (aunque sea solo con el motivo cerrado). Vale la misma consideración que ya tenía `PacienteProfesional`: nada de esto pasa por un canal cifrado distinto del resto de la app ni tiene controles de acceso más allá de lo que ya hace `AutorizacionCasos`.

## Fase 2: archivos sin uso

Ninguno. Esta fase solo agregó: no reemplazó ni dejó obsoleta ninguna pantalla, tabla o endpoint existente.

## Fase 2: pendientes

- **Vocabulario de chips de la Fase 1** (Paso 3: "Respondido por el bot"/"Nueva"/"Sin responder"): seguía pendiente de la fase anterior, sigue sin tocarse acá.
- ~~El texto libre de `CasoDerivacion.Motivo` en reasignaciones manuales~~ — resuelto en la Fase 3a, Paso 0: ahora es una lista cerrada más una nota aparte que nunca llega a auditoría.
- **Entrenamiento del bot:** a propósito fuera de esta fase (el menú CRM no lo agrega). `CasoDerivacion.Origen = 'Bot'` y `Caso.Origen = 'Canal'/'Bot'` ya están reservados en el modelo para cuando llegue.
- **Reasignación manual no permite cambiar de grupo**, solo de profesional (un cambio de grupo pasa por "Corregir" mientras la derivación sigue pendiente, o por derivar de nuevo). Si hace falta reasignar de grupo a un caso ya confirmado, hoy no hay una acción directa para eso.
- **No se migraron los pacientes/casos de antes de esta fase:** un paciente que ya tenía un profesional asignado por el mecanismo viejo no tiene un `Caso` hasta que entre una admisión nueva o alguien lo cree a mano (no hay un endpoint para crear un `Caso` suelto, solo vía admisión) — es consistente con "no se inventan datos", pero significa que "Mis casos" y el panel del lead van a estar vacíos para esos pacientes hasta que pase algo nuevo. Ver la propuesta (sin construir) en "Fase 3a, Paso 0" más abajo.

## Fase 3a: disponibilidad por profesional + link público de reserva

Plan más grande, Fase 3a. Sigue en `Cambios-Agus`, local, sin push. La sincronización con Google Calendar es la Fase 3b, aparte — no se toca acá.

### Paso 0, punto 1: motivo de reasignación manual, ahora lista cerrada

Antes `CasoDerivacion.Motivo` para una reasignación manual era el texto que escribía el administrador, sin validar ni límite. Ahora:

- `Services/AsignacionService.cs`, `MotivoReasignacion.Categorias`: `CargaEquilibrada`, `PedidoDelPaciente`, `CambioDeEspecialidad`, `ProfesionalNoDisponible`, `Otro` — es la única fuente de verdad; el endpoint `GET /casos/motivos-reasignacion` se la expone al frontend para no repetir la lista, y `ReasignarManual` tira `ArgumentException` (→ 400) si no coincide con ninguna.
- `CasoDerivacion.Nota` (columna nueva, `migrations/alter_casoderivacion_motivo_cerrado.sql`, aplicada dos veces en la base local — idempotente): texto corto opcional, máximo 140 caracteres (`MotivoReasignacion.NotaLongitudMaxima`), para contexto operativo ("pidió otro horario", "se fue de licencia"). **Nunca se copia** a `HistorialAsignacion.Motivo` (que sigue siendo siempre `"Manual"`, el mecanismo, no el porqué) ni pasa por ningún `LogAuditoria.Insertar` — confirmado contra la base real después de reasignar con una nota: cero filas de `LogAuditoria` la mencionan.
- No se perdió nada de lo ya guardado: la columna es `NULL` por defecto y `CasoDerivacion.Motivo` no se tocó (sigue siendo la misma columna que ya usaba la cascada automática para `AutomaticaPorCarga`/etc.).
- Interfaz: el modal de "Reasignar caso" cambió el campo de texto libre por un `<select>` con la lista cerrada (obligatorio) más un campo de nota aparte (opcional, con la advertencia de que no va a auditoría). El panel de detalle de un caso ahora suma una sección "Notas de reasignación" cuando las hay (leyendo `/casos/{id}/derivaciones`, que no se usaba antes ahí).
- 2 tests nuevos (motivo no reconocido, nota de más de 140) más los 2 existentes de reasignación actualizados para probar que la categoría y la nota quedan donde corresponde.

### Paso 0, punto 2: pacientes sin `Caso` de antes de la Fase 2

Confirmado en la base local: hoy ningún paciente viejo tiene un `Caso` a menos que haya pasado por una admisión nueva desde que se mergeó la Fase 2 — no hay forma de crearlo retroactivamente. Pasa lo mismo con cualquier paciente nuevo que un administrador dé de alta y asigne a mano por fuera del flujo de admisión (`/derivaciones/cambio-directo`, "Solicitudes de cambio" sin admisión previa).

**Propuesta, sin construir, para decidir:** un endpoint `POST /casos` (solo administrador) con `{ idPaciente, idGrupo, idProfesional? }` — crea un `Caso` con `Origen = 'Manual'` y, si viene `idProfesional`, lo deja directo en `Asignado` (vía `ReasignarManual`, mismo camino que ya sincroniza `PacienteProfesional` y deja rastro en `HistorialAsignacion`); si no, queda `Derivado` sin profesional, visible en la cola de "Sin asignar". En la interfaz, el lugar más chico para esto es un botón "Crear caso" en la pantalla de Usuarios, dentro de la ficha de un paciente ya seleccionado (reusando el split-view que ya existe) — así no hace falta una pantalla nueva. No se construyó: es la clase de decisión ("¿se puede crear un caso sin que haya una admisión de por medio?") que le toca a una persona, no a un commit.

### Paso 0, reconocimiento

Árbol limpio al arrancar.

**Cómo se piden y crean turnos hoy — el hallazgo central de este paso:** `Services/TurnoReglasService.cs` (de "Encuentro 5", previo a esta rama) **ya tiene todo lo que pide el Paso 1**, completo y correcto, pero casi nada lo usa todavía:

- `Solicitar(idPaciente, idProfesional, momento, motivo)`: valida RN-01/05/07 (y RN-02 si `Mensajeria:ExigirConsentimientoParaSolicitar` — hoy `false`) y después inserta. La inserción es `ITurnoRepositorio.InsertarSiLibre`, en `DAOs/TurnoRepositorioDAO.cs`: **ya es a prueba de doble reserva** — una sola transacción con `WITH (UPDLOCK, HOLDLOCK)` que revisa huecos y duplicados y recién ahí inserta, así que dos pedidos simultáneos no pueden terminar con el mismo horario. No hay que construir nada de esto, solo usarlo.
- `Cancelar(idTurno, idPaciente)`: valida dueño + estado + RN-05 y cambia el estado.
- `HorariosLibres(idPaciente, idProfesional, dias, maximo)`: RN-01/05/07 para ofrecer horarios.
- El **bot ya usa el servicio entero** (`BotReservasService.cs`: `HorariosLibres`, `Solicitar`, `Cancelar`, `TieneConsentimiento`) — es la referencia de cómo se usa bien.

Lo que **no** pasa por ahí, con lo que hace cada uno en su lugar:

| Camino | Endpoint | Validación hoy | Plan |
|---|---|---|---|
| Paciente pide turno (web) | `POST /turnos/solicitar` | **Ninguna** — inserta directo con `TurnoDAO.Insertar` | pasar a `reglas.Solicitar(...)` |
| Reprogramar (paciente) | cancela + `POST /turnos/solicitar` | ídem | mismo cambio, gratis |
| Cancelar turno | `PUT /turnos/{id}/cancelar` | **Ninguna** — lo llaman paciente y profesional por igual, sin dueño ni RN-05 | ver más abajo: hay que separar por rol |
| Profesional "solicita turno" / Cita urgente | `POST /turnos/profesional-solicitar` | **Ninguna** — mismo endpoint para las dos cosas | ver más abajo: necesitan reglas distintas |
| Confirmar (paciente) | `PUT /turnos/{id}/confirmar-paciente` | RN-02 vía `ValidarPasoAConfirmado` | ya está bien, no se toca |
| Cambiar estado (configurador) | `PUT /turnos/{id}` | RN-02 vía `ValidarCambioDeEstado` | ya está bien, no se toca |
| Alta cruda | `POST /turnos` | Ninguna — **sin usuarios conocidos** (no lo llama ninguna pantalla; se buscó en todo `PANTALLAS/`) | pasarlo igual, por las dudas, es gratis |
| Bot | menú de turnos | Completo, vía el servicio | referencia, no se toca |

**Dos caminos necesitan más que un cambio mecánico:**

1. **`PUT /turnos/{id}/cancelar` lo llaman dos roles distintos** (`Pantalla_Paciente/js/dashboard.js` y `Pantalla_Profesional/js/nuevo_dashboard.js`, mismo endpoint). `reglas.Cancelar` asume que quien cancela es el paciente dueño del turno (chequea `IdPaciente` y aplica RN-05) — aplicárselo tal cual también a un profesional rompería su cancelación de hoy (que no tiene esa restricción) y además el "dueño" no tendría sentido con un `idUsuario` de profesional. Plan: resolver el rol que llama (mismo patrón que `AutorizacionCasos`) y **solo** pasar por `reglas.Cancelar` cuando es un paciente; la cancelación del profesional/configurador sigue exactamente como está hoy (sin restricción) — no se le agrega una regla nueva que hoy no tiene, para no cambiar su comportamiento visible.
2. **`POST /turnos/profesional-solicitar` son dos flujos con la misma ruta:** la propuesta normal de un turno futuro, y "Cita urgente" (`confirmarCitaUrgente()` en `nuevo_dashboard.js`), que manda la fecha/hora **de ahora mismo** — algo que `ValidarSolicitud` rechazaría siempre (RN-01 lo exige en el futuro, alineado a una franja; RN-05 exige 24 h de anticipación). Plan: agregar un campo opcional al request (`esUrgente: bool`, default `false` — agregar un campo a un contrato existente está permitido). Si es `true`, se salta RN-01/05 (es la naturaleza de "urgente": ahora, fuera de la grilla) pero **sí** pasa por la misma inserción segura (`InsertarSiLibre`) para que dos urgencias simultáneas con el mismo profesional no choquen. Si es `false` (el uso normal), sí aplica todas las reglas, como cualquier otra solicitud — hoy no aplica ninguna.

**Cómo se calcula la disponibilidad hoy:** `Agenda` (franjas por día, con `Disponible`) + `Agenda.IntervaloMinutos` (duración del turno) + turnos ocupados (`Pendiente`/`Confirmado` bloquean; `Cancelado` no — no existe un estado `Rechazado` en `EstadoTurno` hoy, aunque el código lo prevé por si se agrega). **La duración del turno ya es configurable** — y más fino de lo que pedía el Paso 2: es **por franja** (cada fila de `Agenda` tiene su propio `IntervaloMinutos`, default 30), no una sola por profesional. "Mis horarios de trabajo" (`Pantalla_Profesional/js/nuevo_dashboard.js`, función `horarios()`) ya tiene alta, edición y borrado completos de franjas, con el intervalo como campo del formulario, y ya tiene un estado vacío explicativo (`YS.vacio` cuando no hay franjas). **No hace falta construir nada de esto para el Paso 2** — es todo lo que pedía, ya hecho; el Paso 2 se reduce a la "vista previa" nueva.

**Un hueco para la vista previa y el link público:** `HorariosLibres` pide un `idPaciente` (para RN-07, "¿ya tiene un turno ese día con este profesional?"). Ni la vista previa de "Mis horarios" ni la página pública tienen un paciente en particular — van a necesitar una variante sin RN-07, solo con RN-01 (agenda + duración + ocupados). Se agrega como una sobrecarga nueva en el Paso 1, sin tocar la que ya usa el bot.

### Diseño propuesto para los pasos siguientes

- **Paso 1:** en `TurnoReglasService`, agregar `HorariosLibresDelProfesional(idProfesional, dias, maximo)` (sin paciente, sin RN-07 — la usan la vista previa y lo público) al lado de la `HorariosLibres` existente (sin tocarla). Rewirear `POST /turnos/solicitar` y, con el caso especial de arriba, `PUT /turnos/{id}/cancelar` y `POST /turnos/profesional-solicitar`. Dejar `POST /turnos` crudo igual que los demás (nadie lo usa, pero de paso queda prolijo). Tests de `HorariosLibresDelProfesional` (los mismos casos borde que ya prueba `HorariosLibres`, más "sin paciente") y de los endpoints rewireados (RN-01/05/07 ahora sí rechazan, doble reserva simultánea con el `InsertarSiLibre` ya existente).
- **Paso 2:** la vista previa en "Mis horarios de trabajo" llama a `GET` sobre la nueva función (nuevo endpoint chico, p. ej. `GET /profesionales/{id}/horarios-libres?dias=14`) y pinta los próximos 14 días con los horarios que devuelve — ya con el sistema de diseño, nada nuevo que inventar en la grilla porque es la misma lógica de slots que ya arma el calendario.
- **Paso 3:** `Profesional.Slug` (nullable, único) y `Profesional.LinkPublicoActivo` (bit, default 0) — migración nueva. Slug: nombre normalizado + sufijo corto aleatorio (p. ej. 6 caracteres alfanuméricos), para que no sea adivinable en secuencia ni choque entre profesionales con nombres parecidos. Página pública nueva en `PANTALLAS/` (fuera del shell con sesión, sin `shared/shell.js`) que llama a un endpoint público de **solo lectura** (`GET /public/profesionales/{slug}/disponibilidad` o similar — por decidir el prefijo) que devuelve nombre, especialidad/grupo, modalidad y horarios libres (via `HorariosLibresDelProfesional`) — nunca ids de pacientes ni de turnos, nunca quién ocupa qué horario. 404 idéntico para slug inexistente y link apagado. Límite de pedidos por IP: un limitador en memoria (ventana corta, ej. 20 pedidos/minuto), sin estado compartido entre procesos — alcanza para esta fase, local. Rango de fechas acotado server-side (máximo 60 días), parámetros validados estrictos. El profesional activa/desactiva desde "Mi perfil"; el administrador también puede apagarlo (no un botón nuevo en Usuarios, sino el mismo lugar donde ya se edita un profesional).
- **Paso 4:** auditoría de activar/desactivar el link y de crear un turno desde ahí — mismo patrón que ya corrigió la Fase 1 (nombre de usuario resuelto, sin texto libre). El aviso al profesional de "turno solicitado" ya existe (`INotificadorTurnos.TurnoSolicitado`) y ya pasa por la agrupación de la Fase 1 — un turno creado desde el link público es un turno más, no necesita un aviso distinto.

### Paso 1: servicio unificado (resultado)

Se construyó tal cual lo proponía el diseño, sin sorpresas grandes:

- `TurnoReglasService.HorariosLibresDelProfesional(idProfesional, dias, maximo)`: nueva, al lado de `HorariosLibres` (sin tocarla). Internamente las dos llaman a un `CalcularHorariosLibres` privado compartido, parametrizado por un `Func<DateTime,bool> pasaRN07` — con paciente, el chequeo real; sin paciente, `_ => true`.
- `POST /turnos/solicitar` ahora pasa por `reglas.Solicitar(...)`; devuelve `ComoConflicto()` (409) en RN-01/02/05/07.
- `PUT /turnos/{id}/cancelar`: ahora toma un `int? idPaciente` opcional por query string. Si viene, pasa por `reglas.Cancelar` (dueño + RN-05); si no viene (lo llama el profesional/configurador), sigue exactamente el mismo código crudo de antes, sin restricción nueva.
- `POST /turnos/profesional-solicitar`: `TurnoProfesionalRequest` ganó `EsUrgente` (default `false`, aditivo). Cita urgente (`confirmarCitaUrgente()`) lo manda en `true` y usa el nuevo `SolicitarUrgente` (sin RN-01/05, con la inserción segura igual). Las otras dos llamadas reales del mismo endpoint (`confirmarSolicitudDiaProf`, `enviarPrimerTurno`) no lo mandan, así que siguen aplicando todas las reglas.
- `POST /turnos` crudo: se dejó como estaba. Se confirmó de nuevo que ninguna pantalla lo llama; cambiarlo no tenía beneficio y sí el riesgo de tocar algo sin poder probarlo con una pantalla real.
- **La doble reserva ya estaba resuelta** (`TurnoRepositorioDAO.InsertarSiLibre`, con `WITH (UPDLOCK, HOLDLOCK)`), solo había que usarla — lo que de verdad hacía falta construir era bastante menos de lo que pedía el enunciado.

Tests nuevos: 6 en `TurnoReglasServiceTests` (horarios libres sin paciente, profesional sin horarios cargados → vacío, urgente salta RN-01/05, urgente también respeta RN-07, `creadoPor` no cambia la validación) y `TurnoDobleReservaIntegrationTests` — 15 pedidos en paralelo (`Task.Run`, conexión propia cada uno) contra la base real, mismo horario: exactamente 1 se crea, confirmado también con una consulta aparte a la base. Bug propio encontrado y corregido antes de llegar a probarlo: una reescritura confusa de `Solicitar` llamaba dos veces al nuevo helper de inserción.

### Paso 2: vista previa (resultado)

`GET /profesionales/{id}/horarios-libres?dias=14` (autenticado — lo pide la propia pantalla del profesional) y una tarjeta nueva "Así te ven los pacientes" en "Mis horarios de trabajo", con los próximos 14 días agrupados. La duración del turno por franja ya estaba resuelta de antes (Paso 0), así que el paso se redujo a esta vista previa.

Bug propio encontrado y corregido antes de commitear: la tarjeta tenía `text-transform: capitalize` en CSS pensando en un formateo de fecha casero que después se cambió por `YS.fecha.separador` (que ya devuelve el texto bien capitalizado) — el CSS de más dejaba "Miércoles, 7 De Octubre" en vez de "Miércoles, 7 de octubre". Se sacó la regla.

### Paso 3: link público de reserva (resultado)

- **Slug:** `Profesional.Slug` (único, índice filtrado por `WHERE Slug IS NOT NULL` — mismo patrón que `Caso.IdInforme` en la Fase 2, SQL Server solo permite un `NULL` en un `UNIQUE` sin filtrar) + `Profesional.LinkPublicoActivo` (bit, default 0). El slug es `nombre-apellido-normalizado` + 8 caracteres hex aleatorios (`RandomNumberGenerator`), se genera **una sola vez** la primera vez que se activa y se conserva siempre después — apagar y volver a prender no cambia el link. `ProfesionalDAO.Modificar`/`Insertar` (el `PUT /profesionales/{id}` genérico que usa "Mi perfil") **no tocan estas dos columnas a propósito**: confirmado con curl que guardar el perfil no las pisa aunque el cliente las reenvíe en el body.
- **Quién lo prende/apaga:** `PUT /profesionales/{id}/link-publico`, mismo patrón de identidad que Casos/Grupos (`AutorizacionCasos.Resolver` — profesional solo el suyo, administrador cualquiera). En la interfaz: "Mi perfil" del profesional (tarjeta con el link completo para copiar, igual al patrón que ya usaba el link de consentimiento RN-02) y el panel de administrador en Usuarios (un chip de estado y, solo si está activo, el botón para apagarlo — el admin no prende el de alguien que no lo pidió).
- **Página pública:** `GET /reservar/{slug}` (ruta limpia, fuera del shell con sesión, `PANTALLAS/Pantalla_Publica/`) sirve la página solo si el slug existe **y** el link está activo; si no, 404 — el mismo código exacto para "no existe" y "está apagado", nunca se distingue cuál de las dos cosas pasó.
- **Disponibilidad pública:** `GET /public/profesionales/{slug}/disponibilidad?dias=` — solo lectura. Devuelve `{ nombre, especialidad, horariosLibres }`, nada más: sin ids (ni de profesional, ni de turno, ni de paciente), sin "modalidad" (no es un dato por profesional en este modelo — `Turno.Modalidad` es siempre `"Virtual"` para cualquiera, así que mostrarlo no distinguía nada; se puso como texto fijo en la página, no como dato). `dias` fuera de 1-60 (o no numérico) da 400, no un clamp silencioso — "parámetros validados estrictos" se interpretó como rechazar, no como corregir solo.
- **Reserva desde la página pública, ya logueado:** `POST /public/profesionales/{slug}/reservar` — resuelve el slug a `idProfesional` server-side y llama al mismo `TurnoReglasService.Solicitar` del Paso 1 (mismas RN-01/02/05/07, mismo aviso al paciente). Sin sesión: la página manda a `index.html?return=/reservar/{slug}?slot=...`, y ese `return` viaja (sin validar, es solo texto de paso) por login → consentimiento → registro → login, validándose **una sola vez**, justo antes de redirigir de verdad, con `YS.rutaRetornoValida` (una regex anclada a la única forma que puede tener hoy — no una regla genérica de "empieza con /", que deja pasar `//evil.com` como protocol-relative).
- **Límite de pedidos por IP:** `Microsoft.AspNetCore.RateLimiting` (el limitador nativo de ASP.NET Core, en memoria, por proceso — es exactamente el "limitador en memoria" que proponía el diseño, usando lo que ya trae el framework en vez de escribirlo a mano), partición por IP, ventana fija de 1 minuto, 20 pedidos, 429 con un mensaje legible. Aplicado a las tres rutas públicas (página, disponibilidad, reservar).
- **Headers:** `X-Content-Type-Options: nosniff` y `Referrer-Policy: no-referrer` en todo lo que empiece con `/public` o `/reservar` — es la primera superficie de esta app pensada para quedar expuesta alguna vez; el resto (mensajería incluida) sigue siendo exclusivamente local.
- **Sin cookies ni estado propio:** la página es un `GET` sin sesión de servidor. Lo único que lee del lado del cliente es el `localStorage` ya existente (`rol`/`idPaciente`) para saber si ya hay sesión de paciente — no es una cookie ni un estado nuevo, es el mismo mecanismo débil que usa el resto de la app para identificar pacientes.
- **Escapado:** nombre y especialidad del profesional pasan por `YS.esc` en la página pública, igual que el resto de la interfaz.

**Bug propio encontrado y corregido durante la verificación:** `elegirHorario` encodeaba el horario elegido con `encodeURIComponent` dos veces (una al armar `/reservar/{slug}?slot=...`, otra al colgar esa ruta entera del `?return=` de `index.html`), pero `URLSearchParams.get()` del otro lado solo decodea una vez — el `:` de la hora quedaba como el texto literal `%3A` en vez de un `:` real, y la regex de `YS.rutaRetornoValida` nunca matcheaba. El `return` se perdía en silencio y todo el que volvía de loguearse caía en el dashboard normal en vez de volver a la reserva. Se corrigió armando el `destino` sin encodear el horario (todo el string se encodea una sola vez, entero, para el `return` externo) y se reprobó de punta a punta con Playwright.

### Paso 4: auditoría (resultado)

Activar/desactivar el link ya quedaba auditado desde que se construyó el endpoint en el Paso 3 (`"Activar Link Público"`/`"Desactivar Link Público"`, `Entidad="Profesional"`). Faltaba crear el turno desde el link: se agregó `LogAuditoria` ahí también (`"Turno Solicitado (Link Público)"`, `Entidad="Turno"`, `IdEntidad` el turno nuevo) — mismo patrón de siempre, solo ids y acción, nada clínico ni el motivo. El usuario se resuelve vía `X-Usuario-Id`, que `YS.fetch` ya manda solo cuando el paciente tiene sesión en `localStorage` (que la tiene: así llegó a poder pedir el turno). Confirmado contra la pantalla real de auditoría que se ve legible: `Turno Solicitado (Link Público) | Turno | #2023 | paciente1`. El aviso al paciente (`INotificadorTurnos.TurnoSolicitado`) es el mismo de siempre, sin duplicar nada — un turno pedido desde el link es un turno más para ese mecanismo.

## Fase 3a: migraciones

- `migrations/alter_casoderivacion_motivo_cerrado.sql` (Paso 0): `CasoDerivacion.Nota NVARCHAR(140) NULL`. Aplicada en la base local, probada dos veces (idempotente).
- `migrations/add_link_publico_profesional.sql` (Paso 3): `Profesional.Slug NVARCHAR(80) NULL` + índice único filtrado, `Profesional.LinkPublicoActivo BIT NOT NULL DEFAULT 0`. Aplicada en la base local, probada dos veces (idempotente). Necesita `SET QUOTED_IDENTIFIER ON` para crearse (filtro) y, aprendido de nuevo en esta fase, **también** para cualquier script suelto que después actualice filas de `Profesional` vía `sqlcmd` (no vía la app: el `SqlConnection` de ADO.NET ya lo trae en `ON` por defecto, así que el código de la app nunca lo necesitó — solo mis scripts de limpieza manual).

Las dos quedaron aplicadas y el código las espera; no hubo que revertir nada en esta fase.

## Fase 3a: qué no se tocó (y por qué)

- **El registro de un paciente nuevo no resume la reserva automáticamente del todo:** el `return` viaja correctamente por consentimiento → registro → el botón "Volver al ingreso" de la pantalla de éxito, pero ahí se frena: el paciente recién registrado tiene que loguearse una vez más (el registro no inicia sesión solo, eso ya era así desde antes de esta fase) y **ahí sí** vuelve derecho a la reserva. Es un paso más que para un paciente que ya tenía cuenta, pero no se perdió el link — y evita tener que auditar de nuevo todo el flujo de consentimiento/registro para asegurar que nada se rompiera.
- **`POST /turnos` crudo** sigue sin pasar por `TurnoReglasService` — confirmado otra vez que ninguna pantalla lo usa; no había manera de probarlo con una pantalla real, así que se dejó.
- **Página pública sin foto del profesional:** no hay un campo de foto en `Profesional` hoy; se muestra nombre + especialidad nada más, consistente con "no se inventan datos".
- **No se construyó el `POST /casos` retroactivo** para pacientes previos a la Fase 2 — sigue como propuesta sin construir (Paso 0, punto 2), es una decisión de negocio, no un commit.

## Fase 3a: seguridad y privacidad (puntos abiertos)

- **`TODO(JWT)` en toda la identidad nueva de esta fase**, igual que la Fase 2: `PUT /profesionales/{id}/link-publico` confía en `X-Usuario-Id` sin firmar — cualquiera que conozca o adivine el id de un profesional puede, hoy, apagarle (o, si es un id de administrador, prenderle) el link público a otro. Se cierra junto con el resto de `AutorizacionCasos`/`AutorizacionReporte` al mergear `videollamadas-seguras`.
- **La reserva pública hereda la autenticación débil de los pacientes, no la empeora:** `POST /public/profesionales/{slug}/reservar` confía en el `idPaciente` que manda el cliente (del mismo `localStorage` que usa toda la app hoy — no hay token, no hay verificación server-side de que ese `idPaciente` sea realmente quien dice ser). Esto **ya era así** para cualquier pedido de turno desde el dashboard del paciente; lo nuevo es que ahora también es alcanzable sin pasar primero por el dashboard, solo con un link público y un navegador con sesión guardada. No es una superficie nueva de ataque distinta, pero sí una forma más directa de llegar a la misma debilidad de siempre.
- **Mientras no hay JWT, la única barrera real del endpoint de reserva pública es el límite de pedidos por IP** (20/minuto) — no impide un script lento, solo un script rápido.
- **Nota para el día que esto se despliegue de verdad (pedida explícitamente):** `/reservar/{slug}` y `/public/profesionales/{slug}/*` son, a propósito, las primeras rutas de esta app pensadas para quedar expuestas detrás de un proxy en algún momento — de ahí los headers, el rate limit y el 404 idéntico. El resto de la app, mensajería incluida, sigue pensado para correr solo local; exponerlo necesitaría su propia revisión, que no es parte de esta fase.

## Fase 3a: pendientes

- **Fase 3b (Google Calendar):** explícitamente aparte, no se tocó nada relacionado.
- **El registro de un paciente nuevo no resume la reserva en un solo paso** (ver arriba) — si en algún momento se quiere resolver, el único tramo que falta es que `registro.html` inicie sesión sola después de un alta exitosa, lo que hoy no hace por diseño propio de antes de esta fase.
- **"Modalidad" no se puede distinguir por profesional** en el modelo actual (es siempre `"Virtual"`, un dato de `Turno`, no de `Profesional`) — si en el futuro se suman modalidades por profesional, la página pública y el endpoint de disponibilidad son los lugares para agregarla.
- **`POST /casos` retroactivo** para pacientes previos a la Fase 2, sigue como propuesta (Paso 0, punto 2).
- Todo lo que ya estaba pendiente de la Fase 1/2 (vocabulario de chips, entrenamiento del bot, reasignación de grupo) sigue igual, sin tocar en esta fase.

## Fase 3a: verificación

`dotnet build` limpio y batería completa en verde (122 tests, los 13 nuevos de esta fase incluidos) en cada commit. Recorrido Playwright + curl de punta a punta, con datos de prueba limpiados después de cada corrida (turnos, `Recordatorio`, `Notificacion`, y el slug/link de los profesionales de prueba vueltos a `NULL`/apagado):

- Profesional activa el link → la página pública muestra sus horarios libres → paciente sin sesión elige un horario → lo manda a loguearse con el horario colgado del link → vuelve derecho a la reserva y se confirma solo → el profesional lo ve pendiente y lo confirma desde su calendario real (RN-02 no bloqueó) → el horario deja de aparecer como libre en la disponibilidad pública.
- Pedir el mismo horario dos veces: la segunda da 409 ("Ese horario ya está ocupado").
- Slug inexistente y link apagado: mismo 404 en la página y en el endpoint de disponibilidad, confirmado con curl.
- `dias` fuera de 1-60 y no numérico: 400 con mensaje, confirmado con curl.
- Límite de pedidos por IP: 25 pedidos seguidos al endpoint de disponibilidad, el límite (20/min) se activa, 429 confirmado.
- Respuesta cruda del endpoint público inspeccionada a mano: solo `nombre`, `especialidad`, `horariosLibres` — cero ids, cero datos de terceros.
- Regresión general: agenda y horarios del profesional, Mis pacientes, Mis casos, perfil; Usuarios, Casos, Derivaciones y Auditoría del administrador; bandeja de CRM > Chats — todo sin errores de consola nuevos.
- Capturas en `docs/capturas-cambios-agus/fase3a/` (no versionadas, igual que las de la Fase 1 y 2).

No hubo que revertir nada en esta fase; las dos migraciones quedaron aplicadas y en sync con el código.

## Demo y JWT

- **Inicio:** martes 2026-10-06, 12:10:52.
- **Hora límite:** 16:25:52 (inicio + 4 h 15 min). A esa hora se hace el Cierre, pase lo que pase.
- Plan: Parte 1 (kit de demo, en `Cambios-Agus`) primero y completo; Parte 2 (JWT, en `Cambios-Agus-jwt`) después, cortando si a la hora límite le quedan menos de 60 minutos y JWT no está verificado de punta a punta.
- **Terminado:** 13:22, el mismo martes 2026-10-06 — con margen de sobra respecto a la hora límite (16:25:52), las dos partes completas y verificadas. Tags locales: `demo-estable` (`Cambios-Agus`) y `demo-jwt` (`Cambios-Agus-jwt`, último commit `e6d51c5`).

### Parte 1: kit de demo

`docs/DEMO.md` (cómo levantar todo, URLs, usuarios de prueba, guion de 10-15 min, Plan B), `docs/datos-demo.sql` + `docs/reset-demo.sql` (caso de prueba en la cola + conversación escalada, sin ejecutar) y `docs/credenciales-demo.sql` (sin ejecutar — ver más abajo). Capturas de respaldo en `docs/capturas-cambios-agus/demo/`.

**Contraseñas de los usuarios de prueba:** no hay forma de leer una ya cargada (están hasheadas). Intenté comparar los hashes de la base contra una lista de contraseñas comunes por SQL de solo lectura para ver si alguna coincidía — Claude Code lo bloqueó solo, clasificándolo como "exploración de credenciales", y tiene razón: es exactamente el patrón de un intento de fuerza bruta, aunque la intención acá fuera encontrar una contraseña de demo ya mía. No insistí por otro camino. Quedó `docs/credenciales-demo.sql` (sin ejecutar) como alternativa: le pone la que elijas al ejecutarlo a `admin`/`paciente1`/`smanto` si decidís aplicarlo.

**Un hallazgo al armar el guion, antes de escribirlo mal:** iba a decir que `paciente1` (Juan García) "ya tiene turnos" — es falso. Los 3 turnos futuros que hay en la base (1009, 1014, 2007) son de dos pacientes sin cuenta de login (Camila Vega y Emiliano Salas), no de `paciente1`. Por eso el guion quedó reordenado: la página pública de reserva (que sí crea un turno para `paciente1`) va **antes** del bloque del paciente, no después — así "Mis turnos" tiene algo para mostrar en vez de arrancar vacío.

**Tropiezo anotado, no corregido:** `GET /pacientes/{id}/proximo-turno` devuelve 404 (comportamiento correcto: no hay turno próximo) cuando un paciente no tiene ninguno, pero el frontend de "Mis turnos" lo deja como un error de red en la consola en vez de manejarlo como un estado vacío normal. No es nuevo de esta rama ni rompe la pantalla visualmente (no se corrigió: no estaba claro en el tiempo disponible si tocarlo podía afectar otro lugar que también llama a ese endpoint).

Batería completa (122 tests) en verde antes de empezar y de nuevo antes de este commit. `git status` limpio salvo los archivos nuevos/`CAMBIOS_AGUS.md`.

### Parte 2: JWT — Paso J0 (reconocimiento)

Rama `Cambios-Agus-jwt`, creada desde `Cambios-Agus` en el commit del kit de demo.

- **Login hoy** (`POST /login`, Program.cs): username + SHA-256 del password (sin salt) comparado contra `Usuario.PasswordHash`; bloqueo a los 3 intentos fallidos por 15 minutos (`UsuarioDAO.RegistrarIntentoFallido`); si entra, resuelve el rol mirando si el `IdUsuario` está en `UsuarioPaciente` o `UsuarioProfesional` (si no está en ninguna, es `configurador`) y devuelve `{ rol, idUsuario, idPaciente|idProfesional }`. El frontend (`PANTALLAS/index.html`) guarda esos tres valores sueltos en `localStorage`, sin firmar nada.
- **Cómo se lee la identidad hoy, en todo el backend:** el header `X-Usuario-Id` (23 lugares entre `Program.cs` y `Endpoints/*.cs` — `AutorizacionCasos`/`AutorizacionReporte` de la Fase 2/3a son los únicos que lo resuelven con una función común; el resto lo parsea suelto, `int.TryParse(...)`, en cada endpoint), más ids sueltos en la URL sin ninguna verificación de dueño (`/pacientes/{id}/...`, `/profesionales/{id}/...`, el propio `/turnos/{id}/cancelar` que la Fase 3a ya separó por rol pero no por identidad verificada). 164 rutas registradas solo en `Program.cs`, sin contar `Endpoints/*.cs`.
- **`videollamadas-seguras` ya tiene su propio JWT** (`git show origin/videollamadas-seguras:Services/JwtService.cs`, de solo lectura): HS256 hecho a mano (no el paquete `System.IdentityModel.Tokens.Jwt`), claims propios `idUsuario`/`rol` (no los `ClaimTypes` estándar), configuración `Jwt:Secret`/`Jwt:Issuer`, expiración de 8 horas para el login — coincide en las 8 horas con lo que pide este prompt, pero la implementación es otra. Esta rama va a usar el paquete oficial (`Microsoft.AspNetCore.Authentication.JwtBearer`) como pide el prompt, con `ClaimTypes` estándar y `Jwt:Key` — **es una reconciliación pendiente para cuando se mergee con `videollamadas-seguras`**, no algo que se resuelva acá. Lo mínimo para no chocar en los nombres: evitar la clave de configuración `Jwt:Secret` (usar `Jwt:Key`, distinta a propósito) para que las dos puedan convivir en el mismo `appsettings`/user-secrets sin pisarse mientras no estén mergeadas.

**Plan de los pasos siguientes:** paquete + `user-secrets` + emisión en los 3 logins + función única de identidad + flag `Auth:PermitirEncabezadoLegacy` (J1); proteger por grupos en el orden del prompt, parando si hace falta en el grupo 3 (J2); tests + Playwright + docs (J3). Sigo sin esperar respuesta.

### Paso J1: infraestructura (resultado)

Se armó con una diferencia respecto al diseño original: en vez de que `IdentidadActualService` leyera el header legacy ella misma, el fallback se resolvió **a nivel de esquema de autenticación** (`LegacyHeaderAuthHandler` + `AddPolicyScheme`, que elige JWT si hay `Bearer` o el esquema legacy si no). Motivo: así `IdentidadActualService` termina siendo una lectura simple de claims ya resueltos (sin importar cuál esquema autenticó), y los endpoints que solo necesitan un rol pueden usar `RequireAuthorization(p => p.RequireRole(...))` directo, sin tener que inyectar nada ni escribir el chequeo a mano — mucho menos código para repetir en 60+ endpoints.

- `Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.9 (la versión más nueva que bajó `dotnet add package` sin fijar la versión era para net10.0, incompatible con este proyecto — hubo que fijar `--version 9.0.9` a mano).
- `Jwt:Key`: generada con `RNGCryptoServiceProvider` (no `RandomNumberGenerator.GetBytes` — no existe en el PowerShell 5.1 de este entorno) y guardada con `dotnet user-secrets set` sin que el valor pasara nunca por una salida visible de ninguna herramienta.
- `JwtEmisorService`/`JwtConfig.ClaveOFallar`: mensaje claro si falta la clave o tiene menos de 32 caracteres; se fuerza la resolución del singleton apenas arranca la app (antes de `UseCors`), así el error aparece al levantar, no en el primer login.
- Frontend: `index.html` guarda `data.token`; `YS.fetch` lo manda como `Authorization: Bearer` (al lado del `X-Usuario-Id` de siempre — no se quitó, no hacía falta para esto y sacarlo era un riesgo aparte). El manejo de 401 (`YS.sesion.expirada()`) ya estaba escrito de antes, pensado exactamente para esto.

### Paso J2: grupos protegidos (resultado)

Los 5 grupos del prompt, completados en este orden, con un alcance más chico que "ideal" en los últimos dos (documentado en cada commit y en el `README.md`):

1. **Mensajería:** rol Profesional o Configurador en todo el grupo; `AllowAnonymous` explícito en las dos rutas que simulan un mensaje entrante del paciente (son del lado de afuera, como un webhook — protegerlas habría roto el simulador de chat, que además es parte del guion de la demo).
2. **Consentimiento:** firmar exige rol Paciente + que el id de la URL sea el del propio token.
3. **Administración:** usuarios, auditoría, catálogos (solo las escrituras), solicitudes de registro y de cambio, "Solicitudes de cambio"/`Derivacion` completo — todo Configurador. `AutorizacionCasos.Resolver` (Fase 2/3a) se reescribió sobre `IdentidadActualService` en vez de tocar cada uno de sus ~20 call sites: **encontró y cerró dos huecos reales** que tenía desde la Fase 2 — sin ninguna identidad resuelta, o con un paciente (que no está en `UsuarioProfesional`), el código devolvía "administrador" por default. Confirmado con curl que una request sin ningún header entraba a `/casos` con acceso total antes de este fix.
4. **Turnos:** dueño verificado al ver (`GET /turnos/{id}`) y cancelar — antes cualquier profesional podía cancelar el turno de cualquier otro, sin ningún chequeo. `/profesionales/{id}/agenda` quedó sin tocar a propósito (lo necesita cualquier paciente para ver horarios de cualquier profesional al pedir turno — no es dato sensible, son horarios de trabajo).
5. **Historia clínica/evoluciones/eventos de crisis/sesiones clínicas:** lo más sensible de toda la app estaba completamente abierto. Mínimo seguro aplicado: rol Profesional o Configurador (confirmado que ningún paciente los llama). **No se llegó** al filtro más fino de "solo el profesional asociado a ese paciente" — alcance más amplio que `Casos` (que sí filtra por asignación), anotado como pendiente.

**Bug propio encontrado y corregido durante la verificación, no durante la escritura:** 19 lugares (14 en `Program.cs`, 4 en `CasosEndpoints.cs`, 1 en `MensajeriaEndpoints.cs`) seguían armando el `IdUsuario` de `LogAuditoria` leyendo `X-Usuario-Id` directo del header, en vez de `IdentidadActualService` — exactamente lo que pedía el Paso J1 ("esto también reemplaza a la auditoría actual") y quedó sin hacer en su momento. Se encontró probando el link público con un token puro, sin el header legacy: tiraba una `SqlException` (violación de FK, `IdUsuario=0` no existe) en vez de guardar la auditoría. Con el navegador real nunca se iba a notar (`YS.fetch` manda los dos headers), pero cualquier llamada que mandara solo el `Bearer` rompía cualquier endpoint que auditara algo. Corregido en los 19 lugares con el mismo patrón de una línea, sin tocar ninguna firma de método.

**Grupos 6 (públicos) confirmados sin tocar:** login, registro de paciente, registro de profesional, recuperación de contraseña, `/reservar/{slug}` + su disponibilidad pública (Fase 3a), archivos estáticos — los seis responden igual con o sin token, confirmado con curl después de poner `Auth:PermitirEncabezadoLegacy` en `false`.

**Lo que quedó en modo legacy, sin restricción nueva (anotado, no es peor que antes):** `/agendas` (alta/baja/edición de franjas), la coincidencia idPaciente/idProfesional del cuerpo en `/turnos/solicitar` y `/turnos/profesional-solicitar`, todo `/informes-admision*`, y el filtro fino de historia clínica por paciente asociado.

### Paso J3: verificación (resultado)

- **7 tests xUnit** (`JwtTests.cs`): claims correctos por rol, ~8 horas de expiración, firma alterada y token vencido no pasan validación (`SecurityTokenSignatureKeyNotFoundException`/`SecurityTokenExpiredException`), mensaje claro si falta la clave o es muy corta. No se agregó un harness de `WebApplicationFactory` (no existía ninguno en el proyecto, y el resto de los endpoints de esta rama tampoco se probaron así) — en su lugar, la verificación de los endpoints protegidos se hizo con curl y tokens reales, igual que el resto de esta rama viene verificando fases anteriores.
- **Tokens reales para verificar**, generados con la clave real de `user-secrets` desde un proyecto de consola chico y descartable (no comiteado) — nunca se imprimió la clave en sí, solo tokens firmados con ella (igual que hace la app en cada login real).
- **Verificado con curl, con la clave real:** JWT puro (sin `X-Usuario-Id`) por cada rol anda y respeta roles (configurador 200, profesional 403 en `/usuarios`); token vencido → 401; firma alterada (un carácter cambiado) → 401; `X-Usuario-Id` solo, sin token, con `Auth:PermitirEncabezadoLegacy` en `false` → 401; paciente con su propio token sobre el turno de otro paciente → 403; endpoints públicos → sin cambios.
- **Verificado con Playwright, con `Auth:PermitirEncabezadoLegacy` en `false` (el valor por defecto ya, no una variable de entorno) y tokens reales en `localStorage`, sin ningún `X-Usuario-Id`:** panel de administrador (inicio, usuarios, casos, auditoría, grupos), panel de profesional (agenda, horarios, pacientes, casos, perfil), panel de paciente (turnos) — 11/11 pantallas sin errores de consola nuevos. Pedir un turno y cancelarlo con un token real de punta a punta. Abrir la historia clínica de un paciente propio desde el panel del profesional. Sin ninguna sesión, abrir `/nuevo_dashboard.html` directo rebota al login. Cerrar sesión borra el token de `localStorage` y vuelve al login.
- **`dotnet build` limpio y 129 tests en verde** (122 de antes + 7 nuevos) en cada commit.
- Datos de prueba de cada verificación (turnos, `Recordatorio`, `Notificacion`, slug/link de prueba) limpiados y confirmados en 0 filas después de cada corrida.

## Parte 2: JWT — cierre

- **Tag local `demo-jwt`** en el último commit de esta rama (ver más abajo si se decidió ponerlo).
- **Qué configurar para que esto arranque en otra máquina:** `dotnet user-secrets set "Jwt:Key" "<algo de 32+ caracteres>"` — sin esto la app no levanta, con un mensaje que explica exactamente este paso.
- **Qué está protegido y qué no:** ver la tabla nueva en el `README.md`, sección "Autenticación con JWT".
- **Abierto a propósito, sin JWT:** login, registros, recuperación de contraseña, la superficie pública de la Fase 3a (`/reservar/{slug}` y su disponibilidad), archivos estáticos.
- **Pendiente explícito:** filtro fino de historia clínica por paciente asociado (hoy: cualquier profesional autenticado, no cualquiera), `/agendas` CRUD, coincidencia de cuerpo en la creación de turnos, `/informes-admision*`, y la reconciliación con el `JwtService` de `videollamadas-seguras` (dos implementaciones distintas, sin coordinarse, ver Paso J0). El `signaling-server` de Node sigue sin autenticar nada — no estaba en el alcance de esta rama.

## Limpieza previa al push (auditoría de secretos + reconciliación)

Una auditoría de seguridad (solo lectura, antes de pushear) encontró que `docs/credenciales-demo.sql` tenía la contraseña de demo elegida ese día, en texto plano, y que esta rama reintroducía un bug que `fix-registro-publico` (rama aparte, un día antes) ya había resuelto distinto: el registro público de un profesional (sin sesión) volvía a tirar 500 porque la auditoría intentaba guardar `IdUsuario = 0`, que no existe en `Usuario`.

**Contraseña de demo:** se sacó del historial completo de `Cambios-Agus-jwt` (reescribiendo los 118 commits del rango `origin/main..Cambios-Agus-jwt` con `git filter-branch --tree-filter`, nunca tocando `main` ni ninguna otra rama) — `docs/credenciales-demo.sql` ahora declara `@Clave` sin valor por defecto real (`'<ELEGIR-ANTES-DE-EJECUTAR>'`) y corta con `THROW` si no se completa antes de ejecutar. `backup-pre-limpieza` (rama local, nunca se pushea) guarda la historia vieja por si hace falta algo de ahí.

**El bug `IdUsuario = 0`:** se resolvió mergeando `fix-registro-publico` dentro de `Cambios-Agus-jwt` (no al revés) y combinando las dos soluciones en el único conflicto real (`Program.cs`, 12 de los ~14 lugares que leían el auditor): `LogAuditoria.IdUsuario` nullable (modelo + DAO + `migrations/alter_logauditoria_idusuario_nullable.sql`, igual que proponía esa rama) **y** la identidad resuelta vía `IdentidadActualService` (en vez del `X-Usuario-Id` crudo que leía esa rama) — sin `?? 0`, pasando `null` cuando no hay identidad, como corresponde ahora que `IdUsuario` admite NULL. Los otros 2 lugares que mi propio trabajo de hoy había agregado (el link público de la Fase 3a) no aparecían en esa rama y por lo tanto no generaron conflicto de git, pero necesitaban el mismo arreglo — se corrigieron igual, a mano, después del merge.

De paso, `SincronizarCasoPorCambioDeProfesional` (la sincronización de `Caso`/`HistorialAsignacion` cuando se acepta una "Solicitud de cambio") tenía su propio `idUsuario == 0 ? null : idUsuario` — el mismo patrón de "0 como null disimulado" que se estaba limpiando en todos lados. Pasó a recibir `int?` directo, sin la traducción.

Verificado: los 130 tests en verde (los 129 de antes + el test de integración de `fix-registro-publico`, que levanta la app entera con `WebApplicationFactory<Program>`), reproducido a mano el 500 original contra la app corriendo (ahora da 201 y la auditoría queda con `IdUsuario = NULL`, confirmado contra la base y limpiado después), y repetido el recorrido de JWT puro (Playwright, sin ningún header legacy) para confirmar que el merge no rompió nada de lo de hoy.

## Mejora de chats y centrado

### Paso 0: reconocimiento

Rama `mejora-chats-y-centrado`, parte de `Cambios-Agus-jwt` (no existía `cierre-seguridad`). `rediseno-visual` ya es ancestro de `Cambios-Agus-jwt` desde el 2026-09-25 — `shared/tema.css` y el sistema de diseño compartido ya están, no hace falta mergear nada aparte.

**Chats (`/mensajeria`, `Endpoints/MensajeriaEndpoints.cs`):**
- Un solo archivo de UI: `PANTALLAS/Pantalla_ChatSimulado/js/bandeja.js` (~1071 líneas: bandeja + hilo + panel de contexto), cargado como cajón superpuesto por `shared/shell.js` (`cargarBandeja`) **solo para Configurador** — el backend ya autoriza a Profesional pero el botón Chats no está enganchado para ese rol todavía. Se engancha en este trabajo.
- Tiempo real: polling adaptativo ya existente (`bandeja.js`, backoff 3-12s / 20-45s según haya hilo abierto o no), sin WebSockets. No se reemplaza, solo se arregla lo que falle.
- Estados reales (`Services/Mensajeria/ContextoBot.cs`): `Activa` (bot) / `DerivadaHumano` (escalada) / `Urgente` / `Cerrada`. Los filtros que ya existen en la bandeja (Todas / No leídas / Escaladas a persona / Del bot / Cerradas) son casi los pedidos; falta "Mías".
- `Conversacion` no tiene columna de asignación propia. Decisión: **no se inventa una asignación paralela.** "Asignar/reasignar conversación" = resolver el Caso abierto del paciente de esa conversación (`CasoDAO.ObtenerAbiertoPorPaciente`) y llamar al `AsignacionService.ReasignarManual` que ya usan los Casos — mismas categorías de motivo (`MotivoReasignacion.Categorias`), misma nota opcional (máx. 140, nunca a auditoría), mismo gate ("solo el administrador puede reasignar", igual que `PUT /casos/{id}/reasignar`). Si el paciente no tiene caso abierto (o la conversación todavía no tiene paciente identificado), la acción queda deshabilitada con un texto que lo explica — no se crea un caso desde el chat, no se pidió.
- Decisión explícita, para no agrandar el alcance: **no se agrega chequeo de pertenencia para leer/responder/cerrar conversaciones.** Hoy cualquier Profesional o Configurador ve y opera cualquier conversación — parece ser el diseño real (bandeja compartida tipo mesa de entradas: cualquiera del equipo atiende lo que está escalado), no un bug. "Mías" es un filtro de comodidad (comparar el profesional asignado al caso del paciente contra el usuario logueado), no un límite de acceso nuevo. Si esto fuera un error y no la intención, avisar para tratarlo aparte — restringirlo ahora sin que se haya pedido sería alcance nuevo.
- `NotificacionInterna` (tabla ya existente) es otra cosa (alertas para el equipo), no las "notas internas por conversación" que pide A3 — esas van en una tabla nueva.
- No existen hoy "respuestas rápidas" ni "etiquetas" de conversación — migraciones nuevas, idempotentes.

**Pantallas / migas de pan (`shared/shell.js`, `shared/tema.css`):**
- Las migas no dicen literalmente "Menú < X": son un `<ol class="ui-migas">`, armado en `marcar()`/`encabezado()` (`shell.js` ~271-332) como `etiqueta del rol / grupo (si hay) / pantalla`, separadas por "/" puesto en CSS (no es texto "<"). El resaltado del ítem activo (`data-id` + clase `.activo` / `aria-current`) es un mecanismo **separado** de las migas — se puede sacar la miga sin tocar el resaltado.
- El contenedor centrado ya existe: `.ui-contenido` + variable `--contenido-max`, pero el valor por defecto es `none` ("ancho completo en las pantallas principales" — decisión explícita anterior, de la rama `rediseno-visual`). Este pedido la revierte a propósito: paso el default a un ancho cómodo (~1200px) y mantengo `none` solo donde se pidió explícitamente (sala de video, cajón de Chats).
- Pendiente de revisar aparte, no es de este frente: `Pantalla_Profesional/dashboard.html` (legacy, sin referencias fuera de un changelog) y la copia sin uso `consentimiento_crear_cuenta.html` (ya documentados más arriba en este archivo; nada que hacer ahora).

**Herramientas:** sin servidor MCP de Playwright en esta sesión. Al revisar qué se usó antes, `docs/pruebas-ui-cambios-agus/` ya tiene un arnés propio (Python + Playwright, Edge instalado vía `channel="msedge"`, sin descargar nada) con `comun.py`/`correr.py` y un script por paso — se sigue usando ese en vez de armar uno nuevo. (Un intento inicial con el paquete `playwright` de Node en el scratchpad de la sesión quedó sin usar, al encontrar el de Python.)

**Arreglo necesario al arnés antes de poder usarlo:** `comun.py` simulaba la sesión escribiendo solo `idUsuario`/`rol`/etc. en `localStorage`, como dejaba el login antes de JWT. Con `Auth:PermitirEncabezadoLegacy=false` (cierre de la Parte 2, rama madre), eso ya no alcanza — todo pedido a un endpoint protegido da 401 sin un Bearer real. Se agregó `YoSoy.Tests/_MintTokensPlaywright.cs` (herramienta de desarrollo, no una prueba real) que levanta `WebApplicationFactory<Program>` — con el `Jwt:Key` real de los user-secrets de esta máquina, igual que usa `dotnet run` — y emite un token por rol para los mismos 3 usuarios de prueba que ya usaba `comun.py`. Los tokens quedan en `docs/pruebas-ui-cambios-agus/tokens.json` (gitignored, no se versiona; duran 8 horas, se regeneran con `dotnet test --filter _MintTokensPlaywright`). `comun.py` los lee y los suma a la sesión simulada.

**Nota sobre anchos angostos:** la memoria de este proyecto decía no invertir en breakpoints angostos (YoSoy es PC-first, hay una app mobile aparte para eso). Este pedido pide explícitamente revisar hasta ~480px. Se sigue el pedido nuevo tal cual — es específico y a propósito — pero vale la aclaración porque cambia un criterio anotado antes.

**Orden de ejecución:** A1 (bugs del chat) → A2 (diseño bandeja/hilo) → A3 (asignar, cerrar/reabrir, notas internas, respuestas rápidas, etiquetas) → A4 (panel del lead) → B (contenido centrado) → C (sin migas de pan) → verificación → informe final. Commits chicos en español en cada paso.

### Corrección al leer `shared/shell.js` completo

El párrafo de arriba decía "se engancha [Chats para Profesional] en este trabajo" como si fuera solo un cable suelto. No es así: `shell.js:551-553` tiene un comentario explícito — Chats es solo para Configurador **porque** la bandeja muestra las conversaciones de todos los pacientes, no porque falte enganchar un botón. Es una decisión de privacidad a propósito, no un bug. La resolución real queda más abajo, en "Chats para Profesional, correctamente acotado".

### Frente A1: bugs encontrados en Chats (recorrido con Playwright)

**Bug 1 — enlace directo a una sección del grupo CRM se pierde (`shared/shell.js`).** Al cargar de cero (refrescar, abrir en pestaña nueva, un link guardado) directo en `#chats`, `#escalamientos`, `#casos` o `#casosderivaciones`, la pantalla termina mostrando Inicio — sin aviso, con los datos de chats ya cargados en segundo plano pero invisibles. Causa: `ir()` corre de forma sincrónica apenas arranca el panel, antes de que `/capacidades` resuelva y agregue el grupo CRM al menú; como `buscar(id)` todavía no encuentra "chats", cae a `cfg.inicial` y `marcar()` hace `history.replaceState(..., "#inicio")`, **pisando la URL**. El reintento que ya existía para este caso releía `location.hash` — pero para cuando corre, el hash ya es `"#inicio"`, así que su condición nunca se cumple y el reintento no hace nada. Tampoco cubría `#casos`/`#casosderivaciones`, solo los dos ids más viejos. Arreglado guardando el pedido original (`leerHash()`) una sola vez, antes de que nada pueda reescribir la URL, y usando ese valor (no `location.hash`) en el reintento, generalizado a cualquier id de `GRUPO_CRM.items`. Verificado con los 4 deep links contra la app corriendo: los 4 quedan en la sección pedida, sin errores de consola.

**Chats para Profesional, correctamente acotado.** Al enganchar el cajón para Profesional (mismo cambio de archivo), **no** se reutilizó el camino de Configurador tal cual: `agregarGrupoCRM()` (y su reintento de deep link, que es un concepto de sección/hash) sigue siendo solo de Configurador; Profesional solo gana `cargarBandeja()`, que al no recibir `chats:{modo:"pagina",...}` arranca en modo cajón (el mismo mecanismo ya construido en `bandeja.js`, nunca antes enganchado a ningún rol real desde que Configurador pasó a tener su sección propia). Para que esto no reabra el problema de privacidad de la corrección de arriba: no se agrega una restricción dura de pertenencia en el backend (seguiría rompiendo escalamientos sin asignar todavía, que cualquiera del equipo tiene que poder atender). En cambio, el filtro "Mías" (ver más abajo) se usa como **default** al abrir la bandeja en rol Profesional — arranca viendo su propia cartera, no todo el mundo — con "Todas" disponible para cuando necesite entrar a algo escalado sin asignar. En Configurador "Mías" no tiene referente (no tiene cartera propia): el botón del filtro no se muestra para ese rol, arranca en "Todas" como hoy.

**Bug 2 — un mensaje nuevo se marcaba leído (y no avisaba) si Configurador estaba en otra pestaña del panel (`bandeja.js`).** `estaMirando()` y `avisarNuevos()` trataban `est.modo === "pagina"` como "siempre a la vista" — cierto en el viejo `Pantalla_ChatSimulado/index.html` de página completa, falso ahora que "pagina" vive dentro de una pestaña del CRM que `dashboard.js` tapa con `.oculto` (sin desmontarla) al mostrar otra sección. Resultado: con Chats abierto de fondo en una conversación y el usuario mirando, por ejemplo, Admisiones, un mensaje nuevo del paciente quedaba marcado como leído en el servidor sin que nadie lo viera — sin toast, sin sonido, sin contador. Confirmado en vivo (API del servidor, no solo el DOM): antes del arreglo, `noLeidos` volvía a 0 tras el pulso aunque la pestaña de Chats estuviera oculta; después, se queda en 1 (toast y contador incluidos) hasta que de verdad se la mira, momento en el que sí se marca leída. Arreglado con un chequeo de visibilidad real (`raiz.offsetParent !== null`, que sí distingue "oculto por CSS" de "mostrado") en los 4 lugares que usaban ese criterio, incluido el ritmo del polling (más lento si nadie mira, aunque eso no cambia corrección, solo tráfico).

**Bug 3 — "Abrir" desde el toast de un aviso no hacía nada visible en otra pestaña (`bandeja.js` + `dashboard.js`).** Mismo origen que el Bug 2 (modo "pagina" dentro de una pestaña ajena), pero del otro lado: `abrir(idConversacion)` en modo "pagina" solo llama a `seleccionar()` — no tiene ninguna forma de pedirle a `dashboard.js` que muestre la sección de Chats. Resultado: tocar "Abrir" en el toast de un mensaje nuevo mientras se estaba en otra pestaña (por ejemplo Admisiones) seleccionaba la conversación **detrás** de lo que ya se veía, sin cambiar la pantalla ni la URL — un clic que no parece hacer nada. Arreglado con un evento (`ys:chats-ir-a-pagina`, mismo patrón que `ys:shell-listo`/`ys:chats-resumen` ya usados en el código): `bandeja.js` lo dispara sin saber nada de pestañas, `dashboard.js` lo escucha y navega a `#chats` si no se estaba ya ahí o en `#escalamientos`. Verificado en vivo: antes, el hash y la sección quedaban sin cambios; después, navega a Chats y se ve el mensaje. (Nota aparte: el primer intento de aplicar el parche a `dashboard.js` con un script de PowerShell corrompió las tildes del comentario nuevo — ver [[yosoy-finales-de-linea-mixtos]], agregado ahí el motivo exacto. Se corrigió antes de commitear.)

**Bug 4 — los atajos de teclado de Chats se apagaban para siempre apenas se abría cualquier modal viejo de Usuarios/Solicitudes (`bandeja.js`).** `teclasGlobales()` usaba `document.querySelector(".ui-modal-fondo")` para no robarle Esc a un modal abierto — razonable para `YS.modal` (shared `ui.js`), que al cerrar hace `fondo.remove()`. Pero dashboard.js tiene modales más viejos (Usuarios, Solicitudes de cambio) que reusan esa misma clase por el estilo y, al cerrarse, solo la tapan con `display:none` en vez de sacarla del DOM: `querySelector` la encuentra igual, abierta o no. En la práctica, apenas uno de esos modales existe en la página (aparecieron incluso sin haberlos abierto a propósito, en pruebas que ni tocaron Usuarios), `?`, `/` y las flechas de Chats dejan de responder **el resto de la sesión**, sin ningún aviso. Arreglado revisando que alguno de los `.ui-modal-fondo` que haya esté de verdad visible (`getComputedStyle().display !== "none"`), no solo que exista. No se tocó el modal viejo de Usuarios/Solicitudes (dejar de usar `display:none` ahí es un cambio más grande, para otro momento si hace falta).

Recorrido completo de Playwright por los tres roles (paciente ↔ bot ↔ escalada a persona ↔ profesional), no solo los 4 bugs de arriba: `p3_chats.py` (Configurador, 57/57), `p9_chats_profesional.py` (Profesional — el cajón, nunca antes probado con un rol real: botón, abrir/cerrar/expandir, Esc, "Mías" por default, panel sin nada clínico, responder, 16/16).

### Filtro "Mías" (cierre de A1 para Profesional + parte de A2)

`ConversacionResumen` suma `IdProfesionalAsignado`/`NombreProfesionalAsignado`, calculado en `ConversacionDAO.ListarResumen` con un `OUTER APPLY` al Caso abierto del paciente (no hay columna propia en `Conversacion` ni asignación nueva — reusa lo que ya existe, igual que A3 va a reusar `AsignacionService` para el botón de reasignar). En `bandeja.js`, "Mías" es un toggle aparte de `FILTROS` (se puede combinar con cualquier estado, no es excluyente) y solo existe para Profesional — para Configurador no tiene referente (sin cartera propia) y no se renderiza. Arranca prendido para Profesional (ve su cartera al abrir, no la bandeja entera) y apagado/inexistente para Configurador.

Verificado: con datos reales, ningún paciente de prueba tiene todavía un Caso abierto (Fase 2 no se usó sobre estos), así que no hay ejemplo visual real con dueño — se verificó la lógica con dos conversaciones de prueba interceptadas por red (`page.route`, sin tocar la base): con "Mías" prendido, de 12 conversaciones visibles (10 reales + 2 de prueba) solo pasa la que tiene `idProfesionalAsignado` igual al profesional logueado; apagado, las 12.

### Frente A3: backend (asignar, reabrir, notas, etiquetas, respuestas rápidas)

Migración nueva `migrations/add_chats_notas_etiquetas_respuestas.sql` (idempotente, corrida dos veces local): `NotaInternaConversacion` (texto nunca a auditoría), `EtiquetaConversacion` + `ConversacionEtiqueta` (catálogo chico, ABM tan abierto como `PreguntaFrecuente` — sin restricción de rol aparte del grupo `/mensajeria`), `RespuestaRapida` (`IdProfesional` NULL = global).

- **Asignar** (`PUT /mensajeria/conversaciones/{id}/asignar`): resuelve el Caso abierto del paciente y llama a `AsignacionService.ReasignarManual` — mismo gate que `/casos/{id}/reasignar` (solo Configurador, 403 a Profesional). Sin Caso abierto (hoy, ningún paciente de prueba tiene uno — tampoco hay Grupos activos en esta base) da 400 explicando por qué, no crea nada.
- **Reabrir** (`PUT /mensajeria/conversaciones/{id}/reabrir`): vuelve a `DerivadaHumano` (no al bot — la reabrió el equipo a propósito), 400 si no estaba Cerrada.
- **Notas**: GET/POST abiertos al grupo; DELETE solo para el autor o Configurador (403 para cualquier otro, incluido un paciente sin ese rol siquiera — 403 de `RequireRole`, ni llega al handler).
- **Respuestas rápidas**: Configurador solo puede crear/editar/borrar las globales (`IdProfesional` NULL); Profesional solo las propias — ninguno puede tocar las del otro (403 cruzado verificado en los dos sentidos).

Verificado con `docs/pruebas-ui-cambios-agus/p10_chats_a3.py` (API pura, matriz de rol): 27/27 — sin token (401), rol equivocado (403), recurso ajeno (403), inexistente (404), y el camino correcto (200/201/204) para cada endpoint nuevo. No se armó un Caso+Grupo de prueba solo para probar el camino feliz de "asignar" (no hay ninguno activo en esta base y `AsignacionService.ReasignarManual` ya tiene su propia batería de tests — lo nuevo acá es solo la resolución del Caso a partir de la conversación, que sí está cubierta). Filas de prueba (1 etiqueta, 2 respuestas, dieron de baja lógica como corresponde) borradas a mano después; las 3 tablas nuevas quedaron en 0 filas.

### Frente A3: interfaz (reabrir, asignar, etiquetas, notas, respuestas rápidas)

- **Reabrir**: botón en la cabecera del hilo cuando el estado es Cerrada, vuelve a "Escalada a persona" (no al bot).
- **Asignar/Reasignar**: botón en el panel de contexto, solo para Configurador, al lado del dato "Caso". Sin caso abierto queda deshabilitado con el motivo en el `title` (no hace nada raro, no inventa un caso). El modal reusa `/casos/motivos-reasignacion` — la misma lista cerrada que ya usa la reasignación de casos, no una nueva.
- **Etiquetas**: chips en el panel de contexto y en cada fila de la bandeja; un picker (checkboxes sobre el catálogo) para poner/sacar. **Bug encontrado y arreglado en el camino**: el pulso incremental de la bandeja (`ListarResumen`) solo trae conversaciones con un `MensajeChat` nuevo — poner o sacar una etiqueta no genera uno, así que la UI quedaba mostrando el estado viejo hasta la próxima vuelta completa (cada 60 s). Se corrige actualizando `est.convs` en el momento, ya que la propia respuesta 204 confirma que el servidor quedó así. El catálogo en sí se vuelve a pedir cada vez que se abre el picker (no alcanza con el caché de `cargarContexto`, que solo se carga una vez por sesión) y una vez más, sin bloquear, al iniciar la bandeja (para que las filas ya tengan chip desde el primer dibujo). Pendiente, con costo más alto y no bloqueante (la consigna permite dejarlo si el costo es alto): un filtro por etiqueta en la bandeja (hoy "filtrable" solo quiere decir que el chip ya se ve en cada fila) y un ABM del catálogo con interfaz propia (hoy se administra por API; lo prueba `p11_chats_a3_ui.py` así, y sirve igual porque el picker ya lista lo que haya).
- **Notas internas**: sección en el panel de contexto, debajo de "General" — lista + textarea para agregar. Igual que pide la consigna: nunca al paciente, nunca a auditoría, solo el autor o Configurador puede borrar una (ya reforzado del lado del servidor, ver A3 backend).
- **Respuestas rápidas**: siguen funcionando igual en el composer (`/`, flechas, Enter) pero ahora salen de `/mensajeria/respuestas-rapidas` en vez de `plantillas.json`. Nuevo: "Administrar respuestas rápidas" al pie del desplegable, abre un modal con alta/edición/baja; Configurador solo ve/toca las globales (las únicas que le devuelve su propio `GET`), Profesional ve globales + las propias y solo puede tocar las propias — ya aplicado del lado del servidor, la interfaz no agrega ni necesita una restricción aparte.
  - **Encontrado en la verificación final, no en la primera pasada:** al cambiar la fuente de datos, la base arranca vacía — las 6 respuestas que ya traía `plantillas.json` (Saludo, Ya lo estamos viendo, Pedir el DNI, Volver al asistente, Consentimiento pendiente, Despedida) habían dejado de existir en la práctica, aunque el código estuviera bien. Se notó porque `p3_chats.py` (que no toca respuestas rápidas a propósito, solo las usa) pasó de 57/57 a 55/57 al re-correr toda la batería junta. Arreglado agregando el seed de esas 6, como globales, al final de la misma migración (`IF NOT EXISTS (... WHERE IdProfesional IS NULL)` — no por título, para no reponerlas si alguien las borra todas a propósito más adelante; es contenido de arranque, no un dato de negocio). `plantillas.json` queda igual, sin usarse, como referencia de qué decía cada una.

Verificado con `docs/pruebas-ui-cambios-agus/p11_chats_a3_ui.py`: 14/14 contra la app corriendo, incluida la reproducción en vivo del bug del pulso incremental (antes/después del arreglo). Filas de prueba borradas a mano después de cada corrida; las 3 tablas nuevas, en 0.

### Frente A4: panel del lead — qué ya estaba y la única decisión nueva

Al leer `cargarContexto()`/`dibujarContexto()` completo (Paso 0), la mayor parte de A4 ya existía de antes: datos administrativos del paciente, caso vigente con link, profesional/grupo asignado, próximo turno, estado de consentimiento (RN-02), nada clínico, y colapsable. Lo que sumó este frente: el botón Asignar/Reasignar (A3) y la corrección de que dejara de forzarse abierto (A1, Bug adyacente). "Próximo turno" muestra uno solo, no un historial de "últimos turnos" — el botón "Ver turnos del paciente" ya cubre eso (filtra la tabla completa); no se agregó una lista corta duplicada al lado, por la misma regla de "no lógica nueva duplicada".

**Decisión:** "datos editables donde ya existan reglas" se resuelve con el link "Ver ficha" que ya estaba (navega a Usuarios con el paciente elegido) en vez de un formulario in-line nuevo: ese formulario — con sus validaciones y su relación con el consentimiento — ya existe ahí, y duplicarlo en el panel de contexto sería exactamente la lógica repetida que la consigna pide evitar.

### Frente B: contenido centrado

El mecanismo ya existía (`--contenido-max` + `.ui-contenido`, de `rediseno-visual`) pero con default `none` a propósito — ver Paso 0. Se cambia a `1240px` (dentro del rango pedido) y se agrega `body.ui-pagina-ancha { --contenido-max: none; }`, puesta en las dos `sesion_video.html` (paciente y profesional) para que sigan a ancho completo. CRM/Chats no necesitó nada: no usa `.ui-contenido`, es una sección aparte (`#vistaChatsCRM`) que ya convive al lado.

Verificado con `docs/pruebas-ui-cambios-agus/p12_centrado.py` en los 3 roles × 1280/1440/1920 (ancho del contenedor ≤ 1241px, sin scroll horizontal) y × 900/480 (sin scroll horizontal) — 28/28 — más las dos excepciones (video, Chats) confirmadas a ancho completo. Capturas revisadas a mano: a 1920px una pantalla con tabla ancha (Turnos) queda con márgenes parejos de verdad (la tabla ya tenía su propio `.ui-tabla-scroll` con `overflow-x:auto`, no hizo falta tocarlo). El menú superior no se tocó: es una barra propia, nunca usó `.ui-contenido`, y ancharla igual que el contenido se vería raro para una barra de navegación.

### Frente C: sin migas de pan

Por el Paso 0: las migas no decían literalmente "Menú < X" (eran `etiqueta del rol / grupo / pantalla`, separadas por "/"), pero igual se sacan — es el mismo concepto de ruta visible que pide la consigna. Se saca `<ol class="ui-migas">` de `crearEncabezado()`, el armado del array en `marcar()` y el parámetro `migas` de `encabezado()` en `shared/shell.js` — era el único lugar de todo el código que las generaba (confirmado en el Paso 0). El resaltado del ítem activo (`data-id` + `.activo`/`aria-current`, incluido el botón del grupo en los desplegables) es un mecanismo aparte, que no se tocó. CSS muerto (`.ui-migas` y sus reglas hijas) sacado también de `tema.css`.

No se encontró ningún otro lugar con rutas/ids visibles: los botones "volver" que ya existían (registro, consentimiento, crear cuenta, "Volver al ingreso"/"Volver al inicio de sesión") ya tenían etiqueta humana, ninguno mostraba una ruta cruda — revisado con una búsqueda de "volver" en todo `PANTALLAS/*.html`, nada que cambiar ahí.

Verificado con `docs/pruebas-ui-cambios-agus/p13_sin_migas.py`: 18/18 en los 3 roles (sin `.ui-migas` en el DOM, título de pantalla presente, ítem de menú resaltado) más una pantalla de grupo (Clínico > Admisiones) para confirmar que el botón del grupo también se resalta.

## Verificación final

- **Build y batería completa:** `dotnet build` sin errores en cada paso de este trabajo; `dotnet test` en 136/136 al cierre (131 de antes + 5 nuevos en `YoSoy.Tests/ChatsGestionTests.cs`, permanentes, para los gates más sensibles de A3: asignar sin ser administrador, asignar sin token, reabrir una conversación no cerrada, borrar una nota ajena, respuesta rápida global/propia cruzada entre roles).
- **Matriz de pruebas por rol:** `docs/MATRIZ_ENDPOINTS.md` (nuevo — no existía antes de este trabajo, ver nota ahí sobre el alcance) documenta rol y pertenencia de todo `/mensajeria`, viejo y nuevo. Sin token → 401, rol equivocado → 403, recurso ajeno → 403, inexistente → 404, camino correcto → 200/201/204: probado para cada endpoint nuevo en `p10_chats_a3.py` (27/27) y los puntos más sensibles quedan además en `ChatsGestionTests.cs`.
- **Recorrido con Playwright, los tres roles:** `p3_chats.py` (Configurador, sección de Chats completa, 57/57), `p9_chats_profesional.py` (Profesional, el cajón — nunca antes probado con un rol real, 16/16), `p12_centrado.py` (contenido centrado en los 3 roles × 5 anchos, 28/28), `p13_sin_migas.py` (sin migas, resaltado del menú, en los 3 roles, 18/18). Paciente (el bot/simulador) se ejercita como parte del flujo completo en `p3_chats.py` y `p9_chats_profesional.py` (manda mensajes, recibe las respuestas del equipo, pide escalación). Consola sin errores nuevos en todas las corridas (los únicos 4xx esperados — `/pacientes/{id}/caso` cuando el paciente no tiene caso — están explícitamente en la lista de "ignorar" de cada script, con el motivo anotado).
- **Datos:** cada fila de prueba que se creó (conversaciones, mensajes, notas, etiquetas, respuestas rápidas) se borró después de verificar; confirmado con consultas a la base antes de cada commit relevante. Nunca se tocaron datos ya existentes.
- **Qué no llegó a esta vuelta** (con el motivo, no solo la lista): filtro por etiqueta en la bandeja y ABM del catálogo de etiquetas con interfaz propia (hoy por API — ver Frente A3 interfaz; la consigna permite dejarlo si el costo es alto); "últimos turnos" como lista aparte en el panel del lead (ya cubierto por el botón "Ver turnos del paciente", agregarlo hubiera sido lógica duplicada); capturas "antes/después" pedidas en `docs/capturas-cambios-agus/mejora-chats/` — las capturas de verificación de este trabajo quedaron en `docs/pruebas-ui-cambios-agus/capturas/` (la carpeta del arnés de pruebas, mismo criterio que usaba `Cambios-Agus`); si hace falta la carpeta específica con el par antes/después, es un paso aparte, pedirlo.

## Pertenencia de conversaciones (cierre del alcance abierto en A1)

### Paso 0: reconocimiento

**"Historia clínica" no tiene hoy una pertenencia que copiar.** El pedido dice "la misma lógica de pertenencia que ya usamos para la historia clínica", pero `Program.cs` (arriba de `/historiasclinicas`) tiene un comentario explícito: es Profesional/Configurador **sin filtrar por paciente asociado todavía**, con un TODO pendiente de antes de este trabajo (ver también "Parte 2: JWT — cierre", pendiente explícito). Leo el pedido como "el mismo *principio* que ya aplicamos bien en otro lado" — y el lugar donde SÍ está bien resuelto es Casos (`AutorizacionCasos`, `AsignacionService`), no historia clínica. Me guío por ese.

**Dos relaciones paciente-profesional ya existen, y no siempre coinciden:**
- `Caso.IdProfesionalAsignado` (vía `CasoDAO.ObtenerAbiertoPorPaciente`): el profesional del caso abierto. Es lo que usé la vuelta pasada para "Mías" y para el botón Asignar. **Problema real para esto:** hoy no hay ningún Caso activo en toda la base (Fase 2 no se usó todavía) — casi ninguna conversación real tendría dueño nunca si dependo de esto, y "Tomar" (Paso 3) quedaría roto para casi todo.
- `PacienteProfesional` (tabla N:M, `PacienteProfesionalDAO`): el equipo tratante vigente de un paciente — la usa la pantalla "Mis profesionales" del paciente y "Mis pacientes" del profesional. Se mantiene en par (`Desasignar(viejo)` + `Asignar(nuevo)`) en el camino de Derivaciones (`/derivaciones/{id}/aceptar`, `/derivaciones/cambio-directo`). **No así** en el camino de Casos (`AsignacionService.ConfirmarDerivacion`/`CorregirDerivacion`/`ReasignarManual`): ahí `SincronizarPacienteProfesional` solo agregaba (`Asignar(nuevo)`), nunca sacaba al anterior — **a propósito**, dice el comentario de `IAsignacionRepositorio`: "sacar a alguien ya no corresponde a este caso en particular sigue siendo una decisión de Solicitudes de cambio". No es un bug, es que Casos y "cambio de profesional formal" son conceptos separados ahí a propósito. Este pedido, sin embargo, exige explícitamente que la reasignación "mueva" la visibilidad sin dejar rastro para el anterior — una exigencia nueva que ese diseño original no necesitaba cubrir. Se extiende `SincronizarPacienteProfesional` para sacar al anterior también por este camino (mismo par que ya hace Derivaciones), documentando el cambio de alcance en el comentario de la interfaz en vez de borrar la explicación original.

**Decisión (usando lo que existe, con el ajuste mínimo que hace falta):**
- **Conversación `Activa` (bot, nunca escalada):** visible para quien esté en `PacienteProfesional` del paciente (+ Configurador). Es la relación de tratamiento vigente, ya mantenida aparte de cualquier Caso — no depende de que exista uno.
- **Conversación escalada (`DerivadaHumano`/`Urgente`) y asignada:** visible solo para quien la tiene asignada (+ Configurador). **No uso `Caso` para esto** — ver el problema de arriba. Columna nueva y mínima: `Conversacion.IdProfesionalAsignado INT NULL REFERENCES Profesional` — es soporte de almacenamiento para una acción explícitamente nueva en este pedido (Tomar/Devolver/reasignar la conversación), no una regla de autorización paralela: sigue resuelta por el mismo punto único de decisión, nunca por el cliente.
- **Conversación escalada y sin asignar:** visible para cualquier Profesional + Configurador (para que cualquiera la pueda tomar), tal como pide el Paso 1.
- El botón "Asignar/Reasignar" que agregué la vuelta pasada (sobre `Caso`, vía `AsignacionService`) pasa a operar sobre esta columna nueva en `Conversacion` en vez de `Caso` — unifica con "Tomar": las dos acciones leen/escriben el mismo dato, cambia quién puede llamarlas y la atomicidad, no el concepto. Ya no hace falta que el paciente tenga un Caso abierto para asignar su conversación (quedaba documentado como pendiente la vuelta pasada).
- Tomar/asignar una conversación **no** toca `PacienteProfesional` ni `Caso`: atender un escalamiento puntual no es lo mismo que pasar a ser el profesional tratante del paciente. Son decisiones separadas a propósito (la segunda sigue siendo Configurador, vía Casos/Derivaciones, sin cambios).

**Punto único de decisión:** clase nueva `Services/VisibilidadConversacion.cs`, mismo patrón que `AutorizacionCasos` (lee `IdentidadActualService`, no toma ids del cuerpo/URL sin contrastar). Expone `Puede(ctx, conversacion)` para un endpoint de un id, y una forma de filtrar una lista entera en una sola pasada (sin N consultas) para `/conversaciones` y el buscador — precalcula el `HashSet<int>` de pacientes del profesional (`PacienteProfesionalDAO.ObtenerIdPacientesDeProfesional`) una vez por pedido, no por fila.

**Dónde puede filtrarse/revelarse una conversación** (repaso completo de `MensajeriaEndpoints.cs` antes de tocar nada): `GET /conversaciones` (lista + contadores que arma el frontend a partir de esto), `GET /conversaciones/{id}`, `GET /conversaciones/{id}/paciente`, `GET /conversaciones/{id}/mensajes`, `PUT .../leida`, `POST .../responder`, `PUT .../cerrar`, `PUT .../reabrir`, `PUT .../asignar` (pasa a tomar/devolver también), `GET/POST/DELETE .../notas*`, `POST/DELETE .../etiquetas/{idEtiqueta}` (poner/sacar en una conversación — el catálogo en sí, `GET/POST/PUT/DELETE /mensajeria/etiquetas`, no revela nada de ningún paciente, sigue sin pertenencia), `GET /respuestas-rapidas` (ya tiene su propia pertenencia, sin cambios). El buscador es client-side sobre lo que ya devolvió `/conversaciones` — alcanza con filtrar ahí, no hay endpoint de búsqueda aparte. Avisos: `avisarNuevos`/contador de `#btnChats`/notificaciones internas de "Chats escalados" en el Inicio — todo sale de `/conversaciones` y de `/mensajeria/notificaciones-internas`; el primero ya queda cubierto al filtrar la lista, el segundo necesita el mismo filtro (hoy sin pertenencia).

### Paso 1: regla de visibilidad — confirmación de lo decidido en el Paso 0

La regla pedida (sin asignar → cualquier Profesional + Configurador; asignada → solo el dueño + Configurador, 404 para cualquier otro; bot-only → equipo tratante vigente del paciente + Configurador) queda tal cual la definí en el Paso 0, implementada en `VisibilidadConversacion.Puede`/`Filtrar`. Dos precisiones que no estaban explícitas ahí y conviene dejar escritas:

- **Qué cuenta como "escalada" para esta regla:** solo `Estado` = `DerivadaHumano` o `Urgente`. Una conversación `Cerrada` (una escalación ya resuelta) **no** sigue la regla de "asignada" — cae en la regla de bot-only, el equipo tratante vigente del paciente. Es a propósito: una vez resuelta, no tiene sentido que siga siendo exclusiva de quien la cerró para siempre — vuelve a ser parte del historial del paciente, visible para quien lo trate ahora. `Conversacion.IdProfesionalAsignado` **no se borra** al cerrar ni al reabrir (ni falta hacerlo: mientras está `Cerrada` esa columna no decide nada; si se reabre, vuelve a `DerivadaHumano` y el valor que tenía — probablemente el mismo profesional — vuelve a tener efecto, sin necesidad de volver a tomarla). Nada de esto es una regla nueva inventada: es la consecuencia directa de que la columna solo importa en el branch "escalada" de `Puede`.
- **Reasignación/derivación del Caso:** la exigencia de que "la visibilidad debe acompañar al profesional actual, sin dejar conversaciones visibles para el anterior" se resuelve en dos capas separadas, porque son dos relaciones separadas (ver Paso 0): para la pertenencia de **conversaciones**, es automático — el dueño es literalmente lo que lee `VisibilidadConversacion`, cambiarlo (Tomar/Devolver/Asignar) cambia la visibilidad en el mismo pedido, no hay nada que "sincronizar" aparte. Para el **Caso** (Fase 2, equipo tratante del paciente en general), la extensión de `SincronizarPacienteProfesional` (Paso 0) es la que ahora saca al profesional anterior de `PacienteProfesional` al reasignar — así una conversación bot-only de ese paciente también deja de ser visible para el que ya no lo trata, no solo las escaladas.

### Paso 2: aplicado a todo el módulo

`VisibilidadConversacion.Puede`/`Filtrar` se usa ahora en cada endpoint de `/mensajeria` que toca una conversación puntual o la lista — detalle endpoint por endpoint en `docs/MATRIZ_ENDPOINTS.md` (quedó totalmente reescrito, ya no dice "Pertenencia: Ninguna" en ningún lado salvo donde genuinamente no aplica: catálogo de etiquetas, preguntas frecuentes, respuestas rápidas que tienen su propia pertenencia por dueño). Puntos que no eran obvios de entrada:

- **404, nunca 403**, en todos lados — mismo criterio que ya usaba `RequiereMensajeriaHabilitada`: no confirmar que una conversación existe para quien no puede verla.
- **`/notificaciones-internas` no tenía pertenencia** (los avisos de "un paciente pidió hablar con una persona"/urgencia se mandan con `Destinatario=Administrador`, un valor compartido por todo el equipo — campana de `bandeja.js`, que pide siempre ese mismo valor sin distinguir quién pregunta). Si no se filtraba, un profesional podía enterarse por la campana de una conversación que ya es de otro (por ejemplo, si Fulano la toma después de que se creó el aviso). La lista ahora re-resuelve la pertenencia **actual** de la conversación que referencia cada aviso (no un dato grabado al crearlo) — si ya la tomó otra persona, deja de aparecer aunque el aviso sea de antes.
- **El buscador y "Mías" no necesitaron tocarse**: son client-side sobre `est.convs`, que ya llega filtrado por pertenencia desde `/conversaciones`. Una vez filtrado el origen, todo lo que se arma encima queda filtrado gratis — confirmado con Playwright (ver Verificación).
- **Respuestas rápidas revisadas, sin cambios**: no son pertenencia de conversación, son pertenencia de "quién es el dueño de esta respuesta" (global del Configurador, o propia de un profesional) — ya estaba bien resuelto, documentado en la matriz para que quede explícito que se revisó.

### Paso 3: Tomar una conversación sin asignar

`PUT /mensajeria/conversaciones/{id}/tomar` (cualquier Profesional) y `PUT .../devolver` (quien la tiene, o Configurador). `PUT .../asignar` (ya existía, para asignación directa de Configurador) se reescribió para operar sobre `Conversacion.IdProfesionalAsignado` en vez de `Caso` — ya no hace falta un caso abierto, y el campo "Nota" que tenía el modal se sacó (iba a `CasoDerivacion.Nota`; sin `Caso` de por medio no hay dónde guardarlo sin inventar una tabla nueva que nadie pidió para este alcance). Las tres acciones se restringen a conversaciones escaladas (ver Paso 1).

**Atomicidad de Tomar:** `ConversacionDAO.TomarSiNoTieneDueno` hace `UPDATE Conversacion SET IdProfesionalAsignado=@id WHERE IdConversacion=@id AND IdProfesionalAsignado IS NULL` y mira `ExecuteNonQuery() == 1`. Si dos profesionales la piden al mismo tiempo, SQL Server serializa el `UPDATE` a nivel de fila: una lo gana (1 fila afectada), la otra pierde (0 filas, el `WHERE` ya no matchea) — nunca hace falta una transacción explícita ni un `SELECT`-luego-`UPDATE` que pueda perder la carrera. Probado con un test que dispara los dos `PUT /tomar` a la vez (`Task.WhenAll`) contra la base real — ver Verificación.

**Decisión de diseño que no era obvia:** `/tomar` **no** pasa por `VisibilidadConversacion.Puede` para decidir el código de error — a propósito. `Puede` respondería 404 para "ya asignada a otro" (correcto para un GET pasivo: no revela que existe). Pero Tomar es la acción de **disputar** una escalación que la propia pantalla acababa de mostrar como sin asignar — si en el instante entre que la vio y que la pidió alguien más se la llevó, lo que pide el Paso 3 es un **409** claro ("ya la tomó otra persona"), no un 404 que parezca que desapareció. Se encontró este matiz recién al escribir el test de toma simultánea (ver "Errores encontrados" más abajo): con la versión que sí pasaba por `Puede`, el que perdía la carrera a veces veía 404 en vez de 409, según el timing exacto de los dos pedidos. `/tomar` ahora chequea existencia/estado directo: si existe y está escalada, cualquier profesional puede intentarla — perder frente a alguien que ya la tiene da 409, nunca 404. Este es el único punto de todo el módulo donde pedir una conversación ajena no da 404: es un costo aceptado y acotado (confirma "alguien la tiene", nunca quién, nunca nada del paciente) a cambio de un mensaje de error que tiene sentido para quien lo ve.

### Paso 4: auditoría de apertura

`GET /mensajeria/conversaciones/{id}/paciente` es, confirmado leyendo `bandeja.js`, el único endpoint que se llama **solo** al abrir o refrescar el panel de una conversación puntual (`cargarContexto()`, disparado desde `seleccionar()` y desde los puntos que ya refrescan tras una acción) — nunca desde el polling de la lista (`pulso()`, que solo pide `/conversaciones`). Ahí se agregó `AuditarConIds(http, "Apertura de conversación de chat", id, c.IdPaciente)`: quién (id de usuario), qué (conversación y paciente por id), cuándo — nunca el nombre del paciente ni ningún contenido, variante más estricta que el `Auditar` que ya usaban cerrar/reabrir/responder (que sí llevan el nombre del paciente en el detalle, heredado de la vuelta anterior — no se tocó, para no reabrir una decisión ya tomada fuera del alcance de este pedido, pero toda auditoría **nueva** de este trabajo, incluida la de Tomar/Devolver/Asignar, sigue el criterio estricto "solo ids").

**Interpretación de "una vez por apertura, no por cada refresh de polling":** no hizo falta deduplicar nada aparte — el polling de la lista nunca toca este endpoint (confirmado arriba), así que cada pedido a `/paciente` ya representa una razón real para mirar esos datos (abrir la conversación, o refrescar el panel después de cerrar/reabrir/etiquetar/tomar/devolver/asignar). Agregar lógica para distinguir "apertura nueva" de "refresco tras una acción mía" hubiera sido una regla nueva que el pedido no pidió — cada una de esas acciones ya tiene su propio registro de auditoría aparte, así que un segundo renglón de "apertura" junto al de la acción es, si acaso, más fiel al estándar de bitácora de acceso (quién miró qué y cuándo), no menos.

Se ve en la pantalla de Auditoría existente (con nombre de usuario — reutiliza `LogAuditoriaDAO`, el mismo mecanismo que ya usaban cerrar/reabrir/responder, no uno nuevo).

### Paso 5: cierres pendientes de la vuelta anterior

1. **Filtro por etiqueta en la bandeja:** `idEtiqueta` en `GET /conversaciones` (sobre lo ya filtrado por pertenencia) más un `<select>` nuevo en la barra de herramientas de `bandeja.js`, client-side sobre `est.convs` (mismo criterio que "Mías"/búsqueda — no hizo falta que la bandeja use el parámetro del servidor, ya tiene todo lo visible cargado).
2. **Pantalla de administración del catálogo de etiquetas**, Configurador únicamente: `#etiquetas` nueva en `dashboard.js` (entrada de menú en el grupo CRM de `shell.js`, mismo gate que Chats/Escalamientos — solo con la mensajería habilitada). Crear, renombrar, desactivar — mismo patrón que Grupos/Reglas de derivación (`YS.tabla` + funciones propias, **no** el motor genérico de `renderTabla`: ese motor asume que la ruta de la API vive en la raíz (`/{tab}`) y `/mensajeria/etiquetas` no lo es; replicar esa excepción en el motor compartido tocaba código del que dependen Roles/Especialidades/Coberturas/Turnos/Agendas — más riesgo que escribir unas pocas funciones propias, aisladas). El PUT/POST/DELETE del catálogo pasó a ser solo Configurador (antes, cualquiera) — ver matriz.
3. **Capturas:** `docs/capturas-cambios-agus/mejora-chats/` (carpeta nueva — no existía; la vuelta anterior las había dejado en la carpeta del arnés de pruebas por error, ver "Qué no llegó a esta vuelta" en Verificación final de arriba). Seis capturas con datos de demo (teléfonos de prueba sin identificar, nunca un paciente real): sin asignar con el botón Tomar, tomada con Devolver y "Atendida por", la lista de cada profesional sin lo del otro (dos capturas, lado a lado prueban el aislamiento), el aviso de "ya no podés ver esta conversación" al intentar abrir una ajena, y la pantalla de administración de etiquetas.

### Errores encontrados mientras se escribía esto (autocorregidos antes de seguir)

- **`/tomar` podía devolver 404 en vez de 409 al perder la carrera** — ver Paso 3. Encontrado al escribir el test concurrente, corregido antes del primer commit que lo incluía.
- **Estado local obsoleto tras Tomar/Devolver/Asignar:** `tomarConversacion`/`devolverConversacion`/el callback de `abrirAsignar` refrescaban llamando a `pulso()`, que en modo incremental (`?desdeMensaje=`) nunca trae de vuelta una conversación sin mensajes nuevos — así que el botón Tomar/Devolver de la propia cabecera podía quedar desactualizado hasta la próxima vuelta completa (antes, hasta 60 s). Encontrado con Playwright (dos profesionales de verdad, no simulado) al verificar que el botón cambiaba después de Tomar. Corregido: esas tres acciones ahora corrigen `est.convs` en el momento (ya saben el valor nuevo, es el que el servidor acaba de confirmar) en vez de esperar al polling. De paso, `YS.bandeja.actualizar()` (el gancho de refresco manual/pruebas) pasó a forzar una vuelta completa siempre, y la vuelta completa automática bajó de 60 a 20 segundos — ver el límite conocido más arriba y la nota en `MATRIZ_ENDPOINTS.md`.
- **Fixture de `ChatsGestionTests` rota por la pertenencia nueva:** su conversación de prueba quedaba `Activa` sin paciente identificado (con la regla nueva, eso es "solo Configurador la ve") — un profesional de prueba empezó a recibir 404 en vez del 200/403 que el test esperaba para probar permisos de notas. No era un bug de la pertenencia: el fixture probaba algo distinto (quién puede borrar la nota de otro) con una conversación que, sin querer, nunca debió ser visible para un profesional común. Se corrigió el fixture (la escala a `DerivadaHumano`, el estado real en el que tendría sentido asignar/reabrir/dejar notas), no la regla.

## Verificación de pertenencia de conversaciones

- **Build y batería:** `dotnet build` sin errores en cada paso; `dotnet test` en 147/147 al cierre (136 de antes + 11 nuevos en `YoSoy.Tests/PertenenciaConversacionTests.cs`: conversación asignada → 404 para el otro profesional/200 para el dueño y Configurador; escalada sin asignar → visible para cualquiera; bot-only → solo el equipo tratante del paciente; lista/contador sin fuga entre dos profesionales reales; Tomar con toma simultánea (test concurrente, un 200 y un 409, dueño único verificado en la base); Devolver; reasignación por Configurador (la visibilidad se mueve, no se duplica); notificaciones internas filtradas; auditoría de apertura con solo ids, nunca el nombre del paciente; rol Paciente fuera del módulo administrador).
- **Matriz de endpoints:** `docs/MATRIZ_ENDPOINTS.md` reescrita con la regla definitiva de pertenencia en cada fila (ver arriba).
- **Playwright, dos profesionales reales y distintos en sesiones separadas** (`docs/pruebas-ui-cambios-agus/p14_pertenencia.py`, no versionado, 21/21): sin asignar visible para ambos; Tomar hace desaparecer la conversación del otro (lista y contador, sin toast ni contador para quien no es dueño); un mensaje nuevo en la conversación de B no genera ni toast ni contador para A; abrir directo una conversación ajena da 404 con un aviso claro, sin romper la pantalla; Devolver la vuelve a dejar disponible para cualquiera; la reasignación por Configurador mueve la visibilidad (A la pierde, B la gana, nunca las dos); la pantalla de administración de etiquetas carga y crea una etiqueta nueva. Consola sin errores nuevos (los 404 esperados — la propia prueba de "abrir una conversación ajena" — están en la lista de "ignorar", con el motivo anotado).
- **Capturas:** `docs/capturas-cambios-agus/mejora-chats/` (`docs/pruebas-ui-cambios-agus/p15_capturas_pertenencia.py`, no versionado) — ver Paso 5.
- **Datos:** cada fila de prueba (conversaciones, mensajes, notificaciones, logs de auditoría, etiquetas) se creó con teléfonos/nombres de prueba y se borró al final de cada corrida — confirmado con consultas a la base antes de cada commit. Se encontraron y limpiaron 4 filas de auditoría huérfanas de una verificación manual anterior a este cierre (conversaciones de prueba ya borradas de una vuelta previa cuya auditoría no se había limpiado) — no eran de este trabajo, pero violaban la misma regla de "0 filas colgando", así que se borraron al encontrarlas. Nunca se tocaron datos reales ya existentes (los profesionales 1 y 2 usados como A/B en las pruebas son filas reales de la base de desarrollo, reutilizadas solo por su id — nunca se les modificó ningún dato propio).

### Qué no llegó a esta vuelta

Nada de lo pedido en Pasos 0 a 5 quedó afuera. Dos límites, no vacíos de alcance sino inherentes al diseño existente (documentados arriba, no descubiertos ahora): el margen de hasta 20 segundos en que una sesión que ya tenía abierta una conversación puede tardar en notar que dejó de ser suya (sin WebSocket, ver Paso 2/MATRIZ_ENDPOINTS.md); y que el botón Asignar/Reasignar del panel de información sigue necesitando que el paciente esté identificado para ofrecerse (así ya era antes de este trabajo, no se tocó esa condición — una conversación escalada sin paciente identificado puede Tomarse y Devolverse igual, pero el Configurador no tiene ahí un botón de asignación directa; no se pidió explícitamente y ampliarlo es una decisión de producto aparte).

---

**Recordatorio: todo queda local en `mejora-chats-y-centrado`. Nada de push, y nunca a `main`.**

## Cierre de seguridad

Rama `cierre-seguridad`, creada desde `mejora-chats-y-centrado` (que ya incluye
`Cambios-Agus-jwt`). Cierra los pendientes de seguridad que quedaron al terminar la Fase
JWT y los chats, para que el merge a `main` sea responsable. Plan: Paso 0 (reconocimiento +
matriz por reflexión), Paso 1 (guardia automática), Paso 2 (historia clínica por
pertenencia), Paso 3 (agendas y turnos), Paso 4 (resto de la matriz), Paso 5 (apagar modo
legacy), Paso 6 (auditoría de datos clínicos), Paso 7 (cabeceras), Paso 8 (secretos y
config). Se sigue sin esperar respuesta, como ya era la norma en esta rama.

### Paso 0: reconocimiento

`Auth:PermitirEncabezadoLegacy` ya estaba en `false` en `appsettings.json` — quedó así al
cerrar la Fase JWT, verificado entonces con curl (no con un recorrido completo). Se revisó
el camino del header en todo el repo: el único lugar del **backend** que todavía leía
`X-Usuario-Id` directo, al margen del JWT, era `Services/AutorizacionReporte.cs` (ver
CB-04 en Paso 4) — todo lo demás ya pasaba por `IdentidadActualService`/
`AutorizacionCasos`. En el **frontend**, `PANTALLAS/shared/ui.js` (`YS.fetch`) sigue
mandando `X-Usuario-Id` además de `Authorization: Bearer` en cada pedido autenticado
(vestigio de antes del JWT) — con el flag en `false`, el esquema de autenticación siempre
prioriza el Bearer si está presente, así que el header queda muerto pero inofensivo: no
hay ningún camino que mande `X-Usuario-Id` **sin** mandar también el Bearer, así que
apagar el legacy no rompe ninguna pantalla. No se tocó `ui.js`: limpiarlo es prolijidad,
no seguridad, y no lo pidió el encargo.

La matriz de endpoints se generó por reflexión real sobre `EndpointDataSource`
(`YoSoy.Tests/ReflexionEndpoints.cs`), nunca a mano — ver Paso 1 para el detalle de cómo,
y `docs/MATRIZ_ENDPOINTS.md` para el resultado. Quedó un detalle no trivial: el overload
`RequireAuthorization(p => p.RequireRole(...))` que usa casi toda la app **no** deja el
rol en `IAuthorizeData.Roles` (eso es para el viejo `[Authorize(Roles=...)]`) — el
framework arma una `AuthorizationPolicy` aparte con un `RolesAuthorizationRequirement`
adentro, y la adjunta como su propio metadato de endpoint. Se encontró inspeccionando a
mano los metadatos reales de un endpoint conocido (`POST /especialidades`) antes de
confiar en la columna de rol del volcado.

### Paso 1: guardia automática

`YoSoy.Tests/GuardiaEndpointsTests.cs`: un solo test que enumera todos los endpoints por
reflexión y falla si alguno no exige autenticación y no está en `Publicos` (lista
explícita con el motivo de cada uno — ver `docs/MATRIZ_ENDPOINTS.md`). La primera corrida,
antes de tocar nada, marcó **109** endpoints sin decidir — esa lista fue la guía real del
resto del trabajo (Pasos 2 a 4), no una recorrida manual del código. Al cerrar el cierre de
seguridad, la lista quedó en 0 sin agregar ningún "Público" que no estuviera ya justificado
por una pantalla real que lo necesita antes de loguearse (confirmado buscando en
`PANTALLAS/`, no supuesto).

### Paso 2: historia clínica y datos clínicos por pertenencia

`Services/AutorizacionClinica.cs`: mismo patrón que `AutorizacionCasos`/
`VisibilidadConversacion` (nunca confía en un id del cuerpo/URL, siempre
`IdentidadActualService` + `PacienteProfesionalDAO.ObtenerIdPacientesDeProfesional` —
"asociado" es el mismo "equipo tratante vigente" que ya usan los chats, no una regla
nueva). Antes: cualquier profesional autenticado veía la historia clínica, evoluciones,
eventos de crisis o sesiones de **cualquier** paciente (más ancho que Casos, que ya
filtraba por asignación desde la Fase JWT). Ahora: solo el profesional asociado a ese
paciente, o el Configurador — 404 para el resto, nunca 403 (mismo criterio de no revelar
existencia que ya usan los chats).

`InteraccionIA` se suma al mismo tratamiento aunque el encargo no la nombraba
explícitamente: son interacciones con la IA de apoyo emocional, contenido tan sensible
como una evolución (la propia descripción del README la lista como tal). Confirmado que
hoy no la usa ninguna pantalla real (solo `mockup.html`, el scaffold de antes de
`PANTALLAS/`), así que subirle el nivel de protección no corre ningún riesgo de romper
algo que funcione.

Los volcados sin filtro (`GET /historiasclinicas`, `/evoluciones`, `/eventoscrisis`,
`/interaccionesia`, todos sin parámetro de paciente) quedaron Configurador-only en vez de
inventarles un "todos los historiales de mis pacientes" por profesional: se confirmó
revisando `Pantalla_Configurador/js/dashboard.js` que **solo** la tabla genérica del
Configurador los llama (las pantallas de Profesional usan siempre las rutas por paciente/
historia) — así que filtrar por profesional sería una funcionalidad nueva que nadie pidió,
no una corrección de seguridad.

### Paso 3: agendas y turnos

**Agendas:** antes, alta/baja/edición de franjas sin ningún chequeo — cualquiera podía
crear o borrar la agenda de cualquier profesional, con solo adivinar un id. Ahora: el
profesional solo administra las propias (`IdProfesional` se fuerza desde el token al
crear, nunca se confía en el del cuerpo; PUT/DELETE verifican contra la fila existente
antes de tocarla), el Configurador, cualquiera. El alias `/agenda` (sin "s", de una
pantalla anterior de Profesional) recibió el mismo tratamiento para no dejar una puerta
de atrás.

**Turnos:** se revisaron los cinco caminos que pedía el encargo (paciente, cita urgente,
profesional que solicita, cancelación, reprogramación) y se encontraron **cuatro**
endpoints sin ningún chequeo de identidad: `PUT /turnos/{id}` (el genérico que usa
`nuevo_dashboard.js` para confirmar/aceptar/rechazar — cualquiera podía modificar
cualquier turno), `PUT /turnos/{id}/confirmar-paciente`, `PUT /turnos/{id}/generar-sala` y
`POST /turnos/solicitar`/`POST /turnos/profesional-solicitar` (estos dos sí tenían rol,
pero confiaban en el `idPaciente`/`idProfesional` del cuerpo tal cual llegaba). Los cinco
ahora fuerzan la identidad desde el token — el paciente que pide un turno, o el profesional
que lo propone/confirma/genera la sala, es siempre el del token, nunca uno elegido en el
pedido. `/turnos/{id}/cancelar` ya estaba bien (Fase JWT).

`POST /turnos` crudo (sin pasar por ninguna regla de `TurnoReglasService`: sin RN-01/05/07,
sin la inserción a prueba de doble reserva) se confirmó sin ningún uso en todo
`PANTALLAS/` — se decidió **apagarlo (410 Gone)** en vez de protegerlo: protegido seguiría
siendo un atajo que ignora las reglas de negocio para quien sea que tenga sesión, sin
ningún beneficio real, mientras que apagado deja claro que el camino correcto es
`/turnos/solicitar` o `/turnos/profesional-solicitar`.

### Paso 4: resto de la matriz

Con la guardia del Paso 1 como lista de trabajo (no la memoria ni una relectura manual),
se cerró cada grupo restante:

- **CB-04 (reportes):** el hueco que el propio código marcaba con un comentario.
  `AutorizacionReporte.ResolverAlcance` leía `X-Usuario-Id` directo del header, **al
  margen del pipeline de JWT** — ni ese archivo ni `GET /reportes/atenciones[.html]`
  exigían sesión a nivel de ruta, así que cualquiera, sin ningún token, podía mandar
  cualquier `X-Usuario-Id` (o ninguno: caía en idUsuario=0, "sin vínculo", tratado como
  Configurador con alcance total) y bajar nombre, DNI y NHC de pacientes. Corregido para
  leer `IdentidadActualService`, igual que el resto de la app; la lógica de alcance
  (propio para un profesional, total para el Configurador) no cambió, solo de dónde sale
  la identidad.
- **Casos/Grupos/ReglasDerivacion/CasosDerivaciones:** ya filtraban por dueño
  (`AutorizacionCasos`), pero el grupo entero no exigía sesión a nivel de ruta — varios
  (`/grupos`, `/reglasderivacion`, `/grupos/{id}/miembros`, `/casos/motivos-reasignacion`,
  `/pacientes/{id}/caso`) no tenían **ningún** chequeo, ni de sesión ni de rol.
  `MapCasos()` ahora agrupa todo con `app.MapGroup("").RequireAuthorization()` (mismo
  patrón que ya usa `MapMensajeria()` con su grupo `/mensajeria` — acá sin prefijo, porque
  las rutas de Casos no comparten uno) en vez de agregar `.RequireAuthorization()` a mano
  en 28 lugares.
- **Catálogos** (EstadoTurno, TipoConsentimiento, ObraSocial): las escrituras no tenían
  **ningún** chequeo, a diferencia de Especialidad/Rol (ya Configurador-only desde la Fase
  JWT) — se les pareja el mismo criterio. Las lecturas pasan a exigir sesión (sin rol
  específico): se confirmó en `PANTALLAS/Pantalla_Usuario/` que solo `/especialidades` la
  necesita un registro público (de profesional); los otros cuatro no, así que no hay
  riesgo de romper un registro.
- **Pacientes/Profesionales:** el volcado completo, la ficha por id, y hasta "Mi perfil"
  (PUT) estaban sin **ningún** chequeo — cualquiera, sin sesión, podía leer o reescribir
  la ficha de cualquier paciente o profesional con solo adivinar un id. El volcado
  completo queda Configurador-only (confirmado que solo la tabla admin lo usa); la ficha y
  "Mi perfil" exigen sesión del propio dueño (o Configurador).
- **Tutor/PacienteTutor/Cobertura:** confirmado sin ningún uso real en `PANTALLAS/` (solo
  `mockup.html`) — se cierran a Profesional/Configurador sin ningún riesgo de romper algo
  que funcione.
- **Informes de admisión, notificaciones, asignaciones:** `POST /informes-admision` fuerza
  el `IdPaciente` del token (lo manda el propio paciente desde su panel); el resto del
  flujo de admisión, confirmado Configurador-only por quién lo llama realmente.
  `/pacientes/{id}/notificaciones` y `/notificaciones/{id}/leida` no tenían ningún
  chequeo — cualquiera podía leer el título y mensaje completos de cualquier aviso.

### Paso 5: apagar el modo legacy

Ya estaba en `false` (ver Paso 0). Se agregó `YoSoy.Tests/ModoLegacyTests.cs` en vez de
confiar en la memoria de que "ya se verificó con curl" en la Fase JWT: un `X-Usuario-Id`
solo, sin Bearer, da 401; un JWT real sigue identificando igual. Ningún camino necesitaba
seguir con el flag en `true`.

### Paso 6: auditoría de accesos a datos clínicos

El mecanismo de auditoría "solo ids, nunca contenido" que ya usaba la apertura de
conversaciones de chat se extrajo a `Services/AuditoriaIds.cs`, compartido, en vez de
reescribirlo — `MensajeriaEndpoints.AuditarConIds` ahora es un alias de una línea que
delega ahí, sin tocar ninguno de sus 4 call sites. Se aplicó a las cuatro aperturas reales
(confirmadas en `nuevo_dashboard.js`/`dashboard.js` del Configurador, no supuestas):
`GET /pacientes/{id}/historiaclinica`, `GET /historiasclinicas/{id}/evoluciones`,
`GET /pacientes/{id}/eventoscrisis`, `GET /pacientes/{id}/sesiones-clinicas`. Quién (id),
qué (tipo de recurso e id del paciente), cuándo — nunca el contenido. "Una vez por
apertura, no por cada refresco" no necesitó deduplicar nada aparte: a diferencia de los
chats, esta parte de la app no tiene ningún polling que repita el pedido, así que cada
llamado ya es una apertura real. Se ve en la pantalla de Auditoría existente con el
nombre de usuario resuelto — el mismo mecanismo genérico que ya resolvía los nombres para
la auditoría de chats, sin ningún cambio de frontend.

No se audita el acceso a los endpoints por id propio de la fila (`/historiasclinicas/{id}`,
`/evoluciones/{id}`, `/eventoscrisis/{id}`) ni los volcados Configurador-only: no son la
"apertura de la ficha de un paciente" que pide el encargo, y no hay evidencia de que
los use ninguna pantalla real aparte de la tabla admin.

### Paso 7: cabeceras y endurecimiento básico

`X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` y
`X-Frame-Options: SAMEORIGIN` pasaron de estar solo en `/public` y `/reservar` (Fase 3a) a
ser globales — toda la app es candidata a quedar detrás de un proxy algún día, no solo esas
dos rutas. `SAMEORIGIN`, no `DENY`: se confirmó en `nuevo_dashboard.js` que la sala de
video sigue embebida en un `<iframe src="sesion_video.html?...">` del mismo origen —
`DENY` la hubiera roto. CSP queda afuera a propósito, como pedía el encargo: hay demasiado
JavaScript y CSS inline en `PANTALLAS/` (ninguna pantalla usa nonces ni separa los
estilos), y una política restrictiva rompería pantallas sin un trabajo de migración propio
y más grande que este cierre. Queda anotado como mejora futura; no se armó ni siquiera un
modo "solo reporte" porque igual exige mapear cada script/estilo inline existente para no
llenar la consola de falsos positivos, y eso es trabajo de otra vuelta.

Se revisó además que ningún mecanismo pueda filtrar una traza de error interna: no hay
`UseDeveloperExceptionPage`, `UseExceptionHandler` ni `AddProblemDetails` en ningún lugar
de la app, en ningún entorno — así que no hace falta "apagar" nada para Production, porque
nunca existió el camino que lo encendería.

### Paso 8: configuración y secretos

`appsettings.json` → `Email:Password` tenía una contraseña real de Gmail, versionada.
Se reemplazó por un string vacío (no un placeholder con corchetes como
`[COMPLETAR Y VALIDAR]`, que ya usa `Mensajeria:LineasEmergencia` para otra cosa — un
string vacío es lo que `EmailService` ya interpreta como "no autentiques de verdad",
mismo efecto práctico que la contraseña en blanco que ya documentaba `docs/DEMO.md`). Se
documentó en el `README.md`, mismo patrón que ya existía para `Jwt:Key`: `dotnet
user-secrets set "Email:Password" "..."` o la variable de entorno `Email__Password`. La
rotación de la contraseña expuesta en el proveedor (Gmail) la hace el equipo, no este
commit — no corresponde que un cambio de código la rote.

La tabla de migraciones del README se completó con las dos que faltaban de la rama
anterior (`add_chats_notas_etiquetas_respuestas.sql`, `add_conversacion_asignacion.sql` —
ambas idempotentes, confirmado leyendo sus `IF`/`IF NOT EXISTS`), y la sección de JWT se
actualizó para reflejar que los huecos que listaba como "pendiente" ya se cerraron en esta
vuelta.

### Errores encontrados mientras se escribía esto (autocorregidos antes de seguir)

- **La columna de rol del volcado por reflexión salía vacía** para casi todos los
  endpoints — ver Paso 0. Encontrado al revisar el primer volcado antes de redactar la
  matriz (todas las filas con rol conocido aparecían sin rol), corregido inspeccionando
  los metadatos reales de un endpoint conocido antes de confiar en el resultado.
- **El `_DumpEndpoints.cs` de un solo uso** (para generar el volcado de la matriz) tenía
  un error de sintaxis en el primer intento (una línea de diagnóstico mal escrita) — se
  corrigió antes de usarlo, y el archivo se borró después de generar la matriz: no es
  parte de la batería permanente.

No se encontró ningún otro bug de los propios cambios al correr la batería completa: las
160 pruebas (148 de antes + las nuevas de este cierre) pasaron en la primera corrida
después de escribir cada bloque, sin ciclos de corrección — a diferencia de la vuelta
anterior (pertenencia de chats), que encontró 3 bugs reales durante la propia
verificación.

## Verificación del cierre de seguridad

- **Build:** `dotnet build --no-incremental` sin errores nuevos en cada paso (solo
  advertencias de nulabilidad preexistentes, ninguna de este trabajo).
- **Batería completa:** 160/160 en verde (148 de antes de este cierre + 12 nuevas:
  `GuardiaEndpointsTests` ×1, `ModoLegacyTests` ×2, `CierreSeguridadTests` ×10 — historia
  clínica por pertenencia con auditoría verificada, agenda ajena, identidad de turno
  desde el token, CB-04 de reportes, cabeceras globales).
- **Guardia automática:** `GuardiaEndpointsTests` pasó de marcar 109 endpoints sin decidir
  a 0, sin agregar ningún "Público" sin una pantalla real pre-login que lo justifique.
- **Matriz de endpoints:** `docs/MATRIZ_ENDPOINTS.md` reescrita por completo — ver Paso 0.
- **Datos:** `CierreSeguridadTests` crea su propia `HistoriaClinica`/`Agenda`/`Turno` de
  prueba cuando hace falta y los borra en su `DisposeAsync` (confirmado con consultas a la
  base después de cada corrida: 0 filas colgando). No se tocó ningún dato real existente
  más allá de referenciar por id a los profesionales/pacientes reales 1, 2 y 3 que ya
  usaba `PertenenciaConversacionTests` de la vuelta anterior.
- **Recorrido manual pendiente:** el recorrido con Playwright por los tres roles
  (`docs/DEMO.md` completo, con dos profesionales en Chats) y la matriz de pruebas por rol
  con curl/token real para los grupos no cubiertos por `CierreSeguridadTests` quedan
  anotados en "Qué no llegó a esta vuelta" si no se completan antes de este commit —
  revisar esa sección antes de dar el cierre por terminado.

### Qué no llegó a esta vuelta

- El recorrido visual completo con Playwright de `docs/DEMO.md` (los tres roles, con dos
  profesionales reales en Chats) no se repitió en esta vuelta: la lógica de pertenencia de
  chats no cambió (solo se le agregó autenticación a grupos que no la tenían en
  endpoints que ningún flujo del demo ejercita — Casos/Grupos internos del Configurador,
  no pantallas de Profesional/Paciente), y el resto de los cambios (historia clínica,
  turnos, agendas) se verificó con `CierreSeguridadTests` contra la base real en vez de
  con el navegador. Si algo de la interfaz dependía silenciosamente de un endpoint que
  pasó de "abierto" a "exige sesión" sin que ninguna pantalla real lo note (por ejemplo,
  un `fetch` suelto sin `YS.fetch`, que no manda el token), un recorrido visual lo
  encontraría y esta verificación con tests no.
- `signaling-server` (Node) sigue sin autenticar nada — fuera de alcance explícito de esta
  rama (lo trabaja `videollamadas-seguras`), solo queda anotado en el README.
- CSP queda como mejora futura (ver Paso 7) — no se activó ni en modo reporte.
- El cierre de sesión por inactividad (bug 13) sigue pendiente de una decisión de equipo,
  sin tocar.

---

**Recordatorio: todo queda local en la rama `cierre-seguridad`. Nada de push, y nunca a `main`.**
