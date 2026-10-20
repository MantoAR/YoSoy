-- Migración 19: auditoría inalterable.
-- Idempotente: se puede correr más de una vez. Requiere la migración 18 (VersionEsquema y LogError).
-- Origen: rama nuevo-circuito1 de Alearmas01 (su migración "17" más la columna Espacio de su "16"), renumerada.
--
-- Qué hace:
--  1. LogAuditoria se divide en dos categorías: 'Administrativa' (por defecto) y 'Clinica'. Guarda también en qué módulo
--     se hizo (Espacio, nulo en lo anterior). Los registros que ya existen y son accesos a datos clínicos pasan a 'Clinica'
--     AHORA, porque después de esta migración nadie puede modificarlos.
--  2. Cada registro nuevo guarda el hash del anterior (cadena): si alguien con acceso de administrador a la base cambia o
--     borra una fila, sp_VerificarCadenaAuditoria lo detecta. Los registros anteriores (Hash nulo) no se encadenan.
--  3. LogAuditoria y LogError pasan a ser de solo agregar: un trigger rechaza UPDATE y DELETE (error 50018 y 50019).
--  La tercera capa (un usuario de base sin permisos de administrador para la aplicación, yosoy_app) NO está en esta
--  etapa: es un paso posterior y separado (docs/DEPLOY_ETAPA1_ERRORES_AUDITORIA_PRODUCCION.md, apéndice).
--
-- EFECTOS A TENER EN CUENTA:
--  - Las pruebas y los scripts que borraban filas de LogAuditoria o de LogError dejan de poder hacerlo (eso es lo que se
--    quiere). Una fila de LogAuditoria que apunta a un usuario impide borrar ese usuario (FK_LogAuditoria_Usuario).
--  - Cada INSERT en LogAuditoria toma un bloqueo sobre la tabla mientras se calcula el hash: las escrituras de auditoría
--    se hacen de a una. Con el volumen de esta aplicación no se nota.
--  - Una API anterior a esta etapa sigue pudiendo insertar (usa los valores por defecto); no puede modificar ni borrar.
--
-- Con sqlcmd se corre así:
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrationsdd_auditoria_inalterable.sql
--
-- Verificar (tiene que dar 3 triggers y la cadena íntegra):
--   SELECT COUNT(*) AS triggers FROM sys.triggers WHERE name IN ('TR_LogAuditoria_Encadenar', 'TR_LogAuditoria_SoloAgregar', 'TR_LogError_SoloAgregar');
--   EXEC sp_VerificarCadenaAuditoria;
--
-- Deshacer (con un backup antes; no borra datos, solo vuelve a permitir modificar la auditoría):
--   DROP TRIGGER TR_LogAuditoria_SoloAgregar; DROP TRIGGER TR_LogError_SoloAgregar; DROP TRIGGER TR_LogAuditoria_Encadenar;
--   DROP PROCEDURE sp_VerificarCadenaAuditoria; DROP FUNCTION dbo.fn_HashAuditoria;
--   DELETE FROM VersionEsquema WHERE Numero = 19;
-- Las columnas Categoria, Espacio, Hash y HashAnterior pueden quedar: la API anterior no las usa.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── 1) Columnas nuevas ────────────────────────────────────────
-- En qué módulo (permiso activo) se hizo la acción. Nulo en lo anterior.
IF COL_LENGTH('dbo.LogAuditoria', 'Espacio') IS NULL
    ALTER TABLE LogAuditoria ADD Espacio NVARCHAR(30) NULL;
GO
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
-- porque el trigger ya existe y este bloque se saltea. La lista es la de entidades que identifican la atención de un
-- paciente; es la misma que usa el listado de la auditoría clínica de la etapa 2.)
IF OBJECT_ID('dbo.TR_LogAuditoria_SoloAgregar', 'TR') IS NULL
    UPDATE LogAuditoria SET Categoria = 'Clinica'
    WHERE Categoria = 'Administrativa'
      AND Entidad IN ('HistoriaClinica', 'Evolucion', 'EventoCrisis', 'SesionClinica', 'Conversacion', 'Consentimiento', 'InteraccionIA',
                      'InformeAdmision', 'Derivacion', 'PacienteProfesional', 'Caso', 'Reporte', 'Turno');
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
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 19)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (19, N'add_auditoria_inalterable.sql', 'Script');
GO

PRINT N'Migración 19 aplicada.';
GO
