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
- Interacciones con IA de apoyo emocional
- Recetas digitales (psiquiatría)

---

## Stack tecnológico

| Capa | Tecnología |
|---|---|
| Lenguaje | C# (.NET) |
| UI | HTML |
| Acceso a datos | ADO.NET |
| Base de datos | SQL Server Express |
| Control de versiones | Git / GitHub |

---

## Base de datos

La base de datos `yosoy` contiene las siguientes entidades:

**Referencia:** `Especialidad`, `Rol`, `EstadoTurno`, `TipoConsentimiento`

**Principales:** `Usuario`, `Paciente`, `Profesional`, `Tutor`

**Operativas:** `Agenda`, `Turno`, `Consentimiento`

**Clínicas (inmutables):** `HistoriaClinica`, `Evolucion`, `Derivacion`, `EventoCrisis`, `InteraccionIA`

---

## Cómo ejecutar el proyecto

### Requisitos
- .NET 6 o superior
- SQL Server Express
- Visual Studio Code

### Pasos
1. Clonar el repositorio
```bash
git clone https://github.com/MantoAR/YoSoy.git
cd YoSoy
```

2. Restaurar la base de datos ejecutando el script `yosoy.sql` en SQL Server Management Studio

3. Configurar la cadena de conexión en `Conexion.cs`:
```csharp
private static string cadena = "Server=TU_SERVIDOR;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;";
```

4. Ejecutar el proyecto
```bash
dotnet run
```

---

## Estructura del proyecto

```
YoSoy/
├── Conexion.cs              # Clase de conexión a SQL Server
├── EspecialidadDAO.cs       # ABM Especialidad
├── ...                      # Resto de DAOs
├── Form1.cs                 # Formulario principal
├── Program.cs               # Punto de entrada
└── YoSoy.csproj             # Configuración del proyecto
```

---

## Materia

**Práctica Profesionalizante I**
Docente: Fernando Loza
Carrera: Tecnicatura Superior en Análisis de Sistemas de Computación — 42NMS (AS) 2-25
