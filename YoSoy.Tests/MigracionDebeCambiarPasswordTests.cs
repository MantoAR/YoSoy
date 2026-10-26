using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Migración 24 (add_debe_cambiar_password.sql) ensayada en una base DESCARTABLE con la forma de producción: 54 tablas (sin la de la migración 16), con la
// 17, y sin la 24 (la prueba desregistra lo que el esquema consolidado ya trae). Se crea con docs/produccion/01-produccion.sql con otro nombre y se borra al
// terminar: no toca yosoy_test, donde corre el resto de la suite en paralelo.
[Trait("Categoria", "Integracion")]
public class MigracionDebeCambiarPasswordTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    private static IEnumerable<string> Lotes(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private sealed class BaseDeProduccion : IDisposable
    {
        public string Nombre { get; } = "yosoy_m24_" + Guid.NewGuid().ToString("N")[..8];
        private readonly string _cadenaMaestra, _cadena;

        public BaseDeProduccion()
        {
            var b = new SqlConnectionStringBuilder(Conexion.ObtenerConexion().ConnectionString) { InitialCatalog = "master" };
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
            // La forma de producción ANTES de la 24: sin la tabla de la migración 16 (54 tablas) y sin la columna ni su versión.
            Ejecutar("DROP TABLE ConsentimientoWhatsApp");
            // El esquema consolidado ya trae la 27 (tabla AltaClinica), posterior a la 24: se saca para que la forma de producción siga siendo de 54 tablas.
            foreach (var sentencia in Migracion28Pruebas.QuitarDesdeLa28) Ejecutar(sentencia);
            Ejecutar("ALTER TABLE Usuario DROP CONSTRAINT DF_Usuario_DebeCambiarPassword");
            Ejecutar("ALTER TABLE Usuario DROP COLUMN DebeCambiarPassword");
            Ejecutar("DELETE FROM VersionEsquema WHERE Numero = 24");
        }

        public SqlConnection Abrir() { var c = new SqlConnection(_cadena); c.Open(); return c; }

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

        public void Correr(string archivoRelativo)
        {
            var sql = File.ReadAllText(Path.Combine(Raiz, archivoRelativo));
            using var con = Abrir();
            foreach (var lote in Lotes(sql))
            {
                using var cmd = new SqlCommand(lote, con) { CommandTimeout = 120 };
                cmd.ExecuteNonQuery();
            }
        }

        public int Tablas() => Escalar<int>("SELECT COUNT(*) FROM sys.tables");
        public bool ExisteColumna() => Escalar<int>("SELECT CASE WHEN COL_LENGTH('dbo.Usuario', 'DebeCambiarPassword') IS NULL THEN 0 ELSE 1 END") == 1;

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

    private static void ConUsuarios(BaseDeProduccion b)
    {
        foreach (var n in new[] { "ana", "beto", "carla" })
            b.Ejecutar("INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@u, @h, 'Activo', @m)", ("@u", n), ("@h", "HASH-" + n), ("@m", n + "@ejemplo.test"));
    }

    [Fact]
    public void Sobre_la_forma_de_produccion_agrega_la_columna_en_cero_registra_la_version_y_no_cambia_las_tablas()
    {
        using var b = new BaseDeProduccion();
        ConUsuarios(b);
        Assert.Equal(54, b.Tablas());
        Assert.False(b.ExisteColumna());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 24"));
        var marcador = VerificadorEsquema.Migraciones.Single(m => m.Numero == 24).Marcador;
        Assert.Equal(0, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));      // el verificador la da por faltante

        b.Correr("migrations/add_debe_cambiar_password.sql");

        Assert.True(b.ExisteColumna());
        Assert.Equal(54, b.Tablas());                                                              // solo una columna: ninguna tabla nueva
        Assert.Equal(1, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));       // y ahora el verificador la da por aplicada
        // BIT NOT NULL, con la restricción por defecto en cero.
        Assert.Equal(0, b.Escalar<int>("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Usuario') AND name = 'DebeCambiarPassword'"));
        Assert.Equal("bit", b.Escalar<string>("SELECT t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.Usuario') AND c.name = 'DebeCambiarPassword'"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_Usuario_DebeCambiarPassword'"));
        // Las cuentas que ya existían quedan en cero y con su contraseña intacta.
        Assert.Equal(3, b.Escalar<int>("SELECT COUNT(*) FROM Usuario WHERE DebeCambiarPassword = 0 AND PasswordHash LIKE 'HASH-%'"));
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM Usuario WHERE DebeCambiarPassword = 1"));
        // Una cuenta nueva sin decir nada también queda en cero.
        b.Ejecutar("INSERT INTO Usuario (Username, PasswordHash, Estado) VALUES ('dora', 'x', 'Activo')");
        Assert.Equal(0, b.Escalar<int>("SELECT CAST(DebeCambiarPassword AS INT) FROM Usuario WHERE Username = 'dora'"));
        // Registrada una sola vez, con origen Script.
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 24 AND Origen = 'Script' AND Archivo = N'add_debe_cambiar_password.sql'"));
    }

    [Fact]
    public void Es_idempotente_la_segunda_corrida_no_cambia_nada_ni_duplica_la_version()
    {
        using var b = new BaseDeProduccion();
        ConUsuarios(b);
        b.Correr("migrations/add_debe_cambiar_password.sql");
        b.Ejecutar("UPDATE Usuario SET DebeCambiarPassword = 1 WHERE Username = 'ana'");    // un valor real puesto después

        b.Correr("migrations/add_debe_cambiar_password.sql");
        b.Correr("migrations/add_debe_cambiar_password.sql");

        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 24"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Usuario') AND name = 'DebeCambiarPassword'"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Usuario WHERE DebeCambiarPassword = 1"));   // no pisa lo que ya estaba
        Assert.Equal(54, b.Tablas());
    }

    [Fact]
    public void El_esquema_consolidado_ya_trae_lo_mismo_que_la_migracion()
    {
        using var b = new BaseDeProduccion();
        b.Correr("migrations/add_debe_cambiar_password.sql");
        // El esquema completo de una base nueva, con su bloque de la 24, queda igual que la base migrada.
        var esquema = File.ReadAllText(Path.Combine(Raiz, "docs", "produccion", "01-produccion.sql"));
        var bloque = Regex.Match(esquema, @"-- ── Migración 24.*?DebeCambiarPassword.*?\(24, N'add_debe_cambiar_password\.sql', 'Script'\);\s*GO", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(bloque));
        Assert.Contains("DF_Usuario_DebeCambiarPassword", bloque);
        b.Ejecutar("DELETE FROM VersionEsquema WHERE Numero = 24");
        b.Ejecutar("ALTER TABLE Usuario DROP CONSTRAINT DF_Usuario_DebeCambiarPassword");
        b.Ejecutar("ALTER TABLE Usuario DROP COLUMN DebeCambiarPassword");
        using (var con = b.Abrir())
            foreach (var lote in Lotes(bloque))
            {
                using var cmd = new SqlCommand(lote, con);
                cmd.ExecuteNonQuery();
            }
        Assert.True(b.ExisteColumna());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 24 AND Origen = 'Script'"));
    }

    [Fact]
    public void El_deshacer_propio_deja_la_base_como_antes_y_se_puede_volver_a_migrar()
    {
        using var b = new BaseDeProduccion();
        ConUsuarios(b);
        b.Correr("migrations/add_debe_cambiar_password.sql");
        b.Ejecutar("UPDATE Usuario SET DebeCambiarPassword = 1 WHERE Username = 'beto'");

        b.Correr("docs/produccion/deshacer-migracion-24.sql");

        Assert.False(b.ExisteColumna());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_Usuario_DebeCambiarPassword'"));
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 24"));
        Assert.Equal(54, b.Tablas());
        Assert.Equal(3, b.Escalar<int>("SELECT COUNT(*) FROM Usuario WHERE PasswordHash LIKE 'HASH-%'"));   // ningún dato de las cuentas se perdió

        b.Correr("docs/produccion/deshacer-migracion-24.sql");           // también idempotente
        b.Correr("migrations/add_debe_cambiar_password.sql");
        Assert.True(b.ExisteColumna());
    }
}
