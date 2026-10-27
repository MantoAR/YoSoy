/* ============================================================================
   add_asistencia_abandono.sql — Migración 37 (rama AAA1).

   Llegada tarde, inasistencia y abandono de tratamiento (reunión con la Lic. Villalón del
   26/10 y decisiones de Araceli del 27/10):
     - A los 15 minutos el turno NO pasa solo a Ausente: lo marca una persona (el profesional
       del turno, o la Recepcionista con el privilegio nuevo "Gestión de pacientes").
     - Cancelar con menos de 24 hs o no venir deja el turno "cobrable" (para el futuro módulo
       de Cobros), salvo fuerza mayor que justifica el profesional.
     - 2 ausencias seguidas: alerta "Posible abandono"; 3: el sistema sugiere el abandono, que
       confirma el profesional tratante o el auditor.
     - La llegada tarde sale de la aceptación de la videollamada del paciente (migración 28):
       no hace falta una columna nueva.

   Qué agrega:
     1) Turno: Cobrable, MotivoNoCobrable, IdUsuarioAsistencia, FechaAsistenciaUtc.
     2) SeguimientoAsistencia: cada contacto con un paciente que falta (resultado y nota corta,
        sin datos clínicos).
     3) AbandonoTratamiento: la confirmación del abandono. De SOLO AGREGAR (trigger, error 50030):
        es un registro de la historia clínica.
     4) Privilegio "gestion_pacientes" (Gestión de pacientes), solo para la Recepcionista (crm).
     5) Los parámetros de la organización (ConfiguracionOrganizacion), si no estaban:
        Asistencia.AvisoMinutos = 5, Asistencia.ToleranciaMinutos = 15,
        Asistencia.HorasCancelacionSinCargo = 24, Asistencia.AusenciasAlerta = 2,
        Asistencia.AusenciasAbandono = 3.

   Efectos: solo agrega. No cambia ningún turno ni usuario existente.

   Cómo correrla:
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_asistencia_abandono.sql
   Verificación: termina con "Migración 37 aplicada." y muestra los parámetros.
   Deshacer: docs/produccion/deshacer-migracion-37.sql
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'dbo.VersionEsquema', N'U') IS NULL OR OBJECT_ID(N'dbo.AceptacionVideollamada', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ConfiguracionOrganizacion', N'U') IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.EstadoTurno WHERE Descripcion = N'Ausente')
    RAISERROR(N'Faltan migraciones anteriores (18, 20, 28 o 33): aplicalas antes.', 16, 1);
GO

-- ── 1) Turno ───────────────────────────────────────────────
IF COL_LENGTH(N'dbo.Turno', N'Cobrable') IS NULL
    ALTER TABLE dbo.Turno ADD Cobrable BIT NULL;              -- NULL = no corresponde (turno normal)
IF COL_LENGTH(N'dbo.Turno', N'MotivoNoCobrable') IS NULL
    ALTER TABLE dbo.Turno ADD MotivoNoCobrable NVARCHAR(300) NULL;
IF COL_LENGTH(N'dbo.Turno', N'IdUsuarioAsistencia') IS NULL
    ALTER TABLE dbo.Turno ADD IdUsuarioAsistencia INT NULL;   -- quién marcó Ausente o canceló
IF COL_LENGTH(N'dbo.Turno', N'FechaAsistenciaUtc') IS NULL
    ALTER TABLE dbo.Turno ADD FechaAsistenciaUtc DATETIME2(0) NULL;
GO

-- ── 2) SeguimientoAsistencia ───────────────────────────────
IF OBJECT_ID(N'dbo.SeguimientoAsistencia', N'U') IS NULL
CREATE TABLE dbo.SeguimientoAsistencia (
    IdSeguimiento INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SeguimientoAsistencia PRIMARY KEY,
    IdPaciente    INT NOT NULL CONSTRAINT FK_SeguimientoAsistencia_Paciente REFERENCES dbo.Paciente(IdPaciente),
    IdProfesional INT NULL CONSTRAINT FK_SeguimientoAsistencia_Profesional REFERENCES dbo.Profesional(IdProfesional),
    Resultado     NVARCHAR(20) NOT NULL CONSTRAINT CK_SeguimientoAsistencia_Resultado
                  CHECK (Resultado IN (N'Sigue', N'Pausa', N'No responde', N'Otro')),
    Nota          NVARCHAR(500) NULL,
    IdUsuario     INT NOT NULL CONSTRAINT FK_SeguimientoAsistencia_Usuario REFERENCES dbo.Usuario(IdUsuario),
    FechaUtc      DATETIME2(0) NOT NULL CONSTRAINT DF_SeguimientoAsistencia_Fecha DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SeguimientoAsistencia') AND name = N'IX_SeguimientoAsistencia_Paciente')
    CREATE INDEX IX_SeguimientoAsistencia_Paciente ON dbo.SeguimientoAsistencia (IdPaciente, FechaUtc);
GO

-- ── 3) AbandonoTratamiento (de solo agregar) ───────────────
IF OBJECT_ID(N'dbo.AbandonoTratamiento', N'U') IS NULL
CREATE TABLE dbo.AbandonoTratamiento (
    IdAbandono            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AbandonoTratamiento PRIMARY KEY,
    IdPaciente            INT NOT NULL CONSTRAINT FK_AbandonoTratamiento_Paciente REFERENCES dbo.Paciente(IdPaciente),
    IdProfesional         INT NOT NULL CONSTRAINT FK_AbandonoTratamiento_Profesional REFERENCES dbo.Profesional(IdProfesional),
    AusenciasConsecutivas INT NOT NULL,
    Motivo                NVARCHAR(500) NULL,
    TurnosCancelados      INT NOT NULL CONSTRAINT DF_AbandonoTratamiento_Turnos DEFAULT (0),
    IdUsuarioConfirmo     INT NOT NULL CONSTRAINT FK_AbandonoTratamiento_Usuario REFERENCES dbo.Usuario(IdUsuario),
    Calidad               NVARCHAR(20) NOT NULL CONSTRAINT CK_AbandonoTratamiento_Calidad CHECK (Calidad IN (N'tratante', N'auditor')),
    FechaUtc              DATETIME2(0) NOT NULL CONSTRAINT DF_AbandonoTratamiento_Fecha DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AbandonoTratamiento') AND name = N'IX_AbandonoTratamiento_Paciente')
    CREATE INDEX IX_AbandonoTratamiento_Paciente ON dbo.AbandonoTratamiento (IdPaciente, IdProfesional, FechaUtc);
GO
CREATE OR ALTER TRIGGER dbo.TR_AbandonoTratamiento_SoloAgregar ON dbo.AbandonoTratamiento INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 50030, N'El abandono de tratamiento es un registro de la historia clínica: no se puede modificar ni borrar.', 1;
END
GO

-- ── 4) Privilegio Gestión de pacientes (Recepcionista) ─────
IF NOT EXISTS (SELECT 1 FROM dbo.Privilegio WHERE Clave = 'gestion_pacientes')
    INSERT INTO dbo.Privilegio (Clave, Nombre, Descripcion)
    VALUES ('gestion_pacientes', N'Gestión de pacientes',
            N'Ver los pacientes y sus turnos (sin la historia clínica) y cambiar el estado de un turno: marcar Ausente o cancelar.');
INSERT INTO dbo.PermisoPrivilegio (IdPermiso, IdPrivilegio)
SELECT p.IdPermiso, pr.IdPrivilegio
FROM dbo.Permiso p JOIN dbo.Privilegio pr ON pr.Clave = 'gestion_pacientes'
WHERE p.Clave = 'crm'
  AND NOT EXISTS (SELECT 1 FROM dbo.PermisoPrivilegio x WHERE x.IdPermiso = p.IdPermiso AND x.IdPrivilegio = pr.IdPrivilegio);
GO

-- ── 5) Parámetros de la organización ───────────────────────
INSERT INTO dbo.ConfiguracionOrganizacion (Clave, Valor)
SELECT v.Clave, v.Valor
FROM (VALUES (N'Asistencia.AvisoMinutos', N'5'), (N'Asistencia.ToleranciaMinutos', N'15'),
             (N'Asistencia.HorasCancelacionSinCargo', N'24'), (N'Asistencia.AusenciasAlerta', N'2'),
             (N'Asistencia.AusenciasAbandono', N'3')) AS v(Clave, Valor)
WHERE NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionOrganizacion c WHERE c.Clave = v.Clave);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 37)
    INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (37, N'add_asistencia_abandono.sql', 'Script');
GO
PRINT N'Migración 37 aplicada.';
SELECT Clave, Valor FROM dbo.ConfiguracionOrganizacion WHERE Clave LIKE N'Asistencia.%' ORDER BY Clave;
GO
