using Microsoft.Extensions.Configuration;

// La configuración de la lectura de emociones (sección Video:Emociones): los valores de appsettings.json son los mismos
// que los del código, la clave de cifrado NO está en el archivo, y los valores fuera de rango no rompen nada.
public class EmocionesConfiguracionTests
{
    [Fact]
    public void El_appsettings_trae_los_valores_documentados_y_ni_rastro_de_la_clave()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(raiz, "appsettings.json"), optional: false).Build();
        var enArchivo = new VideoEmocionesOptions();
        config.GetSection("Video:Emociones").Bind(enArchivo);
        var porDefecto = new VideoEmocionesOptions();

        Assert.Null(config["Video:Emociones:ClaveCifrado"]);   // la clave va solo por variable de entorno
        Assert.Equal("v0-provisorio", enArchivo.VersionTexto);
        Assert.Equal(3650, enArchivo.RetencionDias);
        Assert.False(enArchivo.PurgaHabilitada);               // la purga arranca deshabilitada
        Assert.False(enArchivo.PermitirEdadDesconocida);       // y sin fecha de nacimiento no se ofrece el análisis
        Assert.Equal(porDefecto.VersionTexto, enArchivo.VersionTexto);
        Assert.Equal(porDefecto.IdClave, enArchivo.IdClave);
        Assert.Equal(porDefecto.RetencionDias, enArchivo.RetencionDias);
        Assert.Equal(porDefecto.PurgaHabilitada, enArchivo.PurgaHabilitada);
        Assert.Equal(porDefecto.PurgaCadaHoras, enArchivo.PurgaCadaHoras);
        Assert.Equal(porDefecto.PurgaLote, enArchivo.PurgaLote);
        Assert.Equal(porDefecto.ConsultaCadaSegundos, enArchivo.ConsultaCadaSegundos);
        Assert.Equal(porDefecto.VigenciaSegundos, enArchivo.VigenciaSegundos);
        Assert.Equal(porDefecto.ToleranciaSegundos, enArchivo.ToleranciaSegundos);
        Assert.Equal(porDefecto.MuestrasMaxPorSegundo, enArchivo.MuestrasMaxPorSegundo);
        Assert.Equal(porDefecto.PermitirEdadDesconocida, enArchivo.PermitirEdadDesconocida);
        Assert.Equal(porDefecto.LimitePorMinuto, enArchivo.LimitePorMinuto);
    }

    [Fact]
    public void Ningun_archivo_versionado_de_configuracion_trae_una_clave_de_cifrado()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        foreach (var archivo in new[] { "appsettings.json", "appsettings.Development.json", "appsettings.Production.json" })
        {
            var ruta = Path.Combine(raiz, archivo);
            if (!File.Exists(ruta)) continue;
            Assert.DoesNotContain("ClaveCifrado", File.ReadAllText(ruta));
        }
    }

    [Fact]
    public void La_variable_de_entorno_Video__Emociones__ClaveCifrado_llega_a_las_opciones()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Es lo que hace .NET con Video__Emociones__ClaveCifrado: el doble guion bajo es el separador de secciones.
            ["Video:Emociones:ClaveCifrado"] = EscenarioEmociones.ClaveDePrueba,
            ["Video:Emociones:PurgaHabilitada"] = "true",
            ["Video:Emociones:RetencionDias"] = "90"
        }).Build();
        var o = new VideoOptions();

        config.GetSection(VideoOptions.Seccion).Bind(o);

        Assert.Equal(EscenarioEmociones.ClaveDePrueba, o.Emociones.ClaveCifrado);
        Assert.True(o.Emociones.PurgaHabilitada);
        Assert.Equal(90, o.Emociones.RetencionDias);
    }

    [Fact]
    public void Con_valores_absurdos_el_servicio_los_acota_y_no_se_rompe()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Reloj.Avanzar(TimeSpan.FromMinutes(30));
        var o = e.Opciones.CurrentValue.Emociones;
        o.ToleranciaSegundos = -50;          // se toma como 0
        o.MuestrasMaxPorSegundo = 100000;    // se acota a 30
        o.ConsultaCadaSegundos = int.MinValue;
        o.VigenciaSegundos = int.MaxValue;

        var ev = e.Guardia.Evaluar(e.Profesional, EscenarioEmociones.IdPaciente,
            e.Emociones(EscenarioEmociones.Inicio, EscenarioEmociones.Inicio.AddMinutes(30)), false);
        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;

        Assert.True(ev.Aprobada);
        Assert.Equal(1, permiso.ConsultarCadaSegundos);
        Assert.Equal(300, permiso.VigenciaSegundos);
    }
}
