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
- **Pantallas:** los cambios de frontend se verificaron en navegadores reales (Playwright, Chromium) con un servidor
  estático de solo lectura y la API simulada, con el reloj y la zona del navegador fijados (Argentina y UTC). Esos
  scripts no se versionan.
- **Finales de línea:** los archivos nuevos y editados quedan en CRLF en el árbol de trabajo, como el resto del
  repo; se verificó con `git ls-files --eol` antes de editar.

## Estado

| # | Tarea | Rama | Estado |
|---|---|---|---|
| 1 | Registro de profesional: 409 / 400 en vez de 500 | `fix/registro-profesional-409` | Hecha |
| 2 | Bloqueo del login: el conteo vence y se desbloquea | `fix/admin-lockout-decay` | Hecha |
| 3 | Crear el usuario de un profesional desde el Configurador | `feat/crear-usuario-profesional-admin` | Hecha (opción A; la B queda como diseño) |
| 4 | Mensajes que permiten enumerar, política de contraseña y hallazgos | `chore/hardening-mensajes` | Hecha (parte aplicada; hallazgos para decidir) |
| 5 | Tests legacy independientes de la base de desarrollo | `test/legacy-independientes` | Hecha |
| 6 | Documentación del servidor y del despliegue | `docs/servidor-y-despliegue` | Hecha (sin verificar contra el VPS) |
| 7 | Revisión del flujo de turnos: zona horaria explícita y restos de la regla de 24 h | `chore/revision-turnos` | Hecha |

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
| H1 · *parcial* | **Alta** | **Credenciales reales en el repositorio:** `appsettings.json` versiona `Email:Password` (la cuenta de Gmail) y queda en el historial de git. | Clave `Password` y host de Gmail presentes (el valor no se imprimió). **Corrección del 2026-10-16:** eso era cierto hasta el 2026-10-06; `Email:Password` ya está vacío en `main` desde `160a716` (2026-10-07), y lo que queda es el valor viejo en el historial (ver (d)). | Rotar la contraseña de aplicación **ahora** (sacarla del historial no alcanza), pasarla a `Email__Password` (variable de entorno / user-secrets) y dejar un valor vacío en `appsettings.json`. |
| H2 · *abierto* | **Alta** | **La reserva pública toma `idPaciente` del cuerpo y no autentica.** Cualquiera puede crear turnos Pendientes a nombre de cualquier paciente (hasta 20 pedidos por minuto por IP), llenar la agenda de un profesional y disparar avisos al paciente; y las respuestas de las reglas revelan si ese paciente ya tiene turno ese día. | `Program.cs`: `POST /public/profesionales/{slug}/reservar` → `reglas.Solicitar(req.IdPaciente, ...)`, sin `RequireAuthorization()`; figura como público en `GuardiaEndpointsTests`. La página (`reservar.js`) ya exige sesión de paciente y manda el token. | Exigir autenticación, tomar `IdPaciente` del token (rechazar si el cuerpo trae otro) y sacar la ruta de la lista de públicos. No se aplicó porque cambia el contrato y la lista de la guardia. |
| H3 · *abierto* | **Alta** | **Dar de baja no quita el acceso.** `DELETE /profesionales/{id}` y `DELETE /pacientes/{id}` ponen `Estado = 'Inactivo'` en la ficha, pero `/login` solo mira `Usuario.Estado`, y el JWT (8 h) no se revalida. | Probado: un profesional con `Profesional.Estado = 'Inactivo'` entra por `/login` (200). | En el login, exigir también que la ficha vinculada esté `Activo`; y/o dar de baja el `Usuario` en esos endpoints; y revalidar el estado por pedido o acortar el token. |
| H4 · *abierto, mitigado al crear* | Media | **El rol "configurador" es el valor por defecto:** un usuario sin vínculo a paciente ni a profesional entra como administrador (en `/login` y en el modo legacy). Cualquier `Usuario` huérfano es un administrador. | `Program.cs` (`/login`), `LegacyHeaderAuthHandler`. Las tablas `Rol` y `UsuarioRol` existen y no se usan. | Rol explícito (por `UsuarioRol`); sin rol, sin acceso. La Tarea 3 evita crear huérfanos, pero la causa sigue. |
| H5 · *abierto* | Media | **`POST /login` no tiene límite por IP.** Se pueden probar contraseñas contra todas las cuentas sin freno (el bloqueo es por cuenta: 3 intentos cada 15 minutos) y cualquiera puede mantener bloqueada una cuenta conocida. | `Program.cs`: `/login` sin `RequireRateLimiting`. | Política de límite por IP para `/login` (por ejemplo 20 cada 5 minutos) además del bloqueo por cuenta. |
| H6 · *abierto* | Media | **Contraseñas con SHA-256 sin sal.** Hash rápido y sin sal en `/login`, el alta, el restablecimiento y `POST/PUT /usuarios`. | `SHA256.HashData(...)` en `Program.cs` (y en `CrearUsuarioProfesionalService`, Tarea 3). | PBKDF2 (`Rfc2898DeriveBytes`, SHA-256, ≥ 600 000 iteraciones) o Argon2id, guardando `algoritmo$iteraciones$sal$hash` en la misma columna (255 caracteres alcanzan) y migrando de forma transparente en el próximo login exitoso. |
| H7 · *abierto* | Media | **Datos personales a roles de más:** `GET /profesionales/{id}` devuelve la ficha completa (DNI, mail, teléfono, fecha de nacimiento) a cualquier usuario autenticado, paciente incluido; `GET /pacientes/{id}` lo puede leer cualquier profesional (no solo los suyos); `PUT /pacientes/{id}` lo puede modificar cualquier profesional. | `Program.cs`: solo se restringe al paciente a su propia ficha. | Una proyección pública para `GET /profesionales/{id}`; chequear que el paciente sea del profesional en `GET`/`PUT /pacientes/{id}`. |
| H8 · *abierto* | Media | **Asignación masiva en las ediciones:** `PUT /profesionales/{id}` y `PUT /pacientes/{id}` escriben todo el objeto del cuerpo, `Estado` incluido: un profesional puede cambiarse el estado, un paciente el DNI o el estado. | `ProfesionalDAO.Modificar`, `PacienteDAO.Modificar`. | Un DTO con solo los campos editables; el estado, solo por los endpoints del Configurador. |
| H9 · *abierto, decisión tuya* | Baja | **Mensajes que confirman existencia:** el 423 de `/login` solo sale si la cuenta existe; `GET /pacientes/{id}` da 403 o 404 según el id exista o no. | `Program.cs`. | Decisión de producto: responder 401 igual y avisar del bloqueo por otro canal, o aceptarlo a cambio de que el usuario sepa por qué no entra. |
| H10 · *abierto* | Baja | **`PUT /usuarios/{id}` sin `passwordHash` da 500** (no pisa la contraseña, pero editar mail o estado desde la API exige mandar siempre una contraseña). `POST /usuarios` con usuario o mail repetidos también da 500. | Probado contra una base de prueba. | Validar y responder 409 / 400 como en la Tarea 1; permitir editar sin tocar la contraseña. |
| H11 · *abierto* | Baja | **HTML sin escapar en un mail:** `EmailService.EnviarAprobacionProfesional` interpola el nombre (que viene del formulario público) directo en el HTML. Hoy nadie lo llama; lo usaría la opción B de la Tarea 3. | `Services/EmailService.cs`. | `WebUtility.HtmlEncode` como ya hace `EnviarAviso`. |
| H12 · *abierto* | Baja | **Se sirve cualquier archivo de `PANTALLAS/`** (`ServeUnknownFileTypes = true`): hoy hay un `README.md` en `shared/vendor/` y el resto es web, pero un `.sql` o un respaldo puesto ahí quedaría público. | `Program.cs` (`UseStaticFiles`). | Lista de extensiones permitidas o una prueba que falle si aparece un tipo no web. |
| H13 · *abierto* | Baja | **Sin CSP ni revocación de tokens;** el JWT vive en `localStorage` (un XSS lo roba). `ValidateIssuer`/`ValidateAudience` en `false`. HSTS depende de Caddy. | `Program.cs`. | Anotado en `CAMBIOS_AGUS.md` como mejora futura; empezar por una CSP en modo solo-reporte. |

