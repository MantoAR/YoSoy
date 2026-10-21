using Microsoft.Data.SqlClient;

// Acceso por módulo (etapa 2 de la propuesta de roles y permisos).
//
// Cada rol es un módulo del sistema (Profesional, CRM, Facturación, Configurador). Dentro de
// un módulo, el usuario tiene uno o más permisos (en Profesional: profesional y/o auditor) y
// privilegios sobre esos permisos (admision, gestion_agendas). Todo sale de las tablas de la
// migración 20 (UsuarioRol, UsuarioPermiso, UsuarioPrivilegio), que asigna el Configurador.
//
// Al ingresar, el usuario elige con qué módulo trabajar (solo si tiene más de uno) y recibe
// un token de ESE módulo: con un claim de rol por cada permiso que tiene en él y uno de
// privilegio por cada privilegio. Así, trabajando como Profesional, quien también es
// Configurador no tiene abierta la administración (ISO 27002 5.15 y 8.2).

public static class Modulos
{
    public const string Profesional = "profesional";
    public const string Crm = "crm";
    public const string Facturacion = "facturacion";
    public const string Configurador = "configurador";
    // Vista única (PR 3 del rediseño): NO es un módulo de la base (no existe en Rol.Clave ni se asigna desde Permisos). Es la unión de
    // Configurador y CRM para quien tiene AMBOS permisos: una sola sesión con una sola vista. Se arma siempre en el servidor, desde la base.
    public const string Administracion = "administracion";

    // Orden en que se muestran en el selector.
    public static readonly IReadOnlyList<string> Todos = new[] { Profesional, Crm, Facturacion, Configurador };

    public static string Nombre(string modulo) => modulo switch
    {
        Profesional => "Profesional",
        Crm => "CRM y atención al cliente",
        Facturacion => "Administración de facturación y pagos",
        Configurador => "Configurador",
        Administracion => "Administración",
        _ => modulo
    };

    public static string Descripcion(string modulo) => modulo switch
    {
        Profesional => "Pacientes, agenda, sala de video e historia clínica",
        Crm => "Conversaciones y atención al cliente",
        Facturacion => "Facturación, pagos y liquidaciones",
        Configurador => "Usuarios, permisos y configuración de la organización",
        Administracion => "Chats y casos, usuarios, permisos, configuración y auditoría",
        _ => ""
    };
}

public static class Privilegios
{
    public const string Admision = "admision";
    public const string GestionAgendas = "gestion_agendas";
}

public record PermisoAsignado(string Modulo, string Permiso);
public record PrivilegioAsignado(string Permiso, string Privilegio);

public record AccesosUsuario(int IdUsuario, IReadOnlyList<PermisoAsignado> Permisos, IReadOnlyList<PrivilegioAsignado> Privilegios)
{
    public static readonly AccesosUsuario Ninguno = new(0, Array.Empty<PermisoAsignado>(), Array.Empty<PrivilegioAsignado>());

    // Módulos habilitados, en el orden de Modulos.Todos.
    public IReadOnlyList<string> Modulos =>
        global::Modulos.Todos.Where(m => Permisos.Any(p => p.Modulo == m)).ToList();

    // Tiene la unión (Administración) quien tiene Configurador Y CRM, los dos de verdad en la base: con uno solo no hay vista única.
    public bool TieneAdministracion => TieneModulo(global::Modulos.Configurador) && TieneModulo(global::Modulos.Crm);

    // Los módulos tal como se ofrecen y se emiten: la unión Configurador + CRM se muestra como UNA opción ("Administración") en el lugar del CRM;
    // los demás (Profesional, Facturación) siguen aparte. Quien tiene solo CRM o solo Configurador los ve como siempre. Si queda un solo
    // módulo, no hay selector.
    public IReadOnlyList<string> ModulosVisibles
    {
        get
        {
            if (!TieneAdministracion) return Modulos;
            var visibles = new List<string>();
            foreach (var m in Modulos)
            {
                if (m == global::Modulos.Configurador) continue;
                visibles.Add(m == global::Modulos.Crm ? global::Modulos.Administracion : m);
            }
            return visibles;
        }
    }

    public bool TieneModulo(string modulo) =>
        modulo == global::Modulos.Administracion ? TieneAdministracion : Permisos.Any(p => p.Modulo == modulo);

    // Los permisos del usuario dentro de un módulo; van al token como claims de rol. Los de Administración son los de Configurador más los de
    // CRM y nada más: ningún permiso de Profesional, de auditor ni de Facturación.
    public IReadOnlyList<string> PermisosDe(string modulo) =>
        Permisos.Where(p => modulo == global::Modulos.Administracion
                ? p.Modulo is global::Modulos.Configurador or global::Modulos.Crm
                : p.Modulo == modulo)
            .Select(p => p.Permiso).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

