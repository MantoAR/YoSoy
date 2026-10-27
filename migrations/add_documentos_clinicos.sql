-- Documentos clínicos a partir de plantillas (octubre 2026, rama AAA1). Es la migración 35.
-- Aditiva e idempotente: si se corta a mitad, se vuelve a correr entera. Requiere la 19 (auditoría), la 20 y la 33 (matrículas).
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_documentos_clinicos.sql
--
-- Qué hace:
--  1. PlantillaDocumento: las plantillas (consentimientos, constancias, informes, plan de seguridad). Las administra el
--     Configurador. RequiereAceptacionPaciente queda en 0: la aceptación desde el panel del paciente es la fase 2.
--  2. PlantillaDocumentoVersion: el texto de cada plantilla, versionado (RN-D3). Una sola versión Publicada por plantilla
--     y un solo Borrador. Una versión publicada no se edita: solo puede pasar de Publicada a Retirada (trigger, error 50027).
--     HashSha256 = SHA-256 de CamposJson + salto de línea + CuerpoJson, en UTF-8 (lo mismo calcula RenderizadorDocumento).
--  3. DocumentoClinico: cada documento emitido, con la "foto" de los datos, el contenido renderizado y el PDF (RN-D2, RN-D6).
--     HashSha256 = SHA-256 del contenido (ver DocumentosService.HashContenido); sus primeros 12 caracteres son el código de
--     verificación impreso en el PDF. HashPdfSha256 = SHA-256 de los bytes del PDF guardado. De solo agregar: UPDATE y DELETE fallan (trigger, error 50028), como la auditoría (RN-18).
--  4. DocumentoClinicoAddendum: las correcciones de un documento emitido. De solo agregar (error 50029).
--  5. Profesional.FirmaImagen: la firma escaneada que el profesional sube en Mi perfil (opcional), para el PDF.
--  6. ConfiguracionOrganizacion 'Organizacion.Localidad' = 'Córdoba' si no existe (membrete y "En ..., a los ...").
--  7. Las 10 plantillas con su versión 1 Publicada y Provisoria = 1 (textos propios de YoSoy, sin validación legal).
--  8. Registra la versión 27 en VersionEsquema.
--
-- Efectos: solo agrega. No toca historias clínicas, evoluciones ni turnos.
--
-- Verificación:
--   SELECT p.Clave, v.Numero, v.Estado, v.Provisoria FROM PlantillaDocumento p JOIN PlantillaDocumentoVersion v ON v.IdPlantilla = p.IdPlantilla ORDER BY p.Orden;
--   -- 10 filas, todas Publicada, versión 1, Provisoria 1.
--   SELECT name FROM sys.triggers WHERE name LIKE 'TR_%Documento%';   -- 3 triggers
--
-- Deshacer: docs/produccion/deshacer-migracion-35.sql (borra los documentos emitidos: solo antes de usarlo en serio).

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR COL_LENGTH('dbo.LogAuditoria', 'Categoria') IS NULL OR OBJECT_ID('dbo.ProfesionalMatricula', 'U') IS NULL
    RAISERROR(N'Faltan migraciones anteriores (19, 20 o 33): aplicalas antes (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

-- ── 1) PlantillaDocumento ─────────────────────────────────
IF OBJECT_ID('dbo.PlantillaDocumento', 'U') IS NULL
CREATE TABLE dbo.PlantillaDocumento (
    IdPlantilla                 INT            IDENTITY(1,1) CONSTRAINT PK_PlantillaDocumento PRIMARY KEY,
    Clave                       VARCHAR(40)    NOT NULL CONSTRAINT UQ_PlantillaDocumento_Clave UNIQUE,
    Grupo                       NVARCHAR(30)   NOT NULL CONSTRAINT CK_PlantillaDocumento_Grupo CHECK (Grupo IN (N'Consentimientos', N'Constancias', N'Informes', N'Clínica')),
    Titulo                      NVARCHAR(150)  NOT NULL,
    Descripcion                 NVARCHAR(400)  NULL,
    Activa                      BIT            NOT NULL CONSTRAINT DF_PlantillaDocumento_Activa DEFAULT (1),
    Orden                       INT            NOT NULL CONSTRAINT DF_PlantillaDocumento_Orden DEFAULT (0),
    RequiereAceptacionPaciente  BIT            NOT NULL CONSTRAINT DF_PlantillaDocumento_Aceptacion DEFAULT (0)
);
GO

