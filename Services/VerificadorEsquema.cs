using Microsoft.Data.SqlClient;

// Avisa con claridad si a la base le falta una migración, en vez de un error 500 suelto en
// la primera pantalla que use la tabla que falta (lo que pasó con los consentimientos el
// 12/10: faltaban las migraciones 6 y 15).
//
// Cada migración tiene un "marcador": una condición SQL que solo se cumple si la migración
// se aplicó (una tabla, una columna o una fila que crea). Se mira el marcador y no solo la
// tabla VersionEsquema porque las migraciones 1 a 15 son anteriores a esa tabla. Los
// marcadores 1 a 15 son los mismos que usa la migración 16 para registrarlas: si se cambia
// uno acá, cambiarlo también en migrations/add_permisos_especialidades_errores.sql.
//
// Una migración nueva se agrega al final de Migraciones con su marcador.
public record MigracionConocida(int Numero, string Archivo, string Marcador);

public record EstadoEsquema(bool Verificado, IReadOnlyList<MigracionConocida> Faltantes, string? ErrorConexion)
{
    public bool Desactualizado => Verificado && Faltantes.Count > 0;

    public string Mensaje => !Verificado
        ? "No se pudo verificar la base de datos: " + ErrorConexion
        : Faltantes.Count == 0
            ? $"Base de datos al día (migración {VerificadorEsquema.VersionRequerida})."
            : "La base de datos no está actualizada. " +
              (Faltantes.Count == 1 ? "Falta la migración " : "Faltan las migraciones ") +
              string.Join(", ", Faltantes.Select(m => $"{m.Numero} ({m.Archivo})")) +
              ". Correlas en orden desde la carpeta migrations (ver README) y volvé a intentar.";
}

public interface IInspectorEsquema
{
    // Una respuesta por condición, en el mismo orden. Lanza si no se puede conectar.
    IReadOnlyList<bool> Evaluar(IReadOnlyList<string> condiciones);
}

public class InspectorEsquemaSql : IInspectorEsquema
{
    public IReadOnlyList<bool> Evaluar(IReadOnlyList<string> condiciones)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var resultado = new List<bool>(condiciones.Count);
        foreach (var condicion in condiciones)
        {
            // Las condiciones son constantes de este archivo, nunca vienen del usuario.
            using var cmd = new SqlCommand($"SELECT CASE WHEN {condicion} THEN 1 ELSE 0 END", con);
            resultado.Add(Convert.ToInt32(cmd.ExecuteScalar()) == 1);
        }
        return resultado;
    }
}

public class VerificadorEsquema
{
    public const int VersionRequerida = 19;

    public static readonly IReadOnlyList<MigracionConocida> Migraciones = new MigracionConocida[]
    {
        new(1,  "add_columnas_faltantes.sql",
            "COL_LENGTH('dbo.Turno', 'CodigoSala') IS NOT NULL AND COL_LENGTH('dbo.PacienteProfesional', 'EstadoAsignacion') IS NOT NULL"),
        new(2,  "add_creadopor_turno.sql",
            "COL_LENGTH('dbo.Turno', 'CreadoPor') IS NOT NULL"),
        new(3,  "add_token_profesional.sql",
            "COL_LENGTH('dbo.Profesional', 'Token') IS NOT NULL"),
        new(4,  "add_token_recuperacion.sql",
            "COL_LENGTH('dbo.RecuperacionPassword', 'TokenExpiracion') IS NOT NULL"),
        new(5,  "add_mensajeria.sql",
            "OBJECT_ID('dbo.Conversacion', 'U') IS NOT NULL AND OBJECT_ID('dbo.PreguntaFrecuente', 'U') IS NOT NULL"),
        new(6,  "add_consentimiento_telemedicina.sql",
            "EXISTS (SELECT 1 FROM TipoConsentimiento WHERE Descripcion = N'Consentimiento Informado - Atención por Telemedicina')"),
        new(7,  "add_lectura_conversacion.sql",
            "COL_LENGTH('dbo.Conversacion', 'IdUltimoMensajeLeido') IS NOT NULL"),
        new(8,  "alter_logauditoria_idusuario_nullable.sql",
            "EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.LogAuditoria') AND name = 'IdUsuario' AND is_nullable = 1)"),
        new(9,  "add_tablas_sin_migracion_previa.sql",
            "OBJECT_ID('dbo.InformeAdmision', 'U') IS NOT NULL AND OBJECT_ID('dbo.Notificacion', 'U') IS NOT NULL AND OBJECT_ID('dbo.SesionClinica', 'U') IS NOT NULL"),
        new(10, "add_grupos_casos_derivacion.sql",
            "OBJECT_ID('dbo.Caso', 'U') IS NOT NULL AND OBJECT_ID('dbo.ReglaDerivacion', 'U') IS NOT NULL"),
        new(11, "add_link_publico_profesional.sql",
            "COL_LENGTH('dbo.Profesional', 'Slug') IS NOT NULL"),
        new(12, "alter_casoderivacion_motivo_cerrado.sql",
            "COL_LENGTH('dbo.CasoDerivacion', 'Nota') IS NOT NULL"),
        new(13, "add_chats_notas_etiquetas_respuestas.sql",
            "OBJECT_ID('dbo.RespuestaRapida', 'U') IS NOT NULL"),
        new(14, "add_conversacion_asignacion.sql",
            "COL_LENGTH('dbo.Conversacion', 'IdProfesionalAsignado') IS NOT NULL"),
        new(15, "add_atencion_consentimientos_ia_evolucion.sql",
            "OBJECT_ID('dbo.SalaSesion', 'U') IS NOT NULL AND OBJECT_ID('dbo.TR_Evolucion_Inmutable', 'TR') IS NOT NULL"),
        new(16, "add_permisos_especialidades_errores.sql",
            "OBJECT_ID('dbo.VersionEsquema', 'U') IS NOT NULL AND OBJECT_ID('dbo.UsuarioPrivilegio', 'U') IS NOT NULL AND OBJECT_ID('dbo.LogError', 'U') IS NOT NULL"),
        new(17, "add_auditoria_inalterable.sql",
            "COL_LENGTH('dbo.LogAuditoria', 'Categoria') IS NOT NULL AND OBJECT_ID('dbo.TR_LogAuditoria_SoloAgregar', 'TR') IS NOT NULL AND OBJECT_ID('dbo.TR_LogAuditoria_Encadenar', 'TR') IS NOT NULL"),
        new(18, "add_privilegio_agendas_auditoria_permisos.sql",
            "EXISTS (SELECT 1 FROM Privilegio WHERE Clave = 'gestion_agendas') AND OBJECT_ID('dbo.TR_UsuarioPermiso_Auditar', 'TR') IS NOT NULL"),
        new(19, "add_alta_equipo_invitaciones.sql",
            "OBJECT_ID('dbo.InvitacionCuenta', 'U') IS NOT NULL AND COL_LENGTH('dbo.Usuario', 'Telefono') IS NOT NULL"),
    };

