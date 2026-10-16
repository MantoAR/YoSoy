/* ============================================================================
   seed_catalogos.sql -- Datos de catalogo IMPRESCINDIBLES para una base nueva.

   Por que existe: docs/produccion/01-produccion.sql crea el esquema pero solo
   siembra TipoConsentimiento (su seccion 8). En una base armada con el, la
   tabla Especialidad queda vacia: el combo del registro de profesional (y el de
   la admision del paciente) no muestra nada, y sin estados de turno no se puede
   pedir ni confirmar ningun turno.

   Que carga (cada seccion se saltea sola lo que ya esta):
     1) Especialidad       15 filas. Se comparan por Nombre SIN distinguir
                           tildes ni mayusculas, asi que una base de desarrollo
                           que ya tiene "Psiquiatria" no recibe "Psiquiatria"
                           con tilde de nuevo.
     2) EstadoTurno        4 filas con ID FIJO: 1 Pendiente, 2 Confirmado,
                           3 Cancelado, 4 Realizado. El codigo usa esos numeros
                           (Program.cs: 3 = Cancelado, 2 = Confirmado; las
                           pantallas: 1..4), ademas de los nombres.
     3) TipoConsentimiento "Consentimiento Informado - Atencion por Telemedicina"
                           (con tilde). Lo busca el codigo por Descripcion exacta
                           (Consentimiento:TipoRequerido en appsettings.json);
                           sin esta fila nadie puede firmar y los turnos que
                           exigen consentimiento quedan bloqueados.

   Que NO carga, y por que:
     - Rol: el login no la usa (el rol sale de UsuarioPaciente/UsuarioProfesional,
       ver docs/CATALOGO_SCRIPTS_SQL.md, seccion 2).
     - ObraSocial: ninguna pantalla la consume (solo el ABM viejo mockup.html y
       Cobertura por API); cargarla seria inventar datos de negocio.
     - PreguntaFrecuente y RespuestaRapida: contenido de arranque opcional, ya
       esta como seccion 9 de 01-produccion.sql.
     - Grupo y ReglaDerivacion: configuracion que arma el Configurador desde su
       panel, sobre estas especialidades.
     - No existen tablas de tipos de documento, generos ni provincias: DNI, sexo
       y provincia son columnas de texto.

   Como correrlo, con un login que pueda escribir y usar IDENTITY_INSERT (el
   administrador de SQL Server, NO yosoy_app: ese solo lee y escribe datos):
     sqlcmd -S TU_SERVIDOR -d yosoy -U <admin> -C -I -i seed_catalogos.sql
   sqlcmd pide la contrasena. -I = QUOTED_IDENTIFIER ON, como el resto de los
   scripts. Este archivo es ASCII puro a proposito: las tildes se arman con
   NCHAR(), asi que el resultado no depende de -f 65001, del BOM ni de la
   pagina de codigos de la consola.

   Idempotente: se puede correr mas de una vez; solo agrega lo que falta y nunca
   modifica ni borra una fila existente. Si no estas parado en la base de YoSoy
   (es una base del sistema, o faltan las tablas) no hace nada y lo dice.

   Para sacar o agregar una especialidad antes de correrlo, editar la lista de
   la seccion 1. Despues de correrlo, el Configurador puede darlas de baja o
   sumar otras desde su panel (POST /especialidades).
   ============================================================================ */

SET NOCOUNT ON;
GO

-- 0) Guardia: tiene que ser la base de YoSoy. Si no, no se ejecuta nada. Las bases del sistema se
--    rechazan aunque tengan tablas con estos nombres: correr un script sin -d cae en master.
DECLARE @Base NVARCHAR(128) = DB_NAME();
IF @Base IN (N'master', N'tempdb', N'model', N'msdb')
BEGIN
    RAISERROR(N'"%s" es una base del sistema. Corre este script con -d yosoy (la base de YoSoy). No se hizo ningun cambio.', 16, 1, @Base);
    SET NOEXEC ON;
