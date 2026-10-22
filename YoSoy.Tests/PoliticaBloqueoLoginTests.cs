// Bloqueo del login por intentos fallidos (PoliticaBloqueoLogin). Sin base de datos: es la política en C#; que el
// UPDATE de UsuarioDAO.RegistrarIntentoFallido haga lo mismo lo prueba BloqueoLoginIntegrationTests.
public class PoliticaBloqueoLoginTests
{
    private static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void El_umbral_y_la_duracion_son_los_de_siempre()
    {
        Assert.Equal(3, PoliticaBloqueoLogin.MaxIntentos);
        Assert.Equal(15, PoliticaBloqueoLogin.MinutosBloqueo);
    }

    [Theory]
    [InlineData(0, null, 1, null)]
    [InlineData(1, null, 2, null)]
    public void Los_primeros_errores_solo_suman(int intentos, int? bloqueoEnMinutos, int esperado, int? bloqueoEsperadoEnMinutos)
    {
        var r = PoliticaBloqueoLogin.TrasIntentoFallido(intentos, Desde(bloqueoEnMinutos), Ahora);
        Assert.Equal(esperado, r.IntentosFallidos);
        Assert.Equal(Desde(bloqueoEsperadoEnMinutos), r.BloqueadoHasta);
    }

    [Fact]
    public void El_tercer_error_bloquea_15_minutos()
    {
        var r = PoliticaBloqueoLogin.TrasIntentoFallido(2, null, Ahora);
        Assert.Equal(3, r.IntentosFallidos);
        Assert.Equal(Ahora.AddMinutes(15), r.BloqueadoHasta);
    }

    [Theory]
    [InlineData(3, -1)]      // venció hace un minuto
    [InlineData(3, 0)]       // vence justo ahora (BloqueadoHasta == ahora ya no bloquea)
    [InlineData(4, -5)]
    [InlineData(7, -600)]    // un contador que venía creciendo desde hace horas
    public void Vencido_el_bloqueo_el_conteo_arranca_de_nuevo_en_vez_de_volver_a_bloquear(int intentos, int bloqueoEnMinutos)
    {
        // Antes: IntentosFallidos seguía en >= 3 y un solo error más bloqueaba otros 15 minutos, siempre.
        var r = PoliticaBloqueoLogin.TrasIntentoFallido(intentos, Desde(bloqueoEnMinutos), Ahora);
        Assert.Equal(1, r.IntentosFallidos);
        Assert.Null(r.BloqueadoHasta);
    }

    [Fact]
    public void Un_error_mas_con_el_bloqueo_vigente_suma_y_renueva_el_bloqueo()
    {
        // No debería pasar por el login (corta antes con 423), pero dos pedidos simultáneos pueden cruzarse.
        var r = PoliticaBloqueoLogin.TrasIntentoFallido(3, Desde(10), Ahora);
        Assert.Equal(4, r.IntentosFallidos);
        Assert.Equal(Ahora.AddMinutes(15), r.BloqueadoHasta);
    }

    [Fact]
    public void Una_fila_vieja_con_el_contador_alto_y_sin_fecha_de_bloqueo_se_bloquea_otra_vez()
    {
        // Como siempre: sin BloqueadoHasta no hay "bloqueo vencido" que reinicie el conteo.
        var r = PoliticaBloqueoLogin.TrasIntentoFallido(5, null, Ahora);
        Assert.Equal(6, r.IntentosFallidos);
        Assert.Equal(Ahora.AddMinutes(15), r.BloqueadoHasta);
    }

    [Fact]
    public void Secuencia_completa_tres_errores_bloquean_vence_y_otros_tres_vuelven_a_bloquear()
    {
        (int, DateTime?) estado = (0, null);
        var t = Ahora;

        for (var i = 1; i <= 3; i++)
            estado = PoliticaBloqueoLogin.TrasIntentoFallido(estado.Item1, estado.Item2, t);
        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(estado.Item2, t));

        t = t.AddMinutes(15);                                           // vence
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(estado.Item2, t));

        estado = PoliticaBloqueoLogin.TrasIntentoFallido(estado.Item1, estado.Item2, t);
        Assert.Equal(1, estado.Item1);
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(estado.Item2, t));   // un error NO alcanza

        estado = PoliticaBloqueoLogin.TrasIntentoFallido(estado.Item1, estado.Item2, t);
        estado = PoliticaBloqueoLogin.TrasIntentoFallido(estado.Item1, estado.Item2, t);
        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(estado.Item2, t));    // tres sí
    }

    [Fact]
    public void EstaBloqueado_solo_con_una_fecha_futura()
    {
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(null, Ahora));
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(Ahora.AddMinutes(-1), Ahora));
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(Ahora, Ahora));
        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(Ahora.AddSeconds(1), Ahora));
    }

    [Theory]
    [InlineData(900, 15)]    // 15 minutos justos
    [InlineData(841, 15)]    // 14 min 1 s: se redondea para arriba
    [InlineData(61, 2)]
    [InlineData(60, 1)]
    [InlineData(1, 1)]       // nunca "0 minutos" mientras siga bloqueada
    public void Los_minutos_restantes_se_redondean_para_arriba(int segundos, int esperado)
    {
        Assert.Equal(esperado, PoliticaBloqueoLogin.MinutosRestantes(Ahora.AddSeconds(segundos), Ahora));
    }

    [Fact]
    public void El_mensaje_dice_cuanto_falta_y_conserva_el_texto_de_siempre()
    {
        Assert.Equal("Cuenta bloqueada temporalmente. Intentá en 15 minutos.", PoliticaBloqueoLogin.MensajeBloqueo(Ahora.AddMinutes(15), Ahora));
        Assert.Equal("Cuenta bloqueada temporalmente. Intentá en 1 minuto.", PoliticaBloqueoLogin.MensajeBloqueo(Ahora.AddSeconds(20), Ahora));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void El_redondeo_de_DATETIME_no_muestra_16_minutos(int milisegundos)
    {
        // DATETIME puede redondear la expiración unos milisegundos hacia arriba.
        var hasta = Ahora.AddMinutes(PoliticaBloqueoLogin.MinutosBloqueo).AddMilliseconds(milisegundos);
        Assert.Equal(15, PoliticaBloqueoLogin.MinutosRestantes(hasta, Ahora));
        Assert.Equal("Cuenta bloqueada temporalmente. Intentá en 15 minutos.",
            PoliticaBloqueoLogin.MensajeBloqueo(hasta, Ahora));
        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(hasta, Ahora));
    }

    private static DateTime? Desde(int? minutos) => minutos is null ? null : Ahora.AddMinutes(minutos.Value);
}
