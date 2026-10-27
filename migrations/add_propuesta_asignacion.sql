-- Propuesta automática de profesional y horario al aceptar un formulario «Sobre mí» (octubre 2026). Es la migración 32. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y las tablas InformeAdmision, Paciente, Profesional, Usuario y Turno, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_propuesta_asignacion.sql
--
-- Es OPCIONAL para que la API arranque: sin ella, aceptar un formulario anda como hasta ahora (la recepción asigna a mano) y el panel no muestra propuestas.
-- Con ella, al aceptar un formulario el sistema propone profesional y horario y el paciente decide. Aplicarla ANTES de publicar la API que la usa.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-32.sql
--
-- Qué hace:
--  1. Tabla PropuestaAsignacion: cada propuesta de «este profesional, este día y hora» que el sistema le hace a un paciente (de a una Pendiente por formulario; hasta 3 por
--     formulario: NroPropuesta). Estado: Pendiente / Aceptada / Rechazada / Vencida / Cancelada. ExpiraEn = hasta cuándo puede responder (48 horas).
--     IdUsuarioOrigen = la persona de recepción que aceptó el formulario (de quien parte la acción automática). IdTurno = el turno que se creó al aceptarla.
--  2. Dos índices únicos filtrados a las Pendientes: un mismo horario de un profesional no se le propone a dos pacientes a la vez, y un formulario no tiene dos
--     propuestas pendientes (así un doble clic o dos pedidos simultáneos no duplican nada).
--  3. InformeAdmision.AsignacionEstado y AsignacionMotivo (NULL = nunca pasó por la propuesta automática: los formularios de antes siguen como estaban): AsignacionEstado vale
--     'Propuesta' (hay una pendiente) o 'Por resolver' (el sistema no pudo resolver solo y le toca a la recepción), con el motivo en lenguaje claro.
--     Agregar columnas NULL es solo un cambio de metadatos: no recorre la tabla.
--  4. Registra la versión 32 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.InformeAdmision', 'U') IS NULL OR OBJECT_ID('dbo.Paciente', 'U') IS NULL
   OR OBJECT_ID('dbo.Profesional', 'U') IS NULL OR OBJECT_ID('dbo.Usuario', 'U') IS NULL OR OBJECT_ID('dbo.Turno', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, InformeAdmision, Paciente, Profesional, Usuario o Turno: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- ── 1) Las propuestas ─────────────────────────────────────
    IF OBJECT_ID('dbo.PropuestaAsignacion', 'U') IS NULL
        CREATE TABLE dbo.PropuestaAsignacion (
            IdPropuesta      INT            IDENTITY(1,1) CONSTRAINT PK_PropuestaAsignacion PRIMARY KEY,
            IdInformeAdmision INT           NOT NULL CONSTRAINT FK_PropuestaAsignacion_Informe     REFERENCES dbo.InformeAdmision(IdInforme),
            IdPaciente       INT            NOT NULL CONSTRAINT FK_PropuestaAsignacion_Paciente    REFERENCES dbo.Paciente(IdPaciente),
            IdProfesional    INT            NOT NULL CONSTRAINT FK_PropuestaAsignacion_Profesional REFERENCES dbo.Profesional(IdProfesional),
            FechaHora        DATETIME       NOT NULL,
            Estado           NVARCHAR(20)   NOT NULL CONSTRAINT DF_PropuestaAsignacion_Estado DEFAULT N'Pendiente',
            Motivo           NVARCHAR(500)  NULL,
            NroPropuesta     TINYINT        NOT NULL,
            FechaCreacion    DATETIME       NOT NULL CONSTRAINT DF_PropuestaAsignacion_FechaCreacion DEFAULT GETDATE(),
            FechaRespuesta   DATETIME       NULL,
            ExpiraEn         DATETIME       NOT NULL,
            IdUsuarioOrigen  INT            NULL CONSTRAINT FK_PropuestaAsignacion_Usuario REFERENCES dbo.Usuario(IdUsuario),
            IdTurno          INT            NULL CONSTRAINT FK_PropuestaAsignacion_Turno   REFERENCES dbo.Turno(IdTurno),
            CONSTRAINT CK_PropuestaAsignacion_Estado CHECK (Estado IN (N'Pendiente', N'Aceptada', N'Rechazada', N'Vencida', N'Cancelada')),
            CONSTRAINT CK_PropuestaAsignacion_Nro    CHECK (NroPropuesta BETWEEN 1 AND 3)
        );

    -- ── 2) Una sola pendiente por horario y por formulario ────
    -- En un EXEC: la tabla recién creada no existe todavía cuando se compila este lote.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PropuestaAsignacion_HorarioPendiente' AND object_id = OBJECT_ID('dbo.PropuestaAsignacion'))
        EXEC(N'CREATE UNIQUE INDEX UX_PropuestaAsignacion_HorarioPendiente ON dbo.PropuestaAsignacion (IdProfesional, FechaHora) WHERE Estado = N''Pendiente'';');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PropuestaAsignacion_InformePendiente' AND object_id = OBJECT_ID('dbo.PropuestaAsignacion'))
        EXEC(N'CREATE UNIQUE INDEX UX_PropuestaAsignacion_InformePendiente ON dbo.PropuestaAsignacion (IdInformeAdmision) WHERE Estado = N''Pendiente'';');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropuestaAsignacion_Paciente' AND object_id = OBJECT_ID('dbo.PropuestaAsignacion'))
        EXEC(N'CREATE INDEX IX_PropuestaAsignacion_Paciente ON dbo.PropuestaAsignacion (IdPaciente, IdPropuesta);');

    -- ── 3) Dónde quedó el formulario ──────────────────────────
    IF COL_LENGTH('dbo.InformeAdmision', 'AsignacionEstado') IS NULL ALTER TABLE dbo.InformeAdmision ADD AsignacionEstado NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.InformeAdmision', 'AsignacionMotivo') IS NULL ALTER TABLE dbo.InformeAdmision ADD AsignacionMotivo NVARCHAR(500) NULL;

    -- ── 4) Registro de versión ────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 32)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (32, N'add_propuesta_asignacion.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 32 aplicada.';
EXEC(N'SELECT COUNT(*) AS propuestas FROM dbo.PropuestaAsignacion');
GO
