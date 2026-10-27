# Prueba local: agenda (feriados, bloqueos, inasistencia) y matrícula

Bloques 1 y 2 de `AAA1`, migración 33. Se prueba en tu PC contra tu base local `yosoy`, nunca contra producción.

Qué entra:

- **Feriados:** los carga el Configurador. Ese día nadie ofrece turnos, salvo quien marcó "trabajo este feriado".
- **Bloqueos de agenda:** licencia, vacaciones, capacitación, personal u otro, con fecha y hora de inicio y fin.
- **Inasistencia:** el profesional la marca en un turno confirmado cuya hora ya llegó. Queda en estado `Ausente` y en la auditoría clínica.
- **Matrícula:**
  - una por especialidad: la principal es obligatoria y las secundarias opcionales;
  - cada una tiene fecha de vencimiento;
  - **desde el vencimiento de la principal la agenda no ofrece turnos** y no se pueden cambiar los horarios;
  - desde 30 días antes, todos los días, avisa al profesional y a los auditores por la campanita y por mail;
  - hay un reporte de vencimientos.

Lo que **no** hace a propósito: no cancela turnos ya dados. Si quedan dentro de un bloqueo o después del vencimiento, se listan para que el profesional los reprograme. Cancelar sin avisar a un paciente de salud mental es peor que dejar el turno a la vista.

## 1. Migración 33

```bash
cd /d/YoSoy
sqlcmd -S 'DESKTOP-Q065FTM\SQLEXPRESS' -d yosoy -E -C -I -b -f 65001 -i migrations/add_agenda_feriados_bloqueos_matriculas.sql
```

**Esperado:**

- "Migración 33 aplicada.";
- una fila con `matriculas_cargadas` (una por cada profesional que ya tenía matrícula), `sin_vencimiento` (las mismas: el vencimiento hay que cargarlo) y el `id_estado_ausente`.

Se puede correr dos veces sin error. Después arrancá la API (`dotnet run`). No tiene que avisar que falta ninguna migración.

## 2. Configurador: feriados y matrículas

Ingresá por **Configurador** y recargá con **Ctrl+F5**.

| Paso | Esperado |
| --- | --- |
| **Configuración › Feriados**: cargá uno para un día hábil de la semana que viene (por ejemplo, un jueves) | Aparece en la lista. Si cargás otro el mismo día: "Ya hay un feriado cargado…" |
| **Usuarios › Matrículas** | Todos los profesionales activos. Los que ya existían: "Sin vencimiento cargado" |
| **Renovar** en tu profesional de prueba: vencimiento **dentro de 10 días** | Estado "Por vencer", "faltan 10 días" |
| **Otra matrícula**: una segunda especialidad | Queda como "Secundaria"; se puede **Quitar**. La principal no tiene "Quitar" |
| **Enviar avisos ahora** | "Avisos enviados: N en la campanita…". Si el mail no está configurado, cuenta 0 mails y no falla |
| **Usuarios › Registrar usuario** con rol Profesional | Pide **Vencimiento de la matrícula** (no acepta una fecha pasada). Al crearlo, aparece en Matrículas con esa fecha |
| **Auditoría** | "Feriado cargado", "Matrícula modificada", "Matrícula cargada" |

## 3. Profesional: horarios, bloqueos, feriados

Ingresá con el profesional de prueba y entrá a **Agenda › Mis horarios de trabajo**.

1. **Aviso de matrícula**, arriba: "Tu matrícula … vence el … (faltan 10 días)".
2. **Feriados:** aparece el feriado del paso 2. En "Así te ven los pacientes" ese día **no tiene horarios**. Tildá **Trabajo este feriado**: el día vuelve a tener horarios. Destildalo.
3. **Bloqueos de agenda:** bloqueá un día hábil de 10:00 a 12:00, motivo Capacitación. **Esperado:**
   - la vista previa ya no ofrece de 10:00 a 11:30 ese día;
   - si ya había turnos en ese rango, aparecen listados como "Quedan N turnos dentro del bloqueo".
4. **Quitar** el bloqueo: los horarios vuelven.
5. **Campanita:** aparece "Tu matrícula está por vencer" (del paso 2). Al abrirla se marca leída.

## 4. Paciente: no se ofrecen esos horarios

Ingresá como un paciente asignado a ese profesional y pedí turno el día del feriado o del bloqueo. **Esperado:**

- el selector de hora dice "Sin horarios disponibles", o no muestra las horas bloqueadas;
- si se fuerza el pedido desde la consola, el servidor responde **409 RN-08**:

```js
(await YS.fetch("/turnos/solicitar", { method: "POST", json: { idPaciente: ID_PACIENTE, idProfesional: ID_PROF, fecha: "AAAA-MM-DD", hora: "10:00:00", modalidad: "Virtual" } })).status   // 409
```

## 5. Matrícula vencida: agenda suspendida

En **Usuarios › Matrículas › Renovar**, poné de vencimiento **la fecha de hoy**.

| Dónde | Esperado |
| --- | --- |
| Profesional › Mis horarios | Alerta roja "Agenda suspendida…". Agregar o editar un horario: "La matrícula venció el … no se pueden configurar horarios…" (409) |
| "Así te ven los pacientes" | Sin horarios |
| Paciente pidiendo turno | No hay horarios, y el servidor responde 409 RN-08 |
| Cita urgente del profesional | "Tu matrícula no está vigente: no podés iniciar una cita." |
| Usuarios › Matrículas | "Vencida". La columna "Turnos después del vencimiento" cuenta los que hay que reprogramar |

Renová con una fecha futura: todo vuelve solo, sin tocar nada más.

## 6. Inasistencia

Con un turno **Confirmado** cuya hora ya pasó (si no hay, creá uno para hoy un rato antes, confirmalo y esperá a que llegue la hora):

1. **Inicio** (pacientes de hoy) o **Calendario** (panel del día): botón **No se presentó** / **Registrar inasistencia**.
2. **Esperado:**
   - el turno pasa a **Ausente** (rojo);
   - ya no ofrece Cancelar ni Reprogramar;
   - aparece **Corregir: sí se presentó**, que lo devuelve a Confirmado.
3. En un turno cuya hora todavía no llegó, el botón no aparece. Por consola: `PUT /turnos/ID/inasistencia` da **409** "Todavía no llegó la hora del turno".
4. Con el Profesional auditor, en **Auditoría › Accesos clínicos**: "Inasistencia registrada · Turno #… , paciente #…".

## 7. Auditor: reporte de matrículas

Ingresá con un **Profesional auditor**:

- **Auditoría › Matrículas** muestra el mismo reporte, **sin botones** (solo lectura);
- la campanita trae "Matrícula por vencer: Nombre Apellido" de los otros profesionales.

## 8. Pruebas automáticas

```bash
dotnet test YoSoy.Tests --filter "FullyQualifiedName~AgendaRestricciones|FullyQualifiedName~AltaEquipoTests|FullyQualifiedName~VerificadorEsquema|FullyQualifiedName~TurnoReglas"
```

**Esperado:** pasan todas. `AgendaRestriccionesTests` es nuevo: feriado, bloqueo parcial, vencimiento, urgente, reprogramación, estados de la matrícula y motivos.

## Volver atrás

`docs/produccion/deshacer-migracion-33.sql`. Borra feriados, bloqueos y matrículas, y devuelve los turnos Ausente a Confirmado. La API anterior anda igual con la base migrada, así que casi nunca hace falta.
