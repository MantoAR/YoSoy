/* ============================================================================
   01-produccion.sql — Esquema completo de YoSoy para un servidor nuevo.

   Qué es: la base [yosoy] tal como la necesita el código hoy — el script base
   de la rama `DataBase` más las 24 migraciones de migrations/, consolidados
   en un solo archivo con la forma FINAL de cada tabla (no un replay histórico
   de cada ALTER). Pensado para correr UNA vez contra un servidor recién
   instalado, sin nada previo.

   Qué NO incluye, a propósito:
     - Usuarios de SQL Server ni contraseñas (ver 02-usuario-app.sql).
     - Datos de demo, de prueba o correcciones puntuales de una base de
       desarrollo (ver docs/CATALOGO_SCRIPTS_SQL.md — ninguno de esos scripts
       se usó para armar este).
     - Rutas de archivo fijas para los .mdf/.ldf: CREATE DATABASE usa la
       configuración por defecto del servidor en vez de un path de Windows
       de una máquina de desarrollo.

   Qué SÍ incluye como dato (no esquema): un solo INSERT obligatorio —
   TipoConsentimiento de telemedicina, que el código busca por nombre exacto
   (Consentimiento:TipoRequerido en appsettings.json, regla RN-02). Sin esa
   fila, el flujo de consentimiento de telemedicina no tiene con qué
   funcionar. Al final del archivo hay una sección aparte, bien separada y
   comentada, con contenido de arranque OPCIONAL (preguntas frecuentes y
   respuestas rápidas del chat) — no hace falta para que la app funcione,
   decidir si se quiere o no queda en quien corra esto.

   Cómo correrlo:
     sqlcmd -S TU_SERVIDOR -d master -C -f 65001 -i 01-produccion.sql
   (-f 65001 para que los acentos de los nombres/descripciones se guarden bien)
   ============================================================================ */

-- ── 0) Base de datos ────────────────────────────────────────────────────────
IF DB_ID(N'yosoy') IS NULL
    CREATE DATABASE [yosoy];
GO

USE [yosoy];
GO

-- ── 1) Tablas — catálogos y datos base (del script de la rama DataBase) ────

CREATE TABLE [dbo].[Especialidad](
    [IdEspecialidad] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](100) NOT NULL UNIQUE,
    [Estado] [nvarchar](50) NOT NULL CONSTRAINT [DF_Especialidad_Estado] DEFAULT ('Activo')
);
GO

CREATE TABLE [dbo].[Rol](
    [IdRol] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](50) NOT NULL UNIQUE,
    [Estado] [nvarchar](50) NOT NULL CONSTRAINT [DF_Rol_Estado] DEFAULT ('Activo')
);
GO

CREATE TABLE [dbo].[EstadoTurno](
    [IdEstadoTurno] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Descripcion] [nvarchar](50) NOT NULL UNIQUE,
    [Estado] [nvarchar](50) NOT NULL CONSTRAINT [DF_EstadoTurno_Estado] DEFAULT ('Activo')
);
GO

CREATE TABLE [dbo].[TipoConsentimiento](
    [IdTipoConsentimiento] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Descripcion] [nvarchar](100) NOT NULL UNIQUE,
    [Estado] [nvarchar](50) NOT NULL CONSTRAINT [DF_TipoConsentimiento_Estado] DEFAULT ('Activo')
);
GO

CREATE TABLE [dbo].[ObraSocial](
    [IdObraSocial] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](100) NOT NULL UNIQUE
);
GO

CREATE TABLE [dbo].[Tutor](
    [IdTutor] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](100) NULL,
    [Dni] [nvarchar](20) NULL,
    [Vinculo] [nvarchar](50) NULL,
    [Contacto] [nvarchar](100) NULL
);
GO

CREATE TABLE [dbo].[Usuario](
    [IdUsuario] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Username] [nvarchar](50) NOT NULL UNIQUE,
    [PasswordHash] [nvarchar](255) NOT NULL,
    [Estado] [nvarchar](50) NULL,
    [Mail] [nvarchar](100) NULL CONSTRAINT [UQ_Usuario_Mail] UNIQUE,
    [IntentosFallidos] [int] NOT NULL CONSTRAINT [DF_Usuario_IntentosFallidos] DEFAULT (0),
    [BloqueadoHasta] [datetime] NULL
);
GO

CREATE TABLE [dbo].[Paciente](
    [IdPaciente] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](100) NOT NULL,
    [Dni] [nvarchar](20) NOT NULL UNIQUE,
    [FechaNacimiento] [date] NULL,
    [Domicilio] [nvarchar](200) NULL,
    [Telefono] [nvarchar](50) NULL,
    [Mail] [nvarchar](100) NULL,
    [Estado] [nvarchar](50) NULL
);
GO

-- Profesional.Token (recuperación/activación por mail) y Slug/LinkPublicoActivo
-- (link público de reserva, migración 11) ya van en la forma final de la tabla.
CREATE TABLE [dbo].[Profesional](
    [IdProfesional] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](100) NOT NULL,
    [Matricula] [nvarchar](50) NOT NULL UNIQUE,
    [IdEspecialidad] [int] NOT NULL,
    [Estado] [nvarchar](50) NULL,
    [Mail] [nvarchar](100) NULL,
    [Telefono] [nvarchar](50) NULL,
    [Apellido] [nvarchar](100) NULL,
    [SegundoNombre] [nvarchar](100) NULL,
    [SegundoApellido] [nvarchar](100) NULL,
    [Dni] [nvarchar](20) NULL,
    [Sexo] [nvarchar](20) NULL,
    [FechaNacimiento] [date] NULL,
    [Provincia] [nvarchar](100) NULL,
    [OrigenContacto] [nvarchar](200) NULL,
    [FechaSolicitud] [datetime] NULL,
    [Token] [nvarchar](64) NULL,
    [Slug] [nvarchar](80) NULL,
    [LinkPublicoActivo] [bit] NOT NULL CONSTRAINT [DF_Profesional_LinkPublicoActivo] DEFAULT (0)
);
GO

CREATE TABLE [dbo].[Agenda](
    [IdAgenda] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdProfesional] [int] NOT NULL,
    [Dia] [nvarchar](20) NULL,
    [HoraInicio] [time](7) NULL,
    [HoraFin] [time](7) NULL,
    [Disponible] [bit] NOT NULL CONSTRAINT [DF_Agenda_Disponible] DEFAULT ((1)),
    [IntervaloMinutos] [int] NOT NULL CONSTRAINT [DF_Agenda_IntervaloMinutos] DEFAULT (30)
);
GO

-- Turno.ConfirmadoPaciente y CodigoSala (migración 1) ya van en la forma final.
CREATE TABLE [dbo].[Turno](
    [IdTurno] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [IdProfesional] [int] NOT NULL,
    [IdEstadoTurno] [int] NOT NULL,
    [Fecha] [date] NOT NULL,
    [Hora] [time](7) NOT NULL,
    [Modalidad] [nvarchar](50) NULL,
    [Motivo] [nvarchar](500) NULL,
    [CreadoPor] [nvarchar](20) NULL,
    [ConfirmadoPaciente] [bit] NOT NULL CONSTRAINT [DF_Turno_ConfirmadoPaciente] DEFAULT (0),
    [CodigoSala] [nvarchar](50) NULL
);
GO

CREATE TABLE [dbo].[Cobertura](
    [IdCobertura] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [IdObraSocial] [int] NOT NULL,
    [NroAfiliado] [nvarchar](50) NULL,
    [PlanCobertura] [nvarchar](50) NULL,
    [Estado] [nvarchar](50) NULL
);
GO

-- Consentimiento.FechaFirma es DATETIME (migración 6: el tipo original en el
-- script base era DATE; el código que firma guarda fecha y hora).
CREATE TABLE [dbo].[Consentimiento](
    [IdConsentimiento] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [IdTipoConsentimiento] [int] NOT NULL,
    [Version] [nvarchar](20) NULL,
    [FechaFirma] [datetime] NOT NULL,
    [Estado] [nvarchar](50) NULL
);
GO

