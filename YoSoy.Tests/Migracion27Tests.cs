using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

/// <summary>Lo que hay que ejecutar para dejar una base armada con el esquema consolidado como estaba ANTES de la migración 27. Lo usan las pruebas de las migraciones 25 y 26 (que arman su propia forma de producción).</summary>
public static class Migracion27Pruebas
{
    public static readonly string[] SentenciasParaQuitar =
    {
        "DROP INDEX UX_Derivacion_CasoEnCurso ON Derivacion",
        "ALTER TABLE Derivacion DROP CONSTRAINT FK_Derivacion_Caso, FK_Derivacion_Sesion, CK_Derivacion_Origen, DF_Derivacion_Origen",
        "ALTER TABLE Derivacion DROP COLUMN Origen, IdCaso, Observaciones, IdSesion, RespuestaUtc, IdUsuarioRespuesta, ResolucionUtc, IdUsuarioResolucion",
        "DROP TABLE AltaClinica",
        "ALTER TABLE Caso DROP CONSTRAINT CK_Caso_Estado",
        "ALTER TABLE Caso ADD CONSTRAINT CK_Caso_Estado CHECK (Estado IN (N'Nuevo', N'Derivado', N'Asignado', N'En atención', N'Cerrado'))",
        "DELETE FROM VersionEsquema WHERE Numero = 27"
    };
}

