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