**Orden sugerido para atacarlos:** H1 (rotar la clave de correo, es operativo y no espera), H2 y H3 (cambios
chicos con mucho efecto), después H5 y H6.

**Qué falta / decisiones:**

- Elegir el mínimo de contraseña (¿8?) y si se agrega la lista de contraseñas comunes.
- Decidir qué hacer con el 423 del login (H9) y con la reserva pública sin autenticar (H2: ¿se quiere permitir
  reservar sin cuenta algún día?).
- `POST /pacientes` queda con el mensaje de usuario en uso específico: si se prefiere neutral también, es
  `MensajesConflictoRegistro.UsuarioEnUso`.

### (d) Estado de cada hallazgo — revisión del 2026-10-16

Se releyó la tabla de arriba y se contrastó con el código de `main` (`27ad47f`) y de todas las ramas locales con commits
propios (`git show <rama>:<archivo>`, sin modificar ninguna): las siete de la tanda anterior, `fix/sacar-secretos-appsettings`
y `qa-exploratorio` (un informe de QA de interfaz, sin hallazgos de seguridad). **Ninguno de los 13 está resuelto en `main` ni
en una rama local.** H1 avanzó a medias, H4 está mitigado solo al crear usuarios y los otros once son idénticos en todas las
ramas.

| # | Estado | Dónde se verificó | Siguiente paso concreto |
|---|---|---|---|
| H1 | **Parcial** | `appsettings.json` en `main`: `Email:Password` vacío desde `160a716` (2026-10-07). `fix/sacar-secretos-appsettings` (`33a1cd0`, sin mergear): también `Email:From` (la cuenta, que es el usuario del SMTP) y el aviso "Correo no configurado". El valor viejo (16 caracteres) sigue en 11 versiones del archivo (2026-06-02 a 2026-10-06), dentro de `main` y `origin/main`. | 1) **Revocar** la contraseña de aplicación en Google y crear otra: es lo único que invalida la vieja. 2) Cargar `Email__From`, `Email__Password` y `Email__AppUrl` en `/opt/yosoy/config/.env` **antes** de publicar. 3) Mergear `fix/sacar-secretos-appsettings`. Si se revoca, no hace falta reescribir el historial. |
| H2 | **Abierto** | `Program.cs`, igual en todas las ramas: `POST /public/profesionales/{slug}/reservar` sin `RequireAuthorization` y con `req.IdPaciente` del cuerpo; solo lo frena la política `publico`. Figura como pública en `YoSoy.Tests/GuardiaEndpointsTests.cs`. `Pantalla_Publica/js/reservar.js` ya manda la sesión. | 1) `.RequireAuthorization(p => p.RequireRole(Roles.Paciente))` y tomar `IdPaciente` de `identidad.Resolver(ctx)` (403 si el cuerpo trae otro). 2) Sacar la ruta de la lista de públicos de `GuardiaEndpointsTests` y de `docs/MATRIZ_ENDPOINTS.md`. 3) Pruebas: 401 sin token, 403 con otro `idPaciente`, 201 con el propio. **Decisión tuya:** si algún día se quiere reservar sin cuenta (hoy la página igual pide ingresar). |
| H3 | **Abierto** | `ProfesionalDAO.Baja` y `PacienteDAO.Baja` solo ponen la ficha en `Inactivo`; `/login` mira solo `Usuario.Estado`. Igual en todas las ramas. | 1) En cada `Baja`, en la misma transacción, `UPDATE Usuario SET Estado = 'Inactivo'` de los usuarios vinculados (`UsuarioPaciente` / `UsuarioProfesional`). 2) Un script en `migrations/` (sin ejecutar) que inactive los usuarios de las fichas que ya están dadas de baja. 3) En `/login`, negar también si la ficha vinculada no está `Activo`. 4) Decidir si se revalida el estado en cada pedido o se acorta el JWT de 8 h. Prueba: baja → `/login` da 401. |
| H4 | **Abierto** (mitigado al crear) | `/login` devuelve `rol = "configurador"` cuando no hay vínculo (igual en todas las ramas); `UsuarioRolDAO` y las tablas `Rol` y `UsuarioRol` existen y el login no las usa. `feat/crear-usuario-profesional-admin` evita crear huérfanos (usuario y vínculo en una transacción) pero no cambia el valor por defecto. | 1) Un script en `migrations/` (sin ejecutar) que inserte en `UsuarioRol` el rol de Configurador de los administradores actuales (los `Usuario` sin vínculo). 2) `/login` y `LegacyHeaderAuthHandler` toman el rol de `UsuarioRol`; sin rol, 403. 3) Probarlo en `tempdb` con un usuario huérfano. |
| H5 | **Abierto** | `Program.cs`: `MapPost("/login")` sin `RequireRateLimiting` en todas las ramas. `fix/admin-lockout-decay` mejora el bloqueo por cuenta (cuándo vence el conteo), no agrega un límite por IP. | 1) Política `login` por IP en `AddRateLimiter` (ventana fija, por ejemplo 20 cada 5 minutos, configurable) y `.RequireRateLimiting("login")` en `/login`. 2) Prueba con el límite bajado por configuración: el pedido N+1 da 429, porque el limitador corta antes del handler. |
| H6 | **Abierto** | 7 usos de `SHA256.HashData` en `Program.cs` (alta, login, restablecimiento, `POST`/`PUT /usuarios`) y otro en `CrearUsuarioProfesionalService` (Tarea 3), sin cambios. `Usuario.PasswordHash` es `nvarchar(255)`: alcanza para `pbkdf2$iteraciones$sal$hash`. | 1) `Services/HashContrasena.cs`: PBKDF2-SHA256 con 600 000 iteraciones o más y una sal por usuario; `Verificar` acepta el hex SHA-256 viejo e indica "rehacer". 2) `/login` rehace el hash después de un ingreso correcto; el alta, el restablecimiento, `POST`/`PUT /usuarios` y la Tarea 3 usan `HashContrasena.Crear`. 3) Pruebas unitarias sin base: formato, verificación y migración del hash viejo. |
| H7 | **Abierto** | `Program.cs`: `GET /profesionales/{id}` solo con `RequireAuthorization()` (la ficha completa, a cualquier rol); `GET` y `PUT /pacientes/{id}` solo restringen al paciente a su propia ficha. Igual en todas las ramas. | 1) Un DTO público para `GET /profesionales/{id}` (nombre y especialidad); la ficha completa, solo para el propio profesional y el Configurador. 2) En `GET`/`PUT /pacientes/{id}`, que el profesional solo entre si el paciente le está asignado (`PacienteProfesionalDAO`). 3) Pruebas de matriz de acceso (modelo: `CierreSeguridadTests`). |
| H8 | **Abierto** | `ProfesionalDAO.Modificar` y `PacienteDAO.Modificar` siguen escribiendo `Estado` y el resto desde el cuerpo. La Tarea 1 le sumó al `PUT` de profesionales solo el chequeo de matrícula y DNI repetidos. | 1) DTOs de edición sin `Estado` (ni DNI en pacientes). 2) El estado, solo por los endpoints de baja y alta del Configurador. 3) Prueba: un `PUT` con `estado: "Inactivo"` no lo cambia. |
| H9 | **Abierto** — decisión tuya | `/login` sigue respondiendo 423 solo si la cuenta existe (la Tarea 2 cambió cuándo vence el bloqueo, no esto). Igual en todas las ramas. | Decidir: (a) responder 401 igual y dejar el motivo en el log o avisar por otro canal, o (b) aceptar el 423 a cambio de que la persona sepa por qué no entra. Si es (a): `/login` devuelve 401 también bloqueada y se ajustan `BloqueoLoginIntegrationTests` y el mensaje de `index.html`. |
| H10 | **Abierto** | `PUT /usuarios/{id}` llama a `UsuarioDAO.Modificar`, que escribe `PasswordHash = @PasswordHash` siempre (sin contraseña, 500); la Tarea 2 solo le agregó el desbloqueo al cambiar la clave. `POST /usuarios` sigue sin validar. | 1) `Modificar` que no toque `PasswordHash` cuando viene vacío (`ISNULL(@Hash, PasswordHash)`). 2) Validar usuario y mail repetidos con 409 y la contraseña con `PoliticaContrasena` (Tarea 4), como hace `RegistroProfesionalService` (Tarea 1). 3) Pruebas. |
| H11 | **Abierto** | `EmailService.EnviarAprobacionProfesional` interpola `{nombreProfesional}` y `{link}` sin escapar en todas las ramas (también en `fix/sacar-secretos-appsettings`, que no toca ese texto). Hoy nadie lo llama. | Cambio mecánico: `WebUtility.HtmlEncode(nombreProfesional)` y `Uri.EscapeDataString(token)`; sacar el cuerpo a un método estático (como `CuerpoResetPassword`) y probarlo con `<b>` y comillas. |
| H12 | **Abierto** | `Program.cs`: `ServeUnknownFileTypes = true` en todas las ramas. | Una lista explícita de extensiones (`FileExtensionContentTypeProvider`) sin `ServeUnknownFileTypes`, y una prueba que recorra `PANTALLAS/` y falle si aparece un tipo que no es web. |
| H13 | **Abierto** | No hay `Content-Security-Policy` en el código; `ValidateIssuer` y `ValidateAudience` siguen en `false`; el JWT vive en `localStorage` y no hay revocación. | 1) Cabecera `Content-Security-Policy-Report-Only` (middleware o Caddy) y medir lo que reporta. 2) Endurecerla por etapas. 3) Para el token, decidir entre acortar la vida con renovación o una lista de revocación. |

