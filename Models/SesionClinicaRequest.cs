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

    // De qué sesión de video viene (misma pareja origen/id que /video/acceso): si esa sesión se estaba grabando,
    // al guardar la sesión clínica la grabación se vincula a ella y pasa a "pendiente" (ver EncoladoGrabacionReglas).
    // Sin este campo, o si no hay grabación activa, la sesión se guarda igual, sin transcripción.
    public GrabacionRefRequest? Grabacion { get; set; }

    // Las emociones del PR 6: promedios + ventana + de qué sesión de video son. Se guardan solo con un consentimiento
    // vigente (ver GuardiaEmocionesService).
    public EmocionesSesionRequest? Emociones { get; set; }

    // El campo de antes del PR 6 (un texto libre con el promedio por emoción). Ya no se guarda: llega acá solo para poder
    // avisarle a una pantalla vieja que lo mande de la forma nueva.
    public string? HistorialEmociones { get; set; }

    // El cierre de sesión (migración 26). Los dos son opcionales: un pedido sin ellos se guarda como siempre.
    // La clave identifica el cierre (un GUID que genera la sala): repetir el pedido con la misma clave no duplica nada.
    public string? ClaveCierre { get; set; }
    // El profesional confirmó, con un clic en «Sí, se realizó», que la sesión se hizo. Ausente o false: el turno no se toca.
    public bool? ConfirmaRealizacion { get; set; }
}

// PUT /sesiones-clinicas/{id}/evolucion: la evolución de la historia clínica que el profesional vincula a la sesión (a mano).
public class VincularEvolucionRequest
{
    public int IdEvolucion { get; set; }
}

// Misma pareja (Origen, Id) que usa /video/acceso para identificar una sesión de video en curso.
public class GrabacionRefRequest
{
    public string? Origen { get; set; }
    public int? Id { get; set; }
}
