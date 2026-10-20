-- Migración 20: roles, permisos y privilegios; datos de personas y profesionales; datos de la organización y vista de soporte.
-- Idempotente: se puede correr más de una vez. Solo AGREGA (tablas, columnas, índices, filas de catálogo, un trigger y una vista); el único
-- UPDATE sobre datos existentes completa columnas nuevas de Especialidad (Disciplina, Tipo) y la clave de las filas de Rol que ya
-- hubiera (en producción Rol está vacío). No asigna permisos a nadie: eso lo hace la migración 23, después de la 21 (para que
-- quede en la auditoría).
-- Origen: rama nuevo-circuito1 de Alearmas01 (su migración "16", partes C a F y H), renumerada; las partes A, B y la
-- columna Espacio ya están en las migraciones 18 y 19 (etapa 1) y NO se repiten acá. Requiere la 18 y la 19.
--
-- Cómo se piensa (ver docs/MATRIZ_ENDPOINTS.md):
--   Rol        = el área (Profesional, CRM, Facturación, Configurador).
--   Permiso    = el nivel dentro del rol; cada permiso es un espacio de trabajo al ingresar.
--   Privilegio = una capacidad puntual que se suma a un permiso (Admisión, Gestión de agendas).
-- El código busca siempre por Clave, nunca por id ni por nombre visible.
--
-- Con sqlcmd se corre así:
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrationsdd_permisos_roles_privilegios.sql
--
-- Verificar: la salida de abajo tiene que dar 4 roles con clave, 5 permisos, 1 privilegio y 2 permisos con privilegio (la 21 agrega el segundo privilegio).
--
-- Deshacer (solo si todavía no se asignó ningún permiso; con un backup antes): DROP VIEW vw_UsuarioAccesos;
--   DROP TRIGGER TR_UsuarioPermiso_RequiereProfesional; DROP TABLE UsuarioPrivilegio, UsuarioPermiso, PermisoPrivilegio, Privilegio, Permiso,
--   ConfiguracionOrganizacion, Organizacion; y las columnas nuevas de Usuario, Profesional, Especialidad y Rol.Clave (con sus restricciones).
--   Los datos de negocio no cambian. Si ya hay permisos asignados, volver atrás es restaurar el backup.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ══════════════════════════════════════════════════════════════
-- C) ROLES, PERMISOS Y PRIVILEGIOS
-- ══════════════════════════════════════════════════════════════
-- Rol       = el área (Profesional, CRM, Facturación, Configurador).
-- Permiso   = el nivel dentro del rol; cada permiso es un espacio de trabajo al ingresar.
-- Privilegio = una capacidad puntual que se suma a un permiso (por ahora, Admisión).
-- El código busca siempre por Clave, nunca por id ni por nombre visible.

-- C.1) Rol: la tabla ya existía (base original) pero ningún control de acceso la leía.
IF COL_LENGTH('dbo.Rol', 'Clave') IS NULL
    ALTER TABLE Rol ADD Clave NVARCHAR(30) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Rol') AND name = 'UX_Rol_Clave')
    CREATE UNIQUE INDEX UX_Rol_Clave ON Rol(Clave) WHERE Clave IS NOT NULL;
GO

-- Si ya hay filas con estos nombres (scripts de prueba viejos), se les pone la clave en vez
-- de duplicarlas. La comparación ignora mayúsculas y acentos.
UPDATE Rol SET Clave = 'profesional'  WHERE Clave IS NULL AND Nombre COLLATE Latin1_General_CI_AI = N'Profesional'
    AND NOT EXISTS (SELECT 1 FROM Rol WHERE Clave = 'profesional');
UPDATE Rol SET Clave = 'configurador' WHERE Clave IS NULL AND Nombre COLLATE Latin1_General_CI_AI = N'Configurador'
    AND NOT EXISTS (SELECT 1 FROM Rol WHERE Clave = 'configurador');
UPDATE Rol SET Clave = 'paciente'     WHERE Clave IS NULL AND Nombre COLLATE Latin1_General_CI_AI = N'Paciente'
    AND NOT EXISTS (SELECT 1 FROM Rol WHERE Clave = 'paciente');

