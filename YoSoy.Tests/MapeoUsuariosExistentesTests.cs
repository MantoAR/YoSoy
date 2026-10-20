using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Migración 23 (mapear_usuarios_existentes_a_permisos.sql): qué permisos reciben los usuarios que YA existen cuando se pasa de "el rol se deduce de la ficha"
// a "el rol sale de los permisos".
//
// Corre contra una base DESCARTABLE que la propia prueba crea con docs/produccion/01-produccion.sql (con otro nombre) y borra al terminar: el mapeo toca a todos los
// usuarios de la base, así que no se prueba sobre yosoy_test (donde corre el resto de la suite en paralelo). El esquema consolidado ya trae las migraciones 20 a 22
// y deja registrada la 23; la prueba la desregistra y arma la forma de producción: Rol y UsuarioRol sin asignaciones, y 5 cuentas (un administrador que no es
// paciente ni profesional, dos pacientes, dos profesionales con matrícula y especialidad) más un profesional sin matrícula y un usuario inactivo.
[Trait("Categoria", "Integracion")]
public class MapeoUsuariosExistentesTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    private static IEnumerable<string> Lotes(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private sealed class BaseDescartable : IDisposable
    {
        public string Nombre { get; } = "yosoy_map_" + Guid.NewGuid().ToString("N")[..8];
        private readonly string _cadenaMaestra, _cadena;

        public BaseDescartable()
        {
            var b = new SqlConnectionStringBuilder(Conexion.ObtenerConexion().ConnectionString);
            b.InitialCatalog = "master";
            _cadenaMaestra = b.ConnectionString;
            b.InitialCatalog = Nombre;
            _cadena = b.ConnectionString;

            var esquema = File.ReadAllText(Path.Combine(Raiz, "docs", "produccion", "01-produccion.sql"))
                .Replace("[yosoy]", $"[{Nombre}]").Replace("N'yosoy'", $"N'{Nombre}'");
            using var con = new SqlConnection(_cadenaMaestra);
            con.Open();
            foreach (var lote in Lotes(esquema))
            {
                using var cmd = new SqlCommand(lote, con) { CommandTimeout = 120 };
                cmd.ExecuteNonQuery();
            }
        }

        public SqlConnection Abrir()
        {
            var con = new SqlConnection(_cadena);
            con.Open();
            return con;
        }

        public void Ejecutar(string sql, params (string, object?)[] p)
        {
            using var con = Abrir();
            using var cmd = new SqlCommand(sql, con);
            foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public T Escalar<T>(string sql, params (string, object?)[] p)
        {
            using var con = Abrir();
            using var cmd = new SqlCommand(sql, con);
            foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
        }

        public string EstadoDeLaCadena()
        {
            using var con = Abrir();
            using var cmd = new SqlCommand("EXEC sp_VerificarCadenaAuditoria", con);
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            return r.GetString(0);
        }

        // Corre el archivo de la migración 23 lote por lote y devuelve las tablas del lote final (el resultado que lee el operador).
        public DataSet CorrerMigracion23()
        {
            var sql = File.ReadAllText(Path.Combine(Raiz, "migrations", "mapear_usuarios_existentes_a_permisos.sql"));
            var resultado = new DataSet();
            using var con = Abrir();
            foreach (var lote in Lotes(sql))
            {
                using var cmd = new SqlCommand(lote, con) { CommandTimeout = 120 };
                var ds = new DataSet();
                new SqlDataAdapter(cmd).Fill(ds);
                if (lote.Contains("RESULTADO")) resultado = ds;
            }
            return resultado;
        }

        public void Dispose()
        {
            try
            {
                using var con = new SqlConnection(_cadenaMaestra);
                con.Open();
                using var cmd = new SqlCommand($"ALTER DATABASE [{Nombre}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Nombre}];", con);
                cmd.ExecuteNonQuery();
            }
            catch (SqlException) { /* la base descartable ya no estaba */ }
        }
    }

    // Una base con la forma de producción antes de la migración 23.
    private sealed class Escenario : IDisposable
    {
        public readonly BaseDescartable Base = new();
        public int Admin, PacienteA, PacienteB, ProfA, ProfB, ProfSinMatricula, Inactivo;
        public int IdProfSinMatricula;

        public Escenario()
        {
            Base.Ejecutar("DELETE FROM UsuarioPrivilegio; DELETE FROM UsuarioPermiso; DELETE FROM UsuarioRol; DELETE FROM VersionEsquema WHERE Numero = 23;");
            Base.Ejecutar("INSERT INTO Especialidad (Nombre, Estado) VALUES (N'Psicología de prueba', 'Activo')");
            var esp = Base.Escalar<int>("SELECT IdEspecialidad FROM Especialidad WHERE Nombre = N'Psicología de prueba'");

            int Usuario(string nombre, string estado = "Activo") =>
                Base.Escalar<int>("INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) OUTPUT INSERTED.IdUsuario VALUES (@u, 'x', @e, @m)",
                    ("@u", nombre), ("@e", estado), ("@m", nombre + "@ejemplo.test"));
            int Paciente(string dni) =>
                Base.Escalar<int>("INSERT INTO Paciente (Nombre, Dni, Estado) OUTPUT INSERTED.IdPaciente VALUES (N'Paciente de prueba', @d, 'Activo')", ("@d", dni));
            int Profesional(string matricula) =>
                Base.Escalar<int>("INSERT INTO Profesional (Nombre, Matricula, IdEspecialidad, Estado) OUTPUT INSERTED.IdProfesional VALUES (N'Profesional de prueba', @m, @e, 'Activo')",
                    ("@m", matricula), ("@e", esp));

            Admin = Usuario("admin-prueba");
            PacienteA = Usuario("paciente-a"); PacienteB = Usuario("paciente-b");
            ProfA = Usuario("prof-a"); ProfB = Usuario("prof-b"); ProfSinMatricula = Usuario("prof-sin-matricula");
            Inactivo = Usuario("inactivo", "Inactivo");

            Base.Ejecutar("INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente, FechaVinculacion) VALUES (@u, @p, GETDATE())", ("@u", PacienteA), ("@p", Paciente("10000001")));
            Base.Ejecutar("INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente, FechaVinculacion) VALUES (@u, @p, GETDATE())", ("@u", PacienteB), ("@p", Paciente("10000002")));
            Base.Ejecutar("INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@u, @p, GETDATE())", ("@u", ProfA), ("@p", Profesional("MP-A")));
            Base.Ejecutar("INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@u, @p, GETDATE())", ("@u", ProfB), ("@p", Profesional("MP-B")));
            IdProfSinMatricula = Profesional("");
            Base.Ejecutar("INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@u, @p, GETDATE())", ("@u", ProfSinMatricula), ("@p", IdProfSinMatricula));
        }

        public string Permisos(int idUsuario)
        {
            using var con = Base.Abrir();
            using var cmd = new SqlCommand(@"SELECT ISNULL(STUFF((SELECT N',' + p.Clave FROM UsuarioPermiso x JOIN Permiso p ON p.IdPermiso = x.IdPermiso
                WHERE x.IdUsuario = @u ORDER BY p.Clave FOR XML PATH('')), 1, 1, N''), N'')", con);
            cmd.Parameters.AddWithValue("@u", idUsuario);
            return (string)cmd.ExecuteScalar()!;
        }

        public string Roles(int idUsuario)
        {
            using var con = Base.Abrir();
            using var cmd = new SqlCommand(@"SELECT ISNULL(STUFF((SELECT N',' + r.Clave FROM UsuarioRol x JOIN Rol r ON r.IdRol = x.IdRol
                WHERE x.IdUsuario = @u ORDER BY r.Clave FOR XML PATH('')), 1, 1, N''), N'')", con);
            cmd.Parameters.AddWithValue("@u", idUsuario);
            return (string)cmd.ExecuteScalar()!;
        }

        public void Dispose() => Base.Dispose();
    }

    [Fact]
    public void Cada_usuario_existente_recibe_el_permiso_que_le_corresponde_con_Rol_y_UsuarioRol_vacios()
    {
        using var e = new Escenario();
        // La forma de producción antes de la migración: ningún permiso asignado.
        Assert.Equal(0, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPermiso"));
        Assert.Equal(0, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioRol"));

        e.Base.CorrerMigracion23();

        // El administrador (no es paciente ni profesional) queda con los DOS módulos: Configurador y CRM.
        Assert.Equal("configurador,crm", e.Permisos(e.Admin));
        Assert.Equal("configurador,crm", e.Roles(e.Admin));
        // Los profesionales con matrícula y especialidad, con "profesional" (y no con auditor ni Admisión: eso no se da solo).
        Assert.Equal("profesional", e.Permisos(e.ProfA));
        Assert.Equal("profesional", e.Permisos(e.ProfB));
        Assert.Equal("profesional", e.Roles(e.ProfA));
        // Los pacientes, sin ningún permiso del equipo: entran por su flujo.
        Assert.Equal("", e.Permisos(e.PacienteA));
        Assert.Equal("", e.Permisos(e.PacienteB));
        Assert.Equal("", e.Roles(e.PacienteA));
        // El profesional sin matrícula queda SIN permiso (su ingreso da 403 SIN_PERMISOS hasta que se complete la ficha).
        Assert.Equal("", e.Permisos(e.ProfSinMatricula));
        // Un usuario inactivo que no es paciente ni profesional NO recibe permisos (la condición de "activo" es la del login).
        Assert.Equal("", e.Permisos(e.Inactivo));
        Assert.Equal("", e.Roles(e.Inactivo));
        // Nadie recibe privilegios ni el permiso de auditor.
        Assert.Equal(0, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPrivilegio"));
        Assert.Equal(0, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPermiso up JOIN Permiso p ON p.IdPermiso = up.IdPermiso WHERE p.Clave = 'auditor'"));
    }

    [Fact]
    public void La_migracion_informa_el_profesional_que_quedo_sin_permiso_en_vez_de_callarlo()
    {
        using var e = new Escenario();

        var resultado = e.Base.CorrerMigracion23();

        // Tercer conjunto del resultado: "cuentas del equipo sin ningún permiso" (ids y motivo, sin datos personales).
        var sinPermiso = resultado.Tables[2];
        var filas = sinPermiso.Rows.Cast<DataRow>().ToDictionary(f => Convert.ToInt32(f["IdUsuario"]));
        Assert.Equal(2, filas.Count);
        Assert.Equal(e.IdProfSinMatricula, Convert.ToInt32(filas[e.ProfSinMatricula]["IdProfesional"]));
        Assert.Equal("profesional sin matrícula", (string)filas[e.ProfSinMatricula]["motivo"]);
        Assert.Equal("inactivo, sin permisos", (string)filas[e.Inactivo]["motivo"]);
    }

    [Fact]
    public void Correrla_dos_veces_no_cambia_nada_y_no_le_devuelve_permisos_a_quien_el_Configurador_se_los_saco()
    {
        using var e = new Escenario();
        e.Base.CorrerMigracion23();
        var antes = e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPermiso") + 1000 * e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioRol");

        e.Base.CorrerMigracion23();   // la versión 23 ya está registrada: no hace nada

        Assert.Equal(antes, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPermiso") + 1000 * e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioRol"));

        // El Configurador le saca el CRM al administrador y repite la migración: no se lo vuelve a dar.
        e.Base.Ejecutar("DELETE FROM UsuarioPermiso WHERE IdUsuario = @u AND IdPermiso = (SELECT IdPermiso FROM Permiso WHERE Clave = 'crm'); " +
                        "DELETE FROM UsuarioRol WHERE IdUsuario = @u AND IdRol = (SELECT IdRol FROM Rol WHERE Clave = 'crm')", ("@u", e.Admin));
        e.Base.CorrerMigracion23();
        Assert.Equal("configurador", e.Permisos(e.Admin));
    }

    [Fact]
    public void Aun_sin_el_registro_de_la_version_repetirla_no_duplica_filas()
    {
        using var e = new Escenario();
        e.Base.CorrerMigracion23();
        var antes = e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPermiso");

        e.Base.Ejecutar("DELETE FROM VersionEsquema WHERE Numero = 23");   // forzar una repetición a propósito
        e.Base.CorrerMigracion23();

        Assert.Equal(antes, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPermiso"));
    }

    [Fact]
    public void Si_algo_falla_se_deshace_todo_y_la_version_23_no_queda_registrada()
    {
        using var e = new Escenario();
        // Se rompe el catálogo a propósito: sin el permiso "crm" (acá, renombrado) el INSERT del administrador falla DESPUÉS de haber asignado "profesional" a los profesionales.
        e.Base.Ejecutar("UPDATE Permiso SET Clave = 'crm-roto' WHERE Clave = 'crm'");

        Assert.ThrowsAny<SqlException>(() => e.Base.CorrerMigracion23());

        Assert.Equal(0, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioPermiso"));   // lo de los profesionales se deshizo
        Assert.Equal(0, e.Base.Escalar<int>("SELECT COUNT(*) FROM UsuarioRol"));
        Assert.Equal(0, e.Base.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 23"));
    }

    [Fact]
    public void Las_asignaciones_del_mapeo_quedan_en_la_auditoria_administrativa()
    {
        using var e = new Escenario();
        e.Base.CorrerMigracion23();

        // Las dejan los triggers de la migración 21, y como no las hizo la aplicación quedan marcadas como hechas en la base.
        Assert.True(e.Base.Escalar<int>("SELECT COUNT(*) FROM LogAuditoria WHERE Accion = N'Permiso asignado' AND Espacio = 'base'") >= 4);
        Assert.Equal("Íntegra", e.Base.EstadoDeLaCadena());
    }
}
