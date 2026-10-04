# Cambios-Agus: consolidación de la app web

Seguimiento de la rama local `Cambios-Agus`. Cada paso deja anotado qué se hizo, qué se decidió y
qué queda pendiente. **La rama no se sube a GitHub**: queda local hasta revisarla juntos.

**Estado:** Pasos 0 y 1 terminados. Sigue el Paso 2 (shell común).

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
