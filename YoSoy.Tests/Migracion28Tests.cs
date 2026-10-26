using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

/// <summary>Lo que hay que ejecutar para dejar una base armada con el esquema consolidado como estaba ANTES de la migración 28, y después ANTES de la 27. Lo usan las pruebas de las migraciones 24 a 27 (cada una arma su propia forma de producción).</summary>
public static class Migracion28Pruebas
{
    public static readonly string[] SentenciasParaQuitar =
    {
        "DROP TABLE AceptacionVideollamada",
        "ALTER TABLE Consentimiento DROP CONSTRAINT FK_Consentimiento_TutorFirmante",
        "ALTER TABLE Consentimiento DROP COLUMN IdTutorFirmante",
        "DELETE FROM VersionEsquema WHERE Numero = 28"
    };

    /// <summary>Primero la 28 y después la 27 (la 28 es posterior).</summary>
    /// <summary>Las migraciones 29 y 30 (agenda de admisión y vigencia de la agenda, de la rama de Mariana) y la 31 (primeros pasos) son posteriores a la 28: se sacan primero, de la más nueva a la más vieja.</summary>
    public static readonly string[] QuitarLa29YLa30 =
    {
        "DELETE FROM VersionEsquema WHERE Numero = 31",
        "DROP INDEX UX_Notificacion_Evento ON Notificacion",
        "ALTER TABLE Notificacion DROP COLUMN Evento",
        "ALTER TABLE Consentimiento DROP COLUMN IdUsuarioRevocacion, FechaRevocacion",
        "ALTER TABLE InformeAdmision DROP COLUMN AclaracionRecepcion, IdUsuarioRevision, FechaRevision, HorarioPreferido, ProfesionalesPreferidos",
        "DELETE FROM VersionEsquema WHERE Numero = 30",
        "ALTER TABLE Agenda DROP CONSTRAINT CK_Agenda_Vigencia",
        "ALTER TABLE Agenda DROP COLUMN FechaInicio, FechaFin",
        "DELETE FROM VersionEsquema WHERE Numero = 29",
        "ALTER TABLE InformeAdmision DROP CONSTRAINT FK_InformeAdmision_ProfesionalEntrevista",
        "ALTER TABLE InformeAdmision DROP COLUMN IdProfesionalEntrevista",
        "ALTER TABLE Agenda DROP CONSTRAINT DF_Agenda_EsAdmision",
        "ALTER TABLE Agenda DROP COLUMN EsAdmision"
    };

    public static readonly string[] QuitarDesdeLa28 = QuitarLa29YLa30.Concat(SentenciasParaQuitar).Concat(Migracion27Pruebas.SentenciasParaQuitar).ToArray();
}

