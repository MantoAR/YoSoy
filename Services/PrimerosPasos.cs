// «Primeros pasos» del paciente: el recorrido de fondo desde que se registra hasta su primera sesión. El estado de cada paso lo CALCULA el servidor
// con datos reales (consentimiento vigente, formulario, revisión de la recepcionista, profesional asignado, turno, sesión realizada): nunca se marca a mano.
//
//   registro → consentimiento firmado y vigente → «Sobre mí» (el formulario de admisión) → revisión de la recepcionista → profesional asignado
//            → turno → primera sesión
//
// Esta clase es la lógica pura (se prueba sin base de datos); PrimerosPasosDAO junta los datos y Endpoints/PrimerosPasosEndpoints.cs la expone.
// La entrevista de admisión ya no es parte del recorrido: la reemplaza el formulario que revisa la recepcionista.

/// <summary>Estados que puede tener la revisión del formulario (InformeAdmision.Estado). Los demás valores (los de la entrevista) son anteriores y se leen como «en revisión».</summary>
public static class EstadoInforme
{
    public const string Pendiente = "Pendiente";       // enviado, esperando la revisión de la recepcionista
    public const string Aprobado = "Aprobado";         // aceptado por la recepcionista
    public const string Devuelto = "Devuelto";         // devuelto con una aclaración: el paciente lo completa de nuevo
    public const string OtroRecurso = "Otro recurso";  // el ingreso se cierra: es el único caso de «no ingresa»
    public const string Derivado = "Derivado";         // ya tiene profesional asignado
}

public record InformePaso(string Estado, string? Aclaracion, string? MotivoCierre);
public record ProfesionalPaso(int IdProfesional, string Nombre, string? Especialidad);
public record TurnoPaso(int IdTurno, DateTime Inicio, string Estado, bool ConfirmadoPaciente, string? CreadoPor, string? CodigoSala);

/// <summary>Todo lo que el cálculo necesita saber del paciente. Sin base de datos para poder probarlo combinación por combinación.</summary>
public record DatosPrimerosPasos(
    EstadoTratamiento Consentimiento,
    InformePaso? Informe,
    ProfesionalPaso? Profesional,
    IReadOnlyList<TurnoPaso> Turnos);

public static class PasoEstado
{
    public const string Hecho = "hecho";
    public const string Actual = "actual";       // el paciente tiene que hacer algo ahora
    public const string Espera = "espera";       // le toca al equipo: el paciente no tiene nada que hacer
    public const string Pendiente = "pendiente"; // todavía no llegó su turno en el recorrido
    public const string Cerrado = "cerrado";     // el ingreso se cerró (otro recurso)
}

public static class PasoClave
{
    public const string Registro = "registro";
    public const string Consentimiento = "consentimiento";
    public const string Formulario = "formulario";
    public const string Revision = "revision";
    public const string Profesional = "profesional";
    public const string Turno = "turno";
    public const string Sesion = "sesion";
}

/// <summary>Lo que el botón principal le hace hacer al paciente. «Tipo» lo interpreta el panel (abrir la ventana de firma, el formulario, la agenda...).</summary>
public record AccionPaso(string Tipo, string Texto, int? IdTurno = null);

public record PasoCalculado(string Clave, string Titulo, string Estado, string Detalle, AccionPaso? Accion);

/// <summary>El turno confirmado al que se entra desde «Entrar a la sesión». «Habilitada» = ya pasó HabilitaDesde (hora de la clínica, la del servidor): el botón del panel está gris hasta entonces.</summary>
public record SesionPaso(int IdTurno, DateTime Inicio, DateTime HabilitaDesde, bool Habilitada, string? CodigoSala);

public record ResultadoPrimerosPasos(IReadOnlyList<PasoCalculado> Pasos, string? PasoActual, bool Cerrado, string? MotivoCierre, AccionPaso? Principal, string Mensaje, SesionPaso? Sesion = null)
{
    public int Hechos => Pasos.Count(p => p.Estado == PasoEstado.Hecho);
}

public static class AccionTipo
{
    public const string FirmarConsentimiento = "firmar-consentimiento";
    public const string CompletarFormulario = "completar-formulario";
    public const string ConfirmarTurno = "confirmar-turno";
    public const string VerTurnos = "ver-turnos";
    public const string EntrarSesion = "entrar-sesion";
}

