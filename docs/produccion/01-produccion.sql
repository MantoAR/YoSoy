/* ============================================================================
   01-produccion.sql — Esquema completo de YoSoy para un servidor nuevo.

   Qué es: la base [yosoy] tal como la necesita el código hoy — el script base
   de la rama `DataBase` más las 17 migraciones de migrations/, consolidados
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
    [FechaHora] [datetime] NOT NULL CONSTRAINT [DF_LogAuditoria_FechaHora] DEFAULT (getdate())
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
