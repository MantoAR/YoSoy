using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Migración 26 (add_cierre_sesion.sql: SesionClinica.ClaveCierre, TurnoRealizadoUtc, IdEvolucion y sus índices) ensayada en una base DESCARTABLE con la forma de producción: 54 tablas
// (sin la de la migración 16), con la 25 y sin la 26 (la prueba desregistra lo que el esquema consolidado ya trae). Se crea con docs/produccion/01-produccion.sql con otro nombre y se
// borra al terminar: no toca la base donde corre el resto de la suite en paralelo. Mismo molde que Migracion25Tests.
[Trait("Categoria", "Integracion")]
[Trait("Migracion", "26")]
public class Migracion26Tests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    private static IEnumerable<string> Lotes(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private sealed class BaseDeProduccion : IDisposable
    {
        public string Nombre { get; } = "yosoy_m26_" + Guid.NewGuid().ToString("N")[..8];
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
            // La forma de producción ANTES de la 26: sin la tabla de la migración 16 (54 tablas) y sin las columnas, índices ni la versión de la 26.
            Ejecutar("DROP TABLE ConsentimientoWhatsApp");
            // El esquema consolidado ya trae la 27 (alta clínica y derivación asistidas), que es posterior: se saca primero (la 27 necesita la 26).
            foreach (var sentencia in Migracion28Pruebas.QuitarDesdeLa28) Ejecutar(sentencia);
            Ejecutar("DROP INDEX UX_SesionClinica_Clave ON SesionClinica");
            Ejecutar("DROP INDEX UX_SesionClinica_Evolucion ON SesionClinica");
            Ejecutar("DROP INDEX IX_SesionClinica_Turno ON SesionClinica");
            Ejecutar("ALTER TABLE SesionClinica DROP CONSTRAINT FK_SesionClinica_Evolucion");
            Ejecutar("ALTER TABLE SesionClinica DROP COLUMN ClaveCierre, TurnoRealizadoUtc, IdEvolucion");
            Ejecutar("DELETE FROM VersionEsquema WHERE Numero = 26");
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
        public int Columnas() => Escalar<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SesionClinica') AND name IN ('ClaveCierre', 'TurnoRealizadoUtc', 'IdEvolucion')");
        public int Indices() => Escalar<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SesionClinica') AND name IN ('UX_SesionClinica_Clave', 'UX_SesionClinica_Evolucion', 'IX_SesionClinica_Turno')");
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

    /// <summary>Una sesión anterior a la migración, con todo lo que tenía: la migración no puede tocarla.</summary>
    private static void ConUnaSesionVieja(BaseDeProduccion b)
    {
        b.Ejecutar("INSERT INTO Paciente (Nombre, Dni) VALUES (N'Paciente Ficticio', '90000001')");
        b.Ejecutar("INSERT INTO Especialidad (Nombre) VALUES (N'Psicologia ficticia')");
        b.Ejecutar("INSERT INTO Profesional (Nombre, Matricula, IdEspecialidad) SELECT N'Profesional Ficticio', 'MP-0001', IdEspecialidad FROM Especialidad");
        b.Ejecutar(@"INSERT INTO SesionClinica (IdPaciente, IdProfesional, FechaHora, NotasEvolucion, TipoCierre)
                     SELECT TOP 1 p.IdPaciente, f.IdProfesional, '2026-01-02T10:00:00', N'nota vieja', N'Seguimiento' FROM Paciente p CROSS JOIN Profesional f");
    }

    [Fact]
    public void Sobre_la_forma_de_produccion_agrega_columnas_e_indices_registra_la_version_y_no_toca_las_filas()
    {
        using var b = new BaseDeProduccion();
        ConUnaSesionVieja(b);
        Assert.Equal(54, b.Tablas());
        Assert.Equal(25, b.UltimaVersion());
        Assert.Equal(0, b.Columnas());
        var marcador = VerificadorEsquema.Migraciones.Single(m => m.Numero == 26).Marcador;
        Assert.Equal(0, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));         // el verificador la da por faltante

        b.Correr("migrations/add_cierre_sesion.sql");

        Assert.Equal(3, b.Columnas());
        Assert.Equal(3, b.Indices());
        Assert.Equal(54, b.Tablas());                                                                // columnas e índices: ninguna tabla nueva
        Assert.Equal(26, b.UltimaVersion());
        Assert.Equal(1, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));         // ahora el verificador la da por aplicada
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 26 AND Origen = 'Script' AND Archivo = N'add_cierre_sesion.sql'"));
        // Lo que ya existía queda como estaba: las tres columnas nuevas vacías.
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM SesionClinica WHERE NotasEvolucion = N'nota vieja' AND ClaveCierre IS NULL AND TurnoRealizadoUtc IS NULL AND IdEvolucion IS NULL"));
        Assert.Equal("uniqueidentifier", b.Escalar<string>("SELECT t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.SesionClinica') AND c.name = 'ClaveCierre'"));
        // Los índices únicos son FILTRADOS: dos sesiones sin clave (todas las de antes) no chocan entre sí.
        Assert.Equal(1, b.Escalar<int>("SELECT CAST(has_filter AS INT) FROM sys.indexes WHERE name = 'UX_SesionClinica_Clave'"));
        b.Ejecutar(@"INSERT INTO SesionClinica (IdPaciente, IdProfesional, FechaHora) SELECT IdPaciente, IdProfesional, GETDATE() FROM SesionClinica");
        Assert.Equal(2, b.Escalar<int>("SELECT COUNT(*) FROM SesionClinica"));
    }

    [Fact]
    public void La_clave_de_cierre_es_unica_por_profesional_y_la_evolucion_se_vincula_a_una_sola_sesion()
    {
        using var b = new BaseDeProduccion();
        ConUnaSesionVieja(b);
        b.Correr("migrations/add_cierre_sesion.sql");
        var clave = Guid.NewGuid();
        b.Ejecutar("INSERT INTO SesionClinica (IdPaciente, IdProfesional, FechaHora, ClaveCierre) SELECT IdPaciente, IdProfesional, GETDATE(), @c FROM SesionClinica", ("@c", clave));
        var ex = Assert.Throws<SqlException>(() =>
            b.Ejecutar("INSERT INTO SesionClinica (IdPaciente, IdProfesional, FechaHora, ClaveCierre) SELECT TOP 1 IdPaciente, IdProfesional, GETDATE(), @c FROM SesionClinica", ("@c", clave)));
        Assert.Contains(ex.Number, new[] { 2601, 2627 });

        // Una evolución se vincula a lo sumo con una sesión.
        b.Ejecutar("INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) SELECT IdPaciente, GETDATE() FROM Paciente");
        b.Ejecutar("INSERT INTO Evolucion (IdHistoria, IdProfesional, Fecha, Observaciones) SELECT h.IdHistoria, f.IdProfesional, '2026-01-02', N'e' FROM HistoriaClinica h CROSS JOIN Profesional f");
        b.Ejecutar("UPDATE SesionClinica SET IdEvolucion = (SELECT TOP 1 IdEvolucion FROM Evolucion) WHERE ClaveCierre IS NULL");
        Assert.Throws<SqlException>(() => b.Ejecutar("UPDATE SesionClinica SET IdEvolucion = (SELECT TOP 1 IdEvolucion FROM Evolucion) WHERE ClaveCierre IS NOT NULL"));
    }

    [Fact]
    public void Es_idempotente_la_segunda_corrida_no_cambia_nada_ni_duplica_la_version()
    {
        using var b = new BaseDeProduccion();
        ConUnaSesionVieja(b);
        b.Correr("migrations/add_cierre_sesion.sql");
        b.Ejecutar("UPDATE SesionClinica SET ClaveCierre = NEWID()");                                // un valor real puesto después

        b.Correr("migrations/add_cierre_sesion.sql");
        b.Correr("migrations/add_cierre_sesion.sql");

        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 26"));
        Assert.Equal(3, b.Columnas());
        Assert.Equal(3, b.Indices());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM SesionClinica WHERE ClaveCierre IS NOT NULL"));   // no pisa lo que ya estaba
        Assert.Equal(54, b.Tablas());
    }

    [Fact]
    public void Es_transaccional_si_algo_falla_a_la_mitad_no_queda_ni_una_columna_ni_la_version()
    {
        using var b = new BaseDeProduccion();
        // Se rompe el registro de versión (la tabla rechaza el insert) y se comprueba que lo que ya se había agregado se deshace con él.
        b.Ejecutar("ALTER TABLE VersionEsquema ADD CONSTRAINT CK_prueba_rechaza_26 CHECK (Numero <> 26)");
        Assert.ThrowsAny<SqlException>(() => b.Correr("migrations/add_cierre_sesion.sql"));

        Assert.Equal(0, b.Columnas());
        Assert.Equal(0, b.Indices());
        Assert.Equal(25, b.UltimaVersion());

        b.Ejecutar("ALTER TABLE VersionEsquema DROP CONSTRAINT CK_prueba_rechaza_26");
        b.Correr("migrations/add_cierre_sesion.sql");                                                // y con la causa resuelta, se aplica
        Assert.Equal(3, b.Columnas());
    }

    [Fact]
    public void El_esquema_consolidado_ya_trae_lo_mismo_que_la_migracion()
    {
        using var b = new BaseDeProduccion();
        var esquema = File.ReadAllText(Path.Combine(Raiz, "docs", "produccion", "01-produccion.sql"));
        var bloque = Regex.Match(esquema, @"-- ── Migración 26.*?\(26, N'add_cierre_sesion\.sql', 'Script'\);\s*GO", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(bloque));
        using (var con = b.Abrir())
            foreach (var lote in Lotes(bloque))
            {
                using var cmd = new SqlCommand(lote, con);
                cmd.ExecuteNonQuery();
            }
        Assert.Equal(3, b.Columnas());
        Assert.Equal(3, b.Indices());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 26 AND Origen = 'Script'"));
    }

    [Fact]
    public void El_deshacer_propio_deja_la_base_como_antes_sin_perder_sesiones_y_se_puede_volver_a_migrar()
    {
        using var b = new BaseDeProduccion();
        ConUnaSesionVieja(b);
        b.Correr("migrations/add_cierre_sesion.sql");
        b.Ejecutar("UPDATE SesionClinica SET ClaveCierre = NEWID(), TurnoRealizadoUtc = SYSUTCDATETIME()");

        b.Correr("docs/produccion/deshacer-migracion-26.sql");

        Assert.Equal(0, b.Columnas());
        Assert.Equal(0, b.Indices());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_SesionClinica_Evolucion'"));
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 26"));
        Assert.Equal(25, b.UltimaVersion());
        Assert.Equal(54, b.Tablas());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM SesionClinica WHERE NotasEvolucion = N'nota vieja'"));   // ninguna sesión se perdió

        b.Correr("docs/produccion/deshacer-migracion-26.sql");           // también idempotente
        b.Correr("migrations/add_cierre_sesion.sql");
        Assert.Equal(3, b.Columnas());
        Assert.Equal(0, b.Escalar<int>("SELECT COUNT(*) FROM SesionClinica WHERE ClaveCierre IS NOT NULL"));       // después de re-migrar vuelve a nacer vacía
    }

    [Fact]
    public void La_migracion_es_opcional_para_el_verificador()
    {
        Assert.True(VerificadorEsquema.Migraciones.Single(m => m.Numero == 26).Opcional);
        Assert.Equal(28, VerificadorEsquema.VersionRequerida);
    }

    [Fact]
    public void La_API_anda_sin_la_migracion_y_lo_dice_en_vez_de_fallar()
    {
        // Sin las columnas, un pedido con los campos nuevos responde 409 con su código, no un 500.
        var endpoint = File.ReadAllText(Path.Combine(Raiz, "Endpoints", "CierreSesionEndpoints.cs"));
        Assert.Contains("!dao.Migracion26Disponible()", endpoint);
        Assert.Contains("CierreSesionReglas.FaltaMigracion26", endpoint);
        Assert.Contains("\"falta-migracion-26\"", File.ReadAllText(Path.Combine(Raiz, "Services", "CierreSesionReglas.cs")));
        // El pedido de siempre (sin los campos nuevos) no pasa por ese camino, así que sigue andando sin la migración.
        Assert.Contains("if (CierreSesionEndpoints.EsPedidoDeCierre(req))", File.ReadAllText(Path.Combine(Raiz, "Program.cs")));
    }
}