CREATE TABLE [dbo].[Derivacion](
    [IdDerivacion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [IdProfesionalOrigen] [int] NOT NULL,
    [IdProfesionalDestino] [int] NOT NULL,
    [Fecha] [date] NOT NULL,
    [Motivo] [nvarchar](max) NULL,
    [Estado] [nvarchar](50) NOT NULL CONSTRAINT [DF_Derivacion_Estado] DEFAULT ('Pendiente')
);
GO

CREATE TABLE [dbo].[EventoCrisis](
    [IdEvento] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [Fecha] [date] NOT NULL,
    [NivelRiesgo] [nvarchar](50) NULL,
    [Acciones] [nvarchar](max) NULL
);
GO

CREATE TABLE [dbo].[Evolucion](
    [IdEvolucion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdHistoria] [int] NOT NULL,
    [IdProfesional] [int] NOT NULL,
    [IdTurno] [int] NULL,
    [Fecha] [date] NOT NULL,
    [Diagnostico] [nvarchar](max) NULL,
    [Observaciones] [nvarchar](max) NULL,
    [PlanTratamiento] [nvarchar](max) NULL
);
GO

CREATE TABLE [dbo].[HistoriaClinica](
    [IdHistoria] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL UNIQUE,
    [FechaApertura] [date] NULL
);
GO

CREATE TABLE [dbo].[InteraccionIA](
    [IdInteraccion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [Fecha] [datetime] NOT NULL,
    [Consulta] [nvarchar](max) NOT NULL,
    [Respuesta] [nvarchar](max) NOT NULL
);
GO

-- LogAuditoria.IdUsuario admite NULL (migración 8: acciones sin sesión, como
-- el registro público de un profesional).
CREATE TABLE [dbo].[LogAuditoria](
    [IdLog] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdUsuario] [int] NULL,
    [Accion] [nvarchar](100) NOT NULL,
    [Entidad] [nvarchar](50) NOT NULL,
    [IdEntidad] [int] NULL,
    [Detalle] [nvarchar](500) NULL,
    [FechaHora] [datetime] NOT NULL CONSTRAINT [DF_LogAuditoria_FechaHora] DEFAULT (getdate()),
    -- Migración 19: categoría, módulo y cadena de hashes (ver los triggers más abajo, sección 7b).
    [Espacio] [nvarchar](30) NULL,
    [Categoria] [nvarchar](20) NOT NULL CONSTRAINT [DF_LogAuditoria_Categoria] DEFAULT ('Administrativa')
        CONSTRAINT [CK_LogAuditoria_Categoria] CHECK ([Categoria] IN ('Administrativa', 'Clinica')),
    [Hash] [varbinary](32) NULL,
    [HashAnterior] [varbinary](32) NULL
);
GO

CREATE TABLE [dbo].[PacienteProfesional](
    [IdPacienteProfesional] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [IdProfesional] [int] NOT NULL,
    [FechaAsignacion] [datetime] NULL CONSTRAINT [DF_PacienteProfesional_FechaAsignacion] DEFAULT (getdate()),
    [EstadoAsignacion] [nvarchar](50) NULL,
    CONSTRAINT [UQ_PacProf] UNIQUE ([IdPaciente], [IdProfesional])
);
GO

CREATE TABLE [dbo].[PacienteTutor](
    [IdPaciente] [int] NOT NULL,
    [IdTutor] [int] NOT NULL,
    PRIMARY KEY ([IdPaciente], [IdTutor])
);
GO

CREATE TABLE [dbo].[RecuperacionPassword](
    [IdRecuperacion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdUsuario] [int] NOT NULL,
    [FechaSolicitud] [datetime] NOT NULL CONSTRAINT [DF_RecuperacionPassword_FechaSolicitud] DEFAULT (getdate()),
    [Estado] [nvarchar](20) NOT NULL CONSTRAINT [DF_RecuperacionPassword_Estado] DEFAULT ('Pendiente'),
    [PasswordTemporal] [nvarchar](255) NULL,
    [FechaResolucion] [datetime] NULL,
    [Token] [nvarchar](64) NULL,
    [TokenExpiracion] [datetime] NULL
);
GO

CREATE TABLE [dbo].[UsuarioPaciente](
    [IdUsuario] [int] NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [FechaVinculacion] [date] NOT NULL CONSTRAINT [DF_UsuarioPaciente_FechaVinculacion] DEFAULT (getdate())
);
GO

CREATE TABLE [dbo].[UsuarioProfesional](
    [IdUsuario] [int] NOT NULL PRIMARY KEY,
    [IdProfesional] [int] NOT NULL,
    [FechaVinculacion] [date] NOT NULL CONSTRAINT [DF_UsuarioProfesional_FechaVinculacion] DEFAULT (getdate())
);
GO

CREATE TABLE [dbo].[UsuarioRol](
    [IdUsuario] [int] NOT NULL,
    [IdRol] [int] NOT NULL,
    PRIMARY KEY ([IdUsuario], [IdRol])
);
GO

-- ── 2) Tablas — admisión y sesión clínica (migración 9: existían en la base
--      real sin una migración propia; esquema tomado tal cual) y consentimiento
--      de la lectura de emociones en la sala de video (migración 15) ────────

CREATE TABLE [dbo].[InformeAdmision](
    [IdInforme] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [IdEspecialidad] [int] NULL,
    [MotivoConsulta] [nvarchar](1000) NULL,
    [TratamientoAnterior] [bit] NOT NULL CONSTRAINT [DF_InformeAdmision_TratamientoAnterior] DEFAULT (0),
    [TratamientoAnteriorDetalle] [nvarchar](1000) NULL,
    [RecibeMedicacion] [bit] NOT NULL CONSTRAINT [DF_InformeAdmision_RecibeMedicacion] DEFAULT (0),
    [MedicacionDetalle] [nvarchar](1000) NULL,
    [GustosIntereses] [nvarchar](1000) NULL,
    [PreferenciasAtencion] [nvarchar](1000) NULL,
    [TipoEntrevista] [nvarchar](50) NULL,
    [RedApoyo] [nvarchar](1000) NULL,
    [ObjetivosPaciente] [nvarchar](1000) NULL,
    [Estado] [nvarchar](50) NOT NULL CONSTRAINT [DF_InformeAdmision_Estado] DEFAULT ('Pendiente'),
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_InformeAdmision_FechaCreacion] DEFAULT (getdate()),
    [FechaEntrevista] [datetime] NULL,
    [ObservacionesEntrevista] [nvarchar](1000) NULL,
    [IdProfesionalDerivado] [int] NULL,
    [CodigoSala] [nvarchar](50) NULL,
    [MotivoRechazo] [nvarchar](500) NULL
);
GO

CREATE TABLE [dbo].[Notificacion](
    [IdNotificacion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [Titulo] [nvarchar](200) NULL,
    [Mensaje] [nvarchar](1000) NULL,
    [Link] [nvarchar](200) NULL,
    [Leida] [bit] NOT NULL CONSTRAINT [DF_Notificacion_Leida] DEFAULT (0),
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_Notificacion_FechaCreacion] DEFAULT (getdate())
);
GO

CREATE TABLE [dbo].[SesionClinica](
    [IdSesion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdTurno] [int] NULL,
    [IdPaciente] [int] NOT NULL,
    [IdProfesional] [int] NOT NULL,
    [FechaHora] [datetime] NOT NULL,
    [Transcripcion] [nvarchar](max) NULL,
    [NotasEvolucion] [nvarchar](max) NULL,
    [HistorialEmociones] [nvarchar](max) NULL,
    [TipoCierre] [nvarchar](50) NULL,
    -- Migración 15: con qué consentimiento se guardaron las emociones, y cuándo y por qué se anularon.
    [IdConsentimientoVideo] [int] NULL,
    [EmocionesAnuladasUtc] [datetime2](3) NULL,
    [EmocionesAnuladasMotivo] [nvarchar](20) NULL,
    CONSTRAINT [CK_SesionClinica_EmocionesAnuladasMotivo]
        CHECK ([EmocionesAnuladasMotivo] IS NULL OR [EmocionesAnuladasMotivo] IN ('revocacion', 'retencion'))
);
GO

