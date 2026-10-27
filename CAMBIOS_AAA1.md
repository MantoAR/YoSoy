# Cambios en la rama AAA1

Todo está en `D:\YoSoy`, rama `AAA1`, **sin commit todavía** (no se pudo subir: ver abajo).

## Qué cambió

- **Mig. 33 · Agenda y matrícula:** feriados (Configurador), bloqueos de agenda, inasistencia, matrícula por especialidad con vencimiento, aviso 30 días antes y corte de agenda al vencer. En el alta del profesional, cada especialidad trae su matrícula (+ Agregar especialidad).
- **Mig. 34 · Especialidades:** las 6 del Colegio de Psicólogos + las del equipo. Configuración › Especialidades: agregar, editar, dar de baja, borrar (solo si nunca se usó).
- **Mig. 35 · Documentos clínicos en la HC:** pestaña Documentos con 10 plantillas *provisorias* (consentimientos, constancias, informes, plan de seguridad), PDF que no se puede modificar, addendum y firma del profesional. Falta el editor de plantillas.
- **Mig. 36 · Admisión:** el privilegio Admisión se le puede dar a la Recepcionista (admisionista); sin él no ve los formularios. Privilegio nuevo *Entrevista por video* (opcional, solo profesional).
- **Mig. 37 · Asistencia:** privilegio *Gestión de pacientes*. Aviso al paciente a los 5 min; desde los 15 min el profesional o la admisionista marcan Ausente (nunca solo). Ausente o cancelación con menos de 24 hs = cobrable (el profesional puede dejarlo sin cargo). 2 faltas seguidas = alerta; 3 = abandono, lo confirma el tratante o el auditor. Parámetros en Configuración › Asistencia. Pantalla *Pacientes y turnos* y tarjetas *Esperando conexión* / *Posible abandono*.
- **Menú del Configurador:** Configuración (WhatsApp, Asistente de chats, Especialidades, Feriados, Asistencia) · Usuarios (Registrar, Permisos, Usuarios, Matrículas, Solicitudes) · Auditoría.

## Pendiente

- Consentimiento de telemedicina editable (está en tu borrador, falta adaptarlo).
- Editor de plantillas de documentos para el Configurador.

## Para subirlo (Git Bash, en D:\YoSoy)

Todavía **no se compiló**: probalo antes de subir.

```bash
sqlcmd -S "DESKTOP-Q065FTM\SQLEXPRESS" -E -d yosoy -C -I -b -f 65001 -i migrations/add_especialidades_colegio.sql
sqlcmd -S "DESKTOP-Q065FTM\SQLEXPRESS" -E -d yosoy -C -I -b -f 65001 -i migrations/add_documentos_clinicos.sql
sqlcmd -S "DESKTOP-Q065FTM\SQLEXPRESS" -E -d yosoy -C -I -b -f 65001 -i migrations/add_privilegios_admision_video.sql
sqlcmd -S "DESKTOP-Q065FTM\SQLEXPRESS" -E -d yosoy -C -I -b -f 65001 -i migrations/add_asistencia_abandono.sql
dotnet build
dotnet test YoSoy.Tests
dotnet run
```

Si todo anda:

```bash
git add -A
git status
git commit -m "AAA1: agenda y matrícula, especialidades, documentos clínicos, admisión y asistencia (migraciones 33 a 37)"
git push -u origin AAA1
```

En `git status`, revisá que no aparezcan `bin/`, `obj/` ni archivos `.bak`.