    // Los privilegios que el usuario tiene sobre alguno de sus permisos de ese módulo.
    public IReadOnlyList<string> PrivilegiosDe(string modulo)
    {
        var permisos = PermisosDe(modulo);
        return Privilegios.Where(p => permisos.Contains(p.Permiso)).Select(p => p.Privilegio).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}

public interface IAccesosRepositorio
{
    AccesosUsuario Obtener(int idUsuario);
}

public class AccesosDAO : IAccesosRepositorio
{
    public AccesosUsuario Obtener(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();

        // Solo roles y permisos activos. Un rol sin Clave (filas viejas de la pantalla Roles)
        // no da acceso a ningún módulo.
        var permisos = new List<PermisoAsignado>();
        using (var cmd = new SqlCommand(@"
            SELECT r.Clave AS Modulo, p.Clave AS Permiso
            FROM UsuarioPermiso up
            JOIN Permiso p ON p.IdPermiso = up.IdPermiso AND p.Estado = 'Activo'
            JOIN Rol r     ON r.IdRol = p.IdRol AND r.Estado = 'Activo' AND r.Clave IS NOT NULL
            WHERE up.IdUsuario = @Id", con))
        {
            cmd.Parameters.AddWithValue("@Id", idUsuario);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) permisos.Add(new PermisoAsignado(rd.GetString(0), rd.GetString(1)));
        }

        var privilegios = new List<PrivilegioAsignado>();
        using (var cmd = new SqlCommand(@"
            SELECT p.Clave AS Permiso, pv.Clave AS Privilegio
            FROM UsuarioPrivilegio up
            JOIN Permiso p     ON p.IdPermiso = up.IdPermiso AND p.Estado = 'Activo'
            JOIN Privilegio pv ON pv.IdPrivilegio = up.IdPrivilegio AND pv.Estado = 'Activo'
            WHERE up.IdUsuario = @Id", con))
        {
            cmd.Parameters.AddWithValue("@Id", idUsuario);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) privilegios.Add(new PrivilegioAsignado(rd.GetString(0), rd.GetString(1)));
        }

        return new AccesosUsuario(idUsuario, permisos, privilegios);
    }
}

// Lo que necesitan el login y POST /sesion/modulo para armar la respuesta y el token.
public record SesionModulo(string Modulo, IReadOnlyList<string> Permisos, IReadOnlyList<string> Privilegios, int? IdProfesional)
{
    // Lo que ya devolvía POST /login en "rol", para no romper pantallas: el módulo
    // Profesional sigue diciendo "profesional" y el Configurador, "configurador".
    public string RolLegado => Modulo;
}

public static class ResolverSesion
{
    public enum Resultado { SinPermisos, ModuloNoHabilitado, FaltaFichaProfesional, Ok }

    // idProfesional: el de UsuarioProfesional, si el usuario tiene ficha.
    public static (Resultado resultado, SesionModulo? sesion) Para(AccesosUsuario accesos, string modulo, int? idProfesional)
    {
        if (accesos.Modulos.Count == 0) return (Resultado.SinPermisos, null);
        // Quien tiene la unión no obtiene un token de Configurador solo ni de CRM solo: pedir cualquiera de los dos (la pestaña del ingreso o
        // POST /sesion/modulo) da la sesión de Administración. Es la única forma de entrar con esos dos permisos.
        if (accesos.TieneAdministracion && modulo is global::Modulos.Configurador or global::Modulos.Crm) modulo = global::Modulos.Administracion;
        if (!accesos.TieneModulo(modulo)) return (Resultado.ModuloNoHabilitado, null);
        // El trigger RN-17 ya lo impide en la base; se vuelve a mirar por si la ficha se dio
        // de baja después de asignar el permiso.
        if (modulo == Modulos.Profesional && idProfesional is null) return (Resultado.FaltaFichaProfesional, null);
        // El auditor tiene todo lo del profesional (definido el 15/10): el token lleva los dos
        // roles, así lo que exige Roles.Profesional le funciona sin tocar cada endpoint.
        var permisos = accesos.PermisosDe(modulo).ToList();
        if (permisos.Contains(Roles.Auditor) && !permisos.Contains(Roles.Profesional)) permisos.Add(Roles.Profesional);
        permisos.Sort(StringComparer.Ordinal);
        return (Resultado.Ok, new SesionModulo(modulo, permisos, accesos.PrivilegiosDe(modulo),
            modulo == Modulos.Profesional ? idProfesional : null));
    }

    public const string MensajeSinPermisos =
        "Tu usuario todavía no tiene permisos asignados. Comunicate con el administrador de tu organización.";
}

public record SeleccionModuloRequest(string? Modulo);