-- Migración 15. Una fila por DECISIÓN de un paciente sobre una sesión de video (turno o entrevista de admisión);
-- la vigente es la más nueva de esa sesión. Fechas en UTC a propósito (el resto del esquema usa GETDATE()).
CREATE TABLE [dbo].[ConsentimientoVideo](
    [IdConsentimientoVideo] [int] IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ConsentimientoVideo] PRIMARY KEY,
    [IdUsuario] [int] NOT NULL,
    [IdPaciente] [int] NOT NULL,
    [Origen] [nvarchar](10) NOT NULL,
    [IdOrigen] [int] NOT NULL,
    [VersionTexto] [nvarchar](40) NOT NULL,
    [HashTexto] [char](64) NOT NULL,
    [Decision] [nvarchar](10) NOT NULL,
    [DecididoUtc] [datetime2](3) NOT NULL CONSTRAINT [DF_ConsentimientoVideo_DecididoUtc] DEFAULT (sysutcdatetime()),
    [RevocadoUtc] [datetime2](3) NULL,
    CONSTRAINT [CK_ConsentimientoVideo_Origen] CHECK ([Origen] IN ('turno', 'admision')),
    CONSTRAINT [CK_ConsentimientoVideo_Decision] CHECK ([Decision] IN ('acepta', 'rechaza')),
    CONSTRAINT [CK_ConsentimientoVideo_Revocacion] CHECK ([RevocadoUtc] IS NULL OR ([Decision] = 'acepta' AND [RevocadoUtc] >= [DecididoUtc]))
);
GO

-- Migración 16. Una fila por ACEPTACIÓN de un paciente de recibir avisos de sus turnos por WhatsApp; la de su fila más nueva
-- es su estado. Solo se edita para revocarla (volver a aceptar es otra fila). Fechas en UTC a propósito, como arriba.
CREATE TABLE [dbo].[ConsentimientoWhatsApp](
    [IdConsentimientoWhatsApp] [int] IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ConsentimientoWhatsApp] PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [IdUsuario] [int] NOT NULL,
    [Telefono] [varchar](10) NOT NULL,
    [Origen] [nvarchar](10) NOT NULL,
    [VersionTexto] [nvarchar](40) NOT NULL,
    [HashTexto] [char](64) NOT NULL,
    [AceptadoUtc] [datetime2](3) NOT NULL CONSTRAINT [DF_ConsentimientoWhatsApp_AceptadoUtc] DEFAULT (sysutcdatetime()),
    [RevocadoUtc] [datetime2](3) NULL,
    [MotivoRevocacion] [nvarchar](20) NULL,
    CONSTRAINT [CK_ConsentimientoWhatsApp_Origen] CHECK ([Origen] IN ('registro', 'perfil')),
    CONSTRAINT [CK_ConsentimientoWhatsApp_Telefono] CHECK (LEN([Telefono]) = 10 AND [Telefono] NOT LIKE '%[^0-9]%'),
    CONSTRAINT [CK_ConsentimientoWhatsApp_Hash] CHECK (LEN([HashTexto]) = 64),
    CONSTRAINT [CK_ConsentimientoWhatsApp_Revocacion] CHECK (
        ([RevocadoUtc] IS NULL AND [MotivoRevocacion] IS NULL)
        OR ([RevocadoUtc] IS NOT NULL AND [MotivoRevocacion] IS NOT NULL AND [RevocadoUtc] >= [AceptadoUtc]
            AND [MotivoRevocacion] IN ('paciente', 'baja-whatsapp', 'telefono-cambiado', 'texto-nuevo')))
);
GO

-- ── 3) Tablas — grupos, casos y derivación en cascada (migración 10) ───────

CREATE TABLE [dbo].[Grupo](
    [IdGrupo] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](100) NOT NULL,
    [Descripcion] [nvarchar](300) NULL,
    [Activo] [bit] NOT NULL CONSTRAINT [DF_Grupo_Activo] DEFAULT (1)
);
GO

CREATE TABLE [dbo].[ProfesionalGrupo](
    [IdProfesional] [int] NOT NULL,
    [IdGrupo] [int] NOT NULL,
    CONSTRAINT [PK_ProfesionalGrupo] PRIMARY KEY ([IdProfesional], [IdGrupo])
);
GO

-- Caso.IdInforme es único solo entre los NO NULL (índice filtrado, sección 6):
-- SQL Server no permite más de un NULL en un UNIQUE normal.
CREATE TABLE [dbo].[Caso](
    [IdCaso] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdPaciente] [int] NOT NULL,
    [Estado] [nvarchar](20) NOT NULL CONSTRAINT [DF_Caso_Estado] DEFAULT ('Nuevo'),
    [IdGrupoActual] [int] NULL,
    [IdProfesionalAsignado] [int] NULL,
    [Origen] [nvarchar](20) NOT NULL CONSTRAINT [DF_Caso_Origen] DEFAULT ('Admision'),
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_Caso_FechaCreacion] DEFAULT (getdate()),
    [IdInforme] [int] NULL,
    CONSTRAINT [CK_Caso_Estado] CHECK ([Estado] IN ('Nuevo', 'Derivado', 'Asignado', 'En atención', 'Cerrado')),
    CONSTRAINT [CK_Caso_Origen] CHECK ([Origen] IN ('Admision', 'Canal', 'Bot'))
);
GO

CREATE TABLE [dbo].[CasoDerivacion](
    [IdCasoDerivacion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdCaso] [int] NOT NULL,
    [IdGrupo] [int] NOT NULL,
    [IdProfesionalSugerido] [int] NULL,
    [IdProfesionalAsignado] [int] NULL,
    [Origen] [nvarchar](10) NOT NULL CONSTRAINT [DF_CasoDerivacion_Origen] DEFAULT ('Regla'),
    [Estado] [nvarchar](25) NOT NULL CONSTRAINT [DF_CasoDerivacion_Estado] DEFAULT ('Pendiente de confirmar'),
    [Motivo] [nvarchar](200) NULL,
    [IdCreadoPor] [int] NULL,
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_CasoDerivacion_FechaCreacion] DEFAULT (getdate()),
    [FechaResolucion] [datetime] NULL,
    [Nota] [nvarchar](140) NULL,
    CONSTRAINT [CK_CasoDerivacion_Origen] CHECK ([Origen] IN ('Regla', 'Manual', 'Bot')),
    CONSTRAINT [CK_CasoDerivacion_Estado] CHECK ([Estado] IN ('Pendiente de confirmar', 'Confirmada', 'Corregida', 'Rechazada'))
);
GO

CREATE TABLE [dbo].[ReglaDerivacion](
    [IdRegla] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdEspecialidad] [int] NOT NULL,
    [IdGrupoDestino] [int] NOT NULL,
    [Prioridad] [int] NOT NULL CONSTRAINT [DF_ReglaDerivacion_Prioridad] DEFAULT (0),
    [Activa] [bit] NOT NULL CONSTRAINT [DF_ReglaDerivacion_Activa] DEFAULT (1)
);
GO

CREATE TABLE [dbo].[HistorialAsignacion](
    [IdHistorial] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdCaso] [int] NOT NULL,
    [IdProfesional] [int] NULL,
    [Motivo] [nvarchar](25) NOT NULL,
    [IdRealizadoPor] [int] NULL,
    [FechaHora] [datetime] NOT NULL CONSTRAINT [DF_HistorialAsignacion_FechaHora] DEFAULT (getdate()),
    CONSTRAINT [CK_HistorialAsignacion_Motivo] CHECK ([Motivo] IN ('AutomaticaPorCarga', 'AutomaticaAleatoria', 'Manual', 'SolicitudCambio', 'SinDisponibles'))
);
GO

