using System.Linq;
using Microsoft.Extensions.Hosting;

public class RecordatorioSesionService : BackgroundService
{
    private readonly IConfiguration _cfg;

    public RecordatorioSesionService(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { Procesar(); }
            catch (Exception ex) { Console.WriteLine($"RecordatorioSesion: error en el ciclo ({ex.Message})."); }

            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private void Procesar()
    {
        int margen = int.TryParse(_cfg["Sesion:MargenIngresoMinutos"], out var m) ? m : 5;
        var ahora = DateTime.Now;
        var invitacion = new InvitacionSesionService(_cfg);
        var sesionDao = new SesionVideoDAO();
        var eventoDao = new SesionVideoEventoDAO();

        foreach (var t in new TurnoDAO().ObtenerTodos())
        {
            if (t.IdEstadoTurno == 3) continue;

            var (inicio, duracion) = invitacion.Ventana(t);
            var fin = inicio.AddMinutes(duracion);
            if (ahora < inicio.AddMinutes(-margen) || ahora > fin) continue;

            var sesion = sesionDao.ObtenerOCrearPorTurno(t.IdTurno, fin, out var creada);
            if (creada)
                eventoDao.Insertar(new SesionVideoEvento { IdSesion = sesion.IdSesion, Tipo = "creada", Detalle = $"Auto turno {t.IdTurno}" });

            if (eventoDao.ObtenerPorSesion(sesion.IdSesion).Any(e => e.Tipo == "invitacion")) continue;

            eventoDao.Insertar(new SesionVideoEvento { IdSesion = sesion.IdSesion, Tipo = "invitacion", Detalle = "Aviso automatico al iniciar el turno" });
            try
            {
                invitacion.EnviarParaTurno(t);
                Console.WriteLine($"RecordatorioSesion: invitaciones enviadas para turno {t.IdTurno} (sesion {sesion.IdSesion}).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RecordatorioSesion: no se pudo enviar el mail del turno {t.IdTurno} ({ex.Message}).");
            }
        }
    }
}