-- ── 2) PlantillaDocumentoVersion ──────────────────────────
IF OBJECT_ID('dbo.PlantillaDocumentoVersion', 'U') IS NULL
CREATE TABLE dbo.PlantillaDocumentoVersion (
    IdVersion         INT             IDENTITY(1,1) CONSTRAINT PK_PlantillaDocumentoVersion PRIMARY KEY,
    IdPlantilla       INT             NOT NULL CONSTRAINT FK_PlantillaDocumentoVersion_Plantilla REFERENCES dbo.PlantillaDocumento(IdPlantilla),
    Numero            INT             NOT NULL,
    CamposJson        NVARCHAR(MAX)   NOT NULL,
    CuerpoJson        NVARCHAR(MAX)   NOT NULL,
    Estado            NVARCHAR(10)    NOT NULL CONSTRAINT CK_PlantillaDocumentoVersion_Estado CHECK (Estado IN (N'Borrador', N'Publicada', N'Retirada')),
    Provisoria        BIT             NOT NULL CONSTRAINT DF_PlantillaDocumentoVersion_Provisoria DEFAULT (1),
    MotivoCambio      NVARCHAR(300)   NULL,
    IdUsuarioAutor    INT             NULL CONSTRAINT FK_PlantillaDocumentoVersion_Usuario REFERENCES dbo.Usuario(IdUsuario),
    FechaCreacion     DATETIME2(0)    NOT NULL CONSTRAINT DF_PlantillaDocumentoVersion_Creacion DEFAULT SYSDATETIME(),
    FechaPublicacion  DATETIME2(0)    NULL,
    HashSha256        CHAR(64)        NULL,
    CONSTRAINT UQ_PlantillaDocumentoVersion_Numero UNIQUE (IdPlantilla, Numero)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PlantillaDocumentoVersion') AND name = 'UX_PlantillaDocumentoVersion_Publicada')
    EXEC(N'CREATE UNIQUE INDEX UX_PlantillaDocumentoVersion_Publicada ON dbo.PlantillaDocumentoVersion(IdPlantilla) WHERE Estado = N''Publicada'';');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PlantillaDocumentoVersion') AND name = 'UX_PlantillaDocumentoVersion_Borrador')
    EXEC(N'CREATE UNIQUE INDEX UX_PlantillaDocumentoVersion_Borrador ON dbo.PlantillaDocumentoVersion(IdPlantilla) WHERE Estado = N''Borrador'';');
GO

-- RN-D3: un borrador se edita y se borra libremente; una versión publicada o retirada no se toca. Lo único permitido es
-- pasar de Publicada a Retirada (al publicar otra), sin cambiar nada más.
CREATE OR ALTER TRIGGER TR_PlantillaDocumentoVersion_Proteger ON dbo.PlantillaDocumentoVersion AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted WHERE Estado <> N'Borrador')
        THROW 50027, N'RN-D3: una versión publicada o retirada de una plantilla no se puede borrar.', 1;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.IdVersion = d.IdVersion
        WHERE d.Estado <> N'Borrador'
          AND NOT (d.Estado = N'Publicada' AND i.Estado = N'Retirada'
                   AND i.IdPlantilla = d.IdPlantilla AND i.Numero = d.Numero
                   AND CAST(i.CamposJson AS VARBINARY(MAX)) = CAST(d.CamposJson AS VARBINARY(MAX))   -- binario: la intercalación
                   AND CAST(i.CuerpoJson AS VARBINARY(MAX)) = CAST(d.CuerpoJson AS VARBINARY(MAX))   -- no distingue mayúsculas
                   AND i.Provisoria = d.Provisoria
                   AND ISNULL(i.MotivoCambio, N'') = ISNULL(d.MotivoCambio, N'')
                   AND ISNULL(i.IdUsuarioAutor, -1) = ISNULL(d.IdUsuarioAutor, -1)
                   AND i.FechaCreacion = d.FechaCreacion
                   AND ISNULL(i.FechaPublicacion, '19000101') = ISNULL(d.FechaPublicacion, '19000101')
                   AND ISNULL(i.HashSha256, '') = ISNULL(d.HashSha256, '')))
        THROW 50027, N'RN-D3: una versión publicada de una plantilla no se modifica; solo puede retirarse.', 1;
END;
GO

-- ── 3) DocumentoClinico ───────────────────────────────────
IF OBJECT_ID('dbo.DocumentoClinico', 'U') IS NULL
CREATE TABLE dbo.DocumentoClinico (
    IdDocumento      INT              IDENTITY(1,1) CONSTRAINT PK_DocumentoClinico PRIMARY KEY,
    IdPaciente       INT              NOT NULL CONSTRAINT FK_DocumentoClinico_Paciente REFERENCES dbo.Paciente(IdPaciente),
    IdHistoria       INT              NOT NULL CONSTRAINT FK_DocumentoClinico_Historia REFERENCES dbo.HistoriaClinica(IdHistoria),
    IdProfesional    INT              NOT NULL CONSTRAINT FK_DocumentoClinico_Profesional REFERENCES dbo.Profesional(IdProfesional),
    IdVersion        INT              NOT NULL CONSTRAINT FK_DocumentoClinico_Version REFERENCES dbo.PlantillaDocumentoVersion(IdVersion),
    IdTurno          INT              NULL CONSTRAINT FK_DocumentoClinico_Turno REFERENCES dbo.Turno(IdTurno),
    IdEventoCrisis   INT              NULL CONSTRAINT FK_DocumentoClinico_EventoCrisis REFERENCES dbo.EventoCrisis(IdEvento),
    DatosJson        NVARCHAR(MAX)    NOT NULL,
    ContenidoJson    NVARCHAR(MAX)    NOT NULL,
    Pdf              VARBINARY(MAX)   NOT NULL,
    HashSha256       CHAR(64)         NOT NULL,
    HashPdfSha256    CHAR(64)         NULL,
    FechaEmision     DATETIME2(0)     NOT NULL CONSTRAINT DF_DocumentoClinico_Fecha DEFAULT SYSDATETIME(),
    IdUsuarioEmisor  INT              NOT NULL CONSTRAINT FK_DocumentoClinico_Usuario REFERENCES dbo.Usuario(IdUsuario)
);
GO

