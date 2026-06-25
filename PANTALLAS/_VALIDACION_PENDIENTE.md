# Validación pendiente — Registro de Paciente

`registro.html` recolecta campos que el modelo `Paciente` del backend **no puede persistir**.

El modelo `Paciente` acepta únicamente: `Nombre`, `Dni`, `FechaNacimiento`, `Domicilio`, `Telefono`, `Mail` (más `IdPaciente`, `Estado` y `Token`, que gestiona el backend).

En esta fase esos campos **dejaron de validarse y dejaron de enviarse** en el POST a `/pacientes` (antes se mandaban y el backend los descartaba en silencio). El formulario los sigue mostrando en pantalla, pero su dato se pierde.

Hace falta una decisión: **extender el modelo/tabla** para guardarlos, o **sacarlos del formulario**. No se decidió nada todavía; esto es solo el inventario.

## Grupo A — Necesarios para persistir

Datos con valor clínico/legal que hoy se piden pero no se guardan.

| Campo (id en el form) | Tipo | Nota |
|---|---|---|
| `apellido` | text | Está marcado obligatorio en el form, pero `Paciente` no tiene `Apellido`. |
| Sección **Tutor responsable** (se muestra si el paciente es menor de 18) | — | Ningún campo de tutor tiene dónde guardarse. Requiere modelo/tabla propia. |
| ↳ `tutorNombre` | text | |
| ↳ `tutorSegundoNombre` | text | |
| ↳ `tutorApellido` | text | |
| ↳ `tutorSegundoApellido` | text | |
| ↳ `tutorSexo` | select | |
| ↳ `tutorFechaNacimiento` | date | |
| ↳ `tutorEmail` | email | |
| ↳ `parentesco` | text | |
| ↳ `tutorDomicilio` | text | |

Observación: ya existe `Models/Tutor.cs` y `Models/PacienteTutor.cs` en el backend, pero el endpoint `POST /pacientes` no los usa. Si se decide persistir tutores, hay que conectar esa parte (otra fase, backend).

## Grupo B — Decisión de producto

Datos cuya persistencia es opcional según se quiera o no en el alta.

| Campo (id en el form) | Tipo |
|---|---|
| `segundoNombre` | text |
| `segundoApellido` | text |
| `sexo` | select |
| `provincia` | text |
| `numero` (altura del domicilio) | number |

## Opciones a evaluar (no implementadas)

1. **Extender modelo `Paciente`** con los campos del Grupo B que se quieran conservar (requiere tocar modelo, DAO, tabla y endpoint — backend).
2. **Persistir tutores** conectando `Tutor` / `PacienteTutor` al alta de paciente menor (backend).
3. **Sacar del formulario** los campos que se decida no guardar, para no pedir datos que se descartan.
