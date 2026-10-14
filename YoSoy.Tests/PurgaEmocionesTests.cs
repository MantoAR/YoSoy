// La purga por plazo de retención: arranca deshabilitada (solo cuenta y lo dice en el log), con el flag anula solo lo
// vencido y solo las emociones, respeta el borde del plazo, trabaja por lotes y deja una sola auditoría con la cantidad.
public class PurgaEmocionesTests
{
    // Sesiones clínicas en memoria. Anular solo toca las emociones: las notas quedan como estaban.
    private sealed class FakePurgaRepo : IEmocionesPurgaRepositorio
    {
        public sealed class Fila
        {
            public int Id;
            public DateTime FechaHora;
            public string? Emociones;
            public string Notas = "";
            public string? Motivo;
            public DateTime? AnuladasUtc;
        }

        public readonly List<Fila> Filas = new();
        public int LlamadasAnular;

        public int ContarVencidas(DateTime corteLocal) => Filas.Count(f => f.Emociones is not null && f.FechaHora < corteLocal);

        public int AnularVencidas(DateTime corteLocal, int lote, DateTime ahoraUtc)
        {
            LlamadasAnular++;
            var a = Filas.Where(f => f.Emociones is not null && f.FechaHora < corteLocal).Take(lote).ToList();
            foreach (var f in a) { f.Emociones = null; f.Motivo = "retencion"; f.AnuladasUtc = ahoraUtc; }
            return a.Count;
        }
    }

    private static readonly DateTime Hoy = new(2031, 6, 15, 12, 0, 0);

    private static (PurgaEmocionesBackgroundService Servicio, FakePurgaRepo Repo, FakeAuditoriaEmociones Auditoria,
        OpcionesFijas<VideoOptions> Opciones, LogsCapturados Logs) Armar(Action<VideoEmocionesOptions>? ajustar = null)
    {
        var repo = new FakePurgaRepo();
        var auditoria = new FakeAuditoriaEmociones();
        var opciones = new OpcionesFijas<VideoOptions>();
        ajustar?.Invoke(opciones.CurrentValue.Emociones);
        var logs = new LogsCapturados();
        var servicio = new PurgaEmocionesBackgroundService(repo, auditoria, opciones, new RelojFijo(Hoy),
            logs.CreateLogger<PurgaEmocionesBackgroundService>());
        return (servicio, repo, auditoria, opciones, logs);
    }

    private static DateTime CorteConRetencion(int dias) => Hoy.AddDays(-dias);

    // ───────────── Valores por defecto ─────────────

    [Fact]
    public void Por_defecto_la_purga_esta_deshabilitada_y_el_plazo_es_de_3650_dias()
    {
        var o = new VideoEmocionesOptions();

        Assert.False(o.PurgaHabilitada);
        Assert.Equal(3650, o.RetencionDias);
        Assert.Equal(24, o.PurgaCadaHoras);
    }

    // ───────────── Deshabilitada: solo informa ─────────────

    [Fact]
    public void Deshabilitada_no_modifica_nada_y_dice_en_el_log_cuantas_filas_purgaria()
    {
        var (s, repo, auditoria, _, logs) = Armar();
        repo.Filas.Add(new() { Id = 1, FechaHora = CorteConRetencion(3650).AddDays(-10), Emociones = "ys1.k1.x", Notas = "nota 1" });
        repo.Filas.Add(new() { Id = 2, FechaHora = CorteConRetencion(3650).AddDays(-1), Emociones = "ys1.k1.y", Notas = "nota 2" });
        repo.Filas.Add(new() { Id = 3, FechaHora = Hoy.AddDays(-30), Emociones = "ys1.k1.z", Notas = "nota 3" });

        var r = s.EjecutarUnaVez();

        Assert.False(r.Habilitada);
        Assert.Equal(2, r.Vencidas);
        Assert.Equal(0, r.Anuladas);
        Assert.Equal(0, repo.LlamadasAnular);
        Assert.All(repo.Filas, f => Assert.NotNull(f.Emociones));
        Assert.Empty(auditoria.Registros);
        var linea = Assert.Single(logs.Lineas, l => l.Contains("DESHABILITADA"));
        Assert.Contains("2 sesión(es)", linea);
        Assert.Contains("Video__Emociones__PurgaHabilitada=true", linea);
    }

    // ───────────── Habilitada ─────────────

    [Fact]
    public void Habilitada_anula_solo_las_emociones_vencidas_y_deja_las_notas()
    {
        var (s, repo, auditoria, _, _) = Armar(o => o.PurgaHabilitada = true);
        var vieja = new FakePurgaRepo.Fila { Id = 1, FechaHora = CorteConRetencion(3650).AddDays(-5), Emociones = "ys1.k1.x", Notas = "nota de la vieja" };
        var reciente = new FakePurgaRepo.Fila { Id = 2, FechaHora = Hoy.AddDays(-1), Emociones = "ys1.k1.y", Notas = "nota reciente" };
        var sinEmociones = new FakePurgaRepo.Fila { Id = 3, FechaHora = CorteConRetencion(3650).AddDays(-100), Emociones = null, Notas = "solo notas" };
        repo.Filas.AddRange(new[] { vieja, reciente, sinEmociones });

        var r = s.EjecutarUnaVez();

        Assert.True(r.Habilitada);
        Assert.Equal(1, r.Anuladas);
        Assert.Null(vieja.Emociones);
        Assert.Equal("retencion", vieja.Motivo);
        Assert.Equal(Hoy, vieja.AnuladasUtc);
        Assert.Equal("nota de la vieja", vieja.Notas);        // las notas clínicas no se tocan
        Assert.NotNull(reciente.Emociones);
        Assert.Equal("solo notas", sinEmociones.Notas);
        var a = Assert.Single(auditoria.Registros);
        Assert.Equal("purgadas", a.Tipo);
        Assert.Null(a.IdUsuario);                              // la purga automática no tiene una persona detrás
        Assert.Equal(1, a.Cantidad);
        Assert.Equal("retencion", a.Detalle);
    }