-- Para bases donde ya se había corrido una primera versión de esta migración (sin la columna).
IF COL_LENGTH('dbo.DocumentoClinico', 'HashPdfSha256') IS NULL
    ALTER TABLE dbo.DocumentoClinico ADD HashPdfSha256 CHAR(64) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.DocumentoClinico') AND name = 'IX_DocumentoClinico_Paciente')
    EXEC(N'CREATE INDEX IX_DocumentoClinico_Paciente ON dbo.DocumentoClinico(IdPaciente, FechaEmision);');
GO

-- RN-D2: un documento emitido no se modifica ni se borra. Las correcciones van como addendum.
CREATE OR ALTER TRIGGER TR_DocumentoClinico_SoloAgregar ON dbo.DocumentoClinico INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 50028, N'RN-D2: un documento clínico emitido es de solo lectura. No se puede modificar ni borrar: agregá un addendum.', 1;
END;
GO

-- ── 4) DocumentoClinicoAddendum ───────────────────────────
IF OBJECT_ID('dbo.DocumentoClinicoAddendum', 'U') IS NULL
CREATE TABLE dbo.DocumentoClinicoAddendum (
    IdAddendum     INT             IDENTITY(1,1) CONSTRAINT PK_DocumentoClinicoAddendum PRIMARY KEY,
    IdDocumento    INT             NOT NULL CONSTRAINT FK_DocumentoClinicoAddendum_Documento REFERENCES dbo.DocumentoClinico(IdDocumento),
    Texto          NVARCHAR(4000)  NOT NULL,
    IdProfesional  INT             NOT NULL CONSTRAINT FK_DocumentoClinicoAddendum_Profesional REFERENCES dbo.Profesional(IdProfesional),
    Fecha          DATETIME2(0)    NOT NULL CONSTRAINT DF_DocumentoClinicoAddendum_Fecha DEFAULT SYSDATETIME(),
    HashSha256     CHAR(64)        NOT NULL
);
GO

CREATE OR ALTER TRIGGER TR_DocumentoClinicoAddendum_SoloAgregar ON dbo.DocumentoClinicoAddendum INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 50029, N'RN-D2: un addendum es de solo lectura. No se puede modificar ni borrar.', 1;
END;
GO

-- ── 5) Firma del profesional ──────────────────────────────
IF COL_LENGTH('dbo.Profesional', 'FirmaImagen') IS NULL
    ALTER TABLE dbo.Profesional ADD FirmaImagen VARBINARY(MAX) NULL;
GO

-- ── 6) Localidad de la organización ───────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionOrganizacion WHERE Clave = N'Organizacion.Localidad')
    INSERT INTO dbo.ConfiguracionOrganizacion (Clave, Valor) VALUES (N'Organizacion.Localidad', N'Córdoba');
GO

