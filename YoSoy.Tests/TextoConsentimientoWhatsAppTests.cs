// El texto del consentimiento de WhatsApp: el archivo embebido de la versión provisoria existe, trae lo que tiene que traer (qué se manda, qué
// NO se manda, que es voluntario y revocable, cómo darse de baja), lo que falta decidir queda marcado y no inventado, y una versión publicada
// no se edita (cada aceptación guarda el SHA-256 del texto exacto que se mostró).
public class TextoConsentimientoWhatsAppTests
{
    private static TextoConsentimiento Provisorio()
    {
        var opciones = new OpcionesFijas();
        var t = new TextoConsentimientoWhatsApp(opciones, Microsoft.Extensions.Logging.Abstractions.NullLogger<TextoConsentimientoWhatsApp>.Instance).Actual();
        Assert.NotNull(t);
        return t;
    }

    [Fact]
    public void La_version_por_defecto_existe_y_esta_marcada_como_provisoria()
    {
        var t = Provisorio();

        Assert.Equal("v0-provisorio", t.Version);
        Assert.True(t.Provisorio);
        Assert.Equal("Avisos de tus turnos por WhatsApp", t.Titulo);
        Assert.Contains("TEXTO PROVISORIO", string.Join(" ", t.Parrafos));
        Assert.Equal(64, t.Hash.Length);
        Assert.Equal(7, t.Parrafos.Count);
    }

    [Fact]
    public void El_texto_dice_lo_que_hay_que_decirle_al_paciente()
    {
        var todo = string.Join("\n", Provisorio().Parrafos);

        // Qué se manda, qué no, que es voluntario y revocable, cómo darse de baja (y volver), qué pasa si cambia de número, los menores,
        // que WhatsApp es de un tercero y los derechos.
        foreach (var frase in new[]
        {
            "al celular que cargaste en tu perfil", "el día y la hora de tu turno", "nada más sobre tu atención",
            "no traen datos clínicos", "ni el motivo de tu consulta", "ni la especialidad", "ni el nombre de tu profesional",
            "Es voluntario", "no cambia tu atención", "desactivalos desde tu perfil", "respondé BAJA", "respondé también ALTA",
            "Si cambiás de celular", "menor de 18 años", "madre, padre o tutor", "Meta",
            "acceso, rectificación o supresión", "Ley 25.326", "Ley 26.529"
        })
            Assert.Contains(frase, todo);
    }

    [Fact]
    public void El_texto_no_promete_lo_que_la_aplicacion_no_hace()
    {
        var todo = string.Join("\n", Provisorio().Parrafos);

        // Nada de "siempre", "garantizamos" ni "nunca": el único compromiso fuerte es lo clínico, y está acotado a los avisos.
        Assert.DoesNotContain("garantiz", todo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nunca", todo, StringComparison.OrdinalIgnoreCase);
        // No habla de lo que se hace con los datos de otro modo que el de los avisos.
        Assert.DoesNotContain("grabaci", todo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lo_que_falta_decidir_queda_marcado_entre_corchetes_y_no_inventado()
    {
        var todo = string.Join("\n", Provisorio().Parrafos);

        Assert.Contains("[DATOS DE CONTACTO DE LA INSTITUCIÓN: A COMPLETAR]", todo);
        Assert.Contains("[REFERENCIAS A VALIDAR POR EL EQUIPO LEGAL]", todo);
    }

    // Una versión publicada NO se edita: un texto nuevo es una versión nueva (archivo nuevo y Mensajeria:ConsentimientoWhatsApp:VersionTexto).
    // Cada aceptación guarda este hash, así que cambiar el archivo dejaría registros que ya no se pueden contrastar con lo que se mostró.
    // Si este test falla porque editaste v0-provisorio.txt: dejalo como estaba y creá v1.txt.
    [Fact]
    public void La_version_v0_provisorio_no_cambio()
    {
        Assert.Equal("ef9676eb31a07d4f2da1dd8fd2890593dbac39e295a43c74ffb1dba004cf63ef", Provisorio().Hash);
    }

    [Fact]
    public void El_archivo_del_repo_es_el_mismo_que_el_embebido_con_CRLF_o_LF()
    {
        var ruta = Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "Services", "Mensajeria", "Consentimiento", "v0-provisorio.txt");
        var delArchivo = TextoConsentimientoVideo.Interpretar("v0-provisorio", File.ReadAllText(ruta));
        var conCrlf = TextoConsentimientoVideo.Interpretar("v0-provisorio", File.ReadAllText(ruta).Replace("\r\n", "\n").Replace("\n", "\r\n"));

        Assert.Equal(Provisorio().Hash, delArchivo.Hash);
        Assert.Equal(delArchivo.Hash, conCrlf.Hash);
    }

    [Theory]
    [InlineData("v-que-no-existe")]
    [InlineData("../v0-provisorio")]
    [InlineData("")]
    public void Una_version_que_no_existe_o_no_es_valida_no_da_texto_y_avisa_en_el_log(string version)
    {
        var opciones = new OpcionesFijas();
        opciones.CurrentValue.ConsentimientoWhatsApp.VersionTexto = version;
        var logs = new LogsCapturados();
        var servicio = new TextoConsentimientoWhatsApp(opciones, logs.CreateLogger<TextoConsentimientoWhatsApp>());

        Assert.Null(servicio.Actual());
        Assert.Contains(logs.Lineas, l => l.StartsWith("Warning|"));
    }

    [Fact]
    public void Cambiar_la_version_configurada_cambia_el_texto_que_se_sirve()
    {
        var opciones = new OpcionesFijas();
        var servicio = new TextoConsentimientoWhatsApp(opciones, Microsoft.Extensions.Logging.Abstractions.NullLogger<TextoConsentimientoWhatsApp>.Instance);
        Assert.Equal("v0-provisorio", servicio.Actual()!.Version);

        opciones.CurrentValue.ConsentimientoWhatsApp.VersionTexto = "no-existe-todavia";
        Assert.Null(servicio.Actual());
    }
}
