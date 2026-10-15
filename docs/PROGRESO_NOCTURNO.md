# Progreso nocturno

Registro del trabajo autónomo de la tanda de tareas del 2026-10-15. Una rama local por tarea, todas salidas de
`main` actualizado (`27ad47f`, Merge del PR #26). **Sin push, sin merge, sin tocar `main`.** Al final de este
archivo está el orden recomendado para mergear.

## Cómo se trabajó

- **Sin secretos ni producción:** no se leyó ningún `.env` ni `appsettings.json` con valores, no se accedió al VPS
  ni a la base real `yosoy`.
- **Pruebas sin base:** `dotnet test` sin `ConnectionStrings__YoSoy`. Da **79 fallas** en `main` (58 por
  `Conexion` sin cadena, 16 por HTTP 500 en endpoints que usan la base y 5 asserts); es la línea base contra la
  que se compara cada tarea ("fallas nuevas" = fallas que no están en `main`).
- **Pruebas con base:** una copia **descartable del esquema en `tempdb`** (44 tablas, armada con
  `docs/produccion/01-produccion.sql` sin su bloque `CREATE DATABASE`/`USE [yosoy]`, más unas filas de
  catálogo) con la cadena de conexión apuntando solo a `tempdb`. Con eso, `main` tiene **40 fallas**, todas de
  tests que suponen filas que existen en la base de desarrollo (ver Tarea 5). Las tablas de `tempdb` se sueltan
  al terminar.
- **Finales de línea:** los archivos nuevos y editados quedan en CRLF en el árbol de trabajo, como el resto del
  repo; se verificó con `git ls-files --eol` antes de editar.

## Estado

| # | Tarea | Rama | Estado |
|---|---|---|---|
| 1 | Registro de profesional: 409 / 400 en vez de 500 | `fix/registro-profesional-409` | Hecha |
| 2 | Bloqueo del login: el conteo vence y se desbloquea | `fix/admin-lockout-decay` | Hecha |
| 3 | Crear el usuario de un profesional desde el Configurador | `feat/crear-usuario-profesional-admin` | Hecha (opción A; la B queda como diseño) |
| 4 | Mensajes que permiten enumerar, política de contraseña y hallazgos | `chore/hardening-mensajes` | Hecha (parte aplicada; hallazgos para decidir) |

---

## Tarea 1 — `fix/registro-profesional-409`

**Pedido:** `POST /profesionales` devolvía 500 si la matrícula ya existía (UNIQUE) o si la especialidad no
existía (FK). Chequeo previo de matrícula y DNI → 409 con mensaje neutral; `SqlException` 2627/2601 (carrera) →
409; 547 → 400 "La especialidad elegida no existe."; mismo criterio en `PUT /profesionales/{id}`; mantener la
compatibilidad con `registro_profesional.html`; tests.

**Qué se encontró (código real):**

- `POST /profesionales` llamaba a `ProfesionalDAO.Insertar` sin ningún chequeo. Matrícula repetida →
  `SqlException 2627` (`UNIQUE` de `Profesional.Matricula`); especialidad inexistente → `547`
  (`FK_Profesional_Especialidad`); nombre o matrícula vacíos → `515` (NOT NULL). Todos terminaban en 500.
- **`Profesional.Dni` no tiene `UNIQUE`** (la columna admite NULL). Un DNI repetido no daba 500: se creaba el
  duplicado en silencio. El control de DNI es solo de aplicación (ver riesgos).
- `PUT /profesionales/{id}` (lo usa "Mi perfil") tenía el mismo problema con la matrícula.
- El endpoint público no tenía límite de pedidos por IP (el de paciente sí: política `registro`).

**Commits:** `3bc896a` (código y pruebas).

**Resultado de las pruebas:**

| Corrida | `main` | Esta rama |
|---|---|---|
| Sin base (`ConnectionStrings__YoSoy` sin definir) | 551 ok / 79 fallan | 582 ok / 84 fallan: las 79 de siempre + las 5 de integración nuevas, que necesitan base como el resto de las de integración |
| Con la copia de esquema en `tempdb` | 608 ok / 40 fallan | **644 ok / 40 fallan** (las mismas 40, **0 fallas nuevas**, 36 tests nuevos) |

**Qué se cambió:**

| Archivo | Cambio |
|---|---|
| `Services/RegistroProfesionalService.cs` (nuevo) | Alta y edición con los chequeos previos y la traducción de errores de SQL. Usa un repositorio (`IProfesionalRegistroRepositorio`) para poder probarlo sin base. |
| `DAOs/ProfesionalDAO.cs` | Implementa el repositorio: `ExisteEspecialidad`, `ExisteMatricula`, `ExisteDni` (compara sin puntos, espacios ni guiones). |
| `Program.cs` | `POST /profesionales` y `PUT /profesionales/{id}` usan el servicio. El `POST` pasa a tener la política de límite `registro` (10 pedidos por 10 minutos por IP, la misma del alta de paciente). |
| `YoSoy.Tests/RegistroProfesionalServiceTests.cs` (nuevo) | 31 pruebas sin base. |
| `YoSoy.Tests/RegistroProfesionalEndpointsTests.cs` (nuevo) | 5 pruebas de integración con datos propios (categoría `Integracion`). |

**Comportamiento resultante:**

| Caso | Respuesta |
|---|---|
| Alta válida | 201 con la ficha (y su `idProfesional`), como antes |
| Matrícula ya registrada (sin distinguir mayúsculas ni espacios de los costados) | **409** `{ "mensaje": ... }` |
| DNI ya registrado (aunque venga con puntos, espacios o guiones) | **409**, el mismo mensaje |
| Carrera: dos altas con la misma matrícula a la vez (la base dispara 2627/2601) | **409**, el mismo mensaje |
| Especialidad que no existe (chequeo previo o 547 de la base) | **400** "La especialidad elegida no existe." |
| Falta nombre o matrícula | **400** "Faltan datos obligatorios: el nombre y la matrícula." |
| `PUT` que cambia la matrícula o el DNI a los de otro profesional | **409** |
| `PUT` que no cambia ni matrícula ni DNI | 200, aunque la ficha tenga un DNI repetido de antes |
| Cualquier otro error de SQL | Sigue siendo 500 (no se traga) |

El mensaje de duplicado es el mismo para matrícula y DNI y no dice cuál coincidió, ni de quién es, ni en qué
estado está esa ficha.

**Compatibilidad:** `registro_profesional.html` y "Mi perfil" (`nuevo_dashboard.js`) ya muestran el campo
`mensaje` del JSON (`YS.leerMensaje`), así que no se tocó el frontend. El cuerpo de la respuesta de un alta
exitosa no cambia.

**Cómo probarlo:**

```
# Sin base: la lógica de negocio (31 pruebas)
dotnet test YoSoy.Tests --filter "FullyQualifiedName~RegistroProfesionalServiceTests"

# Con una base de prueba (apuntar ConnectionStrings__YoSoy a una base DESCARTABLE, nunca a la real):
dotnet test YoSoy.Tests --filter "FullyQualifiedName~RegistroProfesionalEndpointsTests"
```

A mano (API levantada y la cadena de conexión apuntando a una base de prueba): mandar dos veces el mismo
`POST /profesionales` con la misma `matricula` → la segunda tiene que dar 409; mandar `idEspecialidad: 99999` →
400.

**Riesgos:**

- **DNI sin `UNIQUE`:** el chequeo de DNI es de aplicación; dos altas simultáneas con el mismo DNI y distinta
  matrícula pueden crear los dos registros. Para cerrarlo hace falta un índice único filtrado
  (`CREATE UNIQUE INDEX ... ON Profesional(Dni) WHERE Dni IS NOT NULL`), pero **falla si ya hay DNI repetidos**
  en la base. No se aplicó ni se agregó como migración. Para ver si hay repetidos:
  `SELECT Dni, COUNT(*) FROM Profesional WHERE Dni IS NOT NULL GROUP BY Dni HAVING COUNT(*) > 1;`
- **El 409 confirma que algo existe.** Es inherente a lo pedido (un mensaje claro de duplicado); se mitiga con
  el mensaje neutral y con el límite por IP agregado al `POST`.
- **Límite compartido:** la política `registro` es una sola para pacientes y profesionales (10 por 10 minutos
  por IP). Una oficina con una sola IP pública que registre a más de 10 personas en 10 minutos va a recibir 429.
- **`PUT` y fichas viejas:** a propósito solo se controla lo que cambia. Si en la base ya hay dos profesionales
  con el mismo DNI, ninguno queda bloqueado para guardar su perfil.

**Qué falta / decisiones:**

- Decidir si se agrega el índice único de DNI (previa limpieza de duplicados).
- Los textos del 409 son una propuesta: si el equipo prefiere otro tono, están en
  `RegistroProfesionalService.MensajeDuplicadoAlta` / `MensajeDuplicadoEdicion`.

---

## Tarea 2 — `fix/admin-lockout-decay`

**Pedido:** `IntentosFallidos` no decae: tras N intentos fallidos hay un bloqueo que no se limpia salvo reset
manual. Revisar el login de paciente, profesional y admin/configurador; reseteo de `IntentosFallidos` tras un
login exitoso y expiración coherente del bloqueo (`BloqueadoHasta`), **sin cambiar el umbral actual**; tests.

**Qué se encontró (código real):**

- Los tres roles entran por **un solo** `POST /login` y comparten `Usuario.IntentosFallidos` /
  `Usuario.BloqueadoHasta`. No hay otro lugar que verifique contraseñas con bloqueo.
- Ya existía el reseteo tras un login exitoso (`ResetearIntentos`) y el de la recuperación por mail.
- **El problema real:** `RegistrarIntentoFallido` sumaba 1 y, si el nuevo valor era `>= 3`, ponía
  `BloqueadoHasta = GETDATE() + 15 min`. Al vencer el bloqueo, `IntentosFallidos` seguía en 3 (o más): el
  **siguiente** error (4 `>= 3`) volvía a bloquear 15 minutos, y así indefinidamente. Solo un login exitoso o un
  `UPDATE` a mano lo limpiaban, y un login exitoso exige acertar la clave justo después de equivocarse.
- **Dos relojes:** el bloqueo se escribía con `GETDATE()` (hora de SQL Server) y se comparaba con
  `DateTime.Now` (hora de la aplicación). Si las dos zonas horarias difieren (por ejemplo un servicio que
  arrancó antes de que se cambiara la zona del servidor), el bloqueo dura horas de más o de menos.
- `PUT /usuarios/{id}` con una contraseña nueva (el Configurador atendiendo a alguien) no desbloqueaba.

**Qué se cambió:**

| Archivo | Cambio |
|---|---|
| `Services/PoliticaBloqueoLogin.cs` (nuevo) | La política en una clase pura: umbral 3 y 15 minutos (los de siempre), `EstaBloqueado`, `TrasIntentoFallido`, mensaje con los minutos que faltan. |
| `DAOs/UsuarioDAO.cs` | `RegistrarIntentoFallido(id, ahoraUtc)`: un solo `UPDATE ... OUTPUT` atómico que, si el bloqueo anterior ya venció, **arranca el conteo de nuevo** (este error es el primero de tres). Devuelve el estado que quedó (se evita la segunda lectura). |
| `Program.cs` | `/login` usa el reloj de la aplicación (`TimeProvider`) en UTC y la política; no escribe en la base en un login normal (solo limpia si había algo). `PUT /usuarios/{id}` con contraseña nueva también desbloquea. |
| `YoSoy.Tests/PoliticaBloqueoLoginTests.cs` (nuevo) | 18 pruebas sin base de la política. |
| `YoSoy.Tests/BloqueoLoginIntegrationTests.cs` (nuevo) | 12 pruebas con base (categoría `Integracion`): el `UPDATE` de SQL contra la política en 8 escenarios, y el flujo real de `/login` con un reloj controlado. |

**Comportamiento resultante (umbral y duración sin cambios):**

| Situación | Antes | Ahora |
|---|---|---|
| 3 errores seguidos | 423 en el tercero, 15 minutos | igual |
| Un error después de que venció el bloqueo | **423 de nuevo, otros 15 minutos** | 401 y el conteo vale 1; hacen falta 3 para volver a bloquear |
| Clave correcta con el bloqueo vigente | 423 | igual (423) |
| Clave correcta, bloqueo vencido | entra y limpia | igual |
| Login exitoso con 1 o 2 errores | limpia | igual |
| El Configurador le pone una contraseña nueva | seguía bloqueado | queda desbloqueado |
| Mensaje del 423 | "...Intentá en 15 minutos." siempre | "...Intentá en N minutos." con lo que falta de verdad |

**Commits:** `1a7cf0d` (código y pruebas).

**Resultado de las pruebas:**

| Corrida | `main` | Esta rama |
|---|---|---|
| Sin base (`ConnectionStrings__YoSoy` sin definir) | 551 ok / 79 fallan | 569 ok / 91 fallan: las 79 de siempre + las 12 de integración nuevas, que necesitan base |
| Con la copia de esquema en `tempdb` | 608 ok / 40 fallan | **638 ok / 40 fallan** (las mismas 40, **0 fallas nuevas**, 30 tests nuevos) |

**Cómo probarlo:**

```
# Sin base: la política (18 pruebas)
dotnet test YoSoy.Tests --filter "FullyQualifiedName~PoliticaBloqueoLoginTests"

# Con una base de prueba DESCARTABLE (nunca la real): el UPDATE de SQL y el flujo de /login
dotnet test YoSoy.Tests --filter "FullyQualifiedName~BloqueoLoginIntegrationTests"
```

Se verificó con una mutación: devolviendo el `UPDATE` al comportamiento anterior fallan 5 de las 12 pruebas con
base (las de bloqueo vencido y el flujo completo).

**Riesgos:**

- **`BloqueadoHasta` pasa a UTC.** No hay cambio de esquema. Un bloqueo escrito por la versión anterior (hora
  local del servidor) se lee ahora como UTC: durante como mucho 15 minutos después del despliegue, un bloqueo
  vigente puede aparecer ya vencido (nunca más largo, porque Argentina está detrás de UTC).
- **Los errores sin bloqueo (1 o 2) siguen sin vencer por tiempo**, porque la tabla no guarda cuándo fue el
  último error. Se limpian con un login exitoso o al vencer un bloqueo. Para que 1 o 2 errores viejos tampoco
  cuenten hace falta una columna (`UltimoIntentoFallido datetime NULL`) y una migración aplicada **antes** del
  despliegue del código: es una decisión de despliegue, no se hizo.
- `UPDATE ... OUTPUT` sin `INTO` falla si algún día se le agrega un trigger a `Usuario`.
- El bloqueo es por cuenta: alguien que conozca un nombre de usuario puede dejarlo bloqueado con 3 intentos
  cada 15 minutos. Se anota en la Tarea 4 (límite por IP en `/login`).

**Qué falta / decisiones:**

- Un botón o endpoint de "Desbloquear" en el Configurador (hoy se desbloquea poniendo una contraseña nueva con
  `PUT /usuarios/{id}`, por recuperación por mail o por SQL).
- Decidir si se agrega la columna del último intento para que el conteo parcial también venza por tiempo.

---

## Tarea 3 — `feat/crear-usuario-profesional-admin`

**Pedido:** hoy no hay forma de crear el usuario de un profesional aprobado: `PUT /profesionales/{id}/aprobar`
solo pone `Estado = 'Activo'`, no crea usuario ni manda mail (y la pantalla promete un mail). Escribir primero un
diseño corto con las opciones A (el Configurador crea el usuario y lo vincula) y B (invitación por mail con token
de un solo uso) e implementar **solo la A**, dejando la B documentada.

### Diseño

**Cómo está hoy (código real):**

- `PUT /profesionales/{id}/aprobar` cambia el estado y audita. No hay nada más: la pantalla "Solicitudes de
  Registro" dice *"Al profesional le llega un mail para crear su usuario"* y es falso.
- Un profesional `Activo` sin `Usuario` no puede entrar. El único camino es armarlo a mano con `POST /usuarios` +
  `POST /usuarioprofesionales` desde la consola del navegador: no hay pantalla.
- **El rol sale de los vínculos.** En `POST /login`, un usuario sin fila en `UsuarioPaciente` ni en
  `UsuarioProfesional` es *configurador*. Entre `POST /usuarios` y `POST /usuarioprofesionales` la cuenta nueva es
  un administrador; si la segunda llamada no llega o falla, queda una cuenta **administradora** con clave temporal.
- `POST /usuarios` no valida nada: usuario o mail repetidos terminan en 500 (UNIQUE de `Username` y
  `UQ_Usuario_Mail`) y no exige largo de contraseña. `POST /usuarioprofesionales` tampoco: un profesional
  inexistente es 500, `UsuarioProfesional.IdProfesional` no es único (un profesional puede quedar con dos usuarios)
  y no deja auditoría.
- Hay medio camino de la B en el repo, sin terminar: `PANTALLAS/crear-cuenta-profesional.html` (la página que
  recibe `?token=`), `ActivarCuentaProfesionalRequest`, `EmailService.EnviarAprobacionProfesional` y la columna
  `Profesional.Token`. Falta el backend: en la historia de `main` nunca estuvo `POST /profesionales/activar-cuenta`
  (el comentario de la página dice que está en la rama `videollamadas-seguras`, sin mergear).

**Opción A — el Configurador crea el usuario** *(se implementa)*

- En "Pacientes y profesionales", al elegir un profesional `Activo` sin usuario aparece **Crear usuario**; también
  se ofrece al aprobar una solicitud. Pide nombre de usuario y mail (el del profesional por defecto) y la clave:
  la escribe el Configurador o se genera una temporal que se muestra **una sola vez**.
- Validaciones: usuario de 3 a 50 caracteres (letras, números, `.`, `_`, `-`) y único; contraseña de al menos 8;
  mail con formato válido y único; el profesional existe, está `Activo` y no tiene ya un usuario.
- Auditoría: "Alta Usuario" (igual que `POST /usuarios`) y "Vinculación Usuario-Profesional". Nunca se registra la
  contraseña.
- **Desvío del pedido, a propósito:** el pedido decía que la pantalla llame a `POST /usuarios` y
  `POST /usuarioprofesionales`. Por lo del rol (arriba) la pantalla llama a **un endpoint nuevo**,
  `POST /profesionales/{id}/usuario`, que hace las dos cosas en **una transacción** con los mismos chequeos y la
  misma auditoría. Los dos endpoints existentes quedan como están. Si se prefiere el camino literal, el cambio
  está acotado a una función del frontend; se desaconseja por el riesgo del administrador huérfano.
- Ventajas: no necesita servidor de correo, es inmediato, una migración no hace falta. Desventajas: la clave
  temporal pasa por el Configurador y hay que hacérsela llegar al profesional por otro medio; el profesional no
  elige la suya (puede cambiarla con "Olvidé mi contraseña", que usa su mail).

**Opción B — invitación por mail con token de un solo uso** *(solo diseño)*

1. Al aprobar, el backend genera un token (32 bytes aleatorios), guarda **solo su hash** SHA-256 con vencimiento
   (30 minutos como la recuperación de contraseña, o 7 días para una invitación) y manda
   `EmailService.EnviarAprobacionProfesional` con el link a `crear-cuenta-profesional.html?token=...`.
2. La página (ya existe) pide usuario y contraseña y llama a `POST /profesionales/activar-cuenta`.
3. Ese endpoint, público y con límite por IP, busca el token vigente, valida usuario y contraseña (misma política
   que la A), crea `Usuario` + `UsuarioProfesional` en una transacción, marca el token como usado y audita.
4. Hace falta: dónde guardar el token (la columna `Profesional.Token` es de 64 caracteres y no tiene vencimiento:
   conviene una tabla `InvitacionProfesional`, con migración), reenvío de la invitación, qué hacer si el mail
   rebota, y escapar el nombre en `EnviarAprobacionProfesional` (hoy lo interpola sin escapar en el HTML).
- Ventajas: la clave nunca pasa por el Configurador y el profesional elige la suya. Desventajas: depende del
  correo saliente (hoy una cuenta de Gmail en `appsettings`), una tabla y una pantalla pública más a mantener y
  más superficie pública para endurecer.

**Recomendación:** A ahora (resuelve el bloqueo operativo sin tocar el correo) y B más adelante, reusando la
misma validación y la misma transacción; el endpoint nuevo de A ya queda con la lógica que B necesita.

### Implementación (opción A)

**Commits:** `be3be23` (backend y pruebas), `a7a6170` (frontend y textos), `47ebb2b` (el diseño de arriba).

**Qué se cambió:**

| Archivo | Cambio |
|---|---|
| `Services/CrearUsuarioProfesionalService.cs` (nuevo) | Validación, contraseña temporal, hash (el mismo SHA-256 del resto de la app) y traducción de lo que decide la base a 201 / 400 / 404 / 409. |
| `DAOs/UsuarioProfesionalDAO.cs` | `CrearUsuarioYVincular`: una transacción que bloquea la fila del profesional (`UPDLOCK`), revalida estado, vínculo, usuario y mail, e inserta `Usuario` y `UsuarioProfesional`; `IdsProfesionalesActivosSinUsuario`. |
| `Program.cs` | `POST /profesionales/{id}/usuario` y `GET /profesionales/sin-usuario` (solo Configurador). |
| `Models/CrearUsuarioProfesionalRequest.cs` (nuevo) | Cuerpo del pedido. |
| `PANTALLAS/Pantalla_Configurador/js/dashboard.js` | Botón y modal "Crear usuario"; aviso "Sin usuario"; al aprobar una solicitud se ofrece crear el usuario ahí mismo. Archivo de finales de línea mixtos: se editó solo en zonas CRLF y no cambió el conteo de líneas LF-solo. |
| `PANTALLAS/Pantalla_Usuario/Usuario/registro_profesional.html` | Dos frases que prometían un mail que nunca se manda. |
| `docs/MATRIZ_ENDPOINTS.md` | Los dos endpoints nuevos. |
| `YoSoy.Tests/CrearUsuarioProfesionalServiceTests.cs`, `.../CrearUsuarioProfesionalEndpointsTests.cs` (nuevos) | 32 pruebas sin base y 12 con base. |

**Contrato de `POST /profesionales/{id}/usuario`** (cuerpo `{ username, mail?, contrasena?, generarContrasena }`):

| Caso | Respuesta |
|---|---|
| Creado | **201** `{ idUsuario, idProfesional, username, mail, contrasenaTemporal }` (`contrasenaTemporal` solo si se pidió generarla; header `Cache-Control: no-store`) |
| Usuario fuera de `^[a-zA-Z0-9_.-]{3,50}$`, mail inválido o ausente, contraseña de menos de 8 o más de 100 | **400** con el motivo |
| Profesional inexistente | **404** |
| Profesional que no está `Activo`, que ya tiene usuario, usuario ya usado, mail ya usado | **409** con el motivo (esta pantalla la usa el Configurador: los mensajes son específicos a propósito) |
| Sin sesión / otro rol | 401 / 403 |

**Auditoría:** "Alta Usuario" (`Entidad = Usuario`, `Detalle = username`) y "Vinculación Usuario-Profesional"
(`Entidad = Profesional`), a nombre del Configurador. La contraseña no se registra en ningún lado.

**Resultado de las pruebas:**

| Corrida | `main` | Esta rama |
|---|---|---|
| Sin base | 551 ok / 79 fallan | 583 ok / 91 fallan: las 79 + las 12 de integración nuevas |
| Con la copia de esquema en `tempdb` | 608 ok / 40 fallan | **652 ok / 40 fallan** (las mismas 40, **0 fallas nuevas**, 44 tests nuevos) |
| Navegador real (Chromium, API simulada) | — | 20/20: botón solo donde corresponde, validación sin enviar, 409 con el modal abierto, contraseña temporal una sola vez y fuera del DOM al cerrar, copiar, flujo desde Solicitudes |

Se verificó con una mutación que la prueba de atomicidad detecta el problema: con un `COMMIT` anticipado del
`Usuario`, la prueba que hace fallar el vínculo con un trigger temporal falla.

**Cómo probarlo a mano** (API levantada contra una base de prueba): Configurador → *Pacientes y profesionales* →
elegir un profesional activo → **Crear usuario**; o *Solicitudes de registro* → **Aprobar** → "Crear usuario".
Después entrar con ese usuario por la pantalla de Profesional.

**Riesgos y límites:**

- **La contraseña temporal no se obliga a cambiar:** no hay columna `DebeCambiarPassword`. El profesional puede
  cambiarla con "Olvidé mi contraseña" (le llega un mail). Pedirla en el primer ingreso requiere esquema.
- **El hash sigue siendo SHA-256 sin sal**, igual que en el resto de la app (ver Tarea 4).
- **Un mail por cuenta:** `UQ_Usuario_Mail` hace que si el mail de la ficha ya es de otra cuenta (por ejemplo el
  profesional también es paciente con el mismo mail) la creación dé 409 y haya que escribir otro mail.
- **Un usuario dado de baja sigue contando como "ya tiene usuario"**; no hay pantalla para reactivar un usuario.
- `POST /usuarios` y `POST /usuarioprofesionales` siguen sin validar (usuario o mail repetidos dan 500); se anota
  en la Tarea 4.
- La política de contraseña es solo de largo (8); no exige complejidad.

**Qué falta / decisiones:**

- Opción B (invitación por mail): decidir si se hace; el diseño está arriba y la validación y la transacción ya
  son reutilizables.
- Pedir el cambio de contraseña en el primer ingreso (columna nueva + migración).
- Una acción "Restablecer contraseña" del Configurador desde la pantalla (hoy: `PUT /usuarios/{id}` con la nueva).

---

## Tarea 4 — `chore/hardening-mensajes`

**Pedido:** (a) revisar los endpoints públicos de alta y los mensajes 409/error que permiten enumerar DNI, mail
o matrícula y unificarlos a mensajes neutrales sin romper la experiencia; (b) revisar la política de contraseña
mínima (hoy 8) y proponer el cambio sin aplicarlo si requiere una decisión; (c) listar otros hallazgos de
seguridad menores, sin arreglar los grandes.

**Commits:** `a4c9999` (código y pruebas).

**Resultado de las pruebas:**

| Corrida | `main` | Esta rama |
|---|---|---|
| Sin base | 551 ok / 79 fallan | 566 ok / 81 fallan: las 79 + las 2 de integración nuevas |
| Con la copia de esquema en `tempdb` | 608 ok / 40 fallan | **625 ok / 40 fallan** (las mismas 40, **0 fallas nuevas**, 17 tests nuevos) |

### (a) Endpoints públicos de alta y mensajes

| Endpoint | Qué responde si el dato ya existe | ¿Permite enumerar? | Qué se hizo |
|---|---|---|---|
| `POST /pacientes` | 409. Usuario en uso: mensaje propio. DNI o mail: **antes un mensaje distinto para cada uno** | Sí (DNI y mail) | **Cambiado**: DNI, mail y el resguardo de la carrera dan un único mensaje que no dice cuál coincidió y orienta a "ingresá o recuperá tu contraseña". El usuario en uso queda específico (lo elige la persona). |
| `POST /profesionales` | En `main`: **500** (matrícula repetida, especialidad inexistente) | No (pero tampoco es usable) | Lo resuelve la **Tarea 1** (409 con mensaje único y límite por IP). Esta rama no lo toca para no pisarse con ella. |
| `POST /recuperacion-password` | 200 con el mismo mensaje exista o no el usuario y tenga o no mail | No | Ya estaba neutral. |
| `POST /recuperacion-password/reset` | "Link inválido o vencido" igual para token inexistente, vencido o usado | No | Ya estaba neutral. Se le agrega el mínimo de contraseña en el servidor (ver b). |
| `POST /login` | 401 igual para usuario inexistente, inactivo o clave mala; **423 solo si la cuenta existe y está bloqueada** | **Sí** (la cuenta existe) | **No se cambió**: ver H9. |
| `GET /reservar/{slug}`, `GET /public/profesionales/{slug}/disponibilidad` | 404 idéntico si el slug no existe o el link está apagado | No | Ya estaba neutral. |
| `POST /public/profesionales/{slug}/reservar` | Mensajes de las reglas (por ejemplo "Ya tenés un turno con ese profesional ese día") para **cualquier** `idPaciente` | Sí | Ver H2: es un problema mayor que el mensaje. |

**Límite de esta mejora:** unificar la redacción no cierra la enumeración. Quien puede variar un dato por vez
(un DNI nuevo con un mail que ya existe, y al revés) sigue deduciendo cuál existe. Lo que la frena de verdad es
el límite por IP del alta (política `registro`: 10 pedidos cada 10 minutos, ya aplicada) y, a más largo plazo,
no confirmar la existencia en el momento (mail de verificación con respuesta siempre igual, como ya hace la
recuperación de contraseña).

### (b) Política de contraseña

**Hoy no es 8: es 6 en `main`, y en varios lugares no hay ninguna.**

| Lugar | Servidor | Pantalla |
|---|---|---|
| Alta de paciente (`POST /pacientes`) | mínimo 6 | "Al menos 6 caracteres" |
| Restablecimiento por mail (`POST /recuperacion-password/reset`) | **ninguno** (ahora: mínimo 6 y máximo 100) | `reset-password.html`: 6 |
| Crear cuenta de profesional (`crear-cuenta-profesional.html`) | el endpoint no existe en `main` | 6 |
| `POST /usuarios`, `PUT /usuarios/{id}` (Configurador) | **ninguno** | — |
| Tarea 3: `POST /profesionales/{id}/usuario` | mínimo 8 (y máximo 100) | 8 |

**Lo aplicado (sin decisión de producto):** el mínimo que ya existía se centralizó en `PoliticaContrasena`
(`Minimo = 6`, `Maximo = 100`) y se exige también en el restablecimiento, antes de mirar el token. El mensaje
del mínimo no cambió.

**Propuesta, no aplicada (requiere decidir):**

1. Subir el mínimo a **8** y exigirlo igual en todos los lugares de la tabla. Es cambiar `PoliticaContrasena.Minimo`
   y el `minimo` de `reset-password.html`, `crear-cuenta-profesional.html` y el texto de `registro.html`. Solo
   afecta contraseñas **nuevas**: el login no valida el largo y las existentes de 6 o 7 caracteres siguen
   sirviendo.
2. Rechazar la contraseña igual al usuario o al mail, y una lista corta de las más comunes (`12345678`,
   `password`, ...). Sin exigir mayúsculas ni símbolos (el largo importa más que la composición).
3. Unificar `CrearUsuarioProfesionalService` (Tarea 3) para que use `PoliticaContrasena` en vez de sus propias
   constantes, cuando se mergeen las dos ramas.
4. Cambiar el algoritmo de hash (ver H6): es lo que más pesa, más que el largo.

### (c) Otros hallazgos de seguridad

Verificados leyendo el código (y, donde se indica, con una prueba descartable contra una base de prueba). Ninguno
se aplicó: o requieren una decisión de producto o son más grandes que esta tarea.

| # | Severidad | Hallazgo | Evidencia | Propuesta |
|---|---|---|---|---|
| H1 | **Alta** | **Credenciales reales en el repositorio:** `appsettings.json` versiona `Email:Password` (la cuenta de Gmail) y queda en el historial de git. | Clave `Password` y host de Gmail presentes (el valor no se imprimió). | Rotar la contraseña de aplicación **ahora** (sacarla del historial no alcanza), pasarla a `Email__Password` (variable de entorno / user-secrets) y dejar un valor vacío en `appsettings.json`. |
| H2 | **Alta** | **La reserva pública toma `idPaciente` del cuerpo y no autentica.** Cualquiera puede crear turnos Pendientes a nombre de cualquier paciente (hasta 20 pedidos por minuto por IP), llenar la agenda de un profesional y disparar avisos al paciente; y las respuestas de las reglas revelan si ese paciente ya tiene turno ese día. | `Program.cs`: `POST /public/profesionales/{slug}/reservar` → `reglas.Solicitar(req.IdPaciente, ...)`, sin `RequireAuthorization()`; figura como público en `GuardiaEndpointsTests`. La página (`reservar.js`) ya exige sesión de paciente y manda el token. | Exigir autenticación, tomar `IdPaciente` del token (rechazar si el cuerpo trae otro) y sacar la ruta de la lista de públicos. No se aplicó porque cambia el contrato y la lista de la guardia. |
| H3 | **Alta** | **Dar de baja no quita el acceso.** `DELETE /profesionales/{id}` y `DELETE /pacientes/{id}` ponen `Estado = 'Inactivo'` en la ficha, pero `/login` solo mira `Usuario.Estado`, y el JWT (8 h) no se revalida. | Probado: un profesional con `Profesional.Estado = 'Inactivo'` entra por `/login` (200). | En el login, exigir también que la ficha vinculada esté `Activo`; y/o dar de baja el `Usuario` en esos endpoints; y revalidar el estado por pedido o acortar el token. |
| H4 | Media | **El rol "configurador" es el valor por defecto:** un usuario sin vínculo a paciente ni a profesional entra como administrador (en `/login` y en el modo legacy). Cualquier `Usuario` huérfano es un administrador. | `Program.cs` (`/login`), `LegacyHeaderAuthHandler`. Las tablas `Rol` y `UsuarioRol` existen y no se usan. | Rol explícito (por `UsuarioRol`); sin rol, sin acceso. La Tarea 3 evita crear huérfanos, pero la causa sigue. |
| H5 | Media | **`POST /login` no tiene límite por IP.** Se pueden probar contraseñas contra todas las cuentas sin freno (el bloqueo es por cuenta: 3 intentos cada 15 minutos) y cualquiera puede mantener bloqueada una cuenta conocida. | `Program.cs`: `/login` sin `RequireRateLimiting`. | Política de límite por IP para `/login` (por ejemplo 20 cada 5 minutos) además del bloqueo por cuenta. |
| H6 | Media | **Contraseñas con SHA-256 sin sal.** Hash rápido y sin sal en `/login`, el alta, el restablecimiento y `POST/PUT /usuarios`. | `SHA256.HashData(...)` en `Program.cs` (y en `CrearUsuarioProfesionalService`, Tarea 3). | PBKDF2 (`Rfc2898DeriveBytes`, SHA-256, ≥ 600 000 iteraciones) o Argon2id, guardando `algoritmo$iteraciones$sal$hash` en la misma columna (255 caracteres alcanzan) y migrando de forma transparente en el próximo login exitoso. |
| H7 | Media | **Datos personales a roles de más:** `GET /profesionales/{id}` devuelve la ficha completa (DNI, mail, teléfono, fecha de nacimiento) a cualquier usuario autenticado, paciente incluido; `GET /pacientes/{id}` lo puede leer cualquier profesional (no solo los suyos); `PUT /pacientes/{id}` lo puede modificar cualquier profesional. | `Program.cs`: solo se restringe al paciente a su propia ficha. | Una proyección pública para `GET /profesionales/{id}`; chequear que el paciente sea del profesional en `GET`/`PUT /pacientes/{id}`. |
| H8 | Media | **Asignación masiva en las ediciones:** `PUT /profesionales/{id}` y `PUT /pacientes/{id}` escriben todo el objeto del cuerpo, `Estado` incluido: un profesional puede cambiarse el estado, un paciente el DNI o el estado. | `ProfesionalDAO.Modificar`, `PacienteDAO.Modificar`. | Un DTO con solo los campos editables; el estado, solo por los endpoints del Configurador. |
| H9 | Baja | **Mensajes que confirman existencia:** el 423 de `/login` solo sale si la cuenta existe; `GET /pacientes/{id}` da 403 o 404 según el id exista o no. | `Program.cs`. | Decisión de producto: responder 401 igual y avisar del bloqueo por otro canal, o aceptarlo a cambio de que el usuario sepa por qué no entra. |
| H10 | Baja | **`PUT /usuarios/{id}` sin `passwordHash` da 500** (no pisa la contraseña, pero editar mail o estado desde la API exige mandar siempre una contraseña). `POST /usuarios` con usuario o mail repetidos también da 500. | Probado contra una base de prueba. | Validar y responder 409 / 400 como en la Tarea 1; permitir editar sin tocar la contraseña. |
| H11 | Baja | **HTML sin escapar en un mail:** `EmailService.EnviarAprobacionProfesional` interpola el nombre (que viene del formulario público) directo en el HTML. Hoy nadie lo llama; lo usaría la opción B de la Tarea 3. | `Services/EmailService.cs`. | `WebUtility.HtmlEncode` como ya hace `EnviarAviso`. |
| H12 | Baja | **Se sirve cualquier archivo de `PANTALLAS/`** (`ServeUnknownFileTypes = true`): hoy hay un `README.md` en `shared/vendor/` y el resto es web, pero un `.sql` o un respaldo puesto ahí quedaría público. | `Program.cs` (`UseStaticFiles`). | Lista de extensiones permitidas o una prueba que falle si aparece un tipo no web. |
| H13 | Baja | **Sin CSP ni revocación de tokens;** el JWT vive en `localStorage` (un XSS lo roba). `ValidateIssuer`/`ValidateAudience` en `false`. HSTS depende de Caddy. | `Program.cs`. | Anotado en `CAMBIOS_AGUS.md` como mejora futura; empezar por una CSP en modo solo-reporte. |

**Orden sugerido para atacarlos:** H1 (rotar la clave de correo, es operativo y no espera), H2 y H3 (cambios
chicos con mucho efecto), después H5 y H6.

**Qué falta / decisiones:**

- Elegir el mínimo de contraseña (¿8?) y si se agrega la lista de contraseñas comunes.
- Decidir qué hacer con el 423 del login (H9) y con la reserva pública sin autenticar (H2: ¿se quiere permitir
  reservar sin cuenta algún día?).
- `POST /pacientes` queda con el mensaje de usuario en uso específico: si se prefiere neutral también, es
  `MensajesConflictoRegistro.UsuarioEnUso`.
