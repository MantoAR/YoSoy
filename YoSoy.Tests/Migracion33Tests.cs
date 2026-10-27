using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Migración 33 (add_evolucion_tipocierre.sql: Evolucion.TipoCierre) y el arreglo del 500 de PUT /informes-admision/{id}/derivar: la primera evolución de la historia clínica se escribe con
// o sin esa columna. Se ensaya en una base DESCARTABLE armada con el esquema consolidado de producción (que no trae la columna: es el caso real que fallaba).
[Trait("Categoria", "Integracion")]
[Trait("Migracion", "33")]
public class Migracion33Tests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    private static IEnumerable<string> Lotes(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private sealed class BaseSinTipoCierre : IDisposable
    {
        public string Nombre { get; } = "yosoy_m33_" + Guid.NewGuid().ToString("N")[..8];
        private readonly string _cadenaMaestra, _cadena;

        public BaseSinTipoCierre()
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

        public bool TieneColumna() => Escalar<int>("SELECT CASE WHEN COL_LENGTH('dbo.Evolucion', 'TipoCierre') IS NULL THEN 0 ELSE 1 END") == 1;
        public int UltimaVersion() => Escalar<int>("SELECT MAX(Numero) FROM VersionEsquema");

        /// <summary>Un paciente y dos profesionales ficticios: el admisionista (ADM-001, a cuyo nombre se escribe la evolución) y el profesional al que se deriva.</summary>
        public (int Pac, int Prof) Datos()
        {
            Ejecutar("INSERT INTO Especialidad (Nombre) VALUES (N'Psicología ficticia')");
            var esp = Escalar<int>("SELECT TOP 1 IdEspecialidad FROM Especialidad");
            Ejecutar("INSERT INTO Profesional (Nombre, Matricula, IdEspecialidad, Estado) VALUES (N'Admisión', 'ADM-001', @e, 'Activo'), (N'Profesional Ficticio', 'M33-001', @e, 'Activo')", ("@e", esp));
            Ejecutar("INSERT INTO Paciente (Nombre, Dni) VALUES (N'Paciente Ficticio', '90000033')");
            return (Escalar<int>("SELECT IdPaciente FROM Paciente"), Escalar<int>("SELECT IdProfesional FROM Profesional WHERE Matricula = 'M33-001'"));
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

    [Fact]
    public void El_texto_de_la_consulta_solo_nombra_la_columna_cuando_existe()
    {
        Assert.DoesNotContain("TipoCierre", PrimeraEvolucionAdmision.Sql(false));
        Assert.Contains("TipoCierre", PrimeraEvolucionAdmision.Sql(true));
        Assert.Equal("Derivación", PrimeraEvolucionAdmision.TipoCierreDerivacion);
    }

    [Fact]
    public void Es_opcional_porque_la_API_anda_sin_ella()
    {
        var m = VerificadorEsquema.Migraciones.Single(x => x.Numero == 33);
        Assert.True(m.Opcional);
        Assert.Equal("add_evolucion_tipocierre.sql", m.Archivo);
        Assert.True(VerificadorEsquema.VersionRequerida >= 33);
    }

    [Fact]
    public void Regresion_sin_la_columna_la_primera_evolucion_se_escribe_igual_y_no_hay_500()
    {
        using var b = new BaseSinTipoCierre();
        var (pac, prof) = b.Datos();
        Assert.False(b.TieneColumna());   // la forma de producción: la columna no existe

        using (var con = b.Abrir())
        {
            Assert.False(PrimeraEvolucionAdmision.TieneTipoCierre(con));
            PrimeraEvolucionAdmision.Insertar(con, pac, prof, "Observación ficticia");   // antes: SqlException de columna inexistente → 500
        }

        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Evolucion e JOIN HistoriaClinica h ON h.IdHistoria = e.IdHistoria WHERE h.IdPaciente = @p AND e.Observaciones = N'Observación ficticia' AND e.PlanTratamiento LIKE N'Derivado a: Profesional Ficticio%'", ("@p", pac)));
    }

    [Fact]
    public void Con_la_migracion_aplicada_la_evolucion_queda_con_el_tipo_de_cierre()
    {
        using var b = new BaseSinTipoCierre();
        var (pac, prof) = b.Datos();
        b.Correr("migrations/add_evolucion_tipocierre.sql");

        Assert.True(b.TieneColumna());
        Assert.Equal(33, b.UltimaVersion());
        var marcador = VerificadorEsquema.Migraciones.Single(m => m.Numero == 33).Marcador;
        Assert.Equal(1, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 33 AND Origen = 'Script' AND Archivo = N'add_evolucion_tipocierre.sql'"));

        using (var con = b.Abrir()) PrimeraEvolucionAdmision.Insertar(con, pac, prof, null);
        Assert.Equal("Derivación", b.Escalar<string>("SELECT TipoCierre FROM Evolucion"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Evolucion WHERE Observaciones = N'Entrevista de admision realizada.'"));   // sin observaciones usa el texto de siempre
    }

    [Fact]
    public void La_migracion_es_idempotente_no_toca_las_evoluciones_que_ya_estaban_y_se_deshace()
    {
        using var b = new BaseSinTipoCierre();
        var (pac, prof) = b.Datos();
        using (var con = b.Abrir()) PrimeraEvolucionAdmision.Insertar(con, pac, prof, "Anterior");   // una evolución de antes de la migración

        b.Correr("migrations/add_evolucion_tipocierre.sql");
        b.Correr("migrations/add_evolucion_tipocierre.sql");
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 33"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Evolucion WHERE Observaciones = N'Anterior' AND TipoCierre IS NULL"));   // no se inventa ningún dato

        b.Correr("docs/produccion/deshacer-migracion-33.sql");
        Assert.False(b.TieneColumna());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 33"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Evolucion WHERE Observaciones = N'Anterior'"));   // la evolución sigue ahí
        b.Correr("docs/produccion/deshacer-migracion-33.sql");   // también idempotente
        b.Correr("migrations/add_evolucion_tipocierre.sql");
        Assert.True(b.TieneColumna());
    }

    [Fact]
    public void Es_transaccional_si_falla_el_registro_de_la_version_no_queda_la_columna()
    {
        using var b = new BaseSinTipoCierre();
        b.Ejecutar("ALTER TABLE VersionEsquema ADD CONSTRAINT CK_prueba_rechaza_33 CHECK (Numero <> 33)");
        Assert.ThrowsAny<SqlException>(() => b.Correr("migrations/add_evolucion_tipocierre.sql"));
        Assert.False(b.TieneColumna());
    }
}