    [Fact]
    public void El_borde_del_plazo_es_estricto_exactamente_el_plazo_todavia_no_se_purga()
    {
        var (s, repo, _, _, _) = Armar(o => { o.PurgaHabilitada = true; o.RetencionDias = 100; });
        var justo = new FakePurgaRepo.Fila { Id = 1, FechaHora = CorteConRetencion(100), Emociones = "a" };
        var unSegundoAntes = new FakePurgaRepo.Fila { Id = 2, FechaHora = CorteConRetencion(100).AddSeconds(-1), Emociones = "b" };
        var unSegundoDespues = new FakePurgaRepo.Fila { Id = 3, FechaHora = CorteConRetencion(100).AddSeconds(1), Emociones = "c" };
        repo.Filas.AddRange(new[] { justo, unSegundoAntes, unSegundoDespues });

        var r = s.EjecutarUnaVez();

        Assert.Equal(1, r.Anuladas);
        Assert.NotNull(justo.Emociones);
        Assert.Null(unSegundoAntes.Emociones);
        Assert.NotNull(unSegundoDespues.Emociones);
    }

    [Fact]
    public void Trabaja_por_lotes_hasta_terminar_y_audita_una_sola_vez_el_total()
    {
        var (s, repo, auditoria, _, _) = Armar(o => { o.PurgaHabilitada = true; o.PurgaLote = 500; o.RetencionDias = 30; });
        for (var i = 0; i < 1200; i++)
            repo.Filas.Add(new() { Id = i, FechaHora = Hoy.AddDays(-60), Emociones = "ys1.k1.x", Notas = $"n{i}" });

        var r = s.EjecutarUnaVez();

        Assert.Equal(1200, r.Anuladas);
        Assert.Equal(3, repo.LlamadasAnular);                  // 500 + 500 + 200
        Assert.All(repo.Filas, f => Assert.Null(f.Emociones));
        Assert.All(repo.Filas, f => Assert.Equal($"n{f.Id}", f.Notas));
        Assert.Equal(1200, Assert.Single(auditoria.Registros).Cantidad);
    }

    [Fact]
    public void Repetirla_no_vuelve_a_anular_ni_a_auditar()
    {
        var (s, repo, auditoria, _, _) = Armar(o => o.PurgaHabilitada = true);
        repo.Filas.Add(new() { Id = 1, FechaHora = Hoy.AddDays(-5000), Emociones = "ys1.k1.x" });
        s.EjecutarUnaVez();

        var segunda = s.EjecutarUnaVez();

        Assert.Equal(0, segunda.Vencidas);
        Assert.Equal(0, segunda.Anuladas);
        Assert.Single(auditoria.Registros);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Sin_plazo_de_retencion_no_se_purga_nunca_ni_con_el_flag_prendido(int dias)
    {
        var (s, repo, auditoria, _, logs) = Armar(o => { o.PurgaHabilitada = true; o.RetencionDias = dias; });
        repo.Filas.Add(new() { Id = 1, FechaHora = new DateTime(2000, 1, 1), Emociones = "ys1.k1.x" });

        var r = s.EjecutarUnaVez();

        Assert.Equal(0, r.Anuladas);
        Assert.NotNull(repo.Filas[0].Emociones);
        Assert.Empty(auditoria.Registros);
        Assert.Contains(logs.Lineas, l => l.Contains("no se purga nada"));
    }

    [Fact]
    public void Cambiar_el_flag_en_caliente_se_toma_en_la_proxima_pasada()
    {
        var (s, repo, _, opciones, _) = Armar();
        repo.Filas.Add(new() { Id = 1, FechaHora = Hoy.AddDays(-5000), Emociones = "ys1.k1.x" });

        var antes = s.EjecutarUnaVez();
        opciones.CurrentValue.Emociones.PurgaHabilitada = true;
        var despues = s.EjecutarUnaVez();

        Assert.Equal(0, antes.Anuladas);
        Assert.Equal(1, despues.Anuladas);
    }

    [Fact]
    public void El_log_de_la_purga_no_incluye_ids_ni_contenido()
    {
        var (s, repo, _, _, logs) = Armar(o => o.PurgaHabilitada = true);
        repo.Filas.Add(new() { Id = 98765, FechaHora = Hoy.AddDays(-5000), Emociones = "ys1.k1.CONTENIDO-SECRETO", Notas = "nota-privada" });

        s.EjecutarUnaVez();

        Assert.DoesNotContain("98765", logs.Todo);
        Assert.DoesNotContain("CONTENIDO-SECRETO", logs.Todo);
        Assert.DoesNotContain("nota-privada", logs.Todo);
    }
}
