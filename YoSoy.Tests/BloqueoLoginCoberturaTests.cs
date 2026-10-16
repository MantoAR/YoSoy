// Cobertura del bloqueo del login (Usuario.IntentosFallidos y Usuario.BloqueadoHasta) SIN base de datos y sin tocar el código.
// PoliticaBloqueoLoginTests tiene lo básico de la política y BloqueoLoginIntegrationTests prueba el UPDATE de SQL y el
// endpoint contra una base. Acá van:
//  1. los bordes de la política: N intentos, la duración exacta y el vencimiento al instante;
//  2. el reloj: el bloqueo se mide en UTC con independencia de la zona del servidor (un TimeProvider en memoria);
//  3. un MODELO EN MEMORIA de POST /login (CuentaEnMemoria) que repite, en el mismo orden que Program.cs, las decisiones del
//     endpoint usando la política REAL: qué responde cada intento (401 o 423), que una clave correcta con el bloqueo vigente
//     no entra ni limpia nada, y que un login exitoso pone todo en cero.
// El modelo NO es el endpoint: si /login cambia de orden, hay que cambiarlo acá también. Para que deje de ser un modelo hace
// falta extraer la decisión a un servicio con un repositorio en memoria (queda anotado en docs/PROGRESO_NOCTURNO.md).
public class BloqueoLoginCoberturaTests
{
    private static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    // La cuenta de Usuario en memoria: lo que guardan las dos columnas y lo que hace el endpoint con ellas.
    private enum Respuesta { Ok = 200, NoAutorizado = 401, Bloqueada = 423 }

    private sealed class CuentaEnMemoria
    {
        public int IntentosFallidos { get; private set; }
        public DateTime? BloqueadoHasta { get; private set; }
        public string? Mensaje { get; private set; }          // el cuerpo de una 423
        public int Reseteos { get; private set; }             // cuántas veces el login exitoso tuvo que "escribir" (un login normal, ninguna)

        // Mismo orden que POST /login: primero el bloqueo (aunque la clave sea correcta), después la clave, y solo un login
        // exitoso con algo que limpiar pone todo en cero.
        public Respuesta Intentar(bool claveCorrecta, DateTime ahoraUtc)
        {
            Mensaje = null;
            if (PoliticaBloqueoLogin.EstaBloqueado(BloqueadoHasta, ahoraUtc))
            {
                Mensaje = PoliticaBloqueoLogin.MensajeBloqueo(BloqueadoHasta!.Value, ahoraUtc);
                return Respuesta.Bloqueada;
            }

            if (!claveCorrecta)
            {
                (IntentosFallidos, BloqueadoHasta) = PoliticaBloqueoLogin.TrasIntentoFallido(IntentosFallidos, BloqueadoHasta, ahoraUtc);
                if (PoliticaBloqueoLogin.EstaBloqueado(BloqueadoHasta, ahoraUtc))
                {
                    Mensaje = PoliticaBloqueoLogin.MensajeBloqueo(BloqueadoHasta!.Value, ahoraUtc);
                    return Respuesta.Bloqueada;
                }
                return Respuesta.NoAutorizado;
            }

            if (IntentosFallidos > 0 || BloqueadoHasta.HasValue)
            {
                IntentosFallidos = 0;
                BloqueadoHasta = null;
                Reseteos++;
            }
            return Respuesta.Ok;
        }

        public Respuesta Falla(DateTime ahoraUtc) => Intentar(false, ahoraUtc);
        public Respuesta Entra(DateTime ahoraUtc) => Intentar(true, ahoraUtc);
    }

    // ---------- 1. La política: N intentos, la duración y el vencimiento ----------

