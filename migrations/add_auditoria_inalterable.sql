-- Auditoría inalterable (octubre 2026, etapa 2 de la propuesta de roles y permisos).
-- Es la migración 17 de la tabla del README. Idempotente: se puede correr más de una vez.
-- Requiere la migración 16.
--
-- Qué hace:
--  1. LogAuditoria se divide en dos categorías: 'Administrativa' (la ve el Configurador) y
--     'Clinica' (la ve el Profesional auditor). Guarda también en qué módulo se hizo.
--  2. Cada registro nuevo guarda el hash del anterior (cadena): si alguien con acceso de
--     administrador a la base cambia o borra una fila, sp_VerificarCadenaAuditoria lo detecta.
--  3. LogAuditoria y LogError pasan a ser de solo agregar: un trigger rechaza UPDATE y DELETE.
--  La tercera capa (un usuario de base sin permisos de administrador para la aplicación)
--  está en scripts/crear_usuario_aplicacion.sql, porque crea un login del servidor.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── 1) Columnas nuevas ────────────────────────────────────────
IF COL_LENGTH('dbo.LogAuditoria', 'Categoria') IS NULL
    ALTER TABLE LogAuditoria ADD Categoria NVARCHAR(20) NOT NULL
        CONSTRAINT DF_LogAuditoria_Categoria DEFAULT 'Administrativa'
        CONSTRAINT CK_LogAuditoria_Categoria CHECK (Categoria IN ('Administrativa', 'Clinica'));
IF COL_LENGTH('dbo.LogAuditoria', 'Hash') IS NULL
    ALTER TABLE LogAuditoria ADD Hash VARBINARY(32) NULL;
IF COL_LENGTH('dbo.LogAuditoria', 'HashAnterior') IS NULL
    ALTER TABLE LogAuditoria ADD HashAnterior VARBINARY(32) NULL;
GO

-- Los registros anteriores que son accesos a datos clínicos pasan a la categoría clínica.
-- (Va antes de crear el trigger que impide modificar filas; una segunda corrida no hace nada
-- porque el trigger ya existe y este bloque se saltea.)
IF OBJECT_ID('dbo.TR_LogAuditoria_SoloAgregar', 'TR') IS NULL
    UPDATE LogAuditoria SET Categoria = 'Clinica'
    WHERE Categoria = 'Administrativa'
      AND Entidad IN ('HistoriaClinica', 'Evolucion', 'EventoCrisis', 'SesionClinica', 'Conversacion', 'Consentimiento', 'InteraccionIA');
GO

-- ── 2) Hash de un registro ────────────────────────────────────
-- Una sola función para el trigger y para la verificación, así nunca calculan distinto.
-- No incluye IdLog: se asigna al insertar. El orden lo protege HashAnterior.
CREATE OR ALTER FUNCTION dbo.fn_HashAuditoria (
    @Anterior  VARBINARY(32),
    @IdUsuario INT,
    @Accion    NVARCHAR(100),
    @Entidad   NVARCHAR(50),
    @IdEntidad INT,
    @Detalle   NVARCHAR(500),
    @FechaHora DATETIME,
    @Categoria NVARCHAR(20),
    @Espacio   NVARCHAR(30))
RETURNS VARBINARY(32)
AS
BEGIN
    RETURN HASHBYTES('SHA2_256', CONCAT(
        CONVERT(NVARCHAR(64), @Anterior, 2), N'|',
        @IdUsuario, N'|', @Accion, N'|', @Entidad, N'|', @IdEntidad, N'|', @Detalle, N'|',
        CONVERT(NVARCHAR(23), @FechaHora, 126), N'|', @Categoria, N'|', @Espacio));
END;
GO

-- ── 3) Cada registro nuevo se encadena al anterior ───────────
-- INSTEAD OF INSERT: el trigger hace el INSERT real, de a una fila, con el hash del último
-- registro encadenado. UPDLOCK + HOLDLOCK: dos inserciones simultáneas no pueden tomar el
-- mismo "anterior" (la segunda espera a la primera).
CREATE OR ALTER TRIGGER TR_LogAuditoria_Encadenar ON LogAuditoria INSTEAD OF INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @IdUsuario INT, @Accion NVARCHAR(100), @Entidad NVARCHAR(50), @IdEntidad INT,
            @Detalle NVARCHAR(500), @FechaHora DATETIME, @Categoria NVARCHAR(20), @Espacio NVARCHAR(30),
            @Anterior VARBINARY(32);

    DECLARE filas CURSOR LOCAL FAST_FORWARD FOR
        SELECT IdUsuario, Accion, Entidad, IdEntidad, Detalle,
               ISNULL(FechaHora, GETDATE()), ISNULL(Categoria, 'Administrativa'), Espacio
        FROM inserted;
    OPEN filas;
    FETCH NEXT FROM filas INTO @IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @FechaHora, @Categoria, @Espacio;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Anterior = NULL;
        SELECT TOP 1 @Anterior = Hash
        FROM LogAuditoria WITH (UPDLOCK, HOLDLOCK)
        WHERE Hash IS NOT NULL
        ORDER BY IdLog DESC;

        INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, FechaHora, Categoria, Espacio, Hash, HashAnterior)
        VALUES (@IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @FechaHora, @Categoria, @Espacio,
                dbo.fn_HashAuditoria(@Anterior, @IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @FechaHora, @Categoria, @Espacio),
                @Anterior);

        FETCH NEXT FROM filas INTO @IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @FechaHora, @Categoria, @Espacio;
    END;
    CLOSE filas;
    DEALLOCATE filas;
