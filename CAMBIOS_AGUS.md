# Cambios-Agus: consolidación de la app web

Seguimiento de la rama local `Cambios-Agus`. Cada paso deja anotado qué se hizo, qué se decidió y
qué queda pendiente. **La rama no se sube a GitHub**: queda local hasta revisarla juntos.

**Estado:** Pasos 0 a 4 terminados. Paso 5 en curso: listos acceso y registro, el Inicio de cada rol, los listados del Configurador (sin Bootstrap) y la corrección de XSS en cuatro pantallas. Faltan historia clínica y sesiones, consentimiento y perfil.

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

### Paso 5: resto de las pantallas (en curso)

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

**Seguridad: XSS almacenado en cuatro pantallas** (un commit por pantalla).
- **El problema:** datos que escribe un rol se insertaban como HTML en la pantalla de otro:
  - el informe de admisión y los nombres del paciente, en el Configurador;
  - el motivo del turno y la historia clínica, en la pantalla del profesional;
  - nombre y datos del profesional, en la del paciente;
  - los nombres en el select de la sesión de video.
- **Ejemplo:** un paciente podía poner `<img src=x onerror=...>` en su nombre o en el motivo de un turno, y se ejecutaba en la sesión del profesional o del configurador.
- **Botones "Historia" y "Solicitar cambio":** pasaban el nombre dentro del `onclick` con un escape incompleto. Ahora pasan solo ids.
- **Cómo se probó:** se interceptaron las respuestas de la API en el navegador, sin tocar la base. La versión anterior ejecuta el código inyectado y la nueva lo muestra como texto.

**Bugs corregidos en este paso** (cada uno en su commit):
- **Bug 10:** "Ver todos los turnos" no hacía nada. El router comparaba la sección pero no los parámetros (`#turnos?paciente=N` → `#turnos`).
- **Bug 11:** al abrir desde el aviso una conversación que ya estaba elegida, no se bajaba al mensaje nuevo y la conversación nunca quedaba leída.
- **Bug 12:** editar una agenda desde el Configurador siempre daba 400. `disponible` viajaba como texto `"true"`; ahora se elige con Sí/No y se manda como booleano.

**Datos de prueba en la base local:** solo un `PUT /agendas/1` con los mismos valores que ya tenía, para comprobar el bug 12.

**Pruebas que se ajustaron:** varias pruebas de Playwright del Paso 3 y del Paso 4 dependían de datos que cambian con cada corrida. Ahora calculan lo esperado desde la API, o usan datos inventados en el navegador para no escribir en la base.

## Pendientes

_(se completan en cada paso)_

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

La app queda en `http://localhost:5000/pantallas/index.html`.