-- ── 4) Tablas — mensajería: chatbot, recordatorios, avisos (migración 5) ───

CREATE TABLE [dbo].[Conversacion](
    [IdConversacion] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Telefono] [nvarchar](20) NOT NULL,
    [IdPaciente] [int] NULL,
    [Canal] [nvarchar](20) NOT NULL CONSTRAINT [DF_Conversacion_Canal] DEFAULT ('Simulado'),
    [PasoActual] [nvarchar](50) NOT NULL CONSTRAINT [DF_Conversacion_Paso] DEFAULT ('Inicio'),
    [DatosContexto] [nvarchar](max) NULL,
    [Estado] [nvarchar](20) NOT NULL CONSTRAINT [DF_Conversacion_Estado] DEFAULT ('Activa'),
    [FechaUltimaActividad] [datetime] NOT NULL CONSTRAINT [DF_Conversacion_FUA] DEFAULT (getdate()),
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_Conversacion_FC] DEFAULT (getdate()),
    [IdUltimoMensajeLeido] [int] NULL,
    [IdProfesionalAsignado] [int] NULL,
    -- Migración 17 (chat de WhatsApp para el personal): el nombre del perfil de WhatsApp de quien escribe. No va a los logs.
    [NombreContacto] [nvarchar](100) NULL,
    CONSTRAINT [CK_Conversacion_Estado] CHECK ([Estado] IN ('Activa', 'DerivadaHumano', 'Urgente', 'Cerrada')),
    CONSTRAINT [UQ_Conversacion_Telefono_Canal] UNIQUE ([Telefono], [Canal])
);
GO

CREATE TABLE [dbo].[MensajeChat](
    [IdMensaje] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdConversacion] [int] NOT NULL,
    [Direccion] [nvarchar](10) NOT NULL,
    [Origen] [nvarchar](10) NOT NULL,
    [Texto] [nvarchar](4000) NOT NULL,
    [FechaHora] [datetime] NOT NULL CONSTRAINT [DF_MensajeChat_FechaHora] DEFAULT (getdate()),
    [EstadoEnvio] [nvarchar](10) NOT NULL CONSTRAINT [DF_MensajeChat_EstadoEnvio] DEFAULT ('Pendiente'),
    -- Migración 17 (chat de WhatsApp para el personal): el id del mensaje en WhatsApp (con el índice único filtrado de la sección 6 no se guarda dos veces) y su tipo.
    -- Lo que no es texto se guarda como un aviso del tipo en Texto ("[Imagen]"): el archivo no se guarda.
    [IdExterno] [nvarchar](64) NULL,
    [Tipo] [nvarchar](12) NOT NULL CONSTRAINT [DF_MensajeChat_Tipo] DEFAULT ('texto'),
    CONSTRAINT [CK_MensajeChat_Direccion] CHECK ([Direccion] IN ('Entrante', 'Saliente')),
    CONSTRAINT [CK_MensajeChat_Origen] CHECK ([Origen] IN ('Paciente', 'Bot', 'Humano', 'Sistema')),
    CONSTRAINT [CK_MensajeChat_EstadoEnvio] CHECK ([EstadoEnvio] IN ('Pendiente', 'Enviado', 'Error')),
    CONSTRAINT [CK_MensajeChat_Tipo] CHECK ([Tipo] IN ('texto', 'audio', 'imagen', 'video', 'documento', 'sticker', 'ubicacion', 'contacto', 'otro'))
);
GO

CREATE TABLE [dbo].[Recordatorio](
    [IdRecordatorio] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdTurno] [int] NOT NULL,
    [Tipo] [nvarchar](30) NOT NULL,
    [Canal] [nvarchar](10) NOT NULL,
    [FechaProgramada] [datetime] NOT NULL,
    [FechaEnvio] [datetime] NULL,
    [Estado] [nvarchar](10) NOT NULL CONSTRAINT [DF_Recordatorio_Estado] DEFAULT ('Pendiente'),
    [Intentos] [int] NOT NULL CONSTRAINT [DF_Recordatorio_Intentos] DEFAULT (0),
    [Error] [nvarchar](1000) NULL,
    [Respuesta] [nvarchar](30) NULL,
    [FechaRespuesta] [datetime] NULL,
    CONSTRAINT [CK_Recordatorio_Canal] CHECK ([Canal] IN ('Chat', 'Email', 'App')),
    CONSTRAINT [CK_Recordatorio_Estado] CHECK ([Estado] IN ('Pendiente', 'Enviado', 'Error', 'Omitido'))
);
GO

CREATE TABLE [dbo].[PreguntaFrecuente](
    [IdPregunta] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Pregunta] [nvarchar](300) NOT NULL,
    [Respuesta] [nvarchar](2000) NOT NULL,
    [Orden] [int] NOT NULL CONSTRAINT [DF_PreguntaFrecuente_Orden] DEFAULT (0),
    [Activa] [bit] NOT NULL CONSTRAINT [DF_PreguntaFrecuente_Activa] DEFAULT (1)
);
GO

CREATE TABLE [dbo].[NotificacionInterna](
    [IdNotificacionInterna] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Destinatario] [nvarchar](20) NOT NULL,
    [IdProfesional] [int] NULL,
    [IdTurno] [int] NULL,
    [IdConversacion] [int] NULL,
    [Tipo] [nvarchar](40) NOT NULL,
    [Titulo] [nvarchar](200) NOT NULL,
    [Mensaje] [nvarchar](1000) NOT NULL,
    [Leida] [bit] NOT NULL CONSTRAINT [DF_NotificacionInterna_Leida] DEFAULT (0),
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_NotificacionInterna_FC] DEFAULT (getdate()),
    CONSTRAINT [CK_NotificacionInterna_Destinatario] CHECK ([Destinatario] IN ('Administrador', 'Profesional'))
);
GO

-- ── 5) Tablas — notas, etiquetas y respuestas rápidas de chats (migración 13) ─

CREATE TABLE [dbo].[NotaInternaConversacion](
    [IdNota] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [IdConversacion] [int] NOT NULL,
    [Texto] [nvarchar](1000) NOT NULL,
    [IdUsuario] [int] NULL,
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_NotaInternaConversacion_Fecha] DEFAULT (getdate())
);
GO

CREATE TABLE [dbo].[EtiquetaConversacion](
    [IdEtiqueta] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Nombre] [nvarchar](40) NOT NULL,
    [Tono] [nvarchar](20) NOT NULL CONSTRAINT [DF_EtiquetaConversacion_Tono] DEFAULT ('neutral'),
    [Activa] [bit] NOT NULL CONSTRAINT [DF_EtiquetaConversacion_Activa] DEFAULT (1),
    CONSTRAINT [CK_EtiquetaConversacion_Tono] CHECK ([Tono] IN ('marca', 'warn', 'danger', 'neutral', 'ok'))
);
GO

CREATE TABLE [dbo].[ConversacionEtiqueta](
    [IdConversacion] [int] NOT NULL,
    [IdEtiqueta] [int] NOT NULL,
    CONSTRAINT [PK_ConversacionEtiqueta] PRIMARY KEY ([IdConversacion], [IdEtiqueta])
);
GO

CREATE TABLE [dbo].[RespuestaRapida](
    [IdRespuestaRapida] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Titulo] [nvarchar](60) NOT NULL,
    [Texto] [nvarchar](2000) NOT NULL,
    [IdProfesional] [int] NULL,
    [Activa] [bit] NOT NULL CONSTRAINT [DF_RespuestaRapida_Activa] DEFAULT (1),
    [FechaCreacion] [datetime] NOT NULL CONSTRAINT [DF_RespuestaRapida_Fecha] DEFAULT (getdate())
);
GO