-- ── 7) Las 10 plantillas (versión 1, publicada y provisoria) ─
IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'consent_adulto')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('consent_adulto', N'Consentimientos', N'Consentimiento informado para tratamiento psicológico (adultos)', N'Consentimiento para iniciar un tratamiento psicológico. Complementa el consentimiento de telemedicina, no lo reemplaza.', 1);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"paciente","etiqueta":"Paciente (nombre y apellido)","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dni","etiqueta":"DNI","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"modalidad","etiqueta":"Modalidad","tipo":"texto","requerido":true,"valorInicial":"online, por la sala de video de YoSoy"},{"clave":"frecuencia","etiqueta":"Frecuencia","tipo":"seleccion","requerido":true,"opciones":["semanal","quincenal","mensual","a convenir según la evolución"]},{"clave":"duracion","etiqueta":"Duración de cada sesión","tipo":"seleccion","requerido":true,"opciones":["45 minutos","50 minutos","60 minutos"]},{"clave":"honorarios","etiqueta":"Honorarios por sesión (opcional)","tipo":"texto","requerido":false,"ayuda":"Si se completa, se agrega la cláusula de honorarios."},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Consentimiento informado para tratamiento psicológico"},{"tipo":"parrafo","texto":"Yo, **{{paciente}}**, DNI {{dni}}, declaro que el/la Lic. {{p.profesional.nombre}} (Mat. {{p.profesional.matricula}}) me explicó en forma clara las características del tratamiento que me propone, y que pude hacer todas las preguntas que consideré necesarias."},{"tipo":"subtitulo","texto":"El tratamiento"},{"tipo":"parrafo","texto":"El tratamiento es psicológico. Se realiza en modalidad {{modalidad}}, con frecuencia {{frecuencia}} y sesiones de {{duracion}}. La frecuencia y la duración pueden revisarse de común acuerdo según la evolución."},{"tipo":"subtitulo","texto":"Alcances"},{"tipo":"lista","texto":"El tratamiento no garantiza un resultado determinado: depende de múltiples factores, entre ellos mi participación.\nPuedo plantear en cualquier momento mis dudas sobre el tratamiento, sus objetivos y su duración.\nSi el/la profesional considera necesaria una interconsulta o una derivación, me lo informará y lo decidiremos juntos.\nEste servicio no es de urgencias: ante una situación de riesgo debo recurrir a la guardia o a las líneas de emergencia."},{"tipo":"subtitulo","texto":"Confidencialidad y secreto profesional"},{"tipo":"parrafo","texto":"Lo que converse en las sesiones está protegido por el secreto profesional. El/la profesional solo puede relevarlo en los casos que prevé la ley:"},{"tipo":"lista","texto":"Riesgo cierto e inminente para mi vida o la de terceros.\nOrden judicial.\nMi autorización expresa y por escrito, en la medida que yo indique."},{"tipo":"subtitulo","texto":"Historia clínica y datos personales"},{"tipo":"parrafo","texto":"Lo que surja de la atención se registra en una historia clínica digital, que es de mi titularidad y que custodia la organización con medidas de seguridad, conforme a la Ley 26.529 de Derechos del Paciente, la Ley 26.657 de Salud Mental y la Ley 25.326 de Protección de Datos Personales. Puedo pedir acceso, copia, rectificación o actualización de mis datos. Mis datos de salud no se usan para fines distintos de mi atención sin mi consentimiento expreso."},{"tipo":"subtitulo","texto":"Honorarios","condicion":{"campo":"honorarios","op":"tieneValor"}},{"tipo":"parrafo","texto":"El honorario por sesión es de {{honorarios}}. Las sesiones no canceladas con la anticipación acordada pueden cobrarse.","condicion":{"campo":"honorarios","op":"tieneValor"}},{"tipo":"subtitulo","texto":"Derechos y revocación"},{"tipo":"parrafo","texto":"Conozco mi derecho a aceptar o rechazar el tratamiento y a revocar este consentimiento en cualquier momento, sin expresión de causa (art. 10 de la Ley 26.529). La revocación no tiene efectos sobre lo ya realizado."},{"tipo":"parrafo","texto":"Este consentimiento complementa el consentimiento informado para la atención por telemedicina que firmé en YoSoy, y no lo reemplaza."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Paciente: {{paciente}} · DNI {{dni}}"},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), '82f06f128c42630b113ba070b9fd176f2e8e05ca7a8aa624911d6ffe4e8e5b71');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'consent_menor')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('consent_menor', N'Consentimientos', N'Consentimiento para niños, niñas y adolescentes', N'Consentimiento de los responsables, con asentimiento del adolescente desde los 13 años.', 2);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"menor","etiqueta":"Nombre y apellido del niño, niña o adolescente","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dniMenor","etiqueta":"DNI","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"edad","etiqueta":"Edad","tipo":"numero","requerido":true,"ayuda":"Se calcula desde la fecha de nacimiento.","precarga":"paciente.edad"},{"clave":"responsable1","etiqueta":"Responsable 1","tipo":"texto","requerido":true,"precarga":"tutor1.nombre"},{"clave":"dniResponsable1","etiqueta":"DNI del responsable 1","tipo":"texto","requerido":true,"precarga":"tutor1.dni"},{"clave":"vinculo1","etiqueta":"Vínculo del responsable 1","tipo":"texto","requerido":true,"precarga":"tutor1.vinculo"},{"clave":"unSoloResponsable","etiqueta":"Firma un solo responsable","tipo":"check","requerido":false},{"clave":"responsable2","etiqueta":"Responsable 2","tipo":"texto","requerido":false,"precarga":"tutor2.nombre"},{"clave":"dniResponsable2","etiqueta":"DNI del responsable 2","tipo":"texto","requerido":false,"precarga":"tutor2.dni"},{"clave":"vinculo2","etiqueta":"Vínculo del responsable 2","tipo":"texto","requerido":false,"precarga":"tutor2.vinculo"},{"clave":"modalidad","etiqueta":"Modalidad","tipo":"texto","requerido":true,"valorInicial":"online, por la sala de video de YoSoy"},{"clave":"frecuencia","etiqueta":"Frecuencia","tipo":"seleccion","requerido":true,"opciones":["semanal","quincenal","mensual","a convenir según la evolución"]},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Consentimiento para la atención psicológica de niños, niñas y adolescentes"},{"tipo":"parrafo","texto":"**{{responsable1}}**, DNI {{dniResponsable1}} ({{vinculo1}}), en su carácter de responsable de **{{menor}}**, DNI {{dniMenor}}, de {{edad}} años, presta su consentimiento para que reciba atención psicológica con el/la Lic. {{p.profesional.nombre}} (Mat. {{p.profesional.matricula}})."},{"tipo":"parrafo","texto":"También presta su consentimiento **{{responsable2}}**, DNI {{dniResponsable2}} ({{vinculo2}}).","condicion":{"campo":"responsable2","op":"tieneValor"}},{"tipo":"parrafo","texto":"**Declaración de responsable único.** Quien firma declara bajo su responsabilidad que actúa con conocimiento del otro responsable o que está legalmente habilitado/a para decidir solo/a, y se compromete a informarle del tratamiento cuando corresponda.","condicion":{"campo":"unSoloResponsable","op":"igual","valor":"true"}},{"tipo":"subtitulo","texto":"El tratamiento"},{"tipo":"parrafo","texto":"La atención se realiza en modalidad {{modalidad}}, con frecuencia {{frecuencia}}. Puede incluir entrevistas con los responsables."},{"tipo":"subtitulo","texto":"Derecho a ser oído y confidencialidad"},{"tipo":"parrafo","texto":"El niño, niña o adolescente tiene derecho a ser oído y a que su opinión sea tenida en cuenta según su edad y grado de madurez (Ley 26.061). Para que el espacio terapéutico funcione, lo que diga en sesión es confidencial también frente a sus responsables. El/la profesional compartirá con ellos lo necesario para su cuidado y les informará siempre ante una situación de riesgo."},{"tipo":"subtitulo","texto":"Historia clínica y datos personales"},{"tipo":"parrafo","texto":"Lo que surja de la atención se registra en una historia clínica digital, que es de mi titularidad y que custodia la organización con medidas de seguridad, conforme a la Ley 26.529 de Derechos del Paciente, la Ley 26.657 de Salud Mental y la Ley 25.326 de Protección de Datos Personales. Puedo pedir acceso, copia, rectificación o actualización de mis datos. Mis datos de salud no se usan para fines distintos de mi atención sin mi consentimiento expreso."},{"tipo":"subtitulo","texto":"Asentimiento del adolescente","condicion":{"campo":"edad","op":"mayorIgual","valor":"13"}},{"tipo":"parrafo","texto":"Yo, **{{menor}}**, fui informado/a sobre la atención psicológica de forma que pude entenderla, y estoy de acuerdo con comenzarla (art. 26 del Código Civil y Comercial).","condicion":{"campo":"edad","op":"mayorIgual","valor":"13"}},{"tipo":"subtitulo","texto":"Revocación"},{"tipo":"parrafo","texto":"Este consentimiento puede revocarse en cualquier momento (art. 10 de la Ley 26.529)."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Responsable 1: {{responsable1}} · DNI {{dniResponsable1}}"},{"tipo":"firma","texto":"Responsable 2: {{responsable2}} · DNI {{dniResponsable2}}","condicion":{"campo":"responsable2","op":"tieneValor"}},{"tipo":"firma","texto":"Adolescente: {{menor}}","condicion":{"campo":"edad","op":"mayorIgual","valor":"13"}},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), 'ee95c6b9d56f3d021b8808b997a85f2d0b9e72b71b3d6a533abeeeaad9beb106');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'consent_vincular')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('consent_vincular', N'Consentimientos', N'Consentimiento para terapia de pareja, familiar o vincular', N'De 2 a 4 participantes. Incluye la cláusula sobre la información de las entrevistas individuales.', 3);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"tipo","etiqueta":"Tipo de terapia","tipo":"seleccion","requerido":true,"opciones":["de pareja","familiar","vincular"]},{"clave":"participante1","etiqueta":"Participante 1","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dni1","etiqueta":"DNI participante 1","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"participante2","etiqueta":"Participante 2","tipo":"texto","requerido":true},{"clave":"dni2","etiqueta":"DNI participante 2","tipo":"texto","requerido":true},{"clave":"participante3","etiqueta":"Participante 3","tipo":"texto","requerido":false},{"clave":"dni3","etiqueta":"DNI participante 3","tipo":"texto","requerido":false},{"clave":"participante4","etiqueta":"Participante 4","tipo":"texto","requerido":false},{"clave":"dni4","etiqueta":"DNI participante 4","tipo":"texto","requerido":false},{"clave":"modalidad","etiqueta":"Modalidad","tipo":"texto","requerido":true,"valorInicial":"online, por la sala de video de YoSoy"},{"clave":"frecuencia","etiqueta":"Frecuencia","tipo":"seleccion","requerido":true,"opciones":["semanal","quincenal","mensual","a convenir según la evolución"]},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Consentimiento para terapia {{tipo}}"},{"tipo":"parrafo","texto":"Quienes firmamos aceptamos iniciar una terapia {{tipo}} con el/la Lic. {{p.profesional.nombre}} (Mat. {{p.profesional.matricula}}), en modalidad {{modalidad}} y con frecuencia {{frecuencia}}:"},{"tipo":"lista","texto":"{{participante1}}, DNI {{dni1}}\n{{participante2}}, DNI {{dni2}}"},{"tipo":"lista","texto":"{{participante3}}, DNI {{dni3}}","condicion":{"campo":"participante3","op":"tieneValor"}},{"tipo":"lista","texto":"{{participante4}}, DNI {{dni4}}","condicion":{"campo":"participante4","op":"tieneValor"}},{"tipo":"subtitulo","texto":"El encuadre"},{"tipo":"parrafo","texto":"El objeto del tratamiento es el vínculo. El/la profesional no toma partido por ninguno de los participantes y puede proponer entrevistas individuales cuando lo considere útil."},{"tipo":"subtitulo","texto":"Información de las entrevistas individuales"},{"tipo":"parrafo","texto":"Lo conversado en una entrevista individual puede ser retomado en el espacio conjunto cuando sea relevante para el tratamiento. El/la profesional lo hará con cuidado y, salvo riesgo, lo conversará antes con quien lo dijo. Quien no quiera que algo se comparta debe decirlo expresamente."},{"tipo":"subtitulo","texto":"Confidencialidad"},{"tipo":"parrafo","texto":"Lo tratado está protegido por el secreto profesional frente a terceros, con las excepciones legales (riesgo cierto e inminente, orden judicial o autorización expresa)."},{"tipo":"subtitulo","texto":"Historia clínica y datos personales"},{"tipo":"parrafo","texto":"Lo que surja de la atención se registra en una historia clínica digital, que es de mi titularidad y que custodia la organización con medidas de seguridad, conforme a la Ley 26.529 de Derechos del Paciente, la Ley 26.657 de Salud Mental y la Ley 25.326 de Protección de Datos Personales. Puedo pedir acceso, copia, rectificación o actualización de mis datos. Mis datos de salud no se usan para fines distintos de mi atención sin mi consentimiento expreso."},{"tipo":"subtitulo","texto":"Revocación"},{"tipo":"parrafo","texto":"Cada participante puede revocar su consentimiento y dejar de participar en cualquier momento (art. 10 de la Ley 26.529)."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"{{participante1}} · DNI {{dni1}}"},{"tipo":"firma","texto":"{{participante2}} · DNI {{dni2}}"},{"tipo":"firma","texto":"{{participante3}} · DNI {{dni3}}","condicion":{"campo":"participante3","op":"tieneValor"}},{"tipo":"firma","texto":"{{participante4}} · DNI {{dni4}}","condicion":{"campo":"participante4","op":"tieneValor"}},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), '6ec73c405f937b41d1839056f09a6630295cd48a6458ca5f7c8f64d95db2e3cf');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'autorizacion_info')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('autorizacion_info', N'Consentimientos', N'Autorización para compartir información (relevo parcial del secreto)', N'Autoriza al profesional a compartir información puntual con una persona o institución.', 4);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"actuaComo","etiqueta":"Quién autoriza","tipo":"seleccion","requerido":true,"opciones":["el/la paciente","un responsable legal"]},{"clave":"autoriza","etiqueta":"Nombre y apellido de quien autoriza","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dniAutoriza","etiqueta":"DNI de quien autoriza","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"enRepresentacionDe","etiqueta":"En representación de (si es responsable)","tipo":"texto","requerido":false,"precarga":"paciente.nombre"},{"clave":"destinatario","etiqueta":"Con quién se comparte (nombre y/o institución)","tipo":"texto","requerido":true},{"clave":"finalidad","etiqueta":"Finalidad","tipo":"textoLargo","requerido":true},{"clave":"alcance","etiqueta":"Alcance de la información","tipo":"seleccion","requerido":true,"opciones":["solo asistencia y frecuencia de las sesiones","evolución general, sin detalles clínicos","sugerencias de abordaje","la información clínica necesaria para la finalidad indicada"]},{"clave":"vigencia","etiqueta":"Vigencia","tipo":"seleccion","requerido":true,"opciones":["un único informe","6 meses","1 año","hasta el fin del tratamiento"]},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Autorización para compartir información"},{"tipo":"parrafo","texto":"Yo, **{{autoriza}}**, DNI {{dniAutoriza}}, en mi carácter de {{actuaComo}}, autorizo al/a la Lic. {{p.profesional.nombre}} (Mat. {{p.profesional.matricula}}) a compartir información con **{{destinatario}}**."},{"tipo":"parrafo","texto":"Lo hago en representación de **{{enRepresentacionDe}}**.","condicion":{"campo":"actuaComo","op":"igual","valor":"un responsable legal"}},{"tipo":"subtitulo","texto":"Finalidad"},{"tipo":"parrafo","texto":"{{finalidad}}"},{"tipo":"subtitulo","texto":"Alcance y vigencia"},{"tipo":"parrafo","texto":"Se puede compartir: {{alcance}}. La autorización vale por: {{vigencia}}."},{"tipo":"parrafo","texto":"Solo se compartirá lo necesario para la finalidad indicada. Puedo revocar esta autorización en cualquier momento, por escrito; la revocación no afecta lo ya compartido."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Quien autoriza: {{autoriza}} · DNI {{dniAutoriza}}"},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), '5df1b459124a6f9927b4a032e274ee2b3e99ef42fcb24f57885e04a34c8059df');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'const_asistencia')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('const_asistencia', N'Constancias', N'Constancia de asistencia', N'Asistencia a un turno. Sin información clínica.', 5);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"paciente","etiqueta":"Paciente (nombre y apellido)","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dni","etiqueta":"DNI","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"fechaTurno","etiqueta":"Fecha del turno","tipo":"fecha","requerido":true,"precarga":"turno.fecha"},{"clave":"horaInicio","etiqueta":"Hora de inicio","tipo":"hora","requerido":true,"precarga":"turno.horaInicio"},{"clave":"horaFin","etiqueta":"Hora de fin","tipo":"hora","requerido":true,"precarga":"turno.horaFin"},{"clave":"presentaAnte","etiqueta":"Se presenta ante","tipo":"texto","requerido":true,"valorInicial":"quien corresponda"},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Constancia de asistencia"},{"tipo":"parrafo","texto":"Dejo constancia de que **{{paciente}}**, DNI {{dni}}, asistió a una consulta psicológica el día {{fechaTurno}}, de {{horaInicio}} a {{horaFin}} hs."},{"tipo":"parrafo","texto":"Se extiende a pedido del/de la interesado/a, para ser presentada ante {{presentaAnte}}."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), 'f10b18ed7423d2ec25f082c9ad906aab1762101abeab34fe366754101c5d1dcb');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'const_tratamiento')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('const_tratamiento', N'Constancias', N'Constancia de tratamiento en curso', N'Tratamiento en curso desde una fecha. Sin información clínica.', 6);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"paciente","etiqueta":"Paciente (nombre y apellido)","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dni","etiqueta":"DNI","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"fechaInicio","etiqueta":"Fecha de inicio del tratamiento","tipo":"fecha","requerido":true,"precarga":"tratamiento.fechaInicio"},{"clave":"mostrarFrecuencia","etiqueta":"Mostrar la frecuencia","tipo":"check","requerido":false},{"clave":"frecuencia","etiqueta":"Frecuencia","tipo":"seleccion","requerido":false,"opciones":["semanal","quincenal","mensual"]},{"clave":"destinatario","etiqueta":"Se presenta ante","tipo":"texto","requerido":true,"valorInicial":"quien corresponda"},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Constancia de tratamiento"},{"tipo":"parrafo","texto":"Dejo constancia de que **{{paciente}}**, DNI {{dni}}, se encuentra en tratamiento psicológico conmigo desde el {{fechaInicio}}."},{"tipo":"parrafo","texto":"Asiste con frecuencia {{frecuencia}}.","condicion":{"campo":"mostrarFrecuencia","op":"igual","valor":"true"}},{"tipo":"parrafo","texto":"Se extiende a pedido del/de la interesado/a, para ser presentada ante {{destinatario}}."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), 'f86424e86d082b024c652dfb12754234ac2bdc57baa65970b2443088618fec61');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'const_alta')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('const_alta', N'Constancias', N'Constancia de alta o finalización', N'Fin del tratamiento con su motivo. Se sugiere al cerrar una sesión con tipo de cierre Alta.', 7);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"paciente","etiqueta":"Paciente (nombre y apellido)","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dni","etiqueta":"DNI","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"fechaInicio","etiqueta":"Inicio del tratamiento","tipo":"fecha","requerido":true,"precarga":"tratamiento.fechaInicio"},{"clave":"fechaFin","etiqueta":"Fin del tratamiento","tipo":"fecha","requerido":true,"precarga":"hoy"},{"clave":"motivo","etiqueta":"Motivo","tipo":"seleccion","requerido":true,"opciones":["alta por objetivos cumplidos","finalización acordada","interrupción a pedido del/de la paciente","derivación"]},{"clave":"recomendaciones","etiqueta":"Recomendaciones (opcional)","tipo":"textoLargo","requerido":false},{"clave":"destinatario","etiqueta":"Se presenta ante","tipo":"texto","requerido":false},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Constancia de finalización de tratamiento"},{"tipo":"parrafo","texto":"Dejo constancia de que **{{paciente}}**, DNI {{dni}}, realizó tratamiento psicológico conmigo desde el {{fechaInicio}} hasta el {{fechaFin}}. Motivo de la finalización: {{motivo}}."},{"tipo":"subtitulo","texto":"Recomendaciones","condicion":{"campo":"recomendaciones","op":"tieneValor"}},{"tipo":"parrafo","texto":"{{recomendaciones}}","condicion":{"campo":"recomendaciones","op":"tieneValor"}},{"tipo":"parrafo","texto":"Se extiende para ser presentada ante {{destinatario}}.","condicion":{"campo":"destinatario","op":"tieneValor"}},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), '15d1e3f6f1db3d21ccc71f0ce580f79f4e331c3ac9adc57daa768463bb2ac771');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'inf_escolar')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('inf_escolar', N'Informes', N'Informe psicológico para la escuela', N'Requiere una autorización para compartir información emitida para el paciente.', 8);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"alumno","etiqueta":"Alumno/a","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dni","etiqueta":"DNI","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"curso","etiqueta":"Curso o grado","tipo":"texto","requerido":true},{"clave":"escuela","etiqueta":"Escuela","tipo":"texto","requerido":true},{"clave":"destinatario","etiqueta":"Destinatario (cargo o nombre)","tipo":"texto","requerido":true},{"clave":"motivo","etiqueta":"Motivo del informe","tipo":"textoLargo","requerido":true},{"clave":"observaciones","etiqueta":"Observaciones","tipo":"textoLargo","requerido":true},{"clave":"sugerencias","etiqueta":"Sugerencias para la escuela","tipo":"textoLargo","requerido":false},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Informe psicológico"},{"tipo":"parrafo","texto":"A: {{destinatario}} · {{escuela}}"},{"tipo":"parrafo","texto":"Alumno/a: **{{alumno}}**, DNI {{dni}}, curso {{curso}}."},{"tipo":"subtitulo","texto":"Motivo"},{"tipo":"parrafo","texto":"{{motivo}}"},{"tipo":"subtitulo","texto":"Observaciones"},{"tipo":"parrafo","texto":"{{observaciones}}"},{"tipo":"subtitulo","texto":"Sugerencias","condicion":{"campo":"sugerencias","op":"tieneValor"}},{"tipo":"parrafo","texto":"{{sugerencias}}","condicion":{"campo":"sugerencias","op":"tieneValor"}},{"tipo":"parrafo","texto":"Este informe se emite con autorización para compartir información y contiene solo lo necesario para acompañar al/a la alumno/a en la escuela. Es confidencial: no debe difundirse ni usarse para otros fines."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), 'd31263d647976a56cb0305f646d92a1f6b091d893c5daebb6ad15fef9fc0b1bd');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'derivacion')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('derivacion', N'Informes', N'Derivación a otro profesional', N'Nota de derivación o interconsulta. Se sugiere al cerrar una sesión con tipo de cierre Derivación.', 9);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"paciente","etiqueta":"Paciente (nombre y apellido)","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"dni","etiqueta":"DNI","tipo":"texto","requerido":true,"precarga":"paciente.dni"},{"clave":"edad","etiqueta":"Edad","tipo":"numero","requerido":false,"precarga":"paciente.edad"},{"clave":"especialidad","etiqueta":"Se deriva a","tipo":"seleccion","requerido":true,"opciones":["Psiquiatría","Neurología","Psicopedagogía","Clínica médica","Fonoaudiología","Terapia ocupacional"]},{"clave":"nombreDestinatario","etiqueta":"Profesional (opcional)","tipo":"texto","requerido":false},{"clave":"prioridad","etiqueta":"Prioridad","tipo":"seleccion","requerido":true,"opciones":["programada","preferente","urgente"]},{"clave":"motivo","etiqueta":"Motivo de la derivación","tipo":"textoLargo","requerido":true},{"clave":"datosClinicos","etiqueta":"Datos clínicos relevantes","tipo":"textoLargo","requerido":true},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Derivación"},{"tipo":"parrafo","texto":"A: {{especialidad}}"},{"tipo":"parrafo","texto":"Profesional: {{nombreDestinatario}}","condicion":{"campo":"nombreDestinatario","op":"tieneValor"}},{"tipo":"parrafo","texto":"Derivo a **{{paciente}}**, DNI {{dni}}, para evaluación y/o tratamiento. Prioridad: **{{prioridad}}**."},{"tipo":"subtitulo","texto":"Motivo"},{"tipo":"parrafo","texto":"{{motivo}}"},{"tipo":"subtitulo","texto":"Datos clínicos relevantes"},{"tipo":"parrafo","texto":"{{datosClinicos}}"},{"tipo":"parrafo","texto":"Quedo a disposición para el intercambio profesional que se considere necesario."},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), 'c5905ae9c67b976fb200f3cd65fc0b84945fa9e62e551533e0a5e98bd5f04a57');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaDocumento WHERE Clave = 'plan_seguridad')
BEGIN
    INSERT INTO dbo.PlantillaDocumento (Clave, Grupo, Titulo, Descripcion, Orden) VALUES ('plan_seguridad', N'Clínica', N'Plan de seguridad', N'Modelo de Stanley y Brown, en seis pasos que se completan junto con el paciente.', 10);
    INSERT INTO dbo.PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, FechaPublicacion, HashSha256)
    VALUES (SCOPE_IDENTITY(), 1,
        N'[{"clave":"paciente","etiqueta":"Paciente","tipo":"texto","requerido":true,"precarga":"paciente.nombre"},{"clave":"senales","etiqueta":"1. Señales de alerta","tipo":"textoLargo","requerido":true,"ayuda":"Pensamientos, emociones o situaciones que anticipan una crisis."},{"clave":"estrategias","etiqueta":"2. Estrategias propias para calmarme","tipo":"textoLargo","requerido":true},{"clave":"distracciones","etiqueta":"3. Personas y lugares que me ayudan a distraerme","tipo":"textoLargo","requerido":true},{"clave":"ayuda","etiqueta":"4. Personas a quienes puedo pedir ayuda (y cómo contactarlas)","tipo":"textoLargo","requerido":true},{"clave":"profesionales","etiqueta":"5. Profesionales y servicios a los que puedo recurrir","tipo":"textoLargo","requerido":true},{"clave":"entorno","etiqueta":"6. Cómo hacer más seguro mi entorno","tipo":"textoLargo","requerido":true},{"clave":"localidad","etiqueta":"Localidad","tipo":"texto","requerido":true,"precarga":"organizacion.localidad"},{"clave":"fecha","etiqueta":"Fecha","tipo":"fecha","requerido":true,"precarga":"hoy"}]',
        N'[{"tipo":"titulo","texto":"Mi plan de seguridad"},{"tipo":"parrafo","texto":"Plan elaborado por **{{paciente}}** junto con el/la Lic. {{p.profesional.nombre}}. Tenerlo a mano y seguir los pasos en orden."},{"tipo":"subtitulo","texto":"1. Señales de alerta"},{"tipo":"parrafo","texto":"{{senales}}"},{"tipo":"subtitulo","texto":"2. Lo que puedo hacer por mi cuenta para calmarme"},{"tipo":"parrafo","texto":"{{estrategias}}"},{"tipo":"subtitulo","texto":"3. Personas y lugares que me ayudan a distraerme"},{"tipo":"parrafo","texto":"{{distracciones}}"},{"tipo":"subtitulo","texto":"4. Personas a quienes puedo pedir ayuda"},{"tipo":"parrafo","texto":"{{ayuda}}"},{"tipo":"subtitulo","texto":"5. Profesionales y líneas de ayuda"},{"tipo":"parrafo","texto":"{{profesionales}}"},{"tipo":"parrafo","texto":"Líneas de emergencia y de salud mental: [VERIFICAR LÍNEAS DE CÓRDOBA]. Si estoy en peligro, voy a la guardia más cercana."},{"tipo":"subtitulo","texto":"6. Cómo hago más seguro mi entorno"},{"tipo":"parrafo","texto":"{{entorno}}"},{"tipo":"parrafo","texto":"En {{localidad}}, a los {{fecha}}."},{"tipo":"firma","texto":"Paciente: {{paciente}}"},{"tipo":"firma","texto":"Profesional: {{p.profesional.nombre}} · Mat. {{p.profesional.matricula}}"}]',
        'Publicada', 1, N'Versión inicial (provisoria, sin validación legal).', SYSDATETIME(), 'fd87875bd0c87a5e6fda62ef20bc28a31eee8e09046a3c3b34067437000a5743');
END
GO

-- ── 8) Registro de versión ────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 35)
    INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (35, N'add_documentos_clinicos.sql', 'Script');
GO

PRINT N'Migración 35 aplicada.';
SELECT p.Clave, v.Numero, v.Estado, v.Provisoria
FROM dbo.PlantillaDocumento p JOIN dbo.PlantillaDocumentoVersion v ON v.IdPlantilla = p.IdPlantilla
ORDER BY p.Orden;
GO
