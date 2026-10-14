using System.Security.Cryptography;
using System.Text;

// El texto del consentimiento: se interpreta igual con CRLF o LF, el hash es el SHA-256 del texto exacto, el archivo
// embebido de la versión provisoria existe y trae lo que tiene que traer, y una versión publicada no se edita.
public class TextoConsentimientoVideoTests
{
    private static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    [Fact]
    public void Interpreta_titulo_y_parrafos_y_el_hash_es_el_sha256_del_texto_normalizado()
    {
        var t = TextoConsentimientoVideo.Interpretar("v1", "Título\n\nPrimero.\n\nSegundo, en\ndos líneas.\n");

        Assert.Equal("Título", t.Titulo);
        Assert.Equal(new[] { "Primero.", "Segundo, en dos líneas." }, t.Parrafos);
        Assert.Equal(Sha256("Título\n\nPrimero.\n\nSegundo, en\ndos líneas."), t.Hash);
        Assert.False(t.Provisorio);
    }

    [Fact]
    public void El_hash_no_depende_de_los_saltos_de_linea_ni_del_bom_ni_del_final()
    {
        var lf = TextoConsentimientoVideo.Interpretar("v1", "Título\n\nUno.\n\nDos.\n");
        var crlf = TextoConsentimientoVideo.Interpretar("v1", "Título\r\n\r\nUno.\r\n\r\nDos.\r\n");
        var conBom = TextoConsentimientoVideo.Interpretar("v1", "﻿Título\n\nUno.\n\nDos.");

        Assert.Equal(lf.Hash, crlf.Hash);
        Assert.Equal(lf.Hash, conBom.Hash);
        Assert.Equal(lf.Parrafos, crlf.Parrafos);
    }

    [Fact]
    public void Un_cambio_de_una_letra_cambia_el_hash()
    {
        var a = TextoConsentimientoVideo.Interpretar("v1", "Título\n\nAceptás.");
        var b = TextoConsentimientoVideo.Interpretar("v1", "Título\n\nAceptas.");

        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Theory]
    [InlineData("v0-provisorio", true)]
    [InlineData("V1-PROVISORIO-legal", true)]
    [InlineData("v1", false)]
    public void Se_marca_como_provisorio_por_el_nombre_de_la_version(string version, bool esperado)
    {
        Assert.Equal(esperado, TextoConsentimientoVideo.Interpretar(version, "T\n\nP.").Provisorio);
    }

    // ───────────── El archivo embebido de la versión provisoria ─────────────

    private static TextoConsentimiento Provisorio()
    {
        var opciones = new OpcionesFijas<VideoOptions>();
        var t = new TextoConsentimientoVideo(opciones, Microsoft.Extensions.Logging.Abstractions.NullLogger<TextoConsentimientoVideo>.Instance).Actual();
        Assert.NotNull(t);
        return t;
    }

    [Fact]
    public void La_version_por_defecto_existe_y_esta_marcada_como_provisoria()
    {
        var t = Provisorio();

        Assert.Equal("v0-provisorio", t.Version);
        Assert.True(t.Provisorio);
        Assert.Contains("PROVISORIO", string.Join(" ", t.Parrafos));
        Assert.Equal(64, t.Hash.Length);
    }

    [Fact]
    public void El_texto_dice_lo_que_hay_que_decirle_al_paciente()
    {
        var todo = string.Join("\n", Provisorio().Parrafos);

        // Qué es, que no es un diagnóstico, qué se guarda y qué no, quién lo ve, que es voluntario y revocable.
        foreach (var frase in new[]
        {
            "estimación automática", "no es un diagnóstico", "No se guarda tu imagen", "cifrado", "equipo de atención",
            "voluntario", "Dejar de compartir mis expresiones", "se elimina", "acceso, rectificación o supresión",
            "Ley 25.326", "Ley 26.529", "Ley 26.657"
        })
            Assert.Contains(frase, todo);
    }

    [Fact]
    public void Lo_que_falta_decidir_queda_marcado_entre_corchetes_y_no_inventado()
    {
        var todo = string.Join("\n", Provisorio().Parrafos);

        Assert.Contains("[PLAZO DE CONSERVACIÓN: A DEFINIR POR EL EQUIPO LEGAL]", todo);
        Assert.Contains("[DATOS DE CONTACTO DE LA INSTITUCIÓN: A COMPLETAR]", todo);
        Assert.Contains("[REFERENCIAS A VALIDAR POR EL EQUIPO LEGAL]", todo);
    }

    // Una versión publicada NO se edita: un texto nuevo es una versión nueva (archivo nuevo y Video:Emociones:VersionTexto).
    // Cada decisión guarda este hash, así que cambiar el archivo dejaría registros que ya no se pueden contrastar con lo
    // que se mostró. Si este test falla porque editaste v0-provisorio.txt: dejalo como estaba y creá v1.txt.
    [Fact]
    public void La_version_v0_provisorio_no_cambio()
    {
        Assert.Equal("1493e6eb222948040cf273301dc5dbb073730a226fbb8f605805f3c4eac278b4", Provisorio().Hash);
    }

    [Theory]
    [InlineData("v-que-no-existe")]
    [InlineData("../v0-provisorio")]
    [InlineData("")]
    public void Una_version_que_no_existe_o_no_es_valida_no_da_texto(string version)
    {
        var opciones = new OpcionesFijas<VideoOptions>();
        opciones.CurrentValue.Emociones.VersionTexto = version;
        var logs = new LogsCapturados();
        var servicio = new TextoConsentimientoVideo(opciones, logs.CreateLogger<TextoConsentimientoVideo>());

        Assert.Null(servicio.Actual());
        Assert.Contains(logs.Lineas, l => l.StartsWith("Warning|"));
    }
}