IF NOT EXISTS (SELECT 1 FROM Rol WHERE Clave = 'profesional')
    INSERT INTO Rol (Nombre, Estado, Clave) VALUES (N'Profesional', 'Activo', 'profesional');
IF NOT EXISTS (SELECT 1 FROM Rol WHERE Clave = 'crm')
    INSERT INTO Rol (Nombre, Estado, Clave) VALUES (N'CRM y atención al cliente', 'Activo', 'crm');
IF NOT EXISTS (SELECT 1 FROM Rol WHERE Clave = 'facturacion')
    INSERT INTO Rol (Nombre, Estado, Clave) VALUES (N'Administración de facturación y pagos', 'Activo', 'facturacion');
IF NOT EXISTS (SELECT 1 FROM Rol WHERE Clave = 'configurador')
    INSERT INTO Rol (Nombre, Estado, Clave) VALUES (N'Configurador', 'Activo', 'configurador');
GO

-- C.2) Permiso
IF OBJECT_ID('dbo.Permiso', 'U') IS NULL
CREATE TABLE Permiso (
    IdPermiso INT           IDENTITY(1,1) PRIMARY KEY,
    IdRol     INT           NOT NULL REFERENCES Rol(IdRol),
    Clave     NVARCHAR(30)  NOT NULL CONSTRAINT UQ_Permiso_Clave UNIQUE,
    Nombre    NVARCHAR(100) NOT NULL,
    Estado    NVARCHAR(20)  NOT NULL CONSTRAINT DF_Permiso_Estado DEFAULT 'Activo',
    -- Permite que UsuarioPermiso compruebe con una clave foránea que el permiso es del rol.
    CONSTRAINT UQ_Permiso_Rol UNIQUE (IdPermiso, IdRol)
);
GO
INSERT INTO Permiso (IdRol, Clave, Nombre)
SELECT r.IdRol, v.Clave, v.Nombre
FROM (VALUES
        ('profesional',  'profesional',  N'Profesional'),
        ('profesional',  'auditor',      N'Profesional auditor'),
        ('crm',          'crm',          N'CRM y atención al cliente'),
        ('facturacion',  'facturacion',  N'Administración de facturación y pagos'),
        ('configurador', 'configurador', N'Configurador')
     ) AS v(ClaveRol, Clave, Nombre)
JOIN Rol r ON r.Clave = v.ClaveRol
WHERE NOT EXISTS (SELECT 1 FROM Permiso p WHERE p.Clave = v.Clave);
GO

-- C.3) Privilegio y qué privilegios admite cada permiso
IF OBJECT_ID('dbo.Privilegio', 'U') IS NULL
CREATE TABLE Privilegio (
    IdPrivilegio INT           IDENTITY(1,1) PRIMARY KEY,
    Clave        NVARCHAR(30)  NOT NULL CONSTRAINT UQ_Privilegio_Clave UNIQUE,
    Nombre       NVARCHAR(100) NOT NULL,
    Descripcion  NVARCHAR(400) NULL,
    Estado       NVARCHAR(20)  NOT NULL CONSTRAINT DF_Privilegio_Estado DEFAULT 'Activo'
);
GO
IF NOT EXISTS (SELECT 1 FROM Privilegio WHERE Clave = 'admision')
    INSERT INTO Privilegio (Clave, Nombre, Descripcion)
    VALUES ('admision', N'Admisión',
            N'Informe de admisión, primera entrevista, derivación, asignación de pacientes, casos y solicitudes de cambio de profesional.');
GO

IF OBJECT_ID('dbo.PermisoPrivilegio', 'U') IS NULL
CREATE TABLE PermisoPrivilegio (
    IdPermiso    INT NOT NULL REFERENCES Permiso(IdPermiso),
    IdPrivilegio INT NOT NULL REFERENCES Privilegio(IdPrivilegio),
    CONSTRAINT PK_PermisoPrivilegio PRIMARY KEY (IdPermiso, IdPrivilegio)
);
GO
-- Admisión solo para Profesional y Profesional auditor (decisión del 15/10).
INSERT INTO PermisoPrivilegio (IdPermiso, IdPrivilegio)
SELECT p.IdPermiso, pr.IdPrivilegio
FROM Permiso p
JOIN Privilegio pr ON pr.Clave = 'admision'
WHERE p.Clave IN ('profesional', 'auditor')
  AND NOT EXISTS (SELECT 1 FROM PermisoPrivilegio x WHERE x.IdPermiso = p.IdPermiso AND x.IdPrivilegio = pr.IdPrivilegio);