// Migración 28 (add_consentimientos_circuito.sql: Consentimiento.IdTutorFirmante y la tabla AceptacionVideollamada) ensayada en una base DESCARTABLE con la forma de producción: 55 tablas (sin la de la
// migración 16, con la de la 27), con la 27 y sin la 28. Mismo molde que Migracion27Tests.
[Trait("Categoria", "Integracion")]
[Trait("Migracion", "28")]
public class Migracion28Tests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    private static IEnumerable<string> Lotes(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private sealed class BaseDeProduccion : IDisposable
    {
        public string Nombre { get; } = "yosoy_m28_" + Guid.NewGuid().ToString("N")[..8];
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
            Ejecutar("DROP TABLE ConsentimientoWhatsApp");
            foreach (var sentencia in Migracion28Pruebas.QuitarLa29YLa30.Concat(Migracion28Pruebas.SentenciasParaQuitar)) Ejecutar(sentencia);
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
        public int UltimaVersion() => Escalar<int>("SELECT MAX(Numero) FROM VersionEsquema");
        public bool Aplicada() => Escalar<int>("SELECT CASE WHEN OBJECT_ID('dbo.AceptacionVideollamada', 'U') IS NOT NULL AND COL_LENGTH('dbo.Consentimiento', 'IdTutorFirmante') IS NOT NULL THEN 1 ELSE 0 END") == 1;

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

    /// <summary>Un paciente, un tutor, el tipo de consentimiento y una firma de siempre: lo que la migración no puede tocar.</summary>
    private static (int Pac, int Tutor, int Tipo) Datos(BaseDeProduccion b)
    {
        b.Ejecutar("INSERT INTO Paciente (Nombre, Dni) VALUES (N'Paciente Ficticio', '90000028')");
        b.Ejecutar("INSERT INTO Tutor (Nombre, Dni) VALUES (N'Tutor Ficticio', '90000029')");
        var pac = b.Escalar<int>("SELECT IdPaciente FROM Paciente");
        b.Ejecutar("IF NOT EXISTS (SELECT 1 FROM TipoConsentimiento WHERE Descripcion = N'Tipo ficticio') INSERT INTO TipoConsentimiento (Descripcion) VALUES (N'Tipo ficticio')");
        var tipo = b.Escalar<int>("SELECT IdTipoConsentimiento FROM TipoConsentimiento WHERE Descripcion = N'Tipo ficticio'");
        b.Ejecutar("INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado) VALUES (@p, @t, 'v1', '2026-10-01', 'Activo')", ("@p", pac), ("@t", tipo));
        return (pac, b.Escalar<int>("SELECT IdTutor FROM Tutor"), tipo);
    }

    [Fact]
    public void Sobre_la_forma_de_produccion_agrega_la_columna_y_la_tabla_registra_la_version_y_no_toca_las_firmas()
    {
        using var b = new BaseDeProduccion();
        var (pac, tutor, tipo) = Datos(b);
        Assert.Equal(55, b.Tablas());
        Assert.Equal(27, b.UltimaVersion());
        Assert.False(b.Aplicada());
        var marcador = VerificadorEsquema.Migraciones.Single(m => m.Numero == 28).Marcador;
        Assert.Equal(0, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));

        b.Correr("migrations/add_consentimientos_circuito.sql");

        Assert.True(b.Aplicada());
        Assert.Equal(56, b.Tablas());
        Assert.Equal(28, b.UltimaVersion());
        Assert.Equal(1, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 28 AND Origen = 'Script' AND Archivo = N'add_consentimientos_circuito.sql'"));
        // La firma que ya existía queda como estaba, sin tutor firmante.
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Consentimiento WHERE Version = 'v1' AND IdTutorFirmante IS NULL AND Estado = 'Activo'"));
        // Y ahora una firma puede nombrar a su tutor (que tiene que existir).
        b.Ejecutar("INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado, IdTutorFirmante) VALUES (@p, @t, 'v2', '2026-10-02', 'Activo', @u)", ("@p", pac), ("@t", tipo), ("@u", tutor));
        Assert.Throws<SqlException>(() => b.Ejecutar("INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado, IdTutorFirmante) VALUES (@p, @t, 'v3', '2026-10-02', 'Activo', 2000000000)", ("@p", pac), ("@t", tipo)));
    }

    [Fact]
    public void La_aceptacion_de_la_videollamada_es_una_sola_vigente_por_sesion_y_paciente_y_se_puede_volver_a_aceptar_tras_revocar()
    {
        using var b = new BaseDeProduccion();
        var (pac, _, _) = Datos(b);
        b.Correr("migrations/add_consentimientos_circuito.sql");

        string Acepta(string origen = "turno", int id = 7) =>
            $"INSERT INTO AceptacionVideollamada (IdPaciente, Origen, IdOrigen, VersionTexto, HashTexto) VALUES ({pac}, N'{origen}', {id}, N'v0-provisorio', REPLICATE('a', 64))";
        b.Ejecutar(Acepta());
        Assert.Throws<SqlException>(() => b.Ejecutar(Acepta()));                           // dos vigentes de la misma sesión: no
        b.Ejecutar(Acepta(id: 8));                                                         // otra sesión: sí
        b.Ejecutar(Acepta("admision", 7));                                                 // el mismo id pero otro origen: sí
        Assert.Throws<SqlException>(() => b.Ejecutar(Acepta("whatsapp")));                  // solo turno o admisión
        b.Ejecutar("UPDATE AceptacionVideollamada SET RevocadoUtc = SYSUTCDATETIME() WHERE Origen = N'turno' AND IdOrigen = 7");
        b.Ejecutar(Acepta());                                                              // revocada, se puede aceptar de nuevo (queda el historial)
        Assert.Equal(2, b.Escalar<int>("SELECT COUNT(*) FROM AceptacionVideollamada WHERE Origen = N'turno' AND IdOrigen = 7"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM AceptacionVideollamada WHERE Origen = N'turno' AND IdOrigen = 7 AND RevocadoUtc IS NULL"));
    }

    [Fact]
    public void Es_idempotente_y_la_segunda_corrida_no_cambia_nada()
    {
        using var b = new BaseDeProduccion();
        var (pac, tutor, tipo) = Datos(b);
        b.Correr("migrations/add_consentimientos_circuito.sql");
        b.Ejecutar("UPDATE Consentimiento SET IdTutorFirmante = @u", ("@u", tutor));
        b.Correr("migrations/add_consentimientos_circuito.sql");
        b.Correr("migrations/add_consentimientos_circuito.sql");
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 28"));
        Assert.Equal(56, b.Tablas());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Consentimiento WHERE IdTutorFirmante IS NOT NULL"));   // no pisa lo que ya estaba
    }

    [Fact]
    public void Es_transaccional_si_falla_el_registro_de_la_version_no_queda_nada_a_medias()
    {
        using var b = new BaseDeProduccion();
        b.Ejecutar("ALTER TABLE VersionEsquema ADD CONSTRAINT CK_prueba_rechaza_28 CHECK (Numero <> 28)");
        Assert.ThrowsAny<SqlException>(() => b.Correr("migrations/add_consentimientos_circuito.sql"));
        Assert.False(b.Aplicada());
        Assert.Equal(55, b.Tablas());
        Assert.Equal(27, b.UltimaVersion());
        Assert.Equal(0, b.Escalar<int>("SELECT CASE WHEN COL_LENGTH('dbo.Consentimiento', 'IdTutorFirmante') IS NULL THEN 0 ELSE 1 END"));
        b.Ejecutar("ALTER TABLE VersionEsquema DROP CONSTRAINT CK_prueba_rechaza_28");
        b.Correr("migrations/add_consentimientos_circuito.sql");
        Assert.True(b.Aplicada());
    }

    [Fact]
    public void El_esquema_consolidado_ya_trae_lo_mismo_que_la_migracion()
    {
        using var b = new BaseDeProduccion();
        var esquema = File.ReadAllText(Path.Combine(Raiz, "docs", "produccion", "01-produccion.sql"));
        var bloque = Regex.Match(esquema, @"-- ── Migración 28.*?\(28, N'add_consentimientos_circuito\.sql', 'Script'\);\s*GO", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(bloque));
        using (var con = b.Abrir())
            foreach (var lote in Lotes(bloque))
            {
                using var cmd = new SqlCommand(lote, con);
                cmd.ExecuteNonQuery();
            }
        Assert.True(b.Aplicada());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 28 AND Origen = 'Script'"));
    }

    [Fact]
    public void El_deshacer_sin_datos_deja_la_base_como_antes_y_se_puede_volver_a_migrar()
    {
        using var b = new BaseDeProduccion();
        Datos(b);
        b.Correr("migrations/add_consentimientos_circuito.sql");

        b.Correr("docs/produccion/deshacer-migracion-28.sql");

        Assert.False(b.Aplicada());
        Assert.Equal(55, b.Tablas());
        Assert.Equal(27, b.UltimaVersion());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Consentimiento WHERE Version = 'v1'"));   // la firma de siempre sigue ahí
        b.Correr("docs/produccion/deshacer-migracion-28.sql");                                          // también idempotente
        b.Correr("migrations/add_consentimientos_circuito.sql");
        Assert.True(b.Aplicada());
    }

    [Fact]
    public void El_deshacer_se_niega_a_borrar_aceptaciones_o_firmas_de_tutor_y_no_cambia_nada()
    {
        using var b = new BaseDeProduccion();
        var (pac, tutor, _) = Datos(b);
        b.Correr("migrations/add_consentimientos_circuito.sql");
        b.Ejecutar("INSERT INTO AceptacionVideollamada (IdPaciente, Origen, IdOrigen, VersionTexto, HashTexto) VALUES (@p, N'turno', 1, N'v', REPLICATE('b', 64))", ("@p", pac));

        var ex = Assert.ThrowsAny<SqlException>(() => b.Correr("docs/produccion/deshacer-migracion-28.sql"));
        Assert.Contains("no se borran en silencio", ex.Message);
        Assert.True(b.Aplicada());
        Assert.Equal(28, b.UltimaVersion());

        b.Ejecutar("DELETE FROM AceptacionVideollamada");
        b.Ejecutar("UPDATE Consentimiento SET IdTutorFirmante = @u", ("@u", tutor));
        Assert.ThrowsAny<SqlException>(() => b.Correr("docs/produccion/deshacer-migracion-28.sql"));
        Assert.True(b.Aplicada());
    }

    [Fact]
    public void La_migracion_es_opcional_para_el_verificador()
    {
        Assert.True(VerificadorEsquema.Migraciones.Single(m => m.Numero == 28).Opcional);
        Assert.Equal(31, VerificadorEsquema.VersionRequerida);
    }
}
