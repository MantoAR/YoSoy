using Microsoft.Data.SqlClient;

// Etapa 3 de roles y permisos: el Configurador asigna, a cada usuario del equipo, módulos,
// permisos y privilegios (Configurador › Usuarios › Permisos). Antes solo se podía con un
// script en la base.
//
// Reglas (definidas el 15 y 16/10):
//   - Profesional y Profesional auditor exigen ficha de profesional con matrícula (RN-17;
//     la base también lo impide con TR_UsuarioPermiso_RequiereProfesional).
//   - Un privilegio solo se asigna sobre un permiso que lo admite (PermisoPrivilegio):
//     Admisión sobre Profesional o Profesional auditor; Gestión de agendas sobre CRM.
//   - Las cuentas de paciente no llevan permisos del equipo.
//   - Nadie se quita a sí mismo el permiso de Configurador (para no dejar la organización sin
//     quien administre por un clic).
//   - Facturación todavía no tiene pantalla (etapa 7): no se ofrece y, si alguien lo tiene
//     asignado por script, se conserva tal cual.
// Los cambios rigen desde el próximo ingreso o cambio de módulo de esa persona: el token que
// ya tiene no se modifica.
// Cada alta o baja queda en la auditoría administrativa, la registra la base (migración 18).

public record PrivilegioCatalogo(string Clave, string Nombre, string? Descripcion);
public record PermisoCatalogo(string Modulo, string Clave, string Nombre, IReadOnlyList<PrivilegioCatalogo> Privilegios);
public record PrivilegioPedido(string Permiso, string Privilegio);
public record AccesosEdicionRequest(List<string>? Permisos, List<PrivilegioPedido>? Privilegios);

public static class AccesosEdicion
{
    // Módulos que se editan desde la pantalla, en este orden.
    public static readonly IReadOnlyList<string> ModulosEditables = new[] { Modulos.Profesional, Modulos.Crm, Modulos.Configurador };

    public record Situacion(bool EsPaciente, bool TieneFichaProfesional, bool EsUnoMismo, bool TeniaConfigurador);

    /// <summary>Devuelve el motivo del rechazo, o null si el pedido es válido. Sin base de datos.</summary>
    public static string? Validar(IReadOnlyList<PermisoCatalogo> catalogo, AccesosEdicionRequest pedido, Situacion s)
    {
        var permisos = (pedido.Permisos ?? new()).Distinct().ToList();
        var privilegios = (pedido.Privilegios ?? new()).Distinct().ToList();

        if (s.EsPaciente && (permisos.Count > 0 || privilegios.Count > 0))
            return "Las cuentas de paciente no llevan permisos del equipo.";

        foreach (var clave in permisos)
        {
            var p = catalogo.FirstOrDefault(x => x.Clave == clave);
            if (p is null || !ModulosEditables.Contains(p.Modulo)) return $"El permiso \"{clave}\" no existe o no se asigna desde esta pantalla.";
        }

        if (!s.TieneFichaProfesional && permisos.Any(p => p is Roles.Profesional or Roles.Auditor))
            return "Para darle Profesional o Profesional auditor, la cuenta tiene que estar vinculada a una ficha de profesional con matrícula (RN-17).";

        foreach (var pv in privilegios)
        {
            if (!permisos.Contains(pv.Permiso))
                return $"El privilegio \"{pv.Privilegio}\" va sobre un permiso que el usuario no tiene.";
            var permiso = catalogo.First(x => x.Clave == pv.Permiso);
            if (!permiso.Privilegios.Any(x => x.Clave == pv.Privilegio))
                return $"El permiso {permiso.Nombre} no admite el privilegio \"{pv.Privilegio}\".";
        }

        if (s.EsUnoMismo && s.TeniaConfigurador && !permisos.Contains(Roles.Configurador))
            return "No podés quitarte tu propio permiso de Configurador. Pedíselo a otro Configurador.";

        return null;
    }
}

public class AccesosEdicionDAO
{
    private record PermisoFila(int IdPermiso, int IdRol, string Clave, string Nombre, string Modulo);