GO

-- C.4) Asignaciones a usuarios. UsuarioRol ya existe (IdUsuario, IdRol).
-- La base garantiza la cadena: no hay permiso sin su rol, ni privilegio en un permiso que
-- no lo admite. Quitar un rol quita sus permisos y privilegios (cascada).
IF OBJECT_ID('dbo.UsuarioPermiso', 'U') IS NULL
CREATE TABLE UsuarioPermiso (
    IdUsuario        INT          NOT NULL,
    IdRol            INT          NOT NULL,
    IdPermiso        INT          NOT NULL,
    FechaAsignacion  DATETIME2(0) NOT NULL CONSTRAINT DF_UsuarioPermiso_Fecha DEFAULT SYSDATETIME(),
    IdUsuarioAsigno  INT          NULL,
    CONSTRAINT PK_UsuarioPermiso PRIMARY KEY (IdUsuario, IdPermiso),
    CONSTRAINT FK_UsuarioPermiso_UsuarioRol FOREIGN KEY (IdUsuario, IdRol)
        REFERENCES UsuarioRol (IdUsuario, IdRol) ON DELETE CASCADE,
    CONSTRAINT FK_UsuarioPermiso_Permiso FOREIGN KEY (IdPermiso, IdRol)
        REFERENCES Permiso (IdPermiso, IdRol)
);
GO

IF OBJECT_ID('dbo.UsuarioPrivilegio', 'U') IS NULL
CREATE TABLE UsuarioPrivilegio (
    IdUsuario        INT          NOT NULL,
    IdPermiso        INT          NOT NULL,
    IdPrivilegio     INT          NOT NULL,
    FechaAsignacion  DATETIME2(0) NOT NULL CONSTRAINT DF_UsuarioPrivilegio_Fecha DEFAULT SYSDATETIME(),
    IdUsuarioAsigno  INT          NULL,
    CONSTRAINT PK_UsuarioPrivilegio PRIMARY KEY (IdUsuario, IdPermiso, IdPrivilegio),
    CONSTRAINT FK_UsuarioPrivilegio_UsuarioPermiso FOREIGN KEY (IdUsuario, IdPermiso)
        REFERENCES UsuarioPermiso (IdUsuario, IdPermiso) ON DELETE CASCADE,
    CONSTRAINT FK_UsuarioPrivilegio_PermisoPrivilegio FOREIGN KEY (IdPermiso, IdPrivilegio)
        REFERENCES PermisoPrivilegio (IdPermiso, IdPrivilegio)
);
GO

-- ══════════════════════════════════════════════════════════════
-- D) DATOS DE PERSONAS Y DE PROFESIONALES
-- ══════════════════════════════════════════════════════════════
-- Nombre, apellido y CUIL para quien no tiene ficha de profesional (CRM, facturación,
-- administración). El dígito verificador del CUIL lo valida la API; la base solo exige 11
-- dígitos y que no se repita.
IF COL_LENGTH('dbo.Usuario', 'Nombre') IS NULL
    ALTER TABLE Usuario ADD Nombre NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.Usuario', 'Apellido') IS NULL
    ALTER TABLE Usuario ADD Apellido NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.Usuario', 'Cuil') IS NULL
    ALTER TABLE Usuario ADD Cuil CHAR(11) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Usuario_Cuil')
    ALTER TABLE Usuario ADD CONSTRAINT CK_Usuario_Cuil
        CHECK (Cuil IS NULL OR (LEN(Cuil) = 11 AND Cuil NOT LIKE '%[^0-9]%'));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Usuario') AND name = 'UX_Usuario_Cuil')
    CREATE UNIQUE INDEX UX_Usuario_Cuil ON Usuario(Cuil) WHERE Cuil IS NOT NULL;
GO