**Orden sugerido, actualizado:** 1) H1, que es operativo y no espera: revocar la contraseña de Gmail, cargar las variables en el
servidor y recién después mergear `fix/sacar-secretos-appsettings`. 2) H3 y H2, cambios chicos con mucho efecto (accesos que no
se revocan y turnos a nombre de cualquiera). 3) H5. 4) H6, el que más pesa si alguna vez se filtra la base. 5) H4, H7 y H8
(control de acceso por rol y por pertenencia). 6) H10 y H11, mecánicos. 7) H12 y H13. H9, cuando lo decidas.

**Cómo repetir la verificación** (solo lectura, sobre cualquier rama; reemplazar `<rama>`):

```bash
git grep -n "ServeUnknownFileTypes" <rama> -- Program.cs                      # H12
git grep -c "SHA256.HashData" <rama> -- Program.cs                             # H6
git grep -n "RequireRateLimiting" <rama> -- Program.cs                         # H5 (ninguno es de /login)
git show <rama>:DAOs/PacienteDAO.cs | grep -n "Baja" -A 6                      # H3
git show <rama>:Services/EmailService.cs | grep -n "nombreProfesional"         # H11
git show <rama>:appsettings.json | python -c "import json,sys; e=json.load(sys.stdin)['Email']; print({k: ('vacío' if not e[k] else 'con valor') for k in ('From','Password')})"   # H1, sin mostrar valores
```

---

## Tarea 5 — `test/legacy-independientes`

**Pedido:** los tests legacy que fallan en `main` dependen de filas de la base de desarrollo. Analizar cuáles y
por qué, y hacerlos independientes (datos propios creados y limpiados por el test, o marcados como integración
con un trait que se pueda excluir). No borrar tests; si alguno es inservible, explicar por qué.

**Commits:** `e7d93ef` (utilidad, pruebas refactorizadas y marca de integración).

### Cómo se midió

No se tocó la base real. Se armó una **copia descartable del esquema** (`docs/produccion/01-produccion.sql` en
`tempdb`, sin su `CREATE DATABASE`/`USE`) y se corrió la suite completa contra ella. En una base con solo unos
catálogos mínimos, `main` falla **40 pruebas en 9 clases** (tus 43 son las mismas más las que dependen de otras
filas de tu base); sin ninguna base, 79 (las que necesitan `ConnectionStrings__YoSoy`).

