// El estado de la sala de un turno (por abrir / abierta / cerrada) en UN solo lugar. Lo usan la API de acceso a la sala (AccesoSalaService), los primeros pasos del paciente y el endpoint
// GET /salas/estado que leen los paneles del profesional y del paciente: así ninguna pantalla vuelve a decidirlo por su cuenta. Docs: docs/ESTADO_DE_SALA.md.
public enum FaseSala { PorAbrir, Abierta, Cerrada }

public static class EstadoDeSala
{
    public const string PorAbrir = "por-abrir";
    public const string Abierta = "abierta";
    public const string Cerrada = "cerrada";

    // TODO-VALIDAR: cuánto después de empezar un turno deja de mostrarse como «próximo turno» del paciente (la sala sigue la regla de VideoOptions; esto es solo para esa tarjeta, que no
    // tiene a mano la duración de la franja). Hoy: la duración por defecto de una sesión + el margen de cierre de la sala.
    public const int MinutosParaOcultarProximoTurno = 60;

    public static FaseSala Fase(DateTimeOffset desde, DateTimeOffset hasta, DateTimeOffset ahora) =>
        ahora < desde ? FaseSala.PorAbrir : ahora >= hasta ? FaseSala.Cerrada : FaseSala.Abierta;

    /// <summary>Lo mismo con horas de pared de la clínica (sin zona), para los paneles y los primeros pasos.</summary>
    public static (FaseSala Fase, DateTime Desde, DateTime Hasta) Evaluar(DateTime inicio, int? duracionMinutos, DateTime ahora, VideoOptions o)
    {
        var duracion = duracionMinutos is > 0 ? duracionMinutos.Value : o.DuracionPorDefectoMinutos;
        var antes = TimeSpan.FromMinutes(AccesoSalaService.Acotar(o.Ventana.MinutosAntes, 0, AccesoSalaService.MaxVentanaMinutos));
        var despues = TimeSpan.FromMinutes(AccesoSalaService.Acotar(o.Ventana.MinutosDespues, 0, AccesoSalaService.MaxVentanaMinutos));
        var desde = inicio - antes;
        var hasta = inicio + TimeSpan.FromMinutes(AccesoSalaService.Acotar(duracion, 1, AccesoSalaService.MaxDuracionMinutos)) + despues;
        var fase = ahora < desde ? FaseSala.PorAbrir : ahora >= hasta ? FaseSala.Cerrada : FaseSala.Abierta;
        return (fase, desde, hasta);
    }

    public static string Nombre(FaseSala f) => f switch { FaseSala.PorAbrir => PorAbrir, FaseSala.Abierta => Abierta, _ => Cerrada };

    /// <summary>
    /// «El turno entrable» de una persona, en UN solo lugar (GET /salas/entrable lo lee el Inicio y el menú «Sala de video» del paciente y del profesional): el turno Confirmado cuya sala está
    /// ABIERTA ahora (fase Abierta de <see cref="Evaluar"/>, la misma regla que AccesoSalaService); con más de uno, el que empieza más cerca de este momento. <c>Proximo</c> es el próximo turno
    /// Confirmado que todavía no abrió (el primero por fecha y hora): para decir «la sala abre a las…». Pendiente, Cancelado y los turnos ya cerrados no cuentan.
    /// </summary>
    public static (TurnoConNombres? Entrable, TurnoConNombres? Proximo) Entrable(IEnumerable<TurnoConNombres> turnos, DateTime ahora, VideoOptions o)
    {
        TurnoConNombres? entrable = null, proximo = null;
        foreach (var t in turnos.OrderBy(x => x.Turno.Fecha).ThenBy(x => x.Turno.Hora))
        {
            if (TextoNormalizado.Normalizar(t.Turno.Estado) != AccesoSalaService.EstadoTurnoConfirmado) continue;
            var inicio = t.Turno.Fecha.Date + t.Turno.Hora;
            var fase = Evaluar(inicio, t.Turno.DuracionMinutos, ahora, o).Fase;
            if (fase == FaseSala.Abierta)
            {
                if (entrable is null || Math.Abs((inicio - ahora).Ticks) < Math.Abs((entrable.Turno.Fecha.Date + entrable.Turno.Hora - ahora).Ticks)) entrable = t;
            }
            else if (fase == FaseSala.PorAbrir && proximo is null) proximo = t;
        }
        return (entrable, proximo);
    }
}