-- La matrícula profesional es provincial: se guarda quién la otorgó. La de especialista es
-- obligatoria solo si la especialidad la pide (lo controla la API al dar de alta).
IF COL_LENGTH('dbo.Profesional', 'JurisdiccionMatricula') IS NULL
    ALTER TABLE Profesional ADD JurisdiccionMatricula NVARCHAR(60) NULL;
IF COL_LENGTH('dbo.Profesional', 'MatriculaEspecialista') IS NULL
    ALTER TABLE Profesional ADD MatriculaEspecialista NVARCHAR(50) NULL;
GO

-- RN-17: el rol Profesional exige ficha de profesional con matrícula y especialidad. La API
-- lo valida antes; este trigger lo garantiza aunque alguien inserte a mano.
CREATE OR ALTER TRIGGER TR_UsuarioPermiso_RequiereProfesional ON UsuarioPermiso AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN Permiso p ON p.IdPermiso = i.IdPermiso
        JOIN Rol r     ON r.IdRol = p.IdRol AND r.Clave = 'profesional'
        WHERE NOT EXISTS (
            SELECT 1
            FROM UsuarioProfesional up
            JOIN Profesional pr ON pr.IdProfesional = up.IdProfesional
            WHERE up.IdUsuario = i.IdUsuario
              AND LTRIM(RTRIM(ISNULL(pr.Matricula, ''))) <> ''
              AND pr.IdEspecialidad IS NOT NULL))
        THROW 50017, N'RN-17: el rol Profesional requiere una ficha de profesional con matrícula y especialidad.', 1;
END;
GO

-- ══════════════════════════════════════════════════════════════
-- E) CATÁLOGO DE ESPECIALIDADES
-- ══════════════════════════════════════════════════════════════
-- Tipo 'Servicio' = filas que no son especialidades de una persona sino servicios a los que
-- se deriva (las usa ReglaDerivacion). No aparecen al elegir la especialidad de un
-- profesional, pero no se borran ni se desactivan.
IF COL_LENGTH('dbo.Especialidad', 'Disciplina') IS NULL
    ALTER TABLE Especialidad ADD Disciplina NVARCHAR(60) NULL;
IF COL_LENGTH('dbo.Especialidad', 'Tipo') IS NULL
    ALTER TABLE Especialidad ADD Tipo NVARCHAR(20) NOT NULL
        CONSTRAINT DF_Especialidad_Tipo DEFAULT 'Especialidad'
        CONSTRAINT CK_Especialidad_Tipo CHECK (Tipo IN ('Especialidad', 'Servicio'));
IF COL_LENGTH('dbo.Especialidad', 'RequiereMatriculaEspecialista') IS NULL
    ALTER TABLE Especialidad ADD RequiereMatriculaEspecialista BIT NOT NULL
        CONSTRAINT DF_Especialidad_ReqMatEsp DEFAULT 0;
GO

UPDATE Especialidad SET Tipo = 'Servicio'
WHERE Nombre COLLATE Latin1_General_CI_AI IN (N'Admision', N'Admision Infanto-Juvenil', N'Atencion Domiciliaria',
                                              N'Centro de Rehabilitacion Sociolaboral', N'Psicologia Infanto-Juvenil')
  AND Tipo <> 'Servicio';
GO

