// Lo que manda la pantalla del profesional para guardar una sesión clínica (POST /sesiones-clinicas). A partir del PR 6 el
// pedido ya no se enlaza directo a la entidad SesionClinica: así nada que no esté acá (la fecha, el id, el consentimiento
// con el que se guardaron las emociones) puede venir del cliente.
public class SesionClinicaRequest
{
    public int IdPaciente { get; set; }

    // Solo lo toma el Configurador; para un profesional la sesión queda a su nombre, salga lo que salga del cuerpo.
    public int? IdProfesional { get; set; }

    public int? IdTurno { get; set; }
    public string? Transcripcion { get; set; }
    public string? NotasEvolucion { get; set; }
    public string? TipoCierre { get; set; }

    // Las emociones del PR 6: promedios + ventana + de qué sesión de video son. Se guardan solo con un consentimiento
    // vigente (ver GuardiaEmocionesService).
    public EmocionesSesionRequest? Emociones { get; set; }

    // El campo de antes del PR 6 (un texto libre con el promedio por emoción). Ya no se guarda: llega acá solo para poder
    // avisarle a una pantalla vieja que lo mande de la forma nueva.
    public string? HistorialEmociones { get; set; }
}
