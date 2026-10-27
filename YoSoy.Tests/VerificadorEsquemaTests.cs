using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Aviso de migraciones faltantes (etapa 1: errores con código y auditoría inalterable).
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
        Assert.Contains("Faltan las migraciones 6 (add_consentimiento_telemedicina.sql), 15 (add_consentimiento_video.sql)", e.Mensaje);
    }

    [Fact]
    public void Sin_conexion_no_se_considera_desactualizada()
    {
        var e = new VerificadorEsquema(new InspectorFalso { SinConexion = true }, new Reloj()).Estado();
        Assert.False(e.Verificado);
        Assert.False(e.Desactualizado);   // no se bloquea: cada endpoint falla como antes, con su código de error
    }

    [Fact]
    public void Las_migraciones_opcionales_no_cuentan_como_faltantes()
    {
        // La 16 (consentimiento de WhatsApp, dormido), la 17 (solo la lee el modo Personal) y la 25 (interruptor de IA por conversación) y la 32 (propuesta automática de profesional y horario: sin ella aceptar un formulario anda como antes) no frenan nada: producción puede no tener la 16 ni la 25.
        var inspector = new InspectorFalso();
        inspector.Faltan.UnionWith(new[] { 16, 17, 25, 26, 27, 28, 32 });
        var e = new VerificadorEsquema(inspector, new Reloj()).Estado();
        Assert.True(e.Verificado);
        Assert.False(e.Desactualizado);
        Assert.Equal(new[] { 16, 17, 25, 26, 27, 28, 32 }, VerificadorEsquema.Migraciones.Where(m => m.Opcional).Select(m => m.Numero));
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
        inspector.Faltan.Add(18);
        var reloj = new Reloj();
        var v = new VerificadorEsquema(inspector, reloj);

        Assert.True(v.Estado().Desactualizado);
        inspector.Faltan.Clear();                       // se corrió la migración 18
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
        // seed_catalogos.sql no es una migración: es el contenido de arranque de un servidor nuevo (ver docs/DEPLOY.md).
        var enDisco = Directory.GetFiles(carpeta, "*.sql").Select(f => Path.GetFileName(f)!).Where(f => f != "seed_catalogos.sql").OrderBy(x => x).ToList();
        var enLista = VerificadorEsquema.Migraciones.Select(m => m.Archivo).OrderBy(x => x).ToList();
        Assert.Equal(enLista, enDisco);
    }

    [Fact]
    public void Los_marcadores_de_la_migracion_18_coinciden_con_los_del_codigo()
    {
        // La migración 18 registra las 1 a 17 con los mismos marcadores: si alguien cambia uno
        // solo de los dos lados, el registro y el aviso dirían cosas distintas.
        var sql = File.ReadAllText(Path.Combine(CarpetaMigraciones(), "add_registro_errores_version_esquema.sql"));
        string Normalizar(string s) => Regex.Replace(s, @"\s+", " ").Trim();
        foreach (var m in VerificadorEsquema.Migraciones.Where(x => x.Numero <= 17))
        {
            var patron = $@"SELECT {m.Numero}, N'{Regex.Escape(m.Archivo)}',\s*CASE WHEN (.*?) THEN 1 ELSE 0 END";
            var hallado = Regex.Match(sql, patron, RegexOptions.Singleline);
            Assert.True(hallado.Success, $"No se encontró el marcador de la migración {m.Numero} en la migración 18.");
            Assert.Equal(Normalizar(m.Marcador), Normalizar(hallado.Groups[1].Value));
        }
    }

    // ── El middleware: viene APAGADO. Prendido, corta con 503 cuando la base responde y le falta una migración ─────────────────

    private class InspectorQueNoTieneNada : IInspectorEsquema
    {
        public IReadOnlyList<bool> Evaluar(IReadOnlyList<string> condiciones) => condiciones.Select(_ => false).ToList();
    }

    private static WebApplicationFactory<Program> FabricaConBaseVacia(bool? verificar) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            if (verificar is bool v)
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Esquema:Verificar"] = v ? "true" : "false" }));
            b.ConfigureServices(s => { s.RemoveAll<IInspectorEsquema>(); s.AddSingleton<IInspectorEsquema>(new InspectorQueNoTieneNada()); });
        });

    [Fact]
    public async Task Apagado_aunque_falten_migraciones_no_bloquea_nada()
    {
        using var fabrica = FabricaConBaseVacia(verificar: false);
        var r = await fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/");
        Assert.NotEqual(System.Net.HttpStatusCode.ServiceUnavailable, r.StatusCode);
    }

    [Fact]
    public async Task Sin_configurar_nada_viene_apagado()
    {
        using var fabrica = FabricaConBaseVacia(verificar: null);
        var r = await fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/");
        Assert.NotEqual(System.Net.HttpStatusCode.ServiceUnavailable, r.StatusCode);
    }

    [Fact]
    public async Task Prendido_y_con_migraciones_faltantes_responde_503_con_el_detalle()
    {
        using var fabrica = FabricaConBaseVacia(verificar: true);
        var r = await fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/");
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.Contains("ESQUEMA_DESACTUALIZADO", cuerpo);
        Assert.Contains("add_auditoria_inalterable.sql", cuerpo);
        Assert.DoesNotContain("add_consentimiento_whatsapp.sql", cuerpo);   // opcional: no se informa como falta
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
