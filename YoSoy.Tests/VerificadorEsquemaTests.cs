using System.Text.RegularExpressions;

// Aviso de migraciones faltantes (etapa 1 de roles y permisos).
public class VerificadorEsquemaTests
{
    private class InspectorFalso : IInspectorEsquema
    {
        public HashSet<int> Faltan { get; } = new();
        public bool SinConexion { get; set; }
        public int Llamadas { get; private set; }

        public IReadOnlyList<bool> Evaluar(IReadOnlyList<string> condiciones)
        {
            Llamadas++;
            if (SinConexion) throw new InvalidOperationException("A network-related error occurred");
            return VerificadorEsquema.Migraciones.Select(m => !Faltan.Contains(m.Numero)).ToList();
        }
    }

    private class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    [Fact]
    public void Base_al_dia_no_bloquea_y_lo_dice()
    {
        var v = new VerificadorEsquema(new InspectorFalso(), new Reloj());
        var e = v.Estado();
        Assert.True(e.Verificado);
        Assert.False(e.Desactualizado);
        Assert.Contains("al día", e.Mensaje);
    }

    [Fact]
    public void Nombra_cada_migracion_que_falta_con_su_archivo()
    {
        var inspector = new InspectorFalso();
        inspector.Faltan.UnionWith(new[] { 6, 15 });
        var e = new VerificadorEsquema(inspector, new Reloj()).Estado();

        Assert.True(e.Desactualizado);
        Assert.Equal(new[] { 6, 15 }, e.Faltantes.Select(m => m.Numero));
        Assert.Contains("Faltan las migraciones 6 (add_consentimiento_telemedicina.sql), 15 (add_atencion_consentimientos_ia_evolucion.sql)", e.Mensaje);
    }

    [Fact]
    public void Sin_conexion_no_se_considera_desactualizada()
    {
        var e = new VerificadorEsquema(new InspectorFalso { SinConexion = true }, new Reloj()).Estado();
        Assert.False(e.Verificado);
        Assert.False(e.Desactualizado);   // no se bloquea: cada endpoint falla como antes, con su código de error
    }

    [Fact]
    public void Al_dia_no_vuelve_a_consultar_la_base()
    {
        var inspector = new InspectorFalso();
        var reloj = new Reloj();
        var v = new VerificadorEsquema(inspector, reloj);
        v.Estado();
        reloj.Ahora = reloj.Ahora.AddHours(5);
        v.Estado();
        Assert.Equal(1, inspector.Llamadas);
    }

    [Fact]
    public void Desactualizada_se_recupera_sola_despues_de_correr_la_migracion()
    {
        var inspector = new InspectorFalso();
        inspector.Faltan.Add(16);
        var reloj = new Reloj();
        var v = new VerificadorEsquema(inspector, reloj);

        Assert.True(v.Estado().Desactualizado);
        inspector.Faltan.Clear();                       // se corrió la migración 16
        Assert.True(v.Estado().Desactualizado);         // dentro de los 30 s usa lo que ya sabía
        reloj.Ahora += VerificadorEsquema.IntervaloReintento;
        Assert.False(v.Estado().Desactualizado);        // pasado el intervalo, vuelve a mirar
        Assert.Equal(2, inspector.Llamadas);
    }

    [Fact]
    public void La_lista_va_de_1_a_la_version_requerida_sin_huecos()
    {
        Assert.Equal(Enumerable.Range(1, VerificadorEsquema.VersionRequerida), VerificadorEsquema.Migraciones.Select(m => m.Numero));
    }

    [Fact]
    public void Cada_archivo_de_la_carpeta_migrations_esta_en_la_lista_y_al_reves()
    {
        var carpeta = CarpetaMigraciones();
        var enDisco = Directory.GetFiles(carpeta, "*.sql").Select(f => Path.GetFileName(f)!).OrderBy(x => x).ToList();
        var enLista = VerificadorEsquema.Migraciones.Select(m => m.Archivo).OrderBy(x => x).ToList();
        Assert.Equal(enLista, enDisco);
    }

    [Fact]
    public void Los_marcadores_de_la_migracion_16_coinciden_con_los_del_codigo()
    {
        // La migración 16 registra las 1 a 15 con los mismos marcadores: si alguien cambia uno
        // solo de los dos lados, el registro y el aviso dirían cosas distintas.
        var sql = File.ReadAllText(Path.Combine(CarpetaMigraciones(), "add_permisos_especialidades_errores.sql"));
        string Normalizar(string s) => Regex.Replace(s, @"\s+", " ").Trim();
        foreach (var m in VerificadorEsquema.Migraciones.Where(x => x.Numero <= 15))
        {
            var patron = $@"SELECT {m.Numero}, N'{Regex.Escape(m.Archivo)}',\s*CASE WHEN (.*?) THEN 1 ELSE 0 END";
            var hallado = Regex.Match(sql, patron, RegexOptions.Singleline);
            Assert.True(hallado.Success, $"No se encontró el marcador de la migración {m.Numero} en la migración 16.");
            Assert.Equal(Normalizar(m.Marcador), Normalizar(hallado.Groups[1].Value));
        }
    }

    private static string CarpetaMigraciones()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "migrations")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "migrations");
    }
}