public static class PrimerosPasos
{
    /// <summary>Cuántos minutos antes del turno se puede entrar a la sesión. La API de video (VideoOptions.Ventana.MinutosAntes) es la que lo exige; esto solo lo muestra.</summary>
    public static ResultadoPrimerosPasos Calcular(DatosPrimerosPasos d, DateTime ahora, int minutosAntesDeLaSesion = 10)
    {
        var pasos = new List<PasoCalculado>();
        SesionPaso? sesion = null;
        var informe = d.Informe;
        var cerrado = informe is { Estado: EstadoInforme.OtroRecurso };
        // Quien ya tiene profesional (por ejemplo llegó por el link público de su profesional) no necesita el formulario ni su revisión: esos pasos cuentan como hechos.
        var conProfesional = d.Profesional is not null;
        var omiteAdmision = conProfesional && informe is null;

        // 1 · Registro: si hay datos, el paciente existe.
        pasos.Add(new(PasoClave.Registro, "Te registraste", PasoEstado.Hecho, "Tu cuenta ya está creada.", null));

        // 2 · Consentimiento firmado y vigente (la misma decisión que usa el servidor para asignar profesional y confirmar turnos).
        var c = d.Consentimiento;
        if (c.Vigente)
            pasos.Add(new(PasoClave.Consentimiento, "Firmaste el consentimiento", PasoEstado.Hecho,
                c.FechaFirma is DateTime f ? $"Lo firmaste el {f:dd/MM/yyyy} a las {f:HH:mm} hs." : "Tu consentimiento está vigente.", null));
        else
            pasos.Add(new(PasoClave.Consentimiento, "Firmá el consentimiento", cerrado ? PasoEstado.Cerrado : PasoEstado.Actual, c.Motivo switch
            {
                ConsentimientoTratamiento.Vencido => "Tu consentimiento venció (pasó más de un año desde que lo firmaste). Firmalo de nuevo para seguir con tu atención.",
                ConsentimientoTratamiento.VersionAnterior => "El texto del consentimiento cambió. Leelo y firmalo de nuevo para seguir con tu atención.",
                ConsentimientoTratamiento.FaltaTutor => "Como sos menor de edad, el consentimiento lo firma tu madre, padre o tutor.",
                _ => "Es el permiso para atenderte por video. Lo necesitás para que te asignemos un profesional y para confirmar turnos."
            }, new(AccionTipo.FirmarConsentimiento, "Leer y firmar")));

        // 3 · «Sobre mí» (formulario de admisión).
        if (omiteAdmision)
            pasos.Add(new(PasoClave.Formulario, "Contanos sobre vos", PasoEstado.Hecho, "No hace falta: ya tenés profesional asignado.", null));
        else if (informe is null)
            pasos.Add(new(PasoClave.Formulario, "Contanos sobre vos", cerrado ? PasoEstado.Cerrado : PasoEstado.Actual,
                "Completá «Sobre mí». Con lo que nos cuentes, el equipo elige el profesional adecuado para vos.", new(AccionTipo.CompletarFormulario, "Completar «Sobre mí»")));
        else if (informe.Estado == EstadoInforme.Devuelto)
            pasos.Add(new(PasoClave.Formulario, "Completá tu formulario", PasoEstado.Actual,
                "La recepción necesita que aclares algo" + (string.IsNullOrWhiteSpace(informe.Aclaracion) ? "." : ": " + informe.Aclaracion), new(AccionTipo.CompletarFormulario, "Completar «Sobre mí»")));
        else
            pasos.Add(new(PasoClave.Formulario, "Completaste «Sobre mí»", PasoEstado.Hecho, "Recibimos tu formulario.", null));

        // 4 · Revisión de la recepcionista.
        if (omiteAdmision)
            pasos.Add(new(PasoClave.Revision, "Revisión de tu formulario", PasoEstado.Hecho, "No hace falta.", null));
        else if (informe is null)
            pasos.Add(new(PasoClave.Revision, "Revisión de tu formulario", PasoEstado.Pendiente, "Empieza cuando envíes «Sobre mí».", null));
        else switch (informe.Estado)
        {
            case EstadoInforme.OtroRecurso:
                pasos.Add(new(PasoClave.Revision, "Revisión de tu formulario", PasoEstado.Cerrado,
                    "Después de revisar tu formulario, la recepción te orientó hacia otro recurso" + (string.IsNullOrWhiteSpace(informe.MotivoCierre) ? "." : ": " + informe.MotivoCierre), null));
                break;
            case EstadoInforme.Devuelto:
                pasos.Add(new(PasoClave.Revision, "Revisión de tu formulario", PasoEstado.Pendiente, "Volvé a enviar tu formulario con la aclaración y lo revisamos de nuevo.", null));
                break;
            case EstadoInforme.Aprobado:
            case EstadoInforme.Derivado:
                pasos.Add(new(PasoClave.Revision, "Aceptamos tu formulario", PasoEstado.Hecho, "La recepción ya lo revisó.", null));
                break;
            default:   // Pendiente y los estados viejos de la entrevista
                pasos.Add(new(PasoClave.Revision, "Estamos revisando tu formulario", PasoEstado.Espera,
                    "No tenés que hacer nada por ahora: cuando la recepción lo revise te avisamos por la campanita y por mail.", null));
                break;
        }

        // 5 · Profesional asignado (nombre y especialidad).
        if (d.Profesional is { } prof)
            pasos.Add(new(PasoClave.Profesional, "Tenés profesional asignado", PasoEstado.Hecho,
                prof.Nombre + (string.IsNullOrWhiteSpace(prof.Especialidad) ? "" : " · " + prof.Especialidad), null));
        else
        {
            var revisado = omiteAdmision || informe is { Estado: EstadoInforme.Aprobado or EstadoInforme.Derivado };
            pasos.Add(new(PasoClave.Profesional, "Te asignamos un profesional", cerrado ? PasoEstado.Cerrado : revisado ? PasoEstado.Espera : PasoEstado.Pendiente,
                revisado ? "Estamos eligiendo el profesional adecuado para vos. Te avisamos apenas esté asignado." : "Se asigna después de la revisión de tu formulario.", null));
        }

        // 6 · Turno: confirmado o realizado = hecho. Uno propuesto por el profesional, a confirmar. Uno pedido por el paciente, esperando al profesional.
        var vivos = d.Turnos.Where(t => t.Estado != "Cancelado").OrderBy(t => t.Inicio).ToList();
        var confirmado = vivos.FirstOrDefault(t => t.Estado is "Confirmado" or "Realizado");
        var propuesto = vivos.FirstOrDefault(t => t.Estado == "Pendiente" && t.CreadoPor == "profesional" && !t.ConfirmadoPaciente);
        var pedido = vivos.FirstOrDefault(t => t.Estado == "Pendiente" && t.CreadoPor != "profesional");
        string Cuando(TurnoPaso t) => $"{t.Inicio:dd/MM/yyyy} a las {t.Inicio:HH:mm} hs";
        if (confirmado is not null)
            pasos.Add(new(PasoClave.Turno, "Tenés turno confirmado", PasoEstado.Hecho, "Tu turno: " + Cuando(confirmado) + ".", null));
        else if (d.Profesional is null)
            pasos.Add(new(PasoClave.Turno, "Confirmá tu primer turno", cerrado ? PasoEstado.Cerrado : PasoEstado.Pendiente, "Se habilita cuando tengas profesional asignado.", null));
        else if (propuesto is not null)
            pasos.Add(new(PasoClave.Turno, "Confirmá tu primer turno", PasoEstado.Actual,
                $"Tu profesional te propuso el turno del {Cuando(propuesto)}. Confirmalo para que quede agendado.", new(AccionTipo.ConfirmarTurno, "Confirmar turno", propuesto.IdTurno)));
        else if (pedido is not null)
            pasos.Add(new(PasoClave.Turno, "Esperando la confirmación de tu turno", PasoEstado.Espera,
                $"Pediste el turno del {Cuando(pedido)}. Tu profesional lo tiene que confirmar: te avisamos por la campanita.", null));
        else
            pasos.Add(new(PasoClave.Turno, "Pedí tu primer turno", PasoEstado.Actual,
                "Tu profesional te va a proponer el primer turno. Si preferís, pedilo vos en un horario libre.", new(AccionTipo.VerTurnos, "Ir a Turnos")));

        // 7 · Primera sesión.
        var realizado = vivos.FirstOrDefault(t => t.Estado == "Realizado");
        if (realizado is not null)
            pasos.Add(new(PasoClave.Sesion, "Hiciste tu primera sesión", PasoEstado.Hecho, "Tuviste tu sesión el " + Cuando(realizado) + ".", null));
        else if (confirmado is null)
            pasos.Add(new(PasoClave.Sesion, "Tu primera sesión", cerrado ? PasoEstado.Cerrado : PasoEstado.Pendiente, "Se habilita cuando confirmes tu primer turno.", null));
        else
        {
            var abre = confirmado.Inicio.AddMinutes(-minutosAntesDeLaSesion);
            var puede = ahora >= abre;
            sesion = new SesionPaso(confirmado.IdTurno, confirmado.Inicio, abre, puede, confirmado.CodigoSala);
            pasos.Add(new(PasoClave.Sesion, "Tu primera sesión", PasoEstado.Actual,
                puede ? "Ya podés entrar a la sala de video." : $"Podés entrar a la sala {minutosAntesDeLaSesion} minutos antes: desde las {abre:HH:mm} hs del {abre:dd/MM/yyyy}.",
                new(AccionTipo.EntrarSesion, "Entrar a la sesión", confirmado.IdTurno)));
        }

        // El paso actual es el primero que no está hecho; si le toca al equipo, el paciente no tiene botón. Con el consentimiento vencido o revocado después de
        // asignar un profesional, el primer paso sin hacer vuelve a ser el consentimiento: ahí queda el único botón.
        var sinHacer = pasos.FirstOrDefault(p => p.Estado != PasoEstado.Hecho);
        var actual = pasos.FirstOrDefault(p => p.Estado is PasoEstado.Actual or PasoEstado.Cerrado);
        if (cerrado) actual = pasos.First(p => p.Clave == PasoClave.Revision);
        var principal = cerrado ? null : actual?.Accion;
        var mensaje = cerrado ? "Tu ingreso se cerró: la recepción te orientó hacia otro recurso."
            : sinHacer is null ? "Completaste todos los primeros pasos."
            : actual is not null ? actual.Detalle
            : sinHacer.Detalle;
        return new(pasos, cerrado ? PasoClave.Revision : (actual ?? sinHacer)?.Clave, cerrado, cerrado ? informe!.MotivoCierre : null, principal, mensaje, sesion);
    }
}