END
ELSE IF OBJECT_ID(N'dbo.Especialidad', N'U') IS NULL
     OR OBJECT_ID(N'dbo.EstadoTurno', N'U') IS NULL
     OR OBJECT_ID(N'dbo.TipoConsentimiento', N'U') IS NULL
BEGIN
    RAISERROR(N'No encuentro las tablas de catalogo en la base "%s". Corre este script con -d yosoy. No se hizo ningun cambio.', 16, 1, @Base);
    SET NOEXEC ON;
END
ELSE
    PRINT N'Base: ' + @Base + N'. Cargando los catalogos imprescindibles (solo agrega lo que falta)...';
GO

-- 1) Especialidad ----------------------------------------------------------------
-- Las 4 primeras, en este orden, son las de la base de desarrollo (IDs 1 a 4: Psicologia,
-- Psiquiatria, Trabajo Social, Terapia Ocupacional). Las siguientes salen del script
-- historico "Consultas armado de estadisticas.sql" (servicios de la institucion) y de lo
-- pedido para salud mental (Neuropsicologia, Terapia de Pareja y Familia, Psicopsiquiatria
-- de Adicciones). "Psicologia clinica" no esta a proposito: en el repo es un GRUPO (docs/
-- datos-prueba-grupos-fase2.sql), y la especialidad que lo alimenta es "Psicologia".
INSERT INTO dbo.Especialidad (Nombre, Estado)
SELECT e.Nombre, N'Activo'
FROM (VALUES
        ( 1, N'Psicolog' + NCHAR(237) + N'a'),
        ( 2, N'Psiquiatr' + NCHAR(237) + N'a'),
        ( 3, N'Trabajo Social'),
        ( 4, N'Terapia Ocupacional'),
        ( 5, N'Psicolog' + NCHAR(237) + N'a Infanto-Juvenil'),
        ( 6, N'Psiquiatr' + NCHAR(237) + N'a Infanto-Juvenil'),
        ( 7, N'Psicopedagog' + NCHAR(237) + N'a'),
        ( 8, N'Psicomotricidad'),
        ( 9, N'Neuropsicolog' + NCHAR(237) + N'a'),
        (10, N'Terapia de Pareja y Familia'),
        (11, N'Psicopsiquiatr' + NCHAR(237) + N'a de Adicciones'),
        (12, N'Admisi' + NCHAR(243) + N'n'),
        (13, N'Admisi' + NCHAR(243) + N'n Infanto-Juvenil'),
        (14, N'Atenci' + NCHAR(243) + N'n Domiciliaria'),
        (15, N'Centro de Rehabilitaci' + NCHAR(243) + N'n Sociolaboral')
     ) AS e (Orden, Nombre)
WHERE NOT EXISTS (SELECT 1
                  FROM dbo.Especialidad AS x
                  WHERE x.Nombre COLLATE Latin1_General_CI_AI = e.Nombre COLLATE Latin1_General_CI_AI)
ORDER BY e.Orden;   -- el ORDER BY fija el orden en que se asignan los IDs

DECLARE @EspAgregadas INT = @@ROWCOUNT;
DECLARE @EspTotal INT = (SELECT COUNT(*) FROM dbo.Especialidad);
PRINT N'Especialidad: ' + CAST(@EspAgregadas AS NVARCHAR(10)) + N' fila(s) agregada(s); '
    + CAST(@EspTotal AS NVARCHAR(10)) + N' en total.';
GO

-- 2) EstadoTurno -- IDs FIJOS ------------------------------------------------------
-- Se insertan con su ID explicito (IDENTITY_INSERT) para no depender de en que valor
-- quedo el contador de la tabla. Si un nombre o un ID ya existe, esa fila se saltea.
DECLARE @EstAgregados INT = 0;
BEGIN TRY
    SET IDENTITY_INSERT dbo.EstadoTurno ON;

    INSERT INTO dbo.EstadoTurno (IdEstadoTurno, Descripcion, Estado)
    SELECT s.Id, s.Descripcion, N'Activo'
    FROM (VALUES (1, N'Pendiente'), (2, N'Confirmado'), (3, N'Cancelado'), (4, N'Realizado')) AS s (Id, Descripcion)
    WHERE NOT EXISTS (SELECT 1
                      FROM dbo.EstadoTurno AS x
                      WHERE x.IdEstadoTurno = s.Id OR x.Descripcion = s.Descripcion)
    ORDER BY s.Id;

    SET @EstAgregados = @@ROWCOUNT;
    SET IDENTITY_INSERT dbo.EstadoTurno OFF;