| Entorno | `main` | Esta rama |
|---|---|---|
| Base vacía (solo lo que siembra `01-produccion.sql`) | no se midió | **649 de 649 pasan** |
| Base con catálogos mínimos (2 especialidades, 4 estados, 1 usuario) | 608 ok / **40 fallan** | **649 de 649 pasan** (las 40 pasan; 1 prueba nueva) |
| La misma suite dos veces seguidas, sin reconstruir la base | — | pasan las dos; no queda ningún paciente, profesional, usuario ni especialidad |
| Sin base, **sin** las de integración (`--filter "Categoria!=Integracion"`) | — | **539 ok, 0 fallas, 0 omitidas** |
| Sin base, todas | 551 ok / 79 fallan / 18 omitidas | 551 ok / 80 fallan / 18 omitidas: las 79 de siempre + la prueba nueva, que necesita base |

### Diagnóstico: qué suponía cada clase

Todas fallaban por lo mismo: suponían filas que existen en la base de desarrollo (el paciente 1, los profesionales
1 y 2, los usuarios 1 a 4, la historia 1, la obra social 1...) y no las creaban. Contra otra base, o contra la
tuya si cambia, rompían con una clave foránea (500 o `SqlException`).

| Clase | Fallaban | Suponía que existía | Cambio |
|---|---|---|---|
| `CierreSeguridadTests` | 10 (todas: fallaba `InitializeAsync`) | paciente 1 con su historia, profesionales 1 y 2 con vínculo, usuarios 1 a 4 y que el profesional 1 tuviera agenda | Paciente, dos profesionales, vínculo, historia y usuarios propios. La prueba del turno **ahora exige 201** y que quede a nombre del paciente del token: antes, sin agenda, la regla la rechazaba y la prueba no verificaba nada. |
| `PertenenciaConversacionTests` | 9 | profesionales 1 y 2, paciente 1 vinculado al 1, usuarios 1 a 4 | Datos propios; el paciente tiene un apellido propio para comprobar que el nombre no sale en la auditoría. |
| `VideoBaseRealTests` | 8 | paciente 1, profesionales 1 y 2, usuario 1, agenda de lunes a viernes de 09 a 18 del profesional 1 y estados con nombre ("Completado") | Datos propios, la misma agenda creada por la prueba, y los estados se buscan por descripción (si no existen, se crean y se borran). |
| `IdsCeroClinicaTests` | 6 | paciente 1, historia 1, obra social 1, tipo de consentimiento 1, profesionales 1 y 2, usuario 1 | Cada alta crea lo suyo. |
| `ChatsGestionTests` | 2 | profesionales 1 y 2, usuarios 1, 2 y 99 | Datos propios. |
| `IdsCeroAgendaTests` | 2 | profesional 1 | Profesional propio; solo se borran las agendas de ese profesional. |
| `ProfesionalLinkPublicoIntegrationTests` | 1 | profesional 1 ("Santiago Manto": le prendía el link y se lo restauraba) | Profesional propio; no toca ninguno real. Se agregó una prueba: un profesional inactivo no aparece por su slug. |
| `AsignacionIntegrationTests` | 1 | profesional 1 con agenda disponible y paciente 1 | Ambos propios, con su agenda; al final se verifica que no queda nada. |
| `TurnoDobleReservaIntegrationTests` | 1 | paciente 1, profesional 1 y que el estado con id 1 existiera | Datos propios y el estado "Pendiente" se busca por descripción. |

**También se arregló lo que no fallaba en esa base pero era frágil:**

| Prueba | Problema | Cambio |
|---|---|---|
| `IdsCeroPersonasTests`, `WhatsAppAdminEndpointsTests` | usaban el usuario 1 en el token; sin ese usuario la auditoría (con clave foránea a `Usuario`) fallaba: `POST /usuarios` daba 500 y la auditoría de WhatsApp quedaba vacía | usuario propio |
| `RegistroPublicoProfesionalTests` | tomaba "la primera especialidad que haya" (`TOP 1`) | especialidad propia |
| `EmocionesBaseRealTests` (PR 6) | tomaba la primera especialidad (compartida con otras pruebas en paralelo) y una prueba dejaba un profesional sin borrar | especialidad propia y limpieza por profesional |
| `EmocionesCifradoServiceTests` (PR 6) | **intermitente (~3 de cada 100)**: afirmaba que el texto cifrado no contenía `60`, y es base64 al azar | solo busca palabras largas |

**Ninguna prueba es inservible; no se borró ninguna.** Se agregó una.

### Cómo quedó resuelto

- **`YoSoy.Tests/DatosDePrueba.cs`** (nuevo). Cada prueba crea lo que necesita (`Paciente()`, `Profesional()`,
  `Usuario()`, `UsuarioDePaciente()`, `UsuarioDeProfesional()`, `PacienteProfesional()`, `HistoriaClinica()`,
  `Agenda()`, `Turno()`, `EstadoTurno()`, `ObraSocial()`...), con un prefijo único (`xtest` + 10 hexadecimales), y
  `Dispose()` lo borra todo en orden inverso al de creación.
- **Borrado en cascada por claves foráneas**, leídas de `sys.foreign_keys` (no de los mensajes de error, que
  cambian con el idioma del servidor): se borra primero lo que apunta a las filas creadas, de forma recursiva. Esto
  limpia también lo que crea el código bajo prueba (auditoría, notas, turnos pedidos por la API). Solo alcanza filas
  que apuntan a filas **creadas por la prueba**, que son nuevas. `AlLimpiar(tabla, columna, valor)` agrega una
  limpieza para lo que genera un endpoint.
- **Catálogo compartido:** `EstadoTurno(descripcion)` usa el estado si existe; si no, lo crea (tolerando que otra
  prueba lo cree a la vez) y al limpiar lo borra **sin cascada**: si otra prueba lo está usando, queda.
- **Trait `[Trait("Categoria", "Integracion")]`** en las 18 clases que necesitan base (110 pruebas). Las pruebas de
  integración nuevas de las otras ramas de esta tanda (Tareas 1 a 4) ya llevan la misma marca. Se verificó que no
  falta ninguna: sin base y sin `Integracion` no falla nada.

### Cómo correrlas

```
# Sin base de datos: solo lo que no la necesita (539 pruebas)
dotnet test YoSoy.Tests --filter "Categoria!=Integracion"

# Con una base DESCARTABLE (nunca la real: las pruebas escriben y borran filas propias)
#   1) crear una base vacía con docs/produccion/01-produccion.sql cambiándole el nombre
#   2) apuntar la cadena de conexión a ella, en el MISMO comando
ConnectionStrings__YoSoy="Server=...;Database=yosoy_prueba;..." dotnet test YoSoy.Tests
```

