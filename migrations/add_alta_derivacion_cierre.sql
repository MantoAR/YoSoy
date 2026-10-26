-- Alta clinica y derivacion interna asistidas desde el cierre de sesion (octubre 2026). Es la migracion 27. Aditiva, idempotente y en una sola transaccion.
-- Requiere la 18 (VersionEsquema), la 26 (SesionClinica.ClaveCierre) y las tablas Caso, Derivacion, Paciente, Profesional y Usuario.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_alta_derivacion_cierre.sql
--
-- Es OPCIONAL: la API de esta version anda sin ella para todo lo de siempre. Si falta, el alta clinica y la derivacion interna responden 409 "falta-migracion-27".
--
-- Deshacer (SOLO si todavia no se uso; si hay altas o derivaciones del cierre, el script se niega a borrarlas):
--   docs/produccion/deshacer-migracion-27.sql
--
-- Que hace:
--  1. Caso.Estado admite un valor nuevo, 'En alta': el alta clinica ya se dio y el plan de control esta guardado. Es el unico estado que se agrega. Queda FUERA de
--     'Asignado' y 'En atencion', asi que el caso deja de contar en la carga del profesional para nuevas asignaciones.
--  2. Tabla AltaClinica: el informe de alta, la fecha de alta y el PLAN DE CONTROL (cuantos contactos, cada cuantos dias y la fecha del primero). El plan se GUARDA, no se
--     programa: una etapa posterior lo lee para crear los contactos en la agenda. Una sola alta por caso (indice unico): confirmar dos veces no duplica nada.
--  3. Derivacion (la "solicitud de cambio" de siempre) gana columnas para la derivacion interna asistida: Origen ('Solicitud' = la de siempre, 'Cierre' = nacida del
--     cierre de sesion), IdCaso, Observaciones, IdSesion, y quien/cuando respondio el profesional de destino y quien/cuando la resolvio Admision. Las filas existentes
--     quedan Origen = 'Solicitud' y no cambian. Indice unico filtrado: un caso tiene a lo sumo UNA derivacion del cierre en curso.
--  4. Registra la version 27 en VersionEsquema, con Origen 'Script'.
-- No toca ninguna fila existente (salvo el valor por defecto 'Solicitud' de la columna nueva).

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Caso', 'U') IS NULL OR OBJECT_ID('dbo.Derivacion', 'U') IS NULL OR COL_LENGTH('dbo.SesionClinica', 'ClaveCierre') IS NULL
    RAISERROR(N'Falta VersionEsquema, Caso, Derivacion o la migracion 26: aplica antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) Caso.Estado: se agrega 'En alta'
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND parent_object_id = OBJECT_ID('dbo.Caso') AND definition NOT LIKE N'%En alta%')
        ALTER TABLE dbo.Caso DROP CONSTRAINT CK_Caso_Estado;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND parent_object_id = OBJECT_ID('dbo.Caso'))
        ALTER TABLE dbo.Caso ADD CONSTRAINT CK_Caso_Estado CHECK (Estado IN (N'Nuevo', N'Derivado', N'Asignado', N'En atención', N'En alta', N'Cerrado'));

    -- 2) El alta clinica
    IF OBJECT_ID('dbo.AltaClinica', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AltaClinica (
            IdAltaClinica      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AltaClinica PRIMARY KEY,
            IdCaso             INT NOT NULL CONSTRAINT FK_AltaClinica_Caso REFERENCES dbo.Caso(IdCaso),
            IdPaciente         INT NOT NULL CONSTRAINT FK_AltaClinica_Paciente REFERENCES dbo.Paciente(IdPaciente),
            IdProfesional      INT NOT NULL CONSTRAINT FK_AltaClinica_Profesional REFERENCES dbo.Profesional(IdProfesional),
            IdSesion           INT NULL CONSTRAINT FK_AltaClinica_Sesion REFERENCES dbo.SesionClinica(IdSesion),
            FechaAlta          DATE NOT NULL,
            Informe            NVARCHAR(MAX) NOT NULL,
            ControlesPrevistos INT NOT NULL CONSTRAINT DF_AltaClinica_Controles DEFAULT (0),
            ControlCadaDias    INT NULL,
            PrimerControl      DATE NULL,
            EstadoCasoAnterior NVARCHAR(20) NOT NULL,
            IdUsuario          INT NULL CONSTRAINT FK_AltaClinica_Usuario REFERENCES dbo.Usuario(IdUsuario),
            CreadaUtc          DATETIME2(3) NOT NULL CONSTRAINT DF_AltaClinica_CreadaUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT CK_AltaClinica_Plan CHECK (
                (ControlesPrevistos = 0 AND ControlCadaDias IS NULL AND PrimerControl IS NULL)
                OR (ControlesPrevistos BETWEEN 1 AND 12 AND ControlCadaDias IS NOT NULL AND ControlCadaDias BETWEEN 7 AND 365 AND PrimerControl IS NOT NULL))
        );
        CREATE UNIQUE INDEX UX_AltaClinica_Caso ON dbo.AltaClinica (IdCaso);
        CREATE INDEX IX_AltaClinica_Sesion ON dbo.AltaClinica (IdSesion) WHERE IdSesion IS NOT NULL;
    END

    -- 3) Derivacion: columnas de la derivacion interna asistida (van en EXEC porque el lote se compila antes de que existan)
    IF COL_LENGTH('dbo.Derivacion', 'Origen') IS NULL
        ALTER TABLE dbo.Derivacion ADD Origen NVARCHAR(20) NOT NULL CONSTRAINT DF_Derivacion_Origen DEFAULT (N'Solicitud');
    IF COL_LENGTH('dbo.Derivacion', 'IdCaso') IS NULL ALTER TABLE dbo.Derivacion ADD IdCaso INT NULL;
    IF COL_LENGTH('dbo.Derivacion', 'Observaciones') IS NULL ALTER TABLE dbo.Derivacion ADD Observaciones NVARCHAR(1000) NULL;
    IF COL_LENGTH('dbo.Derivacion', 'IdSesion') IS NULL ALTER TABLE dbo.Derivacion ADD IdSesion INT NULL;
    IF COL_LENGTH('dbo.Derivacion', 'RespuestaUtc') IS NULL ALTER TABLE dbo.Derivacion ADD RespuestaUtc DATETIME2(3) NULL;
    IF COL_LENGTH('dbo.Derivacion', 'IdUsuarioRespuesta') IS NULL ALTER TABLE dbo.Derivacion ADD IdUsuarioRespuesta INT NULL;
    IF COL_LENGTH('dbo.Derivacion', 'ResolucionUtc') IS NULL ALTER TABLE dbo.Derivacion ADD ResolucionUtc DATETIME2(3) NULL;
    IF COL_LENGTH('dbo.Derivacion', 'IdUsuarioResolucion') IS NULL ALTER TABLE dbo.Derivacion ADD IdUsuarioResolucion INT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Derivacion_Origen')
        EXEC(N'ALTER TABLE dbo.Derivacion ADD CONSTRAINT CK_Derivacion_Origen CHECK (Origen IN (N''Solicitud'', N''Cierre''))');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Derivacion_Caso')
        EXEC(N'ALTER TABLE dbo.Derivacion ADD CONSTRAINT FK_Derivacion_Caso FOREIGN KEY (IdCaso) REFERENCES dbo.Caso(IdCaso)');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Derivacion_Sesion')
        EXEC(N'ALTER TABLE dbo.Derivacion ADD CONSTRAINT FK_Derivacion_Sesion FOREIGN KEY (IdSesion) REFERENCES dbo.SesionClinica(IdSesion)');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Derivacion_CasoEnCurso' AND object_id = OBJECT_ID('dbo.Derivacion'))
        EXEC(N'CREATE UNIQUE INDEX UX_Derivacion_CasoEnCurso ON dbo.Derivacion (IdCaso) WHERE Origen = N''Cierre'' AND IdCaso IS NOT NULL AND Estado IN (N''Pendiente del destino'', N''Aceptada por el destino'')');

    -- 4) Registro de version
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 27)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (27, N'add_alta_derivacion_cierre.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migracion 27 aplicada.';
SELECT (SELECT COUNT(*) FROM dbo.AltaClinica) AS altas, (SELECT COUNT(*) FROM dbo.Derivacion WHERE Origen = N'Cierre') AS derivaciones_del_cierre;
GO