END;
GO

-- ── 4) Solo agregar ───────────────────────────────────────────
-- RN-18: nadie modifica ni borra la auditoría ni el registro de errores, tampoco el backend.
-- Para depurar registros viejos (si algún día se define un plazo de conservación) hace
-- falta un administrador de la base que desactive el trigger a mano, y eso queda en el
-- registro del propio SQL Server.
CREATE OR ALTER TRIGGER TR_LogAuditoria_SoloAgregar ON LogAuditoria INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 50018, N'RN-18: el registro de auditoría es de solo lectura. No se puede modificar ni borrar.', 1;
END;
GO

CREATE OR ALTER TRIGGER TR_LogError_SoloAgregar ON LogError INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 50019, N'RN-18: el registro de errores es de solo lectura. No se puede modificar ni borrar.', 1;
END;
GO

-- ── 5) Verificación de la cadena ──────────────────────────────
-- EXEC sp_VerificarCadenaAuditoria;
-- Devuelve una fila: 'Íntegra' con la cantidad revisada, o 'Rota' con el primer registro
-- donde falla y por qué. Los registros anteriores a esta migración (Hash nulo) no se revisan.
CREATE OR ALTER PROCEDURE sp_VerificarCadenaAuditoria AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @IdLog INT, @IdUsuario INT, @Accion NVARCHAR(100), @Entidad NVARCHAR(50), @IdEntidad INT,
            @Detalle NVARCHAR(500), @FechaHora DATETIME, @Categoria NVARCHAR(20), @Espacio NVARCHAR(30),
            @Hash VARBINARY(32), @HashAnterior VARBINARY(32),
            @Anterior VARBINARY(32) = NULL, @Revisados INT = 0;

    DECLARE filas CURSOR LOCAL FAST_FORWARD FOR
        SELECT IdLog, IdUsuario, Accion, Entidad, IdEntidad, Detalle, FechaHora, Categoria, Espacio, Hash, HashAnterior
        FROM LogAuditoria
        WHERE Hash IS NOT NULL
        ORDER BY IdLog;
    OPEN filas;
    FETCH NEXT FROM filas INTO @IdLog, @IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @FechaHora, @Categoria, @Espacio, @Hash, @HashAnterior;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF ISNULL(@HashAnterior, 0x) <> ISNULL(@Anterior, 0x)
        BEGIN
            CLOSE filas; DEALLOCATE filas;
            SELECT N'Rota' AS Estado, @Revisados AS RegistrosRevisados, @IdLog AS IdLog,
                   N'El registro no sigue al anterior: se borró o se insertó uno en el medio.' AS Motivo;
            RETURN;
        END;
        IF @Hash <> dbo.fn_HashAuditoria(@HashAnterior, @IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @FechaHora, @Categoria, @Espacio)
        BEGIN
            CLOSE filas; DEALLOCATE filas;
            SELECT N'Rota' AS Estado, @Revisados AS RegistrosRevisados, @IdLog AS IdLog,
                   N'El contenido del registro cambió después de guardarse.' AS Motivo;
            RETURN;
        END;
        SET @Anterior = @Hash;
        SET @Revisados += 1;
        FETCH NEXT FROM filas INTO @IdLog, @IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @FechaHora, @Categoria, @Espacio, @Hash, @HashAnterior;
    END;
    CLOSE filas;
    DEALLOCATE filas;
    SELECT N'Íntegra' AS Estado, @Revisados AS RegistrosRevisados, CAST(NULL AS INT) AS IdLog, CAST(NULL AS NVARCHAR(200)) AS Motivo;
END;
GO

-- ── 6) Registro de versión ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 17)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (17, N'add_auditoria_inalterable.sql', 'Script');
GO

PRINT N'Migración 17 aplicada.';
GO