### Riesgos y límites

- **Siguen necesitando una base:** sin `ConnectionStrings__YoSoy` fallan con `TypeInitializationException`, como
  antes. La marca permite excluirlas; no se las omite sola. Si se quiere que `dotnet test` las excluya por defecto,
  se puede poner `<VSTestTestCaseFilter>Categoria!=Integracion</VSTestTestCaseFilter>` en `YoSoy.Tests.csproj`
  (no se hizo: cambia el flujo de todo el equipo).
- **La cascada borra de verdad.** Está acotada a filas que apuntan a filas creadas por la prueba, pero cualquier
  base a la que se apunte la cadena de conexión recibe escrituras y borrados: usar una base de prueba.
- **Dependen del esquema** de `01-produccion.sql` (columnas y claves foráneas); si el esquema cambia, `DatosDePrueba`
  es el único lugar que hay que ajustar.
- **`_MintTokensPlaywright.cs` conserva ids fijos a propósito:** es una herramienta para los scripts de Playwright
  de desarrollo (emite tokens para los usuarios de la base de desarrollo), no una prueba del producto.
- Si el catálogo de estados de la base no tiene "Completado" (lo usa `VideoBaseRealTests`) se crea y se borra.

### Qué falta

- Aplicar `DatosDePrueba` a las pruebas nuevas de las otras ramas de esta tanda (las Tareas 1 a 4 ya crean sus
  propias filas, pero con sus propios ayudantes; al mergear se pueden unificar).
- Decidir si el filtro `Categoria!=Integracion` pasa a ser el valor por defecto de `dotnet test`.

---

## Tarea 6 — `docs/servidor-y-despliegue`

**Pedido:** actualizar `docs/SERVIDOR.md` con lo desplegado en producción (zona horaria, UFW incluyendo coturn,
servicios `signaling-server` y `coturn`, carpetas `/opt/yosoy/{app,config,signaling,whatsapp,backups}`, variables de
entorno de video por nombre, el bloque de Caddy con `import video.caddy`, migraciones aplicadas, 44 tablas y la regla
de turnos) y crear `docs/DEPLOY.md` con el flujo manual de despliegue de la API y el de migraciones. Sin contraseñas,
claves ni IPs sensibles más allá de lo que ya figura en el repo.

**Commits:** `d66b87d` (los dos documentos).

**Qué se hizo:**

| Archivo | Cambio |
|---|---|
| `docs/SERVIDOR.md` | Reescrito sobre la estructura anterior: diagrama de cómo entra el tráfico, zona horaria, tabla de puertos de UFW con los de coturn, tabla de servicios (`yosoy-api`, `caddy`, `signaling-server`, `coturn`, `whatsapp-service`, `mssql-server`, `fail2ban`), el `Caddyfile` con `import /etc/caddy/video.caddy`, carpetas (con `signaling/`, `whatsapp/`, `app-anterior-<fecha>/`), 44 tablas, tabla de migraciones aplicadas, la regla de turnos vigente, variables por archivo (solo nombres), una sección "Verificación rápida" de solo lectura y "Pendiente / a verificar". |
| `docs/DEPLOY.md` (nuevo) | El flujo manual de despliegue de la API (publish self-contained `linux-x64` → `tar` → `scp` → backup `app-anterior-<fecha>` con `diff -rq` → `stop` → vaciar → copiar → `chown` y `chmod +x` → `start` → verificación `302` → rollback), el flujo de migraciones con `sqlcmd -I` (con por qué `-I`, quién las aplica y backup previo), cambios de configuración, otros componentes y qué no hacer. |

**De dónde sale cada dato (y qué no se pudo verificar):**

- **No se accedió al VPS.** Todo sale de los runbooks del repositorio (`DESPLIEGUE_VIDEO.md`, `whatsapp-service/README.md`,
  `deploy/`, `docs/produccion/`) y de lo informado en el pedido. Por eso `SERVIDOR.md` lo dice al principio y trae los
  comandos para comprobarlo.
- **Del repositorio, verificado:** los puertos de UFW y de coturn (`deploy/coturn/turnserver.conf` y el paso d), las
  unidades de `signaling-server` y `whatsapp-service`, el bloque de Caddy y su requisito de Caddy 2.7, los nombres de las
  variables `Video__*` y `SIGNALING_*`, las carpetas de `/opt/yosoy`, las 44 tablas (se contaron las del script).
- **Solo del pedido (no hay forma de contrastarlo con el repo):** la zona horaria del servidor y que `seed_catalogos`
  esté aplicada en producción.

**Lo que apareció al redactar:**

1. **`migrations/seed_catalogos.sql` no está en el repositorio.** El documento lo lista como aplicado, pero el archivo
   sigue sin versionar (lo entregué sin commit, a pedido). Para reconstruir el servidor haría falta. **Decisión tuya:** no
   lo agregué a esta rama para no commitear algo que no pediste.
2. **El documento anterior estaba desactualizado:** decía 43 tablas, UFW con solo 22/80/443 y no mencionaba el signaling,
   coturn ni `/opt/yosoy/signaling` y `/opt/yosoy/whatsapp`. Sus "Pendiente" de la contraseña SMTP y del reinicio por el
   kernel pasaron a "verificar": no sé si siguen vigentes.
3. **SQL Server escucha en `0.0.0.0:1433`** (lo vi en la inspección de solo lectura del 2026-10-14) y lo protege solo
   UFW. Quedó anotado como mejora: atarlo a localhost.
4. **`DESPLIEGUE_VIDEO.md` sigue diciendo "hoy permite 22, 80 y 443"** en su "Antes de empezar": es el estado previo al
   despliegue y es coherente con el runbook; `SERVIDOR.md` es el que describe el estado final.
5. **La cancelación sigue pidiendo 24 h:** la nota de la regla de turnos lo aclara para que no se lea "se eliminó la
   anticipación" como "ya no hay ninguna".

**Riesgos:**

- Si algo de lo informado difiere del servidor real (puertos, nombres de unidades), el documento queda mal hasta
  correr la "Verificación rápida" y corregirlo.
- `DEPLOY.md` repite el flujo del paso f de `DESPLIEGUE_VIDEO.md`: si cambia uno, hay que cambiar el otro.
- El ejemplo de backup de la base (`BACKUP DATABASE ... /var/opt/mssql/backup`) es genérico: no se sabe cuál es la
  herramienta de backup que se usa hoy; se aclara en el texto.

**Qué falta:**

- Correr la "Verificación rápida" en el VPS y ajustar lo que difiera.
- Versionar `migrations/seed_catalogos.sql`.
- Decidir si el `README.md` raíz tiene que apuntar a `docs/DEPLOY.md`.

---

## Tarea 7 — `chore/revision-turnos`