    // Mientras la base esté desactualizada (o no se pueda verificar), se vuelve a mirar cada
    // tanto: así, después de correr las migraciones, la API se recupera sin reiniciarla.
    public static readonly TimeSpan IntervaloReintento = TimeSpan.FromSeconds(30);

    private readonly IInspectorEsquema _inspector;
    private readonly TimeProvider _reloj;
    private readonly object _candado = new();
    private EstadoEsquema? _ultimo;
    private DateTimeOffset _cuando;

    public VerificadorEsquema(IInspectorEsquema inspector, TimeProvider reloj)
    {
        _inspector = inspector;
        _reloj = reloj;
    }

    public EstadoEsquema Estado()
    {
        lock (_candado)
        {
            if (_ultimo is { Verificado: true, Faltantes.Count: 0 }) return _ultimo;   // al día: no se vuelve a mirar
            if (_ultimo is not null && _reloj.GetUtcNow() - _cuando < IntervaloReintento) return _ultimo;
            _ultimo = Evaluar();
            _cuando = _reloj.GetUtcNow();
            return _ultimo;
        }
    }

    public EstadoEsquema Evaluar()
    {
        try
        {
            var cumple = _inspector.Evaluar(Migraciones.Select(m => m.Marcador).ToList());
            var faltantes = Migraciones.Where((m, i) => !cumple[i]).ToList();
            return new EstadoEsquema(true, faltantes, null);
        }
        catch (Exception ex)
        {
            return new EstadoEsquema(false, Array.Empty<MigracionConocida>(), ex.Message);
        }
    }
}

public static class VerificacionEsquemaExtensions
{
    // Va DESPUÉS de UseAuthorization: un pedido sin permiso sigue recibiendo su 401/403 sin
    // tocar la base. Si la base no responde, no se bloquea nada (cada endpoint falla como
    // antes y el error queda registrado con su código); solo se corta cuando la base SÍ
    // responde y le falta una migración. Se apaga con "Esquema:Verificar": false.
    public static WebApplication UseVerificacionEsquema(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Esquema:Verificar", true)) return app;
        var verificador = app.Services.GetRequiredService<VerificadorEsquema>();

        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/pantallas"))
            {
                await next();
                return;
            }
            var estado = verificador.Estado();
            if (estado.Desactualizado)
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    codigo = "ESQUEMA_DESACTUALIZADO",
                    mensaje = estado.Mensaje,
                    faltantes = estado.Faltantes.Select(m => new { numero = m.Numero, archivo = m.Archivo })
                });
                return;
            }
            await next();
        });

        // Al arrancar, en la consola de "dotnet run": si la base está al día o qué le falta.
        // En segundo plano para no demorar el inicio si la base tarda en responder.
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Esquema");
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(() =>
        {
            var estado = verificador.Estado();
            if (estado.Desactualizado) log.LogError("{Mensaje}", estado.Mensaje);
            else if (!estado.Verificado) log.LogWarning("{Mensaje}", estado.Mensaje);
            else log.LogInformation("{Mensaje}", estado.Mensaje);
        }));
        return app;
    }
}