-- Fuentes (no se inventa ninguna denominación):
--  - Disciplinas: Ley 26.657 de Salud Mental, art. 8 (psicología, psiquiatría, trabajo social,
--    enfermería, terapia ocupacional "y otras disciplinas o campos pertinentes"). Psicopedagogía
--    y Psicomotricidad ya estaban en el catálogo de la base.
--  - Especialidades de psicología: las seis "especialidades básicas" del Reglamento de
--    Especialidades del Colegio de Psicólogos de la Provincia de Córdoba (Resolución de Junta
--    de Gobierno N° 083/16, Cap. I inc. b y Cap. II). Máximo dos por psicólogo (Cap. I inc. e);
--    la matrícula de especialista dura cinco años (Cap. I inc. f).
--    https://cppc.org.ar/wp-content/uploads/2023/06/Reglamento-de-Especialidades.pdf
--  Si el Colegio cambia el reglamento, se actualiza el catálogo desde Configuración, sin código.
-- Se completa la disciplina de las filas que ya existen (sin importar acentos); no se duplica ni se cambia ningún estado.
DECLARE @Catalogo TABLE (Nombre NVARCHAR(100), Disciplina NVARCHAR(60), ReqEsp BIT, Estado NVARCHAR(20), Alias NVARCHAR(100) NULL);
INSERT INTO @Catalogo VALUES
    (N'Psicología',                                  N'Psicología',          0, 'Activo',   NULL),
    (N'Psicología Clínica',                          N'Psicología',          1, 'Activo',   NULL),
    (N'Psicología Educacional',                      N'Psicología',          1, 'Activo',   NULL),
    (N'Psicología Jurídica y Forense',               N'Psicología',          1, 'Activo',   NULL),
    (N'Psicología del Trabajo y las Organizaciones', N'Psicología',          1, 'Activo',   NULL),
    (N'Psicología Social',                           N'Psicología',          1, 'Activo',   NULL),
    (N'Psicología Sanitaria',                        N'Psicología',          1, 'Activo',   NULL),
    (N'Psicopedagogía',                              N'Psicopedagogía',      0, 'Activo',   NULL),
    (N'Psicomotricidad',                             N'Psicomotricidad',     0, 'Activo',   NULL),
    (N'Trabajo Social',                              N'Trabajo social',      0, 'Activo',   N'Servicio Social'),
    (N'Terapia Ocupacional',                         N'Terapia ocupacional', 0, 'Activo',   NULL),
    (N'Enfermería',                                  N'Enfermería',          0, 'Activo',   NULL),
    (N'Psiquiatría',                                 N'Psiquiatría',         0, 'Inactivo', NULL),
    (N'Psiquiatría Infanto-Juvenil',                 N'Psiquiatría',         0, 'Inactivo', NULL);

-- Filas existentes: completar disciplina y si pide matrícula de especialista.
UPDATE e SET e.Disciplina = c.Disciplina, e.RequiereMatriculaEspecialista = c.ReqEsp
FROM Especialidad e
JOIN @Catalogo c ON e.Nombre COLLATE Latin1_General_CI_AI = c.Nombre COLLATE Latin1_General_CI_AI
                 OR (c.Alias IS NOT NULL AND e.Nombre COLLATE Latin1_General_CI_AI = c.Alias COLLATE Latin1_General_CI_AI)
WHERE e.Tipo = 'Especialidad' AND e.Disciplina IS NULL;

-- (La rama de origen también agregaba al catálogo las especialidades de psicología del Colegio de Córdoba y las demás disciplinas como filas nuevas.
-- Esta migración NO inserta especialidades en una base que ya existe: se agregan desde Configuración si hacen falta.)
GO

-- ══════════════════════════════════════════════════════════════
-- F) ORGANIZACIÓN Y SU CONFIGURACIÓN
-- ══════════════════════════════════════════════════════════════
-- Una fila: la organización dueña de esta base (una base por cliente, decisión del 15/10).
IF OBJECT_ID('dbo.Organizacion', 'U') IS NULL
CREATE TABLE Organizacion (
    IdOrganizacion INT           IDENTITY(1,1) PRIMARY KEY,
    Nombre         NVARCHAR(150) NOT NULL,
    Cuit           CHAR(11)      NULL CONSTRAINT CK_Organizacion_Cuit CHECK (Cuit IS NULL OR (LEN(Cuit) = 11 AND Cuit NOT LIKE '%[^0-9]%')),
    Modalidad      NVARCHAR(20)  NOT NULL CONSTRAINT CK_Organizacion_Modalidad CHECK (Modalidad IN ('Individual', 'Organizacion')),
    Estado         NVARCHAR(20)  NOT NULL CONSTRAINT DF_Organizacion_Estado DEFAULT 'Activo',
    FechaAlta      DATETIME2(0)  NOT NULL CONSTRAINT DF_Organizacion_Fecha DEFAULT SYSDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM Organizacion)
    INSERT INTO Organizacion (Nombre, Modalidad) VALUES (N'Organización sin configurar', 'Organizacion');
GO