**Pedido:** revisar el flujo de turnos tras eliminar la regla de 24 h para **pedir**: que no queden restos de esa
regla en backend, frontend, textos ni tests; que el rechazo de horarios pasados use la zona horaria de Argentina de
forma consistente **sin depender de la zona del servidor** (una zona explícita o `TimeZoneInfo` configurable, como
`Video__ZonaHoraria`); y que el cálculo de la ventana de video use la misma convención. Corregir solo lo que sea
seguro y esté cubierto con tests; documentar el resto.

**Commits:** `739662a` (reloj de la clínica, DAO, endpoint y pruebas), `c4fb6a1` (pantallas), `ecaa216` (README y
`VIDEOLLAMADAS.md`).

### 1. Restos de la regla de 24 h para pedir turno

Se buscó en backend, pantallas, textos del bot, preguntas frecuentes, scripts SQL, tests y documentos (`24 h`,
`24 horas`, `anticip…`, `HorasMinimasAnticipacion`, `AddHours(24)`): **no queda ningún resto vivo**. Lo que sigue
diciendo "24" es otra cosa y es correcto:

| Dónde | Qué es |
|---|---|
| `TurnoReglasService.HorasMinimasAnticipacion`, `BotReservasService` ("Faltan menos de 24 horas…"), pruebas `Cancelar_con_menos_de_24_horas…` | **Cancelación** (RN-05): se conserva a propósito |
| Pregunta frecuente "Las cancelaciones se aceptan hasta 24 horas antes del turno" (`01-produccion.sql`, `add_mensajeria.sql`) | La misma regla de cancelación: sigue siendo cierta |
| `Turno24h`, `EnviarRecordatorio24h`, `Mensajeria:RecordatorioHorasAntes` | El recordatorio, no una restricción para pedir |
| "Ventana de 24 h" de WhatsApp (`CanalWhatsAppCloud`, README) | Regla del proveedor de mensajería |
| `CAMBIOS_AGUS.md` (línea 938) | Historial de la Fase 3a: cuenta cómo era; no se reescribe |

El texto del recordatorio usa el día y la hora del turno ("jueves 24/09 a las 10:00", `Formato.FechaHora`), así que un
turno pedido para dentro de una hora no dice "mañana". Los comentarios de las pruebas que mencionan "antes se exigían 24 h" son
históricos y están marcados como tales.

### 2. Quién decide "qué hora es" (antes y ahora)

Casi todo el flujo ya pasaba por `TimeProvider.GetLocalNow()`, que en `main` es la zona del **sistema operativo**
del servidor (`TimeProvider.System`). Eso es lo que había que volver explícito.

| Dónde | Antes | Ahora |
|---|---|---|
| `TurnoReglasService` ("ese horario ya pasó", horarios libres, cancelación de 24 h) | zona del sistema operativo | **reloj de la clínica** |
| Bot, notificador y recordatorios, "próximos turnos" del chat, firma del consentimiento, edad para el consentimiento de video, purga | zona del sistema operativo | **el mismo reloj** (reciben el mismo `TimeProvider`) |
| Ventana de la sala (`AccesoSalaService`) | `Video:ZonaHoraria` o la del sistema | `Video:ZonaHoraria` si existe, y si no **la del reloj de la clínica**; avisa una vez si no coinciden |
| "Próximo turno" del paciente (`TurnoDetalleDAO`) | `GETDATE()` de SQL Server | `@Hoy` = fecha del reloj de la clínica |
| `POST /turnos/profesional-solicitar`: la fecha | `DateTime.Parse` (con una "Z" o un offset la **convierte a la zona del servidor**) | el día tal como está escrito (`MomentoDeTurno`) |
| `POST /turnos/profesional-solicitar`: cita urgente | la hora de la **PC del profesional** (con la PC mal de hora, la sala no abría) | la hora del reloj de la clínica |
| Combo de horas del profesional | ofrecía horarios de hoy que ya pasaron | no los ofrece (hora de Argentina) |
| Reprogramar (paciente) | cancelaba y *después* el servidor podía rechazar el horario | comprueba que el horario no haya pasado *antes* de cancelar |

### 3. Lo que se cambió

- **`Services/RelojDeLaClinica.cs` (nuevo).** `RelojDeLaClinica` es un `TimeProvider` con el instante real del
  servidor y la zona de la clínica como "local". La zona sale de `Turnos__ZonaHoraria`, después de
  `Video__ZonaHoraria` (significa lo mismo y `generar-secretos.sh` ya la deja escrita en `video.env`: quien la
  tiene no cambia nada) y por defecto `America/Argentina/Buenos_Aires` (con el identificador de Windows como
  alternativa en máquinas sin ICU). Una `Turnos__ZonaHoraria` que no existe **frena el arranque** con un mensaje que
  nombra la clave; una `Video__ZonaHoraria` mal escrita se ignora, como siempre. La zona se resuelve al crear el
  reloj y `Program.cs` lo pide apenas arranca.
- **`Program.cs`:** `AddRelojDeLaClinica` antes de `AddMensajeria`/`AddVideo` (que pasan a `TryAdd`) y una línea de
  log: `Zona horaria de la clínica: <zona>, diferencia con UTC ahora: <±hh:mm:ss>`.
- **`Services/MomentoDeTurno.cs` (nuevo)** y `POST /turnos/profesional-solicitar`: fecha y hora se leen sin
  convertir a la zona del servidor; una fecha o una hora ilegible da **400** (antes 500); la cita urgente toma la
  hora del servidor.
- **`TurnoDetalleDAO`:** `ObtenerProximoPorPaciente(id, hoy)` y `ObtenerProfesionalesPorPaciente(id, hoy)` reciben
  el "hoy" (el único llamador es `GET /pacientes/{id}/proximo-turno`).
- **`AccesoSalaService`, `VideoOptions`, `TurnoReglasService`:** zona de la ventana tomada del reloj de la clínica y
  comentarios al día.
- **Pantallas:** `nuevo_dashboard.js` (combo del profesional filtra lo pasado y muestra el motivo del rechazo) y
  `Pantalla_Paciente/js/dashboard.js` (Reprogramar comprueba el horario antes de cancelar).
- **Documentos:** `README.md` y `docs/VIDEOLLAMADAS.md` ("La zona horaria").

### 4. Pruebas

78 pruebas nuevas, todas pasan:

