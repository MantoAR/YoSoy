public static class UnionDeTranscripcion
{
    private const long MsParaFundir = 2000;

    public static string ArmarTexto(long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs, IEnumerable<SegmentoTranscripcion> segmentos)
    {
        var cero = new[] { inicioPistaProfesionalMs, inicioPistaPacienteMs }.Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty(0).Min();

        var ordenados = segmentos
            .Select(seg =>
            {
                var baseMs = (seg.Pista == PistaGrabacion.Profesional ? inicioPistaProfesionalMs : inicioPistaPacienteMs) ?? 0;
                return new
                {
                    seg.Pista,
                    Texto = seg.Texto.Trim(),
                    InicioAbsMs = baseMs - cero + (long)Math.Round(seg.Inicio * 1000),
                    FinAbsMs = baseMs - cero + (long)Math.Round(seg.Fin * 1000)
                };
            })
            .Where(x => x.Texto.Length > 0)
            .OrderBy(x => x.InicioAbsMs)
            .ToList();

        var lineas = new List<Linea>();
        foreach (var s in ordenados)
        {
            var ultima = lineas.Count > 0 ? lineas[^1] : null;
            if (ultima is not null && ultima.Pista == s.Pista && s.InicioAbsMs - ultima.FinAbsMs < MsParaFundir)
            {
                ultima.Texto.Append(' ').Append(s.Texto);
                ultima.FinAbsMs = s.FinAbsMs;
            }
            else lineas.Add(new Linea(s.Pista, s.InicioAbsMs, s.FinAbsMs, new System.Text.StringBuilder(s.Texto)));
        }

        return string.Join("\n", lineas.Select(l =>
            $"[{Formatear(l.InicioMs)}] {(l.Pista == PistaGrabacion.Profesional ? "Profesional" : "Paciente")}: {l.Texto}"));
    }

    private static string Formatear(long ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"mm\:ss");

    private sealed class Linea
    {
        public string Pista { get; }
        public long InicioMs { get; }
        public long FinAbsMs { get; set; }
        public System.Text.StringBuilder Texto { get; }

        public Linea(string pista, long inicioMs, long finAbsMs, System.Text.StringBuilder texto)
        {
            Pista = pista; InicioMs = inicioMs; FinAbsMs = finAbsMs; Texto = texto;
        }
    }
}
