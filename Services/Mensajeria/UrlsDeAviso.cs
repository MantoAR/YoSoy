// Las direcciones que llevan los avisos de turno. La base es la que se configura (Mensajeria:UrlIngresoTurno, en el servidor: Mensajeria__UrlIngresoTurno); si no hay ninguna, la pública de la clínica.
// Antes el recordatorio traía lo que hubiera en la configuración tal cual, y con la de desarrollo (localhost) el paciente recibía un link que no abre desde su celular.
public static class UrlsDeAviso
{
    public const string BasePublica = "https://yosoytelemedicina.tech";
    public const string RutaIngreso = "/pantallas/index.html";

    /// <summary>La pantalla de ingreso: la configurada, o la pública si no hay una dirección absoluta (http/https) configurada.</summary>
    public static string Ingreso(MensajeriaOptions op) =>
        Uri.TryCreate((op.UrlIngresoTurno ?? "").Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u.ToString()
            : BasePublica + RutaIngreso;

    /// <summary>
    /// Directo a la sala de ese turno: la pantalla de ingreso con «?return=» a la sala (el login lleva ahí a quien entra; ver YS.rutaRetornoValida). Quien ya está adentro la abre desde Inicio igual.
    /// El destino va codificado y es siempre la misma ruta fija con el número del turno: no hay nada del turno más que su id.
    /// </summary>
    public static string SalaDelTurno(MensajeriaOptions op, int idTurno)
    {
        var ingreso = Ingreso(op);
        var destino = $"/pantallas/Pantalla_Paciente/sesion_video.html?turno={idTurno}";
        return ingreso + (ingreso.Contains('?') ? "&" : "?") + "return=" + Uri.EscapeDataString(destino);
    }
}
