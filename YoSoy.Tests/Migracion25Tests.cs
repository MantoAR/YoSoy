using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Migración 25 (add_conversacion_ia.sql: Conversacion.IaActiva) ensayada en una base DESCARTABLE con la forma de producción: 54 tablas (sin la de la migración 16), con la 17 y la
// 24, y sin la 25 (la prueba desregistra lo que el esquema consolidado ya trae). Se crea con docs/produccion/01-produccion.sql con otro nombre y se borra al terminar: no toca la
// base donde corre el resto de la suite en paralelo. Mismo molde que MigracionDebeCambiarPasswordTests.
[Trait("Categoria", "Integracion")]
[Trait("Migracion", "25")]
public class Migracion25Tests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    private static IEnumerable<string> Lotes(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private sealed class BaseDeProduccion : IDisposable
    {
        public string Nombre { get; } = "yosoy_m25_" + Guid.NewGuid().ToString("N")[..8];
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
            // La forma de producción ANTES de la 25: sin la tabla de la migración 16 (54 tablas) y sin la columna ni su versión.
            Ejecutar("DROP TABLE ConsentimientoWhatsApp");
            // El esquema consolidado ya trae la 27 (alta clínica y derivación asistidas), que es posterior: se saca primero (la 27 necesita la 26).
            foreach (var sentencia in Migracion27Pruebas.SentenciasParaQuitar) Ejecutar(sentencia);
            // El esquema consolidado ya trae la 26 (cierre de sesión), que es posterior: se saca para que la última versión sea la 24.
            Ejecutar("DROP INDEX UX_SesionClinica_Clave ON SesionClinica");
            Ejecutar("DROP INDEX UX_SesionClinica_Evolucion ON SesionClinica");
            Ejecutar("DROP INDEX IX_SesionClinica_Turno ON SesionClinica");
            Ejecutar("ALTER TABLE SesionClinica DROP CONSTRAINT FK_SesionClinica_Evolucion");
            Ejecutar("ALTER TABLE SesionClinica DROP COLUMN ClaveCierre, TurnoRealizadoUtc, IdEvolucion");
            Ejecutar("DELETE FROM VersionEsquema WHERE Numero = 26");
            Ejecutar("ALTER TABLE Conversacion DROP CONSTRAINT DF_Conversacion_IaActiva");
            Ejecutar("ALTER TABLE Conversacion DROP COLUMN IaActiva");
            Ejecutar("DELETE FROM VersionEsquema WHERE Numero = 25");
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
        public bool ExisteColumna() => Escalar<int>("SELECT CASE WHEN COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NULL THEN 0 ELSE 1 END") == 1;
        public int UltimaVersion() => Escalar<int>("SELECT MAX(Numero) FROM VersionEsquema");

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

    private static void ConConversaciones(BaseDeProduccion b)
    {
        foreach (var (tel, estado) in new[] { ("1155550001", "Activa"), ("1155550002", "DerivadaHumano"), ("1155550003", "Cerrada") })
            b.Ejecutar("INSERT INTO Conversacion (Telefono, Canal, Estado) VALUES (@t, 'WhatsApp', @e)", ("@t", tel), ("@e", estado));
    }

    [Fact]
    public void Sobre_la_forma_de_produccion_agrega_la_columna_apagada_registra_la_version_y_no_cambia_las_tablas()
    {
        using var b = new BaseDeProduccion();
        ConConversaciones(b);
        Assert.Equal(54, b.Tablas());
        Assert.Equal(24, b.UltimaVersion());                                                         // la forma de producción: versión 24
        Assert.False(b.ExisteColumna());
        var marcador = VerificadorEsquema.Migraciones.Single(m => m.Numero == 25).Marcador;
        Assert.Equal(0, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));         // el verificador la da por faltante

        b.Correr("migrations/add_conversacion_ia.sql");

        Assert.True(b.ExisteColumna());
        Assert.Equal(54, b.Tablas());                                                                // una columna: ninguna tabla nueva
        Assert.Equal(25, b.UltimaVersion());
        Assert.Equal(1, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));         // ahora el verificador la da por aplicada
        Assert.Equal("bit", b.Escalar<string>("SELECT t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.Conversacion') AND c.name = 'IaActiva'"));
        Assert.Equal(0, b.Escalar<int>("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Conversacion') AND name = 'IaActiva'"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_Conversacion_IaActiva'"));
        // Lo que ya existía queda apagado y sin otro cambio.
        Assert.Equal(3, b.Escalar<int>("SELECT COUNT(*) FROM Conversacion WHERE IaActiva = 0"));
        Assert.Equal(3, b.Escalar<int>("SELECT COUNT(*) FROM Conversacion WHERE Telefono LIKE '11555500%'"));
        // Una conversación nueva, sin decir nada, también nace apagada (es lo que hace el asistente al crearla).
        b.Ejecutar("INSERT INTO Conversacion (Telefono, Canal) VALUES ('1155550009', 'WhatsApp')");
        Assert.Equal(0, b.Escalar<int>("SELECT CAST(IaActiva AS INT) FROM Conversacion WHERE Telefono = '1155550009'"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 25 AND Origen = 'Script' AND Archivo = N'add_conversacion_ia.sql'"));
    }

    [Fact]
    public void Es_idempotente_la_segunda_corrida_no_cambia_nada_ni_duplica_la_version()
    {
        using var b = new BaseDeProduccion();
        ConConversaciones(b);
        b.Correr("migrations/add_conversacion_ia.sql");
        b.Ejecutar("UPDATE Conversacion SET IaActiva = 1 WHERE Telefono = '1155550002'");            // un valor real puesto después

        b.Correr("migrations/add_conversacion_ia.sql");
        b.Correr("migrations/add_conversacion_ia.sql");

        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 25"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Conversacion') AND name = 'IaActiva'"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Conversacion WHERE IaActiva = 1"));      // no pisa lo que ya estaba
        Assert.Equal(54, b.Tablas());
    }

    [Fact]
    public void Es_transaccional_si_algo_falla_a_la_mitad_no_queda_ni_la_columna_ni_la_version()
    {
        using var b = new BaseDeProduccion();
        // Una fila en VersionEsquema con el número 25 pero la columna ausente haría que la migración no la registre de nuevo; para forzar una falla DENTRO de la transacción se
        // rompe el registro de versión (la tabla rechaza el insert) y se comprueba que la columna que ya se había agregado se deshace con él.
        b.Ejecutar("ALTER TABLE VersionEsquema ADD CONSTRAINT CK_prueba_rechaza_25 CHECK (Numero <> 25)");
        Assert.ThrowsAny<SqlException>(() => b.Correr("migrations/add_conversacion_ia.sql"));

        Assert.False(b.ExisteColumna());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 25"));
        Assert.Equal(24, b.UltimaVersion());

        b.Ejecutar("ALTER TABLE VersionEsquema DROP CONSTRAINT CK_prueba_rechaza_25");
        b.Correr("migrations/add_conversacion_ia.sql");                                              // y con la causa resuelta, se aplica
        Assert.True(b.ExisteColumna());
    }

    [Fact]
    public void El_esquema_consolidado_ya_trae_lo_mismo_que_la_migracion()
    {
        using var b = new BaseDeProduccion();
        var esquema = File.ReadAllText(Path.Combine(Raiz, "docs", "produccion", "01-produccion.sql"));
        var bloque = Regex.Match(esquema, @"-- ── Migración 25.*?IaActiva.*?\(25, N'add_conversacion_ia\.sql', 'Script'\);\s*GO", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(bloque));
        Assert.Contains("DF_Conversacion_IaActiva", bloque);
        using (var con = b.Abrir())
            foreach (var lote in Lotes(bloque))
            {
                using var cmd = new SqlCommand(lote, con);
                cmd.ExecuteNonQuery();
            }
        Assert.True(b.ExisteColumna());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 25 AND Origen = 'Script'"));
    }

    [Fact]
    public void El_deshacer_propio_deja_la_base_como_antes_sin_perder_datos_y_se_puede_volver_a_migrar()
    {
        using var b = new BaseDeProduccion();
        ConConversaciones(b);
        b.Correr("migrations/add_conversacion_ia.sql");
        b.Ejecutar("UPDATE Conversacion SET IaActiva = 1 WHERE Telefono = '1155550001'");

        b.Correr("docs/produccion/deshacer-migracion-25.sql");

        Assert.False(b.ExisteColumna());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_Conversacion_IaActiva'"));
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 25"));
        Assert.Equal(24, b.UltimaVersion());
        Assert.Equal(54, b.Tablas());
        Assert.Equal(3, b.Escalar<int>("SELECT COUNT(*) FROM Conversacion WHERE Telefono LIKE '11555500%'"));   // ninguna conversación se perdió

        b.Correr("docs/produccion/deshacer-migracion-25.sql");           // también idempotente
        b.Correr("migrations/add_conversacion_ia.sql");
        Assert.True(b.ExisteColumna());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM Conversacion WHERE IaActiva = 1"));                 // después de re-migrar vuelve a nacer apagada
    }

    [Fact]
    public void La_API_anda_sin_la_columna_y_lo_dice_en_vez_de_fallar()
    {
        // La ruta del interruptor por conversación consulta la columna antes de usarla: sin la migración responde 409 con su código, no un 500.
        var fuente = File.ReadAllText(Path.Combine(Raiz, "Endpoints", "ChatsVinculoEndpoints.cs"));
        Assert.Contains("MIGRACION_25", fuente);
        Assert.Contains("!ia.ColumnaDisponible()", fuente);
        Assert.True(VerificadorEsquema.Migraciones.Single(m => m.Numero == 25).Opcional);
    }
}