// Migración 27 (add_alta_derivacion_cierre.sql: Caso.Estado 'En alta', tabla AltaClinica y columnas de Derivacion) ensayada en una base DESCARTABLE con la forma de producción: 54 tablas (sin la de la
// migración 16), con la 26 y sin la 27. Mismo molde que Migracion26Tests.
[Trait("Categoria", "Integracion")]
[Trait("Migracion", "27")]
public class Migracion27Tests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    private static IEnumerable<string> Lotes(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => !string.IsNullOrWhiteSpace(l));

    private sealed class BaseDeProduccion : IDisposable
    {
        public string Nombre { get; } = "yosoy_m27_" + Guid.NewGuid().ToString("N")[..8];
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
            foreach (var sentencia in Migracion28Pruebas.QuitarDesdeLa28) Ejecutar(sentencia);
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
        public bool Aplicada() => Escalar<int>("SELECT CASE WHEN OBJECT_ID('dbo.AltaClinica', 'U') IS NOT NULL AND COL_LENGTH('dbo.Derivacion', 'Origen') IS NOT NULL THEN 1 ELSE 0 END") == 1;
        public int ColumnasNuevas() => Escalar<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Derivacion') AND name IN ('Origen','IdCaso','Observaciones','IdSesion','RespuestaUtc','IdUsuarioRespuesta','ResolucionUtc','IdUsuarioResolucion')");

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

    /// <summary>Un paciente, dos profesionales, un caso Asignado y una solicitud de cambio de siempre: lo que la migración no puede tocar.</summary>
    private static (int Pac, int ProfA, int ProfB, int Caso) Datos(BaseDeProduccion b)
    {
        b.Ejecutar("INSERT INTO Paciente (Nombre, Dni) VALUES (N'Paciente Ficticio', '90000027')");
        b.Ejecutar("INSERT INTO Especialidad (Nombre) VALUES (N'Psicologia ficticia')");
        b.Ejecutar("INSERT INTO Profesional (Nombre, Matricula, IdEspecialidad) SELECT N'Profesional A', 'MP-27A', IdEspecialidad FROM Especialidad");
        b.Ejecutar("INSERT INTO Profesional (Nombre, Matricula, IdEspecialidad) SELECT N'Profesional B', 'MP-27B', IdEspecialidad FROM Especialidad");
        var pac = b.Escalar<int>("SELECT IdPaciente FROM Paciente");
        var a = b.Escalar<int>("SELECT IdProfesional FROM Profesional WHERE Matricula = 'MP-27A'");
        var pb = b.Escalar<int>("SELECT IdProfesional FROM Profesional WHERE Matricula = 'MP-27B'");
        b.Ejecutar("INSERT INTO Caso (IdPaciente, Estado, IdProfesionalAsignado, Origen) VALUES (@p, 'Asignado', @a, 'Admision')", ("@p", pac), ("@a", a));
        b.Ejecutar("INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado) VALUES (@p, @a, @b, '2026-10-01', N'solicitud de siempre', 'Pendiente')", ("@p", pac), ("@a", a), ("@b", pb));
        return (pac, a, pb, b.Escalar<int>("SELECT IdCaso FROM Caso"));
    }

    [Fact]
    public void Sobre_la_forma_de_produccion_crea_la_tabla_las_columnas_y_el_estado_nuevo_sin_tocar_las_filas()
    {
        using var b = new BaseDeProduccion();
        var (pac, a, pb, caso) = Datos(b);
        Assert.Equal(54, b.Tablas());
        Assert.Equal(26, b.UltimaVersion());
        Assert.False(b.Aplicada());
        Assert.Throws<SqlException>(() => b.Ejecutar("UPDATE Caso SET Estado = N'En alta' WHERE IdCaso = @c", ("@c", caso)));   // antes de la 27, 'En alta' no existe
        var marcador = VerificadorEsquema.Migraciones.Single(m => m.Numero == 27).Marcador;
        Assert.Equal(0, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));

        b.Correr("migrations/add_alta_derivacion_cierre.sql");

        Assert.True(b.Aplicada());
        Assert.Equal(55, b.Tablas());                                                   // AltaClinica
        Assert.Equal(27, b.UltimaVersion());
        Assert.Equal(8, b.ColumnasNuevas());
        Assert.Equal(1, b.Escalar<int>($"SELECT CASE WHEN {marcador} THEN 1 ELSE 0 END"));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 27 AND Origen = 'Script' AND Archivo = N'add_alta_derivacion_cierre.sql'"));
        // Lo que ya existía queda como estaba; la solicitud de cambio de siempre es Origen 'Solicitud'.
        Assert.Equal("Asignado", b.Escalar<string>("SELECT Estado FROM Caso WHERE IdCaso = @c", ("@c", caso)));
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Derivacion WHERE Estado = 'Pendiente' AND Origen = N'Solicitud' AND IdCaso IS NULL AND Observaciones IS NULL"));
        // Y ahora el caso puede pasar a 'En alta'.
        b.Ejecutar("UPDATE Caso SET Estado = N'En alta' WHERE IdCaso = @c", ("@c", caso));
        Assert.Throws<SqlException>(() => b.Ejecutar("UPDATE Caso SET Estado = N'Cualquier cosa' WHERE IdCaso = @c", ("@c", caso)));
    }

    [Fact]
    public void Las_restricciones_cuidan_el_plan_de_control_una_alta_por_caso_y_una_derivacion_en_curso_por_caso()
    {
        using var b = new BaseDeProduccion();
        var (pac, a, pb, caso) = Datos(b);
        b.Correr("migrations/add_alta_derivacion_cierre.sql");

        string Alta(int controles, string cada, string primero) =>
            $"INSERT INTO AltaClinica (IdCaso, IdPaciente, IdProfesional, FechaAlta, Informe, ControlesPrevistos, ControlCadaDias, PrimerControl, EstadoCasoAnterior) VALUES ({caso}, {pac}, {a}, '2026-10-26', N'informe', {controles}, {cada}, {primero}, N'Asignado')";
        // Plan incoherente: controles sin frecuencia, frecuencia fuera de rango, o frecuencia sin controles.
        Assert.Throws<SqlException>(() => b.Ejecutar(Alta(3, "NULL", "'2026-11-02'")));
        Assert.Throws<SqlException>(() => b.Ejecutar(Alta(3, "3", "'2026-11-02'")));
        Assert.Throws<SqlException>(() => b.Ejecutar(Alta(0, "30", "'2026-11-02'")));
        Assert.Throws<SqlException>(() => b.Ejecutar(Alta(13, "30", "'2026-11-02'")));
        b.Ejecutar(Alta(3, "30", "'2026-11-25'"));
        Assert.Throws<SqlException>(() => b.Ejecutar(Alta(0, "NULL", "NULL")));          // una sola alta por caso

        string Deriv(string estado) =>
            $"INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado, Origen, IdCaso) VALUES ({pac}, {a}, {pb}, '2026-10-26', N'm', N'{estado}', N'Cierre', {caso})";
        b.Ejecutar(Deriv("Pendiente del destino"));
        Assert.Throws<SqlException>(() => b.Ejecutar(Deriv("Pendiente del destino")));     // una en curso por caso
        Assert.Throws<SqlException>(() => b.Ejecutar(Deriv("Aceptada por el destino")));
        b.Ejecutar(Deriv("Rechazada por el destino"));                                     // las ya resueltas no cuentan
        b.Ejecutar("UPDATE Derivacion SET Estado = N'Rechazada por el destino' WHERE Estado = N'Pendiente del destino'");
        b.Ejecutar(Deriv("Pendiente del destino"));                                        // y con la anterior resuelta se puede iniciar otra
        Assert.Throws<SqlException>(() => b.Ejecutar("INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Estado, Origen) VALUES (@p, @a, @b, '2026-10-26', 'x', N'Otro')", ("@p", pac), ("@a", a), ("@b", pb)));
    }

    [Fact]
    public void Es_idempotente_y_la_segunda_corrida_no_cambia_nada()
    {
        using var b = new BaseDeProduccion();
        Datos(b);
        b.Correr("migrations/add_alta_derivacion_cierre.sql");
        b.Ejecutar("UPDATE Derivacion SET Observaciones = N'puesta despues'");
        b.Correr("migrations/add_alta_derivacion_cierre.sql");
        b.Correr("migrations/add_alta_derivacion_cierre.sql");
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 27"));
        Assert.Equal(8, b.ColumnasNuevas());
        Assert.Equal(55, b.Tablas());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Derivacion WHERE Observaciones = N'puesta despues'"));
    }

    [Fact]
    public void Es_transaccional_si_falla_el_registro_de_la_version_no_queda_nada_a_medias()
    {
        using var b = new BaseDeProduccion();
        var (pac, a, pb, caso) = Datos(b);
        b.Ejecutar("ALTER TABLE VersionEsquema ADD CONSTRAINT CK_prueba_rechaza_27 CHECK (Numero <> 27)");
        Assert.ThrowsAny<SqlException>(() => b.Correr("migrations/add_alta_derivacion_cierre.sql"));
        Assert.False(b.Aplicada());
        Assert.Equal(0, b.ColumnasNuevas());
        Assert.Equal(54, b.Tablas());
        Assert.Equal(26, b.UltimaVersion());
        Assert.Throws<SqlException>(() => b.Ejecutar("UPDATE Caso SET Estado = N'En alta' WHERE IdCaso = @c", ("@c", caso)));   // el CHECK también volvió atrás
        b.Ejecutar("ALTER TABLE VersionEsquema DROP CONSTRAINT CK_prueba_rechaza_27");
        b.Correr("migrations/add_alta_derivacion_cierre.sql");
        Assert.True(b.Aplicada());
    }

    [Fact]
    public void El_esquema_consolidado_ya_trae_lo_mismo_que_la_migracion()
    {
        using var b = new BaseDeProduccion();
        var esquema = File.ReadAllText(Path.Combine(Raiz, "docs", "produccion", "01-produccion.sql"));
        var bloque = Regex.Match(esquema, @"-- ── Migración 27.*?\(27, N'add_alta_derivacion_cierre\.sql', 'Script'\);\s*GO", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(bloque));
        using (var con = b.Abrir())
            foreach (var lote in Lotes(bloque))
            {
                using var cmd = new SqlCommand(lote, con);
                cmd.ExecuteNonQuery();
            }
        Assert.True(b.Aplicada());
        Assert.Equal(8, b.ColumnasNuevas());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 27 AND Origen = 'Script'"));
    }

    [Fact]
    public void El_deshacer_sin_datos_deja_la_base_como_antes_y_se_puede_volver_a_migrar()
    {
        using var b = new BaseDeProduccion();
        Datos(b);
        b.Correr("migrations/add_alta_derivacion_cierre.sql");

        b.Correr("docs/produccion/deshacer-migracion-27.sql");

        Assert.False(b.Aplicada());
        Assert.Equal(0, b.ColumnasNuevas());
        Assert.Equal(54, b.Tablas());
        Assert.Equal(26, b.UltimaVersion());
        Assert.Equal(1, b.Escalar<int>("SELECT COUNT(*) FROM Derivacion WHERE Estado = 'Pendiente'"));   // la solicitud de siempre sigue ahí
        Assert.Throws<SqlException>(() => b.Ejecutar("UPDATE Caso SET Estado = N'En alta'"));

        b.Correr("docs/produccion/deshacer-migracion-27.sql");                                          // también idempotente
        b.Correr("migrations/add_alta_derivacion_cierre.sql");
        Assert.True(b.Aplicada());
    }

    [Fact]
    public void El_deshacer_se_niega_a_borrar_altas_casos_en_alta_o_derivaciones_del_cierre_y_no_cambia_nada()
    {
        using var b = new BaseDeProduccion();
        var (pac, a, pb, caso) = Datos(b);
        b.Correr("migrations/add_alta_derivacion_cierre.sql");
        b.Ejecutar("UPDATE Caso SET Estado = N'En alta'");

        var ex = Assert.ThrowsAny<SqlException>(() => b.Correr("docs/produccion/deshacer-migracion-27.sql"));
        Assert.Contains("no se borran en silencio", ex.Message);

        Assert.True(b.Aplicada());
        Assert.Equal(27, b.UltimaVersion());
        Assert.Equal("En alta", b.Escalar<string>("SELECT Estado FROM Caso"));

        // Con una derivación del cierre tampoco.
        b.Ejecutar("UPDATE Caso SET Estado = N'Asignado'");
        b.Ejecutar("INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Estado, Origen, IdCaso) VALUES (@p, @a, @b, '2026-10-26', N'Pendiente del destino', N'Cierre', @c)", ("@p", pac), ("@a", a), ("@b", pb), ("@c", caso));
        Assert.ThrowsAny<SqlException>(() => b.Correr("docs/produccion/deshacer-migracion-27.sql"));
        Assert.True(b.Aplicada());
    }

    [Fact]
    public void La_migracion_es_opcional_para_el_verificador()
    {
        Assert.True(VerificadorEsquema.Migraciones.Single(m => m.Numero == 27).Opcional);
        Assert.Equal(34, VerificadorEsquema.VersionRequerida);
    }
}