-- Valores que la organización puede cambiar, con historial de quién y cuándo. Se van
-- pasando acá de a poco desde appsettings.json (etapa 4).
IF OBJECT_ID('dbo.ConfiguracionOrganizacion', 'U') IS NULL
CREATE TABLE ConfiguracionOrganizacion (
    Clave             NVARCHAR(100) NOT NULL PRIMARY KEY,
    Valor             NVARCHAR(MAX) NULL,
    FechaModificacion DATETIME2(0)  NOT NULL CONSTRAINT DF_ConfigOrg_Fecha DEFAULT SYSDATETIME(),
    IdUsuarioModifico INT           NULL REFERENCES Usuario(IdUsuario)
);
GO

-- ══════════════════════════════════════════════════════════════
-- H) VISTA PARA SOPORTE
-- ══════════════════════════════════════════════════════════════
-- Responde en una consulta "qué puede hacer este usuario y por qué no ve tal cosa":
--     SELECT * FROM vw_UsuarioAccesos WHERE Username = 'juan.garcia';
-- FOR XML PATH en lugar de STRING_AGG para que funcione en cualquier versión de SQL Server.
CREATE OR ALTER VIEW vw_UsuarioAccesos AS
SELECT
    u.IdUsuario,
    u.Username,
    u.Estado,
    CASE WHEN up_pac.IdUsuario IS NOT NULL THEN 'Paciente' ELSE 'Equipo' END AS TipoCuenta,
    LTRIM(RTRIM(COALESCE(u.Apellido + N', ', N'') + COALESCE(u.Nombre, N''))) AS NombreCompleto,
    u.Cuil,
    STUFF((SELECT N', ' + r.Nombre
           FROM UsuarioRol ur JOIN Rol r ON r.IdRol = ur.IdRol
           WHERE ur.IdUsuario = u.IdUsuario
           ORDER BY r.Nombre
           FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'') AS Roles,
    STUFF((SELECT N', ' + p.Nombre
           FROM UsuarioPermiso x JOIN Permiso p ON p.IdPermiso = x.IdPermiso
           WHERE x.IdUsuario = u.IdUsuario
           ORDER BY p.Nombre
           FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'') AS Permisos,
    STUFF((SELECT N', ' + p.Nombre + N': ' + pv.Nombre
           FROM UsuarioPrivilegio x
           JOIN Permiso p     ON p.IdPermiso = x.IdPermiso
           JOIN Privilegio pv ON pv.IdPrivilegio = x.IdPrivilegio
           WHERE x.IdUsuario = u.IdUsuario
           ORDER BY p.Nombre, pv.Nombre
           FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'') AS Privilegios,
    pr.Matricula,
    pr.JurisdiccionMatricula,
    e.Nombre AS Especialidad,
    pr.MatriculaEspecialista,
    CASE WHEN up_pac.IdUsuario IS NULL
              AND NOT EXISTS (SELECT 1 FROM UsuarioPermiso x WHERE x.IdUsuario = u.IdUsuario)
         THEN 1 ELSE 0 END AS SinPermisos
FROM Usuario u
LEFT JOIN UsuarioPaciente up_pac    ON up_pac.IdUsuario = u.IdUsuario
LEFT JOIN UsuarioProfesional up_pro ON up_pro.IdUsuario = u.IdUsuario
LEFT JOIN Profesional pr            ON pr.IdProfesional = up_pro.IdProfesional
LEFT JOIN Especialidad e            ON e.IdEspecialidad = pr.IdEspecialidad;
GO


-- ══════════════════════════════════════════════════════════════
-- RESULTADO (para leer en la salida de sqlcmd)
-- ══════════════════════════════════════════════════════════════
PRINT N'Migración 20 aplicada.';
SELECT (SELECT COUNT(*) FROM Rol WHERE Clave IS NOT NULL) AS roles_con_clave,
       (SELECT COUNT(*) FROM Permiso) AS permisos,
       (SELECT COUNT(*) FROM Privilegio) AS privilegios,
       (SELECT COUNT(*) FROM PermisoPrivilegio) AS permisos_con_privilegio;
GO

IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 20)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (20, N'add_permisos_roles_privilegios.sql', 'Script');
GO