-- ── 5b) Tablas — versión del esquema y registro de errores (migración 18) ───────
CREATE TABLE VersionEsquema (
    Numero          INT           NOT NULL PRIMARY KEY,
    Archivo         NVARCHAR(200) NOT NULL,
    FechaAplicacion DATETIME2(0)  NOT NULL CONSTRAINT DF_VersionEsquema_Fecha DEFAULT SYSDATETIME(),
    Origen          NVARCHAR(20)  NOT NULL CONSTRAINT DF_VersionEsquema_Origen DEFAULT 'Script'
        CONSTRAINT CK_VersionEsquema_Origen CHECK (Origen IN ('Script', 'Detectada'))
);
GO

CREATE TABLE LogError (
    IdLogError  BIGINT         IDENTITY(1,1) PRIMARY KEY,
    Codigo      CHAR(9)        NOT NULL,
    FechaHora   DATETIME2(3)   NOT NULL CONSTRAINT DF_LogError_Fecha DEFAULT SYSDATETIME(),
    Metodo      NVARCHAR(10)   NULL,
    Ruta        NVARCHAR(300)  NULL,
    StatusCode  INT            NULL,
    IdUsuario   INT            NULL,
    Rol         NVARCHAR(30)   NULL,
    TipoError   NVARCHAR(200)  NULL,
    Mensaje     NVARCHAR(1000) NULL,
    Detalle     NVARCHAR(MAX)  NULL,
    VersionApi  NVARCHAR(50)   NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.LogError') AND name = 'UX_LogError_Codigo')
    CREATE UNIQUE INDEX UX_LogError_Codigo ON LogError(Codigo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.LogError') AND name = 'IX_LogError_Fecha')
    CREATE INDEX IX_LogError_Fecha ON LogError(FechaHora DESC);
GO


-- ── 6) Índices (únicos filtrados y de búsqueda) ─────────────────────────────
SET QUOTED_IDENTIFIER ON;
GO

CREATE UNIQUE INDEX [UQ_Profesional_Slug] ON [dbo].[Profesional]([Slug]) WHERE [Slug] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UQ_Caso_IdInforme] ON [dbo].[Caso]([IdInforme]) WHERE [IdInforme] IS NOT NULL;
GO
CREATE INDEX [IX_Caso_Estado] ON [dbo].[Caso]([Estado]);
GO
CREATE INDEX [IX_Caso_Grupo] ON [dbo].[Caso]([IdGrupoActual]);
GO
CREATE INDEX [IX_Caso_Profesional] ON [dbo].[Caso]([IdProfesionalAsignado]);
GO
CREATE INDEX [IX_CasoDerivacion_Estado] ON [dbo].[CasoDerivacion]([Estado]);
GO
CREATE INDEX [IX_CasoDerivacion_Caso] ON [dbo].[CasoDerivacion]([IdCaso]);
GO
CREATE INDEX [IX_HistorialAsignacion_Caso] ON [dbo].[HistorialAsignacion]([IdCaso]);
GO
CREATE INDEX [IX_MensajeChat_Conversacion] ON [dbo].[MensajeChat]([IdConversacion], [IdMensaje]);
GO
-- Migración 17. Índice único filtrado: el mismo mensaje de WhatsApp no se guarda dos veces en una conversación (los mensajes sin id no cuentan).
CREATE UNIQUE INDEX [UQ_MensajeChat_IdExterno] ON [dbo].[MensajeChat]([IdConversacion], [IdExterno]) WHERE [IdExterno] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_Recordatorio_Turno_Tipo_Canal] ON [dbo].[Recordatorio]([IdTurno], [Tipo], [Canal]);
GO
CREATE INDEX [IX_Conversacion_ProfesionalAsignado] ON [dbo].[Conversacion]([IdProfesionalAsignado]);
GO
CREATE INDEX [IX_NotaInternaConversacion_Conversacion] ON [dbo].[NotaInternaConversacion]([IdConversacion]);
GO
CREATE UNIQUE INDEX [UQ_EtiquetaConversacion_Nombre] ON [dbo].[EtiquetaConversacion]([Nombre]);
GO
CREATE INDEX [IX_RespuestaRapida_Profesional] ON [dbo].[RespuestaRapida]([IdProfesional]);
GO
CREATE INDEX [IX_ConsentimientoVideo_Sesion] ON [dbo].[ConsentimientoVideo]([Origen], [IdOrigen], [IdPaciente], [IdConsentimientoVideo]);
GO
-- Índice único filtrado: a lo sumo UNA aceptación abierta (sin revocar) de WhatsApp por paciente.
CREATE UNIQUE INDEX [UQ_ConsentimientoWhatsApp_Abierto] ON [dbo].[ConsentimientoWhatsApp]([IdPaciente]) WHERE [RevocadoUtc] IS NULL;
GO
CREATE INDEX [IX_ConsentimientoWhatsApp_Paciente] ON [dbo].[ConsentimientoWhatsApp]([IdPaciente], [IdConsentimientoWhatsApp]);
GO

-- ── 7) Claves foráneas (todas al final, mismo criterio que el script base:
--      ninguna CREATE TABLE de arriba depende del orden de las demás) ──────