| Clase | Pruebas | Necesita base | Qué cubre |
|---|---|---|---|
| `RelojDeLaClinicaTests` | 18 | No | La hora local es la de la clínica y no la del origen (incluida la noche, cuando en UTC ya es "mañana"); de dónde sale la zona (por defecto, `Turnos`, `Video`, mal escrita, inexistente); el registro y que ningún otro `TimeProvider` lo pise |
| `TurnosZonaHorariaTests` | 9 | No | `TurnoReglasService` con un servidor en UTC: el horario de hoy que todavía no pasó se acepta, el que pasó se rechaza, el borde exacto, la noche, la cancelación de 24 h medida en la clínica y otra zona configurada |
| `TurnosZonaHorariaEndpointsTests` | 13 | No | La aplicación real (`Program.cs`, JWT, endpoints) con repositorio y notificador falsos: lo mismo por HTTP, la fecha ISO que manda el navegador, 400 ante lo ilegible y la cita urgente con la hora del servidor |
| `RelojDeLaAplicacionTests` | 4 | No | El reloj que arma `Program.cs` de verdad: Argentina por defecto, `Turnos__ZonaHoraria`, `Video__ZonaHoraria` heredada y que una zona inexistente impide arrancar |
| `MomentoDeTurnoTests` | 23 | No | El día tal como está escrito (con "Z", con offset, a secas), lo ilegible, y la cita urgente truncada a segundos |
| `AccesoSalaServiceTests` | +4 | No | La ventana de la sala con el reloj de la clínica sobre un servidor en UTC, que abre en la misma hora de pared que ven las reglas, y el aviso de zonas distintas (una sola vez) |
| `ProximoTurnoZonaHorariaIntegrationTests` | 7 | **Sí** (`Integracion`) | `ObtenerProximoPorPaciente`/`ObtenerProfesionalesPorPaciente` con el "hoy" que se les pasa y el endpoint con el reloj de la clínica |

| Corrida | `main` | Esta rama |
|---|---|---|
| Sin base | 551 ok / 79 fallan | 622 ok / 86 fallan: las 79 de siempre + las 7 de integración nuevas, que necesitan base |
| Con la copia de esquema en `tempdb` | 608 ok / 40 fallan | **686 ok / 40 fallan** (las mismas 40, **0 fallas nuevas**) |

**Mutaciones** (cada una se revirtió y se verificó): `Ahora` en UTC (12 pruebas fallan), leer la fecha con
`DateTime.Parse` (fallan los casos con "Z" y con offset), sin `AddRelojDeLaClinica` en `Program.cs` (fallan las 4 de la
aplicación), la cita urgente con `DateTime.Now` (fallan las 2 de urgente), el DAO otra vez con `GETDATE()` y el
endpoint con la fecha UTC (fallan las de base).

**Pantallas, en navegador real** (Argentina y UTC): combo del profesional 30/30 (antes de la rama: falla en todos los
casos de "hoy"); combo y reprogramación del paciente 66/66, con el caso nuevo de "el horario pasó mientras decidía"
(antes de la rama: cancelaba y recién después pedía).

**Cómo probarlo:**

```
# Sin base de datos (71 de las nuevas):
dotnet test YoSoy.Tests --filter "FullyQualifiedName~RelojDeLaClinicaTests|FullyQualifiedName~MomentoDeTurnoTests|FullyQualifiedName~TurnosZonaHorariaTests|FullyQualifiedName~TurnosZonaHorariaEndpointsTests|FullyQualifiedName~RelojDeLaAplicacionTests|FullyQualifiedName~AccesoSalaServiceTests"

# Con una base DESCARTABLE (nunca la real): las 7 de "próximo turno"
ConnectionStrings__YoSoy="..." dotnet test YoSoy.Tests --filter "FullyQualifiedName~ProximoTurnoZonaHorariaIntegrationTests"
```

A mano: arrancar la API y mirar el log (`Zona horaria de la clínica: America/Argentina/Buenos_Aires, diferencia con UTC
ahora: -03:00:00`); arrancarla con `Turnos__ZonaHoraria=UTC` y ver que cambia; con `Turnos__ZonaHoraria=Marte/X` no
arranca.

### 5. Lo que se encontró y NO se cambió

1. **Marcas de tiempo con la hora del servidor o de la base** (no deciden nada de turnos, pero dependen de la zona
   del sistema): `DateTime.Now` en `CasoDAO`, `CasoDerivacionDAO`, `HistorialAsignacionDAO`, `PacienteDAO`
   (fecha de vinculación del usuario con el paciente), `AsignacionService`, `RecuperacionPasswordDAO` (token y ventana de pedidos),
   `ReporteHtmlBuilder` y varios lugares de `Program.cs` (derivaciones, auditoría, nombre del reporte); y
   `GETDATE()` en `CasoDerivacionDAO`, `RecuperacionPasswordDAO`, `UsuarioDAO` (bloqueo del login en `main`; la
   Tarea 2 lo pasa a UTC) y en las altas/asignaciones de `Program.cs`. El día que se quiera unificarlo, el camino es
   inyectar `TimeProvider` en esos DAO. Mientras la VPS siga en Argentina no hay diferencia; si la zona del sistema
   cambia, las fechas ya guardadas quedan en la zona vieja (como pasó al corregir la zona del VPS).
2. **Los calendarios de las pantallas usan la hora del navegador** (`new Date()` y `YS.fecha.esHoy`): "hoy", los
   días pasados y la semana. Los combos de horas usan la hora de Argentina fija (`ahoraEnArgentina`), una constante
   que está **repetida** en `Pantalla_Paciente/js/dashboard.js` y `Pantalla_Profesional/js/nuevo_dashboard.js`: si algún
   día se configura otra zona hay que cambiarla en los dos. No se llevó a `shared/ui.js` a propósito: una pantalla
   con el JS nuevo y el `ui.js` viejo en la caché del navegador se rompería.
3. **Reprogramar (paciente) sigue siendo "cancelar y pedir"** (dos pedidos, no atómico): se comprueba antes que el
   horario no haya pasado, pero si el pedido falla por otro motivo (alguien ocupó el horario en el medio, el modo
   estricto del consentimiento, la red) el paciente se queda sin turno. Lo correcto es un endpoint de
   reprogramación transaccional (`PUT /turnos/{id}/reprogramar`); requiere decidir qué pasa con el estado, la
   confirmación y los avisos, así que no se hizo.
4. **Reprogramar desde el profesional** (`PUT /turnos/{id}` con el turno entero) no pasa por `RN-01`: puede dejar un
   turno en un horario pasado o fuera de la agenda. Es el "modo legacy" que ya figura en `CAMBIOS_AGUS.md`.
5. **Pasos de 60 minutos fijos** en el combo del profesional y en Reprogramar (el servidor usa `IntervaloMinutos`):
   ya estaba en las limitaciones del `README.md`.
6. **"Próximo turno"** incluye los turnos de hoy cuya hora ya pasó (hasta la medianoche); es el comportamiento de
   siempre y no se tocó.
7. `Program.cs` arma los códigos de sala con `DateTime.Now.Ticks`: solo busca un valor único, la zona no influye.

### 6. Riesgos

