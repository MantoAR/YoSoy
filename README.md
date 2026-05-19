# YoSoy — Plataforma de Telemedicina en Salud Mental

Sistema de gestión clínica y telemedicina orientado a la atención en salud mental, desarrollado como proyecto académico para la **Tecnicatura Superior en Análisis de Sistemas de Computación** en el Instituto Cervantes.

---

## Equipo

| Rol | Integrante |
|---|---|
| Product Owner | Santiago Mantovani |
| Scrum Master | Agustín Herrera |
| Dev Team | Araceli Armas |
| Dev Team | Mariana Gallo |

---

## Descripción

YoSoy es una plataforma integral que combina gestión clínica, turnos y asistencia por inteligencia artificial para profesionales y pacientes de salud mental. El sistema busca eliminar barreras geográficas y optimizar el acceso a la atención psicológica y psiquiátrica.

### Funcionalidades principales
- Gestión de pacientes y profesionales
- Agenda y turnos online
- Historia clínica digital
- Registro de evoluciones por sesión
- Derivaciones entre profesionales
- Protocolo de eventos de crisis
- Consentimiento informado digital
- Cobertura de obra social
- Interacciones con IA de apoyo emocional

---

## Stack tecnológico

| Capa | Tecnología |
|---|---|
| Backend | C# — ASP.NET Minimal API (.NET 9) |
| Frontend | HTML + JavaScript (Fetch API) |
| Acceso a datos | ADO.NET |
| Base de datos | SQL Server Express |
| Control de versiones | Git / GitHub |

---

## Base de datos

El script de creación se encuentra en la rama `DataBase` del repositorio.

La base de datos `yosoy` contiene las siguientes entidades:

**Referencia:** `Especialidad`, `Rol`, `EstadoTurno`, `TipoConsentimiento`

**Principales:** `Usuario`, `Paciente`, `Profesional`, `Tutor`

**Operativas:** `Agenda`, `Turno`, `Consentimiento`, `Cobertura`

**Clínicas:** `HistoriaClinica`, `Evolucion`, `Derivacion`, `EventoCrisis`, `InteraccionIA`

**Relacionales:** `PacienteTutor`, `UsuarioRol`, `UsuarioPaciente`, `UsuarioProfesional`

---

## API REST

El proyecto expone una API REST construida con ASP.NET Minimal API. Todos los endpoints devuelven y reciben JSON.

| Recurso | Endpoints disponibles |
|---|---|
| Especialidad | GET, POST, PUT, DELETE |
| Rol | GET, POST, PUT, DELETE |
| EstadoTurno | GET, POST, PUT, DELETE |
| TipoConsentimiento | GET, POST, PUT, DELETE |
| ObraSocial | GET, POST, PUT |
| Tutor | GET, POST, PUT |
| Usuario | GET, POST, PUT, DELETE |
| Paciente | GET, POST, PUT, DELETE |
| Profesional | GET, POST, PUT, DELETE |
| Agenda | GET, POST, PUT, DELETE |
| Turno | GET, POST, PUT, DELETE |
| Consentimiento | GET, POST, PUT, DELETE |
| HistoriaClinica | GET, POST |
| Evolucion | GET, POST, PUT |
| Derivacion | GET, POST, PUT |
| EventoCrisis | GET, POST, PUT |
| InteraccionIA | GET, POST |
| Cobertura | GET, POST, PUT, DELETE |

---

## Cómo ejecutar el proyecto

### Requisitos
- .NET 9
- SQL Server Express
- SQL Server Management Studio

### Pasos

1. Clonar el repositorio
```bash
git clone https://github.com/MantoAR/YoSoy.git
cd YoSoy
```

2. Restaurar la base de datos ejecutando el script de la rama `DataBase` en SQL Server Management Studio

3. Configurar la cadena de conexión en `Conexion.cs`:
```csharp
private static string cadena = "Server=TU_SERVIDOR;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;";
```

4. Ejecutar el proyecto
```bash
dotnet run
```

5. Abrir `mockup.html` en el navegador para usar la interfaz

---

## Estructura del proyecto

```
YoSoy/
├── Conexion.cs
├── Program.cs
├── mockup.html
├── YoSoy.csproj
├── Models/
│   ├── Especialidad.cs
│   ├── Rol.cs
│   ├── EstadoTurno.cs
│   ├── TipoConsentimiento.cs
│   ├── ObraSocial.cs
│   ├── Tutor.cs
│   ├── Usuario.cs
│   ├── Paciente.cs
│   ├── Profesional.cs
│   ├── Agenda.cs
│   ├── Turno.cs
│   ├── Consentimiento.cs
│   ├── Cobertura.cs
│   ├── HistoriaClinica.cs
│   ├── Evolucion.cs
│   ├── Derivacion.cs
│   ├── EventoCrisis.cs
│   ├── InteraccionIA.cs
│   ├── PacienteTutor.cs
│   ├── UsuarioRol.cs
│   ├── UsuarioPaciente.cs
│   └── UsuarioProfesional.cs
└── DAOs/
    ├── EspecialidadDAO.cs
    ├── RolDAO.cs
    ├── EstadoTurnoDAO.cs
    ├── TipoConsentimientoDAO.cs
    ├── ObraSocialDAO.cs
    ├── TutorDAO.cs
    ├── UsuarioDAO.cs
    ├── PacienteDAO.cs
    ├── ProfesionalDAO.cs
    ├── AgendaDAO.cs
    ├── TurnoDAO.cs
    ├── ConsentimientoDAO.cs
    ├── CoberturaDAO.cs
    ├── HistoriaClinicaDAO.cs
    ├── EvolucionDAO.cs
    ├── DerivacionDAO.cs
    ├── EventoCrisisDAO.cs
    ├── InteraccionIADAO.cs
    ├── PacienteTutorDAO.cs
    ├── UsuarioRolDAO.cs
    ├── UsuarioPacienteDAO.cs
    └── UsuarioProfesionalDAO.cs
```

---

## Trabajo en equipo con Git

### Bajar los últimos cambios del repositorio (pull)
```bash
cd "ruta\a\tu\carpeta\YoSoy"
git pull
```

### Subir cambios al repositorio (push)
```bash
cd "ruta\a\tu\carpeta\YoSoy"

# 1. Agregar los archivos modificados
git add .

# 2. Crear el commit con una descripción
git commit -m "descripción de lo que cambiaste"

# 3. Subir al repositorio
git push
```

### Agregar un archivo o carpeta específica
```bash
# Un archivo
git add nombre_archivo.html

# Una carpeta completa
git add nombre_carpeta/
```

> **Recomendación:** hacer `git pull` siempre antes de empezar a trabajar para tener la última versión del proyecto.

---

## Materia

**Práctica Profesionalizante I**
Docente: Fernando Loza
Carrera: Tecnicatura Superior en Análisis de Sistemas de Computación — 42NMS (AS) 2-25