    [Fact]
    public void Los_primeros_N_menos_1_errores_suman_sin_bloquear_y_el_N_bloquea()
    {
        (int Intentos, DateTime? Hasta) estado = (0, null);

        for (var error = 1; error < PoliticaBloqueoLogin.MaxIntentos; error++)
        {
            estado = PoliticaBloqueoLogin.TrasIntentoFallido(estado.Intentos, estado.Hasta, Ahora);
            Assert.Equal(error, estado.Intentos);
            Assert.Null(estado.Hasta);
            Assert.False(PoliticaBloqueoLogin.EstaBloqueado(estado.Hasta, Ahora));
        }

        estado = PoliticaBloqueoLogin.TrasIntentoFallido(estado.Intentos, estado.Hasta, Ahora);
        Assert.Equal(PoliticaBloqueoLogin.MaxIntentos, estado.Intentos);
        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(estado.Hasta, Ahora));
    }

    [Fact]
    public void El_bloqueo_dura_exactamente_los_minutos_de_la_politica_medidos_desde_el_error_que_lo_dispara()
    {
        var (_, hasta) = PoliticaBloqueoLogin.TrasIntentoFallido(PoliticaBloqueoLogin.MaxIntentos - 1, null, Ahora);

        Assert.Equal(TimeSpan.FromMinutes(PoliticaBloqueoLogin.MinutosBloqueo), hasta!.Value - Ahora);
        Assert.Equal(DateTimeKind.Utc, hasta.Value.Kind);        // sale en UTC si el reloj lo estaba
    }

    [Fact]
    public void Cada_error_con_el_bloqueo_vigente_renueva_los_minutos_desde_ese_error()
    {
        var (n, hasta) = PoliticaBloqueoLogin.TrasIntentoFallido(2, null, Ahora);                       // bloquea hasta 12:15
        (n, hasta) = PoliticaBloqueoLogin.TrasIntentoFallido(n, hasta, Ahora.AddMinutes(5));            // otro a las 12:05

        Assert.Equal(4, n);
        Assert.Equal(Ahora.AddMinutes(5 + PoliticaBloqueoLogin.MinutosBloqueo), hasta);
    }

    [Fact]
    public void El_bloqueo_vence_exactamente_en_BloqueadoHasta_ni_antes_ni_despues()
    {
        var hasta = Ahora.AddMinutes(15);

        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(hasta, hasta.AddTicks(-1)));    // un instante antes: bloqueada
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(hasta, hasta));                // justo: ya no
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(hasta, hasta.AddTicks(1)));    // un instante después: tampoco
    }

    [Theory]
    [InlineData(900, "15 minutos")]       // 15 min justos
    [InlineData(899, "15 minutos")]
    [InlineData(841, "15 minutos")]       // 14 min 1 s
    [InlineData(840, "14 minutos")]
    [InlineData(121, "3 minutos")]
    [InlineData(120, "2 minutos")]
    [InlineData(61, "2 minutos")]
    [InlineData(60, "1 minuto")]          // el singular, solo con uno
    [InlineData(59, "1 minuto")]
    [InlineData(1, "1 minuto")]
    public void El_mensaje_redondea_para_arriba_y_usa_singular_solo_con_un_minuto(int segundosQueFaltan, string esperado)
    {
        var mensaje = PoliticaBloqueoLogin.MensajeBloqueo(Ahora.AddSeconds(segundosQueFaltan), Ahora);

        Assert.Equal($"Cuenta bloqueada temporalmente. Intentá en {esperado}.", mensaje);
    }

    // Un bloqueo ya vencido dice "1 minuto" (nunca 0 ni negativo). No se ve nunca: el login corta con EstaBloqueado antes.
    [Fact]
    public void Un_bloqueo_ya_vencido_no_da_minutos_negativos()
    {
        Assert.Equal(1, PoliticaBloqueoLogin.MinutosRestantes(Ahora.AddSeconds(-30), Ahora));
        Assert.Equal(1, PoliticaBloqueoLogin.MinutosRestantes(Ahora, Ahora));
        Assert.Equal(1, PoliticaBloqueoLogin.MinutosRestantes(Ahora.AddHours(-5), Ahora));
    }

    // ---------- UTC: lo que se compara siempre está en UTC ----------

    // El error que había: comparar una fecha en UTC contra la hora LOCAL del servidor. En Argentina (UTC-3) el bloqueo que vence
    // a las 12:15 UTC se compararía con "09:20" (la hora local, que ya es 12:20 UTC) y parecería que sigue bloqueado.
    [Fact]
    public void La_hora_local_de_Argentina_hay_que_pasarla_a_UTC_antes_de_comparar()
    {
        var argentina = TimeZoneInfo.CreateCustomTimeZone("ART-prueba", TimeSpan.FromHours(-3), "ART", "ART");
        var hastaUtc = new DateTime(2026, 10, 15, 12, 15, 0, DateTimeKind.Utc);
        var ahoraLocal = new DateTime(2026, 10, 15, 9, 20, 0, DateTimeKind.Unspecified);          // son las 12:20 en UTC

        var ahoraUtc = TimeZoneInfo.ConvertTimeToUtc(ahoraLocal, argentina);

        Assert.Equal(new DateTime(2026, 10, 15, 12, 20, 0), ahoraUtc);
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(hastaUtc, ahoraUtc));                      // en UTC: venció hace 5 minutos
        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(hastaUtc, ahoraLocal));                     // con la hora local: el error de antes
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Con_las_dos_fechas_en_UTC_el_Kind_no_cambia_el_resultado(DateTimeKind kind)
    {
        var ahora = DateTime.SpecifyKind(Ahora, kind);
        var (n, hasta) = PoliticaBloqueoLogin.TrasIntentoFallido(2, null, ahora);

        Assert.Equal(3, n);
        Assert.True(PoliticaBloqueoLogin.EstaBloqueado(hasta, ahora.AddMinutes(14)));
        Assert.False(PoliticaBloqueoLogin.EstaBloqueado(hasta, ahora.AddMinutes(15)));
    }

    // ---------- 2. El reloj en memoria: el vencimiento sigue a UTC, no a la zona del servidor ----------

    [Fact]
    public void El_vencimiento_se_mide_con_el_reloj_UTC_aunque_el_servidor_este_en_otra_zona()
    {
        var argentina = TimeZoneInfo.CreateCustomTimeZone("ART-prueba", TimeSpan.FromHours(-3), "ART", "ART");
        var reloj = new RelojConZona(new DateTime(2026, 10, 15, 12, 0, 0), argentina);        // 12:00 UTC = 09:00 en la zona del servidor
        DateTime AhoraUtc() => reloj.GetUtcNow().UtcDateTime;
        var cuenta = new CuentaEnMemoria();

        Assert.Equal(9, reloj.GetLocalNow().Hour);                                              // la zona del servidor es otra...
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(AhoraUtc()));
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(AhoraUtc()));
        Assert.Equal(Respuesta.Bloqueada, cuenta.Falla(AhoraUtc()));                            // ...y igual bloquea hasta las 12:15 UTC

        reloj.Avanzar(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.Equal(Respuesta.Bloqueada, cuenta.Entra(AhoraUtc()));                            // 14:59 después: sigue
        Assert.Equal("Cuenta bloqueada temporalmente. Intentá en 1 minuto.", cuenta.Mensaje);

        reloj.Avanzar(TimeSpan.FromSeconds(1));                                                 // 15:00 justos
        Assert.Equal(Respuesta.Ok, cuenta.Entra(AhoraUtc()));
    }

    [Fact]
    public void El_mismo_instante_da_el_mismo_resultado_en_cualquier_zona_del_servidor()
    {
        var instante = new DateTime(2026, 10, 15, 12, 0, 0);
        var zonas = new[]
        {
            TimeZoneInfo.Utc,
            TimeZoneInfo.CreateCustomTimeZone("ART-prueba", TimeSpan.FromHours(-3), "ART", "ART"),
            TimeZoneInfo.CreateCustomTimeZone("JST-prueba", TimeSpan.FromHours(9), "JST", "JST")
        };

        var resultados = zonas.Select(zona =>
        {
            var reloj = new RelojConZona(instante, zona);
            var cuenta = new CuentaEnMemoria();
            cuenta.Falla(reloj.GetUtcNow().UtcDateTime);
            cuenta.Falla(reloj.GetUtcNow().UtcDateTime);
            cuenta.Falla(reloj.GetUtcNow().UtcDateTime);
            return (cuenta.IntentosFallidos, cuenta.BloqueadoHasta);
        }).Distinct().ToList();

        Assert.Single(resultados);
        Assert.Equal((3, (DateTime?)instante.AddMinutes(15)), resultados[0]);
    }

    // ---------- 3. El modelo en memoria de POST /login ----------

    [Fact]
    public void Tres_errores_seguidos_dan_401_401_y_423_con_los_minutos_de_la_politica()
    {
        var cuenta = new CuentaEnMemoria();

        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));
        Assert.Equal(Respuesta.Bloqueada, cuenta.Falla(Ahora));

        Assert.Equal("Cuenta bloqueada temporalmente. Intentá en 15 minutos.", cuenta.Mensaje);
        Assert.Equal(3, cuenta.IntentosFallidos);
        Assert.Equal(Ahora.AddMinutes(15), cuenta.BloqueadoHasta);
    }

    [Fact]
    public void Con_el_bloqueo_vigente_la_clave_correcta_tampoco_entra_y_no_limpia_nada()
    {
        var cuenta = new CuentaEnMemoria();
        for (var i = 0; i < 3; i++) cuenta.Falla(Ahora);

        var respuesta = cuenta.Entra(Ahora.AddMinutes(5));

        Assert.Equal(Respuesta.Bloqueada, respuesta);
        Assert.Equal("Cuenta bloqueada temporalmente. Intentá en 10 minutos.", cuenta.Mensaje);
        Assert.Equal(3, cuenta.IntentosFallidos);                         // intacto
        Assert.Equal(Ahora.AddMinutes(15), cuenta.BloqueadoHasta);        // sin renovar ni limpiar
        Assert.Equal(0, cuenta.Reseteos);
    }

    [Fact]
    public void Vencido_el_bloqueo_la_clave_correcta_entra_y_deja_todo_en_cero()
    {
        var cuenta = new CuentaEnMemoria();
        for (var i = 0; i < 3; i++) cuenta.Falla(Ahora);

        var respuesta = cuenta.Entra(Ahora.AddMinutes(15));

        Assert.Equal(Respuesta.Ok, respuesta);
        Assert.Equal(0, cuenta.IntentosFallidos);
        Assert.Null(cuenta.BloqueadoHasta);
        Assert.Equal(1, cuenta.Reseteos);
    }

    [Fact]
    public void Un_login_exitoso_entre_los_errores_reinicia_la_cuenta()
    {
        var cuenta = new CuentaEnMemoria();
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));
        Assert.Equal(2, cuenta.IntentosFallidos);

        Assert.Equal(Respuesta.Ok, cuenta.Entra(Ahora));                  // el login exitoso reinicia
        Assert.Equal(0, cuenta.IntentosFallidos);
        Assert.Null(cuenta.BloqueadoHasta);

        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));        // y arranca de cero: 1.º y 2.º no bloquean
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));
        Assert.Equal(2, cuenta.IntentosFallidos);
        Assert.Equal(Respuesta.Bloqueada, cuenta.Falla(Ahora));           // el 3.º sí
    }

    [Fact]
    public void Un_login_normal_sin_errores_previos_no_escribe_nada()
    {
        var cuenta = new CuentaEnMemoria();

        Assert.Equal(Respuesta.Ok, cuenta.Entra(Ahora));
        Assert.Equal(Respuesta.Ok, cuenta.Entra(Ahora.AddMinutes(1)));

        Assert.Equal(0, cuenta.Reseteos);
        Assert.Equal(0, cuenta.IntentosFallidos);
        Assert.Null(cuenta.BloqueadoHasta);
    }

    [Fact]
    public void Vencido_el_bloqueo_un_error_cuenta_como_el_primero_de_tres_y_no_vuelve_a_bloquear()
    {
        var cuenta = new CuentaEnMemoria();
        for (var i = 0; i < 3; i++) cuenta.Falla(Ahora);
        var despues = Ahora.AddMinutes(15);

        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(despues));
        Assert.Equal(1, cuenta.IntentosFallidos);
        Assert.Null(cuenta.BloqueadoHasta);

        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(despues));
        Assert.Equal(Respuesta.Bloqueada, cuenta.Falla(despues));         // hacen falta tres otra vez
    }

    // Lo que NO vence por tiempo (y está documentado en la política): 1 o 2 errores sin bloqueo, porque la tabla no guarda
    // cuándo fue el último. Si algún día se agrega esa columna, esta prueba es la que hay que cambiar a propósito.
    [Fact]
    public void Dos_errores_sin_bloqueo_no_vencen_por_el_paso_del_tiempo()
    {
        var cuenta = new CuentaEnMemoria();
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));
        Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(Ahora));

        Assert.Equal(Respuesta.Bloqueada, cuenta.Falla(Ahora.AddDays(30)));     // el 3.º, un mes después, igual bloquea
    }

    [Fact]
    public void Varios_ciclos_de_bloqueo_duran_siempre_lo_mismo_y_no_se_acumulan()
    {
        var cuenta = new CuentaEnMemoria();
        var t = Ahora;

        for (var ciclo = 1; ciclo <= 5; ciclo++)
        {
            // Desde cero o desde un bloqueo recién vencido (el primer error lo reinicia): tres errores bloquean.
            Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(t));
            Assert.Equal(Respuesta.NoAutorizado, cuenta.Falla(t));
            Assert.Equal(Respuesta.Bloqueada, cuenta.Falla(t));
            Assert.Equal(t.AddMinutes(15), cuenta.BloqueadoHasta);                // siempre 15 minutos, también en el 5.º ciclo
            Assert.Equal(3, cuenta.IntentosFallidos);                              // y el contador no sigue creciendo

            t = t.AddMinutes(15);                                                  // vence justo
        }
    }

    [Fact]
    public void Una_cuenta_bloqueada_no_afecta_a_las_demas()
    {
        var bloqueada = new CuentaEnMemoria();
        var otra = new CuentaEnMemoria();
        for (var i = 0; i < 3; i++) bloqueada.Falla(Ahora);

        Assert.Equal(Respuesta.Bloqueada, bloqueada.Entra(Ahora));
        Assert.Equal(Respuesta.Ok, otra.Entra(Ahora));
        Assert.Equal(Respuesta.NoAutorizado, otra.Falla(Ahora));
        Assert.Equal(1, otra.IntentosFallidos);
    }
}