END TRY
BEGIN CATCH
    BEGIN TRY
        SET IDENTITY_INSERT dbo.EstadoTurno OFF;
    END TRY
    BEGIN CATCH
        SET @EstAgregados = 0;   -- ya se esta propagando el error original: esto solo evita un bloque vacio
    END CATCH;
    THROW;
END CATCH;

PRINT N'EstadoTurno: ' + CAST(@EstAgregados AS NVARCHAR(10)) + N' fila(s) agregada(s).';

-- Verificacion: cada ID tiene que tener SU nombre. Si no, el codigo (que usa los numeros)
-- se equivocaria de estado: se avisa fuerte y no se toca nada.
DECLARE @Mal NVARCHAR(1000) = N'';
SELECT @Mal = @Mal + N' ' + CAST(s.Id AS NVARCHAR(10)) + N'=' + s.Descripcion + N' (en la tabla: '
            + ISNULL((SELECT TOP (1) x.Descripcion FROM dbo.EstadoTurno AS x WHERE x.IdEstadoTurno = s.Id), N'nada') + N');'
FROM (VALUES (1, N'Pendiente'), (2, N'Confirmado'), (3, N'Cancelado'), (4, N'Realizado')) AS s (Id, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM dbo.EstadoTurno AS x WHERE x.IdEstadoTurno = s.Id AND x.Descripcion = s.Descripcion);

IF @Mal <> N''
    RAISERROR(N'ALERTA EstadoTurno: los IDs no coinciden con lo que espera el codigo:%s Corregirlo a mano antes de usar la app.', 16, 1, @Mal);
ELSE
    PRINT N'EstadoTurno: OK (1 Pendiente, 2 Confirmado, 3 Cancelado, 4 Realizado).';
GO

-- 3) TipoConsentimiento -- el de telemedicina (RN-02) ------------------------------
DECLARE @Tipo NVARCHAR(100) = N'Consentimiento Informado - Atenci' + NCHAR(243) + N'n por Telemedicina';

IF NOT EXISTS (SELECT 1 FROM dbo.TipoConsentimiento WHERE Descripcion = @Tipo)
BEGIN
    INSERT INTO dbo.TipoConsentimiento (Descripcion, Estado) VALUES (@Tipo, N'Activo');
    PRINT N'TipoConsentimiento: agregado el de telemedicina.';
END
ELSE
    PRINT N'TipoConsentimiento: el de telemedicina ya estaba.';

IF EXISTS (SELECT 1
           FROM dbo.TipoConsentimiento
           WHERE Descripcion COLLATE Latin1_General_CI_AI = @Tipo COLLATE Latin1_General_CI_AI
             AND Descripcion <> @Tipo)
    PRINT N'AVISO TipoConsentimiento: hay otra fila casi igual (distinta tilde). El codigo usa solo la exacta; revisar si la otra sobra.';
GO

-- Resumen de lo que quedo en las tres tablas (no se ejecuta si la guardia apago la ejecucion).
SELECT IdEspecialidad, Nombre, Estado FROM dbo.Especialidad ORDER BY IdEspecialidad;
SELECT IdEstadoTurno, Descripcion, Estado FROM dbo.EstadoTurno ORDER BY IdEstadoTurno;
SELECT IdTipoConsentimiento, Descripcion, Estado FROM dbo.TipoConsentimiento ORDER BY IdTipoConsentimiento;
GO

-- Fin: si la guardia habia apagado la ejecucion, se vuelve a prender.
SET NOEXEC OFF;
GO