    public IReadOnlyList<PermisoCatalogo> Catalogo()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var permisos = LeerPermisos(con);
        var privilegios = new List<(string Permiso, PrivilegioCatalogo Privilegio)>();
        using (var cmd = new SqlCommand(@"
            SELECT p.Clave, pv.Clave, pv.Nombre, pv.Descripcion
            FROM PermisoPrivilegio pp
            JOIN Permiso p     ON p.IdPermiso = pp.IdPermiso AND p.Estado = 'Activo'
            JOIN Privilegio pv ON pv.IdPrivilegio = pp.IdPrivilegio AND pv.Estado = 'Activo'
            ORDER BY pv.Nombre", con))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                privilegios.Add((rd.GetString(0), new PrivilegioCatalogo(rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3))));

        return permisos
            .OrderBy(p => Modulos.Todos.ToList().IndexOf(p.Modulo)).ThenBy(p => p.IdPermiso)
            .Select(p => new PermisoCatalogo(p.Modulo, p.Clave, p.Nombre,
                privilegios.Where(x => x.Permiso == p.Clave).Select(x => x.Privilegio).ToList()))
            .ToList();
    }

    public AccesosEdicion.Situacion? LeerSituacion(int idUsuario, int idAutor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT
                CASE WHEN EXISTS (SELECT 1 FROM UsuarioPaciente WHERE IdUsuario = u.IdUsuario) THEN 1 ELSE 0 END,
                CASE WHEN EXISTS (SELECT 1 FROM UsuarioProfesional up JOIN Profesional pr ON pr.IdProfesional = up.IdProfesional
                                  WHERE up.IdUsuario = u.IdUsuario AND LTRIM(RTRIM(ISNULL(pr.Matricula, ''))) <> '') THEN 1 ELSE 0 END,
                CASE WHEN EXISTS (SELECT 1 FROM UsuarioPermiso x JOIN Permiso p ON p.IdPermiso = x.IdPermiso
                                  WHERE x.IdUsuario = u.IdUsuario AND p.Clave = 'configurador') THEN 1 ELSE 0 END
            FROM Usuario u WHERE u.IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new AccesosEdicion.Situacion(rd.GetInt32(0) == 1, rd.GetInt32(1) == 1, idUsuario == idAutor, rd.GetInt32(2) == 1);
    }

    public (List<string> Permisos, List<PrivilegioPedido> Privilegios) Asignados(int idUsuario)
    {
        var a = new AccesosDAO().Obtener(idUsuario);
        return (a.Permisos.Select(p => p.Permiso).Distinct().ToList(),
                a.Privilegios.Select(p => new PrivilegioPedido(p.Permiso, p.Privilegio)).Distinct().ToList());
    }

    /// <summary>Deja al usuario exactamente con lo pedido en los módulos editables, en una transacción.</summary>
    public void Guardar(int idUsuario, AccesosEdicionRequest pedido, int idAutor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            GuardarEn(con, tx, idUsuario, pedido, idAutor);
            Ejecutar(con, tx, "EXEC sp_set_session_context @key = N'IdUsuario', @value = NULL;");
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Lo mismo que Guardar, dentro de una transacción ya abierta (la usa el alta de usuarios
    /// del equipo, que crea la cuenta, la ficha y los permisos todo junto o nada).
    /// </summary>
    public void GuardarEn(SqlConnection con, SqlTransaction tx, int idUsuario, AccesosEdicionRequest pedido, int idAutor)
    {
        var permisosPedidos = (pedido.Permisos ?? new()).Distinct().ToHashSet();
        var privilegiosPedidos = (pedido.Privilegios ?? new()).Distinct().ToHashSet();
        {
            // Quién hace el cambio: lo leen los triggers de auditoría (migración 18).
            Ejecutar(con, tx, "EXEC sp_set_session_context @key = N'IdUsuario', @value = @Autor;", ("@Autor", idAutor));

            var catalogo = LeerPermisos(con, tx).Where(p => AccesosEdicion.ModulosEditables.Contains(p.Modulo)).ToList();
            var idsPrivilegio = new Dictionary<string, int>();
            using (var cmd = new SqlCommand("SELECT Clave, IdPrivilegio FROM Privilegio", con, tx))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) idsPrivilegio[rd.GetString(0)] = rd.GetInt32(1);

            // 1) Privilegios que sobran (en permisos editables).
            foreach (var p in catalogo)
                foreach (var (clave, idPriv) in idsPrivilegio)
                    if (!privilegiosPedidos.Contains(new PrivilegioPedido(p.Clave, clave)))
                        Ejecutar(con, tx, "DELETE FROM UsuarioPrivilegio WHERE IdUsuario = @U AND IdPermiso = @P AND IdPrivilegio = @V;",
                            ("@U", idUsuario), ("@P", p.IdPermiso), ("@V", idPriv));

            // 2) Permisos que sobran, y el rol (módulo) si se quedó sin permisos.
            foreach (var p in catalogo.Where(p => !permisosPedidos.Contains(p.Clave)))
                Ejecutar(con, tx, "DELETE FROM UsuarioPermiso WHERE IdUsuario = @U AND IdPermiso = @P;", ("@U", idUsuario), ("@P", p.IdPermiso));
            foreach (var rol in catalogo.GroupBy(p => p.IdRol).Where(g => !g.Any(p => permisosPedidos.Contains(p.Clave))))
                Ejecutar(con, tx, "DELETE FROM UsuarioRol WHERE IdUsuario = @U AND IdRol = @R;", ("@U", idUsuario), ("@R", rol.Key));

            // 3) Lo que falta: rol, permiso, privilegio (en ese orden, por las claves foráneas).
            foreach (var p in catalogo.Where(p => permisosPedidos.Contains(p.Clave)))
            {
                Ejecutar(con, tx, @"IF NOT EXISTS (SELECT 1 FROM UsuarioRol WHERE IdUsuario = @U AND IdRol = @R)
                                        INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (@U, @R);", ("@U", idUsuario), ("@R", p.IdRol));
                Ejecutar(con, tx, @"IF NOT EXISTS (SELECT 1 FROM UsuarioPermiso WHERE IdUsuario = @U AND IdPermiso = @P)
                                        INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso, IdUsuarioAsigno) VALUES (@U, @R, @P, @A);",
                    ("@U", idUsuario), ("@R", p.IdRol), ("@P", p.IdPermiso), ("@A", idAutor));
            }
            foreach (var pv in privilegiosPedidos)
            {
                var permiso = catalogo.First(p => p.Clave == pv.Permiso);
                Ejecutar(con, tx, @"IF NOT EXISTS (SELECT 1 FROM UsuarioPrivilegio WHERE IdUsuario = @U AND IdPermiso = @P AND IdPrivilegio = @V)
                                        INSERT INTO UsuarioPrivilegio (IdUsuario, IdPermiso, IdPrivilegio, IdUsuarioAsigno) VALUES (@U, @P, @V, @A);",
                    ("@U", idUsuario), ("@P", permiso.IdPermiso), ("@V", idsPrivilegio[pv.Privilegio]), ("@A", idAutor));
            }
        }
    }

    private static List<PermisoFila> LeerPermisos(SqlConnection con, SqlTransaction? tx = null)
    {
        var lista = new List<PermisoFila>();
        using var cmd = new SqlCommand(@"
            SELECT p.IdPermiso, p.IdRol, p.Clave, p.Nombre, r.Clave
            FROM Permiso p
            JOIN Rol r ON r.IdRol = p.IdRol AND r.Estado = 'Activo' AND r.Clave IS NOT NULL
            WHERE p.Estado = 'Activo'", con, tx);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) lista.Add(new PermisoFila(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4)));
        return lista;
    }

    internal static void Ejecutar(SqlConnection con, SqlTransaction tx, string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var cmd = new SqlCommand(sql, con, tx);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        cmd.ExecuteNonQuery();
    }
}