ALTER TABLE [dbo].[Agenda] ADD CONSTRAINT [FK_Agenda_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[Cobertura] ADD CONSTRAINT [FK_Cobertura_ObraSocial] FOREIGN KEY([IdObraSocial]) REFERENCES [dbo].[ObraSocial]([IdObraSocial]);
GO
ALTER TABLE [dbo].[Cobertura] ADD CONSTRAINT [FK_Cobertura_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[Consentimiento] ADD CONSTRAINT [FK_Consentimiento_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[Consentimiento] ADD CONSTRAINT [FK_Consentimiento_Tipo] FOREIGN KEY([IdTipoConsentimiento]) REFERENCES [dbo].[TipoConsentimiento]([IdTipoConsentimiento]);
GO
ALTER TABLE [dbo].[Derivacion] ADD CONSTRAINT [FK_Derivacion_Destino] FOREIGN KEY([IdProfesionalDestino]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[Derivacion] ADD CONSTRAINT [FK_Derivacion_Origen] FOREIGN KEY([IdProfesionalOrigen]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[Derivacion] ADD CONSTRAINT [FK_Derivacion_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[EventoCrisis] ADD CONSTRAINT [FK_Evento_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[Evolucion] ADD CONSTRAINT [FK_Evolucion_Historia] FOREIGN KEY([IdHistoria]) REFERENCES [dbo].[HistoriaClinica]([IdHistoria]);
GO
ALTER TABLE [dbo].[Evolucion] ADD CONSTRAINT [FK_Evolucion_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[Evolucion] ADD CONSTRAINT [FK_Evolucion_Turno] FOREIGN KEY([IdTurno]) REFERENCES [dbo].[Turno]([IdTurno]);
GO
ALTER TABLE [dbo].[HistoriaClinica] ADD CONSTRAINT [FK_Historia_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[InteraccionIA] ADD CONSTRAINT [FK_InteraccionIA_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[LogAuditoria] ADD CONSTRAINT [FK_LogAuditoria_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO
ALTER TABLE [dbo].[PacienteProfesional] ADD CONSTRAINT [FK_PacProf_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[PacienteProfesional] ADD CONSTRAINT [FK_PacProf_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[PacienteTutor] ADD CONSTRAINT [FK_PT_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[PacienteTutor] ADD CONSTRAINT [FK_PT_Tutor] FOREIGN KEY([IdTutor]) REFERENCES [dbo].[Tutor]([IdTutor]);
GO
ALTER TABLE [dbo].[Profesional] ADD CONSTRAINT [FK_Profesional_Especialidad] FOREIGN KEY([IdEspecialidad]) REFERENCES [dbo].[Especialidad]([IdEspecialidad]);
GO
ALTER TABLE [dbo].[RecuperacionPassword] ADD CONSTRAINT [FK_RecuperacionPassword_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO
ALTER TABLE [dbo].[Turno] ADD CONSTRAINT [FK_Turno_Estado] FOREIGN KEY([IdEstadoTurno]) REFERENCES [dbo].[EstadoTurno]([IdEstadoTurno]);
GO
ALTER TABLE [dbo].[Turno] ADD CONSTRAINT [FK_Turno_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[Turno] ADD CONSTRAINT [FK_Turno_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[UsuarioPaciente] ADD CONSTRAINT [FK_UPa_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[UsuarioPaciente] ADD CONSTRAINT [FK_UPa_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO
ALTER TABLE [dbo].[UsuarioProfesional] ADD CONSTRAINT [FK_UP_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[UsuarioProfesional] ADD CONSTRAINT [FK_UP_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO
ALTER TABLE [dbo].[UsuarioRol] ADD CONSTRAINT [FK_UR_Rol] FOREIGN KEY([IdRol]) REFERENCES [dbo].[Rol]([IdRol]);
GO
ALTER TABLE [dbo].[UsuarioRol] ADD CONSTRAINT [FK_UR_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO

ALTER TABLE [dbo].[InformeAdmision] ADD CONSTRAINT [FK_InformeAdmision_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[Notificacion] ADD CONSTRAINT [FK_Notificacion_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[SesionClinica] ADD CONSTRAINT [FK_SesionClinica_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[SesionClinica] ADD CONSTRAINT [FK_SesionClinica_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[ConsentimientoVideo] ADD CONSTRAINT [FK_ConsentimientoVideo_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO
ALTER TABLE [dbo].[ConsentimientoVideo] ADD CONSTRAINT [FK_ConsentimientoVideo_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[SesionClinica] ADD CONSTRAINT [FK_SesionClinica_ConsentimientoVideo] FOREIGN KEY([IdConsentimientoVideo]) REFERENCES [dbo].[ConsentimientoVideo]([IdConsentimientoVideo]);
GO
ALTER TABLE [dbo].[ConsentimientoWhatsApp] ADD CONSTRAINT [FK_ConsentimientoWhatsApp_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[ConsentimientoWhatsApp] ADD CONSTRAINT [FK_ConsentimientoWhatsApp_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO

ALTER TABLE [dbo].[ProfesionalGrupo] ADD CONSTRAINT [FK_ProfesionalGrupo_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[ProfesionalGrupo] ADD CONSTRAINT [FK_ProfesionalGrupo_Grupo] FOREIGN KEY([IdGrupo]) REFERENCES [dbo].[Grupo]([IdGrupo]);
GO
ALTER TABLE [dbo].[Caso] ADD CONSTRAINT [FK_Caso_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[Caso] ADD CONSTRAINT [FK_Caso_Grupo] FOREIGN KEY([IdGrupoActual]) REFERENCES [dbo].[Grupo]([IdGrupo]);
GO
ALTER TABLE [dbo].[Caso] ADD CONSTRAINT [FK_Caso_Profesional] FOREIGN KEY([IdProfesionalAsignado]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[Caso] ADD CONSTRAINT [FK_Caso_Informe] FOREIGN KEY([IdInforme]) REFERENCES [dbo].[InformeAdmision]([IdInforme]);
GO
ALTER TABLE [dbo].[CasoDerivacion] ADD CONSTRAINT [FK_CasoDerivacion_Caso] FOREIGN KEY([IdCaso]) REFERENCES [dbo].[Caso]([IdCaso]);
GO
ALTER TABLE [dbo].[CasoDerivacion] ADD CONSTRAINT [FK_CasoDerivacion_Grupo] FOREIGN KEY([IdGrupo]) REFERENCES [dbo].[Grupo]([IdGrupo]);
GO
ALTER TABLE [dbo].[CasoDerivacion] ADD CONSTRAINT [FK_CasoDerivacion_Sugerido] FOREIGN KEY([IdProfesionalSugerido]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[CasoDerivacion] ADD CONSTRAINT [FK_CasoDerivacion_Asignado] FOREIGN KEY([IdProfesionalAsignado]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[CasoDerivacion] ADD CONSTRAINT [FK_CasoDerivacion_CreadoPor] FOREIGN KEY([IdCreadoPor]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO
ALTER TABLE [dbo].[ReglaDerivacion] ADD CONSTRAINT [FK_ReglaDerivacion_Especialidad] FOREIGN KEY([IdEspecialidad]) REFERENCES [dbo].[Especialidad]([IdEspecialidad]);
GO
ALTER TABLE [dbo].[ReglaDerivacion] ADD CONSTRAINT [FK_ReglaDerivacion_Grupo] FOREIGN KEY([IdGrupoDestino]) REFERENCES [dbo].[Grupo]([IdGrupo]);
GO
ALTER TABLE [dbo].[HistorialAsignacion] ADD CONSTRAINT [FK_HistorialAsignacion_Caso] FOREIGN KEY([IdCaso]) REFERENCES [dbo].[Caso]([IdCaso]);
GO
ALTER TABLE [dbo].[HistorialAsignacion] ADD CONSTRAINT [FK_HistorialAsignacion_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[HistorialAsignacion] ADD CONSTRAINT [FK_HistorialAsignacion_Usuario] FOREIGN KEY([IdRealizadoPor]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO

ALTER TABLE [dbo].[Conversacion] ADD CONSTRAINT [FK_Conversacion_Paciente] FOREIGN KEY([IdPaciente]) REFERENCES [dbo].[Paciente]([IdPaciente]);
GO
ALTER TABLE [dbo].[Conversacion] ADD CONSTRAINT [FK_Conversacion_ProfesionalAsignado] FOREIGN KEY([IdProfesionalAsignado]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[MensajeChat] ADD CONSTRAINT [FK_MensajeChat_Conversacion] FOREIGN KEY([IdConversacion]) REFERENCES [dbo].[Conversacion]([IdConversacion]);
GO
ALTER TABLE [dbo].[Recordatorio] ADD CONSTRAINT [FK_Recordatorio_Turno] FOREIGN KEY([IdTurno]) REFERENCES [dbo].[Turno]([IdTurno]);
GO
ALTER TABLE [dbo].[NotificacionInterna] ADD CONSTRAINT [FK_NotificacionInterna_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO
ALTER TABLE [dbo].[NotificacionInterna] ADD CONSTRAINT [FK_NotificacionInterna_Turno] FOREIGN KEY([IdTurno]) REFERENCES [dbo].[Turno]([IdTurno]);
GO
ALTER TABLE [dbo].[NotificacionInterna] ADD CONSTRAINT [FK_NotificacionInterna_Conversacion] FOREIGN KEY([IdConversacion]) REFERENCES [dbo].[Conversacion]([IdConversacion]);
GO

ALTER TABLE [dbo].[NotaInternaConversacion] ADD CONSTRAINT [FK_NotaInterna_Conversacion] FOREIGN KEY([IdConversacion]) REFERENCES [dbo].[Conversacion]([IdConversacion]);
GO
ALTER TABLE [dbo].[NotaInternaConversacion] ADD CONSTRAINT [FK_NotaInterna_Usuario] FOREIGN KEY([IdUsuario]) REFERENCES [dbo].[Usuario]([IdUsuario]);
GO
ALTER TABLE [dbo].[ConversacionEtiqueta] ADD CONSTRAINT [FK_ConvEtiqueta_Conversacion] FOREIGN KEY([IdConversacion]) REFERENCES [dbo].[Conversacion]([IdConversacion]);
GO
ALTER TABLE [dbo].[ConversacionEtiqueta] ADD CONSTRAINT [FK_ConvEtiqueta_Etiqueta] FOREIGN KEY([IdEtiqueta]) REFERENCES [dbo].[EtiquetaConversacion]([IdEtiqueta]);
GO
ALTER TABLE [dbo].[RespuestaRapida] ADD CONSTRAINT [FK_RespuestaRapida_Profesional] FOREIGN KEY([IdProfesional]) REFERENCES [dbo].[Profesional]([IdProfesional]);
GO

-- ── 7b) Auditoría inalterable (migración 19): hash encadenado y solo agregar ──
-- Hash de un registro ────────────────────────────────────
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

-- Cada registro nuevo se encadena al anterior ───────────
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

-- Solo agregar ───────────────────────────────────────────
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

-- Verificación de la cadena ──────────────────────────────
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

-- ── 7c) Versión del esquema: lo que este script ya deja aplicado (1 a 19) ────
INSERT INTO [dbo].[VersionEsquema] ([Numero], [Archivo], [Origen]) VALUES
(1, N'add_columnas_faltantes.sql', 'Script'),
(2, N'add_creadopor_turno.sql', 'Script'),
(3, N'add_token_profesional.sql', 'Script'),
(4, N'add_token_recuperacion.sql', 'Script'),
(5, N'add_mensajeria.sql', 'Script'),
(6, N'add_consentimiento_telemedicina.sql', 'Script'),
(7, N'add_lectura_conversacion.sql', 'Script'),
(8, N'alter_logauditoria_idusuario_nullable.sql', 'Script'),
(9, N'add_tablas_sin_migracion_previa.sql', 'Script'),
(10, N'add_grupos_casos_derivacion.sql', 'Script'),
(11, N'add_link_publico_profesional.sql', 'Script'),
(12, N'alter_casoderivacion_motivo_cerrado.sql', 'Script'),
(13, N'add_chats_notas_etiquetas_respuestas.sql', 'Script'),
(14, N'add_conversacion_asignacion.sql', 'Script'),
(15, N'add_consentimiento_video.sql', 'Script'),
(16, N'add_consentimiento_whatsapp.sql', 'Script'),
(17, N'add_chat_personal_entrantes.sql', 'Script'),
(18, N'add_registro_errores_version_esquema.sql', 'Script'),
(19, N'add_auditoria_inalterable.sql', 'Script');
GO

-- ── 7d) Roles, permisos y privilegios (migraciones 20 a 22) ───────────────────
-- Idénticas a migrations/add_permisos_roles_privilegios.sql, add_privilegio_agendas_auditoria_permisos.sql y add_alta_equipo_invitaciones.sql
-- (idempotentes). Una base nueva no tiene usuarios que mapear: la 23 (mapear_usuarios_existentes_a_permisos.sql) queda registrada y el
-- primer Configurador recibe sus módulos en 03-usuario-admin.sql.
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


IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 20)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (20, N'add_permisos_roles_privilegios.sql', 'Script');
GO


-- ── 1) Privilegio Gestión de agendas (solo para CRM) ─────────
IF NOT EXISTS (SELECT 1 FROM Privilegio WHERE Clave = 'gestion_agendas')
    INSERT INTO Privilegio (Clave, Nombre, Descripcion)
    VALUES ('gestion_agendas', N'Gestión de agendas',
            N'Ver y modificar los turnos y los horarios de trabajo de todos los profesionales. No da acceso a historias clínicas.');
GO

INSERT INTO PermisoPrivilegio (IdPermiso, IdPrivilegio)
SELECT p.IdPermiso, pr.IdPrivilegio
FROM Permiso p
JOIN Privilegio pr ON pr.Clave = 'gestion_agendas'
WHERE p.Clave = 'crm'
  AND NOT EXISTS (SELECT 1 FROM PermisoPrivilegio x WHERE x.IdPermiso = p.IdPermiso AND x.IdPrivilegio = pr.IdPrivilegio);
GO

-- ── 2) Auditoría de cambios de permisos ───────────────────────
-- En los tres triggers: @Autor = el usuario de la aplicación que hizo el cambio; si no hay
-- (cambio hecho a mano en la base), @Origen agrega el inicio de sesión de SQL Server.
CREATE OR ALTER TRIGGER TR_UsuarioRol_Auditar ON UsuarioRol AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Autor INT = TRY_CAST(SESSION_CONTEXT(N'IdUsuario') AS INT);
    DECLARE @Espacio NVARCHAR(30) = CASE WHEN @Autor IS NULL THEN N'base' ELSE N'configurador' END;
    DECLARE @Origen NVARCHAR(160) = CASE WHEN @Autor IS NULL THEN N' · hecho directo en la base por ' + ORIGINAL_LOGIN() ELSE N'' END;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Rol asignado', N'Usuario', i.IdUsuario,
           LEFT(N'Rol ' + ISNULL(r.Nombre, CONCAT(N'#', i.IdRol)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM inserted i LEFT JOIN Rol r ON r.IdRol = i.IdRol;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Rol quitado', N'Usuario', d.IdUsuario,
           LEFT(N'Rol ' + ISNULL(r.Nombre, CONCAT(N'#', d.IdRol)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM deleted d LEFT JOIN Rol r ON r.IdRol = d.IdRol;
END;
GO

CREATE OR ALTER TRIGGER TR_UsuarioPermiso_Auditar ON UsuarioPermiso AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Autor INT = TRY_CAST(SESSION_CONTEXT(N'IdUsuario') AS INT);
    DECLARE @Espacio NVARCHAR(30) = CASE WHEN @Autor IS NULL THEN N'base' ELSE N'configurador' END;
    DECLARE @Origen NVARCHAR(160) = CASE WHEN @Autor IS NULL THEN N' · hecho directo en la base por ' + ORIGINAL_LOGIN() ELSE N'' END;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Permiso asignado', N'Usuario', i.IdUsuario,
           LEFT(N'Permiso ' + ISNULL(p.Nombre, CONCAT(N'#', i.IdPermiso)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM inserted i LEFT JOIN Permiso p ON p.IdPermiso = i.IdPermiso;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Permiso quitado', N'Usuario', d.IdUsuario,
           LEFT(N'Permiso ' + ISNULL(p.Nombre, CONCAT(N'#', d.IdPermiso)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM deleted d LEFT JOIN Permiso p ON p.IdPermiso = d.IdPermiso;
END;
GO

CREATE OR ALTER TRIGGER TR_UsuarioPrivilegio_Auditar ON UsuarioPrivilegio AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Autor INT = TRY_CAST(SESSION_CONTEXT(N'IdUsuario') AS INT);
    DECLARE @Espacio NVARCHAR(30) = CASE WHEN @Autor IS NULL THEN N'base' ELSE N'configurador' END;
    DECLARE @Origen NVARCHAR(160) = CASE WHEN @Autor IS NULL THEN N' · hecho directo en la base por ' + ORIGINAL_LOGIN() ELSE N'' END;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Privilegio asignado', N'Usuario', i.IdUsuario,
           LEFT(N'Privilegio ' + ISNULL(pv.Nombre, CONCAT(N'#', i.IdPrivilegio)) + N' en ' + ISNULL(p.Nombre, CONCAT(N'#', i.IdPermiso))
                + @Origen, 500),
           'Administrativa', @Espacio
    FROM inserted i
    LEFT JOIN Permiso p     ON p.IdPermiso = i.IdPermiso
    LEFT JOIN Privilegio pv ON pv.IdPrivilegio = i.IdPrivilegio;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Privilegio quitado', N'Usuario', d.IdUsuario,
           LEFT(N'Privilegio ' + ISNULL(pv.Nombre, CONCAT(N'#', d.IdPrivilegio)) + N' en ' + ISNULL(p.Nombre, CONCAT(N'#', d.IdPermiso))
                + @Origen, 500),
           'Administrativa', @Espacio
    FROM deleted d
    LEFT JOIN Permiso p     ON p.IdPermiso = d.IdPermiso
    LEFT JOIN Privilegio pv ON pv.IdPrivilegio = d.IdPrivilegio;
END;
GO

-- ── 3) Registro de versión ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 21)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (21, N'add_privilegio_agendas_auditoria_permisos.sql', 'Script');
GO



-- ── 1) Teléfono de contacto ───────────────────────────────────
IF COL_LENGTH('dbo.Usuario', 'Telefono') IS NULL
    ALTER TABLE Usuario ADD Telefono NVARCHAR(30) NULL;
GO

-- ── 2) Invitaciones ───────────────────────────────────────────
IF OBJECT_ID('dbo.InvitacionCuenta', 'U') IS NULL
CREATE TABLE InvitacionCuenta (
    IdInvitacion   INT          IDENTITY(1,1) CONSTRAINT PK_InvitacionCuenta PRIMARY KEY,
    IdUsuario      INT          NOT NULL CONSTRAINT FK_InvitacionCuenta_Usuario REFERENCES Usuario(IdUsuario),
    TokenHash      CHAR(64)     NOT NULL CONSTRAINT UQ_InvitacionCuenta_TokenHash UNIQUE,
    Creada         DATETIME2(0) NOT NULL CONSTRAINT DF_InvitacionCuenta_Creada DEFAULT SYSDATETIME(),
    Vence          DATETIME2(0) NOT NULL,
    Usada          DATETIME2(0) NULL,
    Anulada        DATETIME2(0) NULL,
    IdUsuarioCreo  INT          NULL CONSTRAINT FK_InvitacionCuenta_Creo REFERENCES Usuario(IdUsuario),
    CONSTRAINT CK_InvitacionCuenta_Vence CHECK (Vence > Creada)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.InvitacionCuenta') AND name = 'IX_InvitacionCuenta_Usuario')
    CREATE INDEX IX_InvitacionCuenta_Usuario ON InvitacionCuenta(IdUsuario);
GO

-- ── 3) Registro de versión ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 22)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (22, N'add_alta_equipo_invitaciones.sql', 'Script');
GO


INSERT INTO [dbo].[VersionEsquema] ([Numero], [Archivo], [Origen])
SELECT 23, N'mapear_usuarios_existentes_a_permisos.sql', 'Script'
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[VersionEsquema] WHERE [Numero] = 23);
GO

-- ── Migración 24: cambio obligatorio de contraseña (migrations/add_debe_cambiar_password.sql) ──
IF COL_LENGTH('dbo.Usuario', 'DebeCambiarPassword') IS NULL
    ALTER TABLE Usuario ADD DebeCambiarPassword BIT NOT NULL
        CONSTRAINT DF_Usuario_DebeCambiarPassword DEFAULT (0);
GO
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 24)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (24, N'add_debe_cambiar_password.sql', 'Script');
GO

-- ── Migración 25: interruptor de IA por conversación (migrations/add_conversacion_ia.sql). Opcional ──
IF COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NULL
    ALTER TABLE dbo.Conversacion ADD IaActiva BIT NOT NULL
        CONSTRAINT DF_Conversacion_IaActiva DEFAULT (0);
GO
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 25)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (25, N'add_conversacion_ia.sql', 'Script');
GO

-- ── Migración 26: franjas de admisión y quién programó la entrevista (migrations/add_admision_agenda_entrevista.sql) ──
IF COL_LENGTH('dbo.Agenda', 'EsAdmision') IS NULL
    ALTER TABLE dbo.Agenda ADD EsAdmision BIT NOT NULL
        CONSTRAINT DF_Agenda_EsAdmision DEFAULT (0);
IF COL_LENGTH('dbo.InformeAdmision', 'IdProfesionalEntrevista') IS NULL
    ALTER TABLE dbo.InformeAdmision ADD IdProfesionalEntrevista INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InformeAdmision_ProfesionalEntrevista')
    ALTER TABLE dbo.InformeAdmision ADD CONSTRAINT FK_InformeAdmision_ProfesionalEntrevista
        FOREIGN KEY (IdProfesionalEntrevista) REFERENCES dbo.Profesional (IdProfesional);
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 26)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (26, N'add_admision_agenda_entrevista.sql', 'Script');
GO

-- ── Migración 27: fecha de inicio y de fin de cada franja de la agenda (migrations/add_vigencia_agenda.sql) ──
IF COL_LENGTH('dbo.Agenda', 'FechaInicio') IS NULL
    ALTER TABLE dbo.Agenda ADD FechaInicio DATE NULL;
IF COL_LENGTH('dbo.Agenda', 'FechaFin') IS NULL
    ALTER TABLE dbo.Agenda ADD FechaFin DATE NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Agenda_Vigencia')
    ALTER TABLE dbo.Agenda ADD CONSTRAINT CK_Agenda_Vigencia
        CHECK (FechaInicio IS NULL OR FechaFin IS NULL OR FechaFin >= FechaInicio);
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 27)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (27, N'add_vigencia_agenda.sql', 'Script');
GO

-- ── 8) Dato obligatorio — RN-02, consentimiento de telemedicina ─────────────
-- El código lo busca por Descripcion exacta (Consentimiento:TipoRequerido en
-- appsettings.json). Sin esta fila, nadie puede firmar el consentimiento de
-- telemedicina y los turnos que lo exigen quedan bloqueados.
INSERT INTO [dbo].[TipoConsentimiento] ([Descripcion], [Estado])
VALUES (N'Consentimiento Informado - Atención por Telemedicina', 'Activo');
GO

/* ============================================================================
   ── 9) OPCIONAL — contenido de arranque, no datos de negocio ────────────────
   Nada de esto es necesario para que la app funcione. Son los mismos textos
   genéricos con los que ya cuenta el repo (antes vivían en PreguntaFrecuente
   vacía / un archivo estático de plantillas) — se puede correr tal cual, con
   otro texto, o no correrlo. Nada de datos de paciente ni de profesional.
   ============================================================================ */

-- Preguntas frecuentes del chatbot (se muestran si alguien pregunta "ayuda").
INSERT INTO [dbo].[PreguntaFrecuente] ([Pregunta], [Respuesta], [Orden]) VALUES
(N'¿Cómo es la modalidad de atención?',
 N'La atención es virtual, por videollamada desde la plataforma YoSoy. Solo necesitás un dispositivo con cámara, micrófono y conexión a internet.', 1),
(N'¿Cómo pido un turno?',
 N'Podés pedirlo por este chat eligiendo la opción 1 del menú, o desde la web ingresando con tu usuario. El turno queda pendiente hasta que tu profesional lo confirme.', 2),
(N'¿Cómo cancelo un turno?',
 N'Podés cancelarlo por este chat con la opción 3 del menú o desde la web. Las cancelaciones se aceptan hasta 24 horas antes del turno.', 3),
(N'¿Cómo ingreso a la videollamada?',
 N'El día del turno ingresá a la web de YoSoy con tu usuario. Cuando tu profesional habilite la sala vas a ver el acceso en tu panel y un aviso en la campanita de notificaciones.', 4);
GO

-- Respuestas rápidas globales del composer de chats (IdProfesional NULL = para todos).
INSERT INTO [dbo].[RespuestaRapida] ([Titulo], [Texto], [IdProfesional]) VALUES
(N'Saludo', N'Hola, te escribe el equipo de YoSoy. ¿En qué te podemos ayudar?', NULL),
(N'Ya lo estamos viendo', N'Gracias por escribirnos. Ya estamos viendo tu consulta y en un rato te respondemos.', NULL),
(N'Pedir el DNI', N'Para buscar tus datos, ¿nos pasás tu DNI?', NULL),
(N'Volver al asistente', N'Si querés pedir, ver o cancelar un turno, escribí "menu" y el asistente te guía paso a paso.', NULL),
(N'Consentimiento pendiente', N'Para confirmar tus turnos necesitamos que firmes el consentimiento informado desde tu panel de YoSoy, en la sección Consentimiento.', NULL),
(N'Despedida', N'Gracias por comunicarte con YoSoy. Si necesitás algo más, escribinos cuando quieras.', NULL);
GO