- **Una `Turnos__ZonaHoraria` mal escrita frena el arranque** (a propósito). Vuelta atrás: sacar la variable. La
  `Video__ZonaHoraria` que deja `generar-secretos.sh` es la misma que el valor por defecto, así que en producción no
  hace falta definir nada.
- **Cambió la firma de `TurnoDetalleDAO.ObtenerProximoPorPaciente`** (recibe el "hoy"); el único llamador es
  `Program.cs`.
- **La cita urgente cambió de comportamiento a propósito:** la hora la pone el servidor. Si algún cliente mandaba
  una hora distinta de "ahora" con `esUrgente`, se ignora.
- **`POST /turnos/profesional-solicitar` contesta 400** donde antes daba 500 ante una fecha u hora ilegible.
- **La hora de Argentina de los combos está fija en el JavaScript** (ver 5.2).

### 7. Qué falta

- Unificar las marcas de tiempo de auditoría con el reloj de la clínica (5.1).
- Decidir si Reprogramar pasa a un endpoint atómico (5.3).
- Cuando se mergee esta rama con `docs/servidor-y-despliegue`, que `SERVIDOR.md` y `DEPLOY.md` mencionen
  `Turnos__ZonaHoraria` y el mensaje de log (ya está hecho en esa rama).

---

## Resumen final y orden para mergear

### Ramas

Todas salen de `main` (`27ad47f`); ninguna se empujó ni se mergeó. Cada una termina con un commit de este archivo.

| # | Rama | Commits de código | Qué entrega |
|---|---|---|---|
| 1 | `fix/registro-profesional-409` | `3bc896a` | `POST`/`PUT /profesionales`: 409 por matrícula o DNI repetidos y 400 por especialidad inexistente (antes 500) y límite por IP |
| 2 | `fix/admin-lockout-decay` | `1a7cf0d` | El conteo de intentos fallidos del login vence con el bloqueo; el bloqueo pasa a UTC |
| 3 | `feat/crear-usuario-profesional-admin` | `be3be23`, `a7a6170` (diseño en `47ebb2b`) | El Configurador crea el usuario de un profesional (opción A, atómica) y se quitan las promesas de mail |
| 4 | `chore/hardening-mensajes` | `a4c9999` | Alta de paciente con mensaje único, política de contraseña centralizada y 13 hallazgos de seguridad documentados |
| 5 | `test/legacy-independientes` | `e7d93ef` | Las pruebas de integración crean y borran sus propios datos: 649/649 sobre una base vacía |
| 6 | `docs/servidor-y-despliegue` | `d66b87d` | `SERVIDOR.md` al día y `DEPLOY.md` nuevo |
| 7 | `chore/revision-turnos` | `739662a`, `c4fb6a1`, `ecaa216` | Reloj de la clínica con zona explícita para turnos y sala de video, y pantallas |

### Qué quedó hecho y qué quedó pendiente

**Hecho y probado** (sin fallas nuevas en ninguna rama, con y sin base): las 7 tareas. Lo único que no se pudo
verificar es lo que depende del VPS (Tarea 6: se escribió desde los runbooks del repositorio) y de la base real
(nada se ejecutó contra `yosoy`).

**Pendiente, por decidir o por hacer** (cada uno está explicado en su tarea):

| Tema | De dónde viene | Qué hace falta |
|---|---|---|
| Mínimo de contraseña (hoy 6 en el alta de paciente y 8 en la Tarea 3) | 3, 4 | Elegir el mínimo y unificar `CrearUsuarioProfesionalService` con `PoliticaContrasena` |
| Hallazgos de seguridad H1 a H13 (credenciales en `appsettings.json`, reserva pública sin autenticar, baja que no quita acceso…) | 4 | Decidir; H1 (rotar la clave del correo) es operativo y no espera |
| Índice único de DNI de profesional | 1 | Limpiar duplicados y aplicar el índice |
| Columna de último intento del login | 2 | Migración previa al despliegue |
| Botón "Desbloquear" en el Configurador | 2 | Hacerlo |
| Opción B (invitación por mail) y cambio obligatorio de contraseña | 3 | Decidir |
| `migrations/seed_catalogos.sql` no está versionado | 6 | Decidir si se commitea |
| Verificar `SERVIDOR.md` contra el VPS (ufw, servicios, SQL en `0.0.0.0:1433`) | 6 | Correr la "Verificación rápida" |
| Filtro `Categoria!=Integracion` por defecto en `dotnet test` | 5 | Decidir |
| Reprogramación atómica y marcas de tiempo con el reloj de la clínica | 7 | Hacerlo cuando se priorice |

### Orden recomendado para mergear

1. **`test/legacy-independientes`**: solo pruebas; deja la suite independiente de la base de desarrollo, así cada
   merge siguiente se puede verificar con `dotnet test` sobre una base vacía.
2. **`fix/registro-profesional-409`**: corrige un 500 en un endpoint público.
3. **`fix/admin-lockout-decay`**: corrige el bloqueo del login (ojo: durante hasta 15 minutos después de
   desplegarlo, un bloqueo vigente puede verse ya vencido; ver Tarea 2).
4. **`chore/hardening-mensajes`**: después de la 1, porque el alta de profesional ya quedó con mensaje neutral y así
   los dos altas son coherentes.
5. **`feat/crear-usuario-profesional-admin`**: después de la 4, para unificar la política de contraseña al mergear.
6. **`chore/revision-turnos`**: cambia el reloj que usa todo el sistema; conviene que entre con las correcciones
   anteriores ya probadas.
7. **`docs/servidor-y-despliegue`**: al final, porque describe el estado final (incluida la zona horaria de la 7).

**Conflictos:** se probó la fusión de las siete ramas de a pares y en ese orden sobre una copia aparte del
repositorio: **no hay conflictos de código** (ni en `Program.cs`, ni en `dashboard.js`, ni en las pruebas). El único
archivo que difiere entre ramas era este `PROGRESO_NOCTURNO.md`; por eso cada rama termina con **el mismo contenido**
de este archivo (un commit final) y los merges no lo tocan. Las siete ramas juntas, fusionadas en ese orden en esa
copia, **compilan** y la suite completa pasa contra una copia descartable del esquema: **844 correctas, 8 omitidas
(las de interoperabilidad con Node) y 0 fallas** de 852.

**Después de mergear todo:**

- Unificar la política de contraseña (Tarea 3 con Tarea 4).
- Revisar que la suite pase sobre una base de prueba vacía: `ConnectionStrings__YoSoy=<base descartable> dotnet test`.
- En el VPS, nada nuevo que configurar: `Turnos__ZonaHoraria` es opcional y `Video__ZonaHoraria` ya está. Mirar en
  el log la línea `Zona horaria de la clínica: …` después del despliegue.
