using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

// Los mensajes que entran por el webhook de whatsapp-service, completos (contrato v2): tipo, id, hora y nombre del perfil, además del teléfono y el texto. En memoria, sin base ni red.
//
//  · TiposDeMensajeTests / DatosDelEntranteTests: la lista cerrada de tipos (la misma que la restricción de la base) y cómo se limpian y acotan los datos que vienen del servicio;
//  · EntrantesVistosTests: el descarte, en memoria, de un mensaje que ya se atendió (whatsapp-service reintenta si la API falla);
//  · ColumnasChatPersonalTests: la puerta que decide si se tocan las columnas de la migración 17: en el modo Asistente, NUNCA;
//  · AtencionPersonalConDatosTests / RecepcionConDatosTests: el modo Personal y la puerta, con esos datos.
// Contra la base de verdad (con y sin la migración 17): EntrantesCompletosEndpointsTests.
public class TiposDeMensajeTests
{
    [Theory]
    [InlineData(null, "texto")]
    [InlineData("", "texto")]
    [InlineData("   ", "texto")]
    [InlineData("texto", "texto")]
    [InlineData("Imagen", "imagen")]
    [InlineData("  AUDIO ", "audio")]
    [InlineData("gif", "otro")]               // un tipo que no se conoce no rechaza el mensaje: se guarda como "otro"
    [InlineData("imagen2", "otro")]
    public void Un_tipo_se_normaliza_sin_tipo_es_texto_y_lo_desconocido_es_otro(string? tipo, string esperado) =>
        Assert.Equal(esperado, TiposDeMensaje.Normalizar(tipo));

    [Fact]
    public void La_lista_de_tipos_de_la_API_es_la_misma_que_la_restriccion_de_la_migracion_17()
    {
        var migracion = File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "migrations", "add_chat_personal_entrantes.sql"));
        var restriccion = Regex.Match(ConsentimientoWhatsAppMigracionTests.SinComentarios(migracion), @"CK_MensajeChat_Tipo\s+CHECK\s*\(\s*Tipo\s+IN\s*\((?<lista>[^)]*)\)", RegexOptions.IgnoreCase);
        Assert.True(restriccion.Success, "no encontré la restricción CK_MensajeChat_Tipo en la migración");

        var deLaBase = Regex.Matches(restriccion.Groups["lista"].Value, @"'([^']+)'").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(deLaBase, TiposDeMensaje.Todos);          // los mismos y en el mismo orden: un tipo nuevo se agrega en los dos lugares
    }

    [Fact]
    public void Todo_tipo_que_no_es_texto_tiene_su_aviso_entre_corchetes_y_el_texto_no_tiene()
    {
        Assert.Equal("", TiposDeMensaje.Aviso(TiposDeMensaje.Texto));
        foreach (var tipo in TiposDeMensaje.Todos.Where(t => t != TiposDeMensaje.Texto))
            Assert.Matches(@"^\[[^\]]+\]$", TiposDeMensaje.Aviso(tipo));
        Assert.Equal(TiposDeMensaje.Todos.Count - 1, TiposDeMensaje.Todos.Where(t => t != TiposDeMensaje.Texto).Select(TiposDeMensaje.Aviso).Distinct().Count());
    }
}

public class DatosDelEntranteTests
{
    private static readonly DateTime Ahora = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly RelojFijo Reloj = new(Ahora);
    private static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    // ───────────── El cuerpo de siempre no genera nada ─────────────

    [Fact]
    public void Un_cuerpo_sin_ninguno_de_los_campos_nuevos_no_genera_datos()
    {
        Assert.Null(DatosDelEntrante.Desde(new MensajeEntranteRequest("5491155550001", "hola"), Reloj));
    }

    [Theory]
    [InlineData("imagen", null, null, null)]
    [InlineData(null, "3EB0AB", null, null)]
    [InlineData(null, null, 1_700_000_000L, null)]
    [InlineData(null, null, null, "Juan")]
    public void Con_cualquiera_de_los_campos_nuevos_ya_hay_datos(string? tipo, string? id, long? ts, string? nombre) =>
        Assert.NotNull(DatosDelEntrante.Desde(new MensajeEntranteRequest("5491155550001", "hola", tipo, id, ts, nombre), Reloj));

    [Fact]
    public void Sin_tipo_pero_con_otros_campos_es_texto()
    {
        var d = DatosDelEntrante.Desde(new MensajeEntranteRequest("5491155550001", "hola", Id: "3EB0AB"), Reloj)!;

        Assert.Equal("texto", d.Tipo);
        Assert.True(d.EsTexto);
    }

    [Fact]
    public void Un_tipo_que_no_es_texto_se_marca_asi()
    {
        var d = DatosDelEntrante.Desde(new MensajeEntranteRequest("5491155550001", "[Audio]", "audio"), Reloj)!;

        Assert.Equal("audio", d.Tipo);
        Assert.False(d.EsTexto);
    }

    // ───────────── El id ─────────────

    [Theory]
    [InlineData("3EB0A1B2C3D4E5F6A7B8", "3EB0A1B2C3D4E5F6A7B8")]
    [InlineData("  ABCDEF0123456789  ", "ABCDEF0123456789")]
    [InlineData("a-b_c.d", "a-b_c.d")]
    [InlineData("x", "x")]
    public void Un_id_valido_se_conserva(string id, string esperado) => Assert.Equal(esperado, DatosDelEntrante.LimpiarId(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tiene espacio")]
    [InlineData("con'comilla")]
    [InlineData("con;punto-y-coma")]
    [InlineData("con\nsalto")]
    [InlineData("ñandú")]
    public void Un_id_que_no_pasa_el_filtro_se_descarta_y_el_mensaje_entra_igual(string? id) => Assert.Null(DatosDelEntrante.LimpiarId(id));

    [Fact]
    public void El_id_tiene_a_lo_sumo_64_caracteres_como_la_columna()
    {
        Assert.Equal(new string('A', 64), DatosDelEntrante.LimpiarId(new string('A', 64)));
        Assert.Null(DatosDelEntrante.LimpiarId(new string('A', 65)));
    }

    // ───────────── El nombre del perfil ─────────────

    [Theory]
    [InlineData("Juan Pérez", "Juan Pérez")]
    [InlineData("  Juan   Pérez  ", "Juan Pérez")]
    [InlineData("Juan\nPérez", "Juan Pérez")]
    [InlineData("Juan\t\r\nPérez", "Juan Pérez")]
    [InlineData("Ana 🌸", "Ana 🌸")]
    [InlineData("👨\u200D👩\u200D👧 familia", "👨\u200D👩\u200D👧 familia")]                 // el unión de ancho cero de un emoji compuesto no se toca
    [InlineData("\u202EJuan\u202C", "Juan")]                       // las marcas que invierten la dirección del texto se sacan
    [InlineData("Ju\u200Ban", "Ju an")]                            // y el espacio de ancho cero (lo que esconde texto)
    [InlineData("\uFEFFJuan", "Juan")]
    public void El_nombre_se_limpia_sin_romper_los_emojis(string nombre, string esperado) => Assert.Equal(esperado, DatosDelEntrante.LimpiarNombre(nombre));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    [InlineData("\u200B\u202E")]
    public void Un_nombre_que_queda_vacio_es_null(string? nombre) => Assert.Null(DatosDelEntrante.LimpiarNombre(nombre));

    [Fact]
    public void El_nombre_tiene_a_lo_sumo_100_caracteres_y_no_parte_un_emoji_al_medio()
    {
        Assert.Equal(new string('a', 100), DatosDelEntrante.LimpiarNombre(new string('a', 150)));

        // 99 letras y un emoji (dos unidades de texto): el corte en 100 caería en el medio del emoji, así que se queda con las 99.
        var cortado = DatosDelEntrante.LimpiarNombre(new string('a', 99) + "🌸 más")!;
        Assert.Equal(new string('a', 99), cortado);
        Assert.Equal(cortado, cortado.Normalize());                         // y no quedó una mitad de emoji suelta
        Assert.DoesNotContain(cortado, c => char.IsSurrogate(c));
    }

    // ───────────── La hora del mensaje ─────────────

    [Fact]
    public void Una_hora_de_hace_un_rato_se_usa_tal_cual()
    {
        var hora = DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddMinutes(-7)), Reloj);

        Assert.Equal(Ahora.AddMinutes(-7), hora);
    }

    [Fact]
    public void La_hora_se_convierte_a_la_zona_de_la_clinica()
    {
        var zona = TimeZoneInfo.CreateCustomTimeZone("clinica", TimeSpan.FromHours(-3), "clínica", "clínica");
        var reloj = new RelojConZona(Ahora, zona);

        var hora = DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddMinutes(-7)), reloj);

        Assert.Equal(new DateTime(2026, 10, 19, 8, 53, 0), hora);          // 11:53 UTC = 08:53 en UTC-3
        Assert.Equal(DateTimeKind.Unspecified, hora!.Value.Kind);
    }

    [Fact]
    public void Un_poquito_en_el_futuro_se_acota_a_ahora_y_mas_que_eso_se_descarta()
    {
        Assert.Equal(Ahora, DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddSeconds(30)), Reloj));             // dentro de la holgura: "ahora", nunca una hora futura
        Assert.Equal(Ahora, DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddMinutes(1)), Reloj));              // justo en el límite
        Assert.Null(DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddMinutes(1).AddSeconds(1)), Reloj));        // pasado el límite: la del servidor
        Assert.Null(DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddDays(3)), Reloj));
    }

    [Fact]
    public void De_hace_mas_de_un_dia_se_descarta_y_de_menos_se_usa()
    {
        Assert.Equal(Ahora.AddHours(-23), DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddHours(-23)), Reloj));
        Assert.Equal(Ahora.AddDays(-1).AddSeconds(1), DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddDays(-1).AddSeconds(1)), Reloj));
        Assert.Null(DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddDays(-1)), Reloj));                         // justo un día: ya no
        Assert.Null(DatosDelEntrante.HoraDelMensaje(Unix(Ahora.AddDays(-30)), Reloj));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(long.MaxValue)]            // fuera de lo que .NET puede representar: no revienta
    [InlineData(long.MinValue)]
    public void Una_hora_absurda_se_descarta_sin_reventar(long? segundos) => Assert.Null(DatosDelEntrante.HoraDelMensaje(segundos, Reloj));

    // ───────────── Lo que sale en la respuesta ─────────────

    [Fact]
    public void Ni_el_id_externo_ni_un_tipo_sin_informar_salen_en_el_JSON_de_un_mensaje()
    {
        var comun = JsonSerializer.Serialize(new MensajeChat { Texto = "hola", IdExterno = "3EB0AB" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("idExterno", comun, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"tipo\"", comun);                                      // en el modo Asistente la respuesta es la de siempre

        var conTipo = JsonSerializer.Serialize(new MensajeChat { Texto = "[Imagen]", Tipo = "imagen", IdExterno = "3EB0AB" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"tipo\":\"imagen\"", conTipo);
        Assert.DoesNotContain("3EB0AB", conTipo);
    }

    [Fact]
    public void El_nombre_del_contacto_solo_sale_en_la_lista_si_lo_hay()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.DoesNotContain("nombreContacto", JsonSerializer.Serialize(new ConversacionResumen(), web));
        Assert.Contains("\"nombreContacto\":\"Juan\"", JsonSerializer.Serialize(new ConversacionResumen { NombreContacto = "Juan" }, web));
    }

    [Fact]
    public void La_respuesta_del_webhook_solo_trae_ignorado_e_idMensaje_cuando_hay_algo_que_decir()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var de_siempre = JsonSerializer.Serialize(new RespuestaBot(7, new List<string> { "hola" }), web);
        Assert.Equal("{\"idConversacion\":7,\"respuestas\":[\"hola\"]}", de_siempre);

        Assert.Equal("{\"idConversacion\":7,\"respuestas\":[],\"ignorado\":\"duplicado\"}", JsonSerializer.Serialize(new RespuestaBot(7, new List<string>(), Ignorado: "duplicado"), web));
        Assert.Equal("{\"idConversacion\":7,\"respuestas\":[],\"idMensaje\":42}", JsonSerializer.Serialize(new RespuestaBot(7, new List<string>(), IdMensaje: 42), web));
    }
}

public class EntrantesVistosTests
{
    private static readonly DateTime Ahora = new(2026, 10, 19, 12, 0, 0);

    [Fact]
    public void El_primer_mensaje_entra_y_el_mismo_otra_vez_es_un_repetido()
    {
        var vistos = new EntrantesVistos(new RelojFijo(Ahora));

        Assert.True(vistos.Reservar("5491155550001", "3EB0AB"));
        Assert.False(vistos.Reservar("5491155550001", "3EB0AB"));
        Assert.True(vistos.Reservar("5491155550001", "3EB0AC"));            // otro id es otro mensaje
    }

    [Fact]
    public void El_mismo_id_de_otra_persona_es_otro_mensaje_y_el_telefono_se_compara_normalizado()
    {
        var vistos = new EntrantesVistos(new RelojFijo(Ahora));

        Assert.True(vistos.Reservar("5491155550001", "3EB0AB"));
        Assert.True(vistos.Reservar("5491155550002", "3EB0AB"));
        Assert.False(vistos.Reservar("+54 9 11 5555-0001", "3EB0AB"));      // el mismo número escrito de otra forma
        Assert.False(vistos.Reservar("11 5555-0001", "3EB0AB"));
    }

    [Fact]
    public void Si_la_atencion_fallo_se_libera_y_el_reintento_entra()
    {
        var vistos = new EntrantesVistos(new RelojFijo(Ahora));
        vistos.Reservar("5491155550001", "3EB0AB");

        vistos.Liberar("5491155550001", "3EB0AB");

        Assert.True(vistos.Reservar("5491155550001", "3EB0AB"));
        vistos.Liberar("5491155550009", "no-estaba");                       // liberar lo que no está no rompe nada
    }

    [Fact]
    public void A_los_30_minutos_se_olvida()
    {
        var reloj = new RelojFijo(Ahora);
        var vistos = new EntrantesVistos(reloj);
        vistos.Reservar("5491155550001", "3EB0AB");

        reloj.Avanzar(EntrantesVistos.Vigencia - TimeSpan.FromSeconds(1));
        Assert.False(vistos.Reservar("5491155550001", "3EB0AB"));            // todavía vale
        reloj.Avanzar(TimeSpan.FromSeconds(2));
        Assert.True(vistos.Reservar("5491155550001", "3EB0AB"));             // ya venció: vale como nuevo
    }

    [Fact]
    public void Nunca_guarda_mas_del_maximo_y_se_queda_con_los_mas_nuevos()
    {
        var reloj = new RelojFijo(Ahora);
        var vistos = new EntrantesVistos(reloj);
        for (var i = 0; i < EntrantesVistos.Maximo + 200; i++)
        {
            vistos.Reservar("5491155550001", "id" + i);
            reloj.Avanzar(TimeSpan.FromMilliseconds(10));
        }

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        vistos.Reservar("5491155550001", "uno-mas");                         // la poda corre a lo sumo una vez por minuto

        Assert.True(vistos.Cantidad <= EntrantesVistos.Maximo);
        Assert.False(vistos.Reservar("5491155550001", "uno-mas"));
        Assert.False(vistos.Reservar("5491155550001", "id" + (EntrantesVistos.Maximo + 199)));   // el último que entró sigue
        Assert.False(vistos.Reservar("5491155550001", "id" + (EntrantesVistos.Maximo - 1)));     // uno de los más nuevos de antes de la poda, también
        Assert.True(vistos.Reservar("5491155550001", "id0"));                                    // y el más viejo se olvidó (la poda saca los más viejos, no los más nuevos)
    }

    [Fact]
    public async Task Dos_pedidos_a_la_vez_con_el_mismo_id_solo_dejan_pasar_a_uno()
    {
        var vistos = new EntrantesVistos(new RelojFijo(Ahora));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => vistos.Reservar("5491155550001", "3EB0AB"))));

        Assert.Equal(1, resultados.Count(r => r));
    }
}

public class ColumnasChatPersonalTests
{
    private sealed class Sonda : ISondaDeColumnasChatPersonal
    {
        public bool Responde { get; set; }
        public Exception? Falla { get; set; }
        public int Consultas { get; private set; }
        public bool Existen() { Consultas++; if (Falla is not null) throw Falla; return Responde; }
    }

    private sealed class Arnes
    {
        public readonly RelojFijo Reloj = new(new DateTime(2026, 10, 19, 12, 0, 0));
        public readonly OpcionesFijas Opciones = new();
        public readonly Sonda Sonda = new();
        public readonly LogsCapturados Logs = new();
        public readonly ColumnasChatPersonal Columnas;

        public Arnes(string modo)
        {
            Opciones.CurrentValue.ModoAtencion = modo;
            Columnas = new ColumnasChatPersonal(Opciones, Sonda, Reloj, Logs.CreateLogger<ColumnasChatPersonal>());
        }
    }

    [Fact]
    public void En_el_modo_Asistente_nunca_se_usan_y_ni_siquiera_se_pregunta_por_ellas()
    {
        var a = new Arnes(ModosAtencion.Asistente);
        a.Sonda.Responde = true;                                            // aunque la base las tenga

        for (var i = 0; i < 5; i++)
        {
            Assert.False(a.Columnas.Disponibles);
            a.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        }

        Assert.Equal(0, a.Sonda.Consultas);                                 // la consulta a la base no se hace
        Assert.Empty(a.Logs.Lineas);
    }

    [Fact]
    public void En_el_modo_Personal_con_las_columnas_se_usan_y_se_pregunta_una_sola_vez()
    {
        var a = new Arnes(ModosAtencion.Personal);
        a.Sonda.Responde = true;

        for (var i = 0; i < 10; i++) Assert.True(a.Columnas.Disponibles);

        Assert.Equal(1, a.Sonda.Consultas);                                 // una vez que existen, no se vuelve a preguntar
        Assert.Empty(a.Logs.Lineas);
    }

    [Fact]
    public void En_el_modo_Personal_sin_las_columnas_no_se_usan_se_avisa_en_el_log_y_se_vuelve_a_mirar_cada_minuto()
    {
        var a = new Arnes(ModosAtencion.Personal);
        a.Sonda.Responde = false;

        Assert.False(a.Columnas.Disponibles);
        Assert.False(a.Columnas.Disponibles);                               // dentro del minuto: no se vuelve a preguntar ni a avisar
        Assert.Equal(1, a.Sonda.Consultas);
        var aviso = Assert.Single(a.Logs.Lineas);
        Assert.StartsWith("Error|", aviso);
        Assert.Contains("migración 17", aviso);
        Assert.Contains("add_chat_personal_entrantes.sql", aviso);

        a.Reloj.Avanzar(ColumnasChatPersonal.ConsultarCada);
        Assert.False(a.Columnas.Disponibles);                               // pasó el minuto: vuelve a mirar (y a avisar)
        Assert.Equal(2, a.Sonda.Consultas);
        Assert.Equal(2, a.Logs.Lineas.Count);
    }

    [Fact]
    public void Aplicar_la_migracion_con_la_API_andando_alcanza_sin_reiniciarla()
    {
        var a = new Arnes(ModosAtencion.Personal);
        a.Sonda.Responde = false;
        Assert.False(a.Columnas.Disponibles);

        a.Sonda.Responde = true;                                            // se aplicó la migración
        Assert.False(a.Columnas.Disponibles);                               // hasta el próximo minuto sigue sin usarlas...
        a.Reloj.Avanzar(ColumnasChatPersonal.ConsultarCada);
        Assert.True(a.Columnas.Disponibles);                                // ...y ahí las empieza a usar
        Assert.True(a.Columnas.Disponibles);
        Assert.Equal(2, a.Sonda.Consultas);
    }

    [Fact]
    public void Si_la_base_no_contesta_no_se_usan_y_se_avisa_sin_romper_nada()
    {
        var a = new Arnes(ModosAtencion.Personal);
        a.Sonda.Falla = new InvalidOperationException("sin conexión");

        Assert.False(a.Columnas.Disponibles);

        Assert.StartsWith("Warning|", Assert.Single(a.Logs.Lineas));
        a.Sonda.Falla = null;
        a.Sonda.Responde = true;
        a.Reloj.Avanzar(ColumnasChatPersonal.ConsultarCada);
        Assert.True(a.Columnas.Disponibles);                                // y cuando vuelve, se usan
    }

    [Fact]
    public void El_modo_se_mira_en_cada_llamada_y_se_puede_cambiar_sin_reiniciar()
    {
        var a = new Arnes(ModosAtencion.Asistente);
        a.Sonda.Responde = true;
        Assert.False(a.Columnas.Disponibles);

        a.Opciones.CurrentValue.ModoAtencion = ModosAtencion.Personal;
        Assert.True(a.Columnas.Disponibles);

        a.Opciones.CurrentValue.ModoAtencion = ModosAtencion.Asistente;     // de vuelta: las deja de usar al instante
        Assert.False(a.Columnas.Disponibles);
    }

    [Fact]
    public void Un_DAO_armado_sin_la_puerta_nunca_las_toca()
    {
        // new MensajeChatDAO() y new ConversacionDAO() (como en algunas pruebas) son el comportamiento de siempre. Se comprueba mirando los DAOs, no una base.
        var mensajes = typeof(MensajeChatDAO).GetConstructors().Single();
        var conversaciones = typeof(ConversacionDAO).GetConstructors().Single();

        Assert.True(mensajes.GetParameters().Single().IsOptional);
        Assert.True(conversaciones.GetParameters().Single().IsOptional);
        Assert.Null(mensajes.GetParameters().Single().DefaultValue);
        Assert.Null(conversaciones.GetParameters().Single().DefaultValue);
    }
}

// El modo Personal con lo que trae whatsapp-service. Los dobles de siempre, con el ServicioMensajes real.
public class AtencionPersonalConDatosTests
{
    private const string Telefono1 = "5491155550001";

    private sealed class Arnes
    {
        public readonly RelojFijo Reloj = new(Escenario.Jueves0900);
        public readonly OpcionesFijas Opciones = new();
        public readonly FakeConversaciones Conversaciones;
        public readonly MensajesChatEnMemoria Historial = new();
        public readonly CanalEspia Canal = new();
        public readonly FakeNotificador Notificador = new();
        public readonly LogsCapturados Logs = new();
        public readonly AtencionPersonalService Personal;

        public Arnes()
        {
            var op = Opciones.CurrentValue;
            op.ModoAtencion     = ModosAtencion.Personal;
            op.PalabrasRiesgo   = new() { "suicid", "no quiero vivir" };
            op.LineasEmergencia = new() { new LineaEmergencia { Nombre = "Emergencias", Telefono = "[COMPLETAR Y VALIDAR]" } };
            Conversaciones = new FakeConversaciones(Reloj);
            var mensajes = new ServicioMensajes(Historial, Canal, Reloj, Logs.CreateLogger<ServicioMensajes>());
            Personal = new AtencionPersonalService(Conversaciones, mensajes, Historial, new FiltroSeguridad(Opciones), Notificador, new FakePoliticaWhatsApp(), Opciones, Reloj,
                Logs.CreateLogger<AtencionPersonalService>());
        }

        public Task<RespuestaBot> Entra(string texto, DatosDelEntrante? datos, string telefono = Telefono1) => Personal.ProcesarAsync(telefono, texto, "Prueba", datos);
        public Conversacion Conversacion(string telefono = Telefono1) => Conversaciones.ObtenerPorTelefono(Telefono.Normalizar(telefono), "Prueba")!;
        public int Avisos(string tipo) => Notificador.Eventos.Count(e => e.StartsWith(tipo + ":"));
    }

    private static DatosDelEntrante Datos(string tipo = "texto", string? id = "3EB0AB", DateTime? hora = null, string? nombre = null) => new(tipo, id, hora, nombre);

    // ───────────── Lo que no es texto ─────────────

    [Theory]
    [InlineData("imagen", "[Imagen]")]
    [InlineData("audio", "[Audio]")]
    [InlineData("video", "[Video]")]
    [InlineData("documento", "[Documento]")]
    [InlineData("sticker", "[Sticker]")]
    [InlineData("ubicacion", "[Ubicación]")]
    [InlineData("contacto", "[Contacto]")]
    public async Task Una_foto_o_un_audio_se_guarda_como_aviso_del_tipo_y_no_se_le_contesta_nada(string tipo, string aviso)
    {
        var a = new Arnes();

        var r = await a.Entra(aviso, Datos(tipo));

        var m = Assert.Single(a.Historial.Todos);
        Assert.Equal((aviso, tipo, "3EB0AB", "Entrante", "Paciente"), (m.Texto, m.Tipo, m.IdExterno, m.Direccion, m.Origen));
        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);
        Assert.Equal(EstadoConversacion.DerivadaHumano, a.Conversacion().Estado);          // queda para atender, como un mensaje de texto
        Assert.Equal(1, a.Avisos("ChatNuevo"));                                            // y avisa en la campanita
        Assert.Equal(m.IdMensaje, r.IdMensaje);
        Assert.Null(r.Ignorado);
    }

    [Fact]
    public async Task Una_foto_con_leyenda_guarda_el_aviso_y_la_leyenda_a_continuacion()
    {
        var a = new Arnes();

        await a.Entra("[Imagen] mi receta del mes pasado", Datos("imagen"));

        Assert.Equal("[Imagen] mi receta del mes pasado", Assert.Single(a.Historial.Todos).Texto);
    }

    [Fact]
    public async Task La_leyenda_de_una_foto_tambien_pasa_por_el_filtro_de_riesgo()
    {
        var a = new Arnes();

        await a.Entra("[Imagen] no quiero vivir", Datos("imagen"));

        Assert.Equal(EstadoConversacion.Urgente, a.Conversacion().Estado);
        Assert.Equal(1, a.Avisos("UrgenciaSinRespuesta"));
        Assert.Equal(0, a.Avisos("ChatNuevo"));
    }

    [Fact]
    public async Task Un_cliente_que_manda_un_audio_sin_aviso_en_el_texto_lo_recibe_armado_por_la_API()
    {
        var a = new Arnes();

        await a.Entra("", Datos("audio"));

        Assert.Equal("[Audio]", Assert.Single(a.Historial.Todos).Texto);
    }

    [Fact]
    public async Task Un_texto_vacio_sigue_siendo_un_error_aunque_traiga_datos()
    {
        var a = new Arnes();

        await Assert.ThrowsAsync<ArgumentException>(() => a.Entra("   ", Datos("texto")));

        Assert.Empty(a.Historial.Todos);
    }

    // ───────────── Un mensaje repetido ─────────────

    [Fact]
    public async Task El_mismo_id_dos_veces_se_guarda_una_vez_y_no_vuelve_a_avisar()
    {
        var a = new Arnes();
        await a.Entra("hola", Datos(id: "3EB0AB"));
        var c = a.Conversacion();
        c.Estado = EstadoConversacion.Cerrada;                      // la cierran antes de que llegue el reintento
        a.Conversaciones.Guardar(c);

        var repetido = await a.Entra("hola", Datos(id: "3EB0AB"));

        Assert.Single(a.Historial.Todos);
        Assert.Equal("duplicado", repetido.Ignorado);
        Assert.Empty(repetido.Respuestas);
        Assert.Null(repetido.IdMensaje);
        Assert.Equal(1, a.Avisos("ChatNuevo"));                     // el aviso de la primera vez; el repetido no avisa
        Assert.Equal(EstadoConversacion.Cerrada, a.Conversacion().Estado);   // y no la reabre
    }

    [Fact]
    public async Task Ids_distintos_son_mensajes_distintos_y_sin_id_no_se_descarta_nada()
    {
        var a = new Arnes();

        await a.Entra("uno", Datos(id: "A1"));
        await a.Entra("dos", Datos(id: "A2"));
        await a.Entra("tres", Datos(id: null));
        await a.Entra("tres", Datos(id: null));                    // sin id no hay cómo saber que es el mismo: entra

        Assert.Equal(4, a.Historial.Todos.Count);
    }

    // ───────────── El nombre del perfil ─────────────

    [Fact]
    public async Task El_nombre_del_perfil_se_guarda_en_la_conversacion_una_vez_y_se_actualiza_si_cambia()
    {
        var a = new Arnes();

        await a.Entra("hola", Datos(id: "A1", nombre: "Juan P."));
        await a.Entra("sigo acá", Datos(id: "A2", nombre: "Juan P."));
        await a.Entra("otra cosa", Datos(id: "A3", nombre: "Juan Pérez"));
        await a.Entra("sin nombre", Datos(id: "A4", nombre: null));          // un mensaje sin nombre no lo borra

        Assert.Equal(new[] { "Juan P.", "Juan Pérez" }, a.Conversaciones.NombresGuardados.Select(n => n.Nombre));
        Assert.Equal("Juan Pérez", a.Conversacion().NombreContacto);
    }

    [Fact]
    public async Task El_nombre_del_perfil_no_aparece_en_ningun_log_ni_en_el_aviso_de_la_campanita()
    {
        var a = new Arnes();

        await a.Entra("hola", Datos(nombre: "Zoraida Xylofona"));
        await a.Entra("[Imagen] no quiero vivir", Datos("imagen", id: "A2", nombre: "Zoraida Xylofona"));

        Assert.DoesNotContain("Zoraida", a.Logs.Todo);
        Assert.DoesNotContain("Xylofona", a.Logs.Todo);
        Assert.DoesNotContain(a.Notificador.Eventos, e => e.Contains("Zoraida"));
    }

    // ───────────── La hora ─────────────

    [Fact]
    public async Task La_hora_real_del_mensaje_se_guarda_si_viene_y_si_no_la_de_ahora()
    {
        var a = new Arnes();
        var hace7 = Escenario.Jueves0900.AddMinutes(-7);

        await a.Entra("con hora", Datos(id: "A1", hora: hace7));
        await a.Entra("sin hora", Datos(id: "A2", hora: null));

        Assert.Equal(new[] { hace7, Escenario.Jueves0900 }, a.Historial.Todos.Select(m => m.FechaHora));
        Assert.Equal(Escenario.Jueves0900, a.Conversacion().FechaUltimaActividad);       // la actividad de la conversación es la del servidor, no la que dice el mensaje
    }

    // ───────────── Sin datos: el simulador, o la versión anterior de whatsapp-service ─────────────

    [Fact]
    public async Task Sin_datos_se_atiende_como_siempre_y_no_se_guarda_id_ni_tipo()
    {
        var a = new Arnes();

        var r = await a.Entra("hola", datos: null);

        var m = Assert.Single(a.Historial.Todos);
        Assert.Null(m.IdExterno);
        Assert.Null(m.Tipo);
        Assert.Empty(a.Conversaciones.NombresGuardados);
        Assert.NotNull(r.IdMensaje);
    }
}

// La puerta con los datos: el modo Asistente no cambia (salvo que ignora lo que no es texto, que antes ni le llegaba) y los repetidos se descartan en los dos modos.
public class RecepcionConDatosTests
{
    private sealed class Arnes
    {
        public readonly Escenario Esc = new();
        public readonly RelojFijo Reloj = new(Escenario.Jueves0900);
        public readonly FakeConversaciones Conversaciones;
        public readonly MensajesChatEnMemoria Historial = new();
        public readonly CanalEspia Canal = new();
        public readonly FakeNotificador Notificador = new();
        public readonly LogsCapturados Logs = new();
        public readonly AtencionPersonalService Personal;
        public readonly RecepcionDeMensajes Puerta;

        public Arnes(string modo)
        {
            Esc.Opciones.CurrentValue.ModoAtencion = modo;
            Esc.Opciones.CurrentValue.PalabrasRiesgo = new() { "suicid" };
            Conversaciones = new FakeConversaciones(Reloj);
            var mensajes = new ServicioMensajes(Historial, Canal, Reloj, Logs.CreateLogger<ServicioMensajes>());
            Personal = new AtencionPersonalService(Conversaciones, mensajes, Historial, new FiltroSeguridad(Esc.Opciones), Notificador, new FakePoliticaWhatsApp(), Esc.Opciones, Reloj,
                Logs.CreateLogger<AtencionPersonalService>());
            Puerta = new RecepcionDeMensajes(Esc.Bot, Personal, Esc.Opciones, new EntrantesVistos(Reloj));
        }
    }

    private static DatosDelEntrante Datos(string tipo = "texto", string? id = "3EB0AB") => new(tipo, id, null, null);

    [Fact]
    public async Task En_modo_Asistente_una_foto_no_se_atiende_ni_crea_una_conversacion()
    {
        var a = new Arnes(ModosAtencion.Asistente);

        var r = await a.Puerta.ProcesarAsync("5491155550001", "[Imagen] mi receta", "Simulado", Datos("imagen"));

        Assert.Equal("no_textual", r.Ignorado);
        Assert.Empty(r.Respuestas);
        Assert.Equal(0, r.IdConversacion);
        Assert.Empty(a.Esc.Conversaciones.Todas);                    // el asistente ni se enteró
        Assert.Empty(a.Historial.Todos);
    }

    [Fact]
    public async Task En_modo_Asistente_un_texto_con_los_campos_nuevos_lo_atiende_el_asistente_como_siempre()
    {
        var conDatos = new Arnes(ModosAtencion.Asistente);
        var comoAntes = new Arnes(ModosAtencion.Asistente);          // otro escenario limpio: mismo teléfono, mismo estado de partida

        var r1 = await conDatos.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", new DatosDelEntrante("texto", "3EB0AB", Escenario.Jueves0900.AddMinutes(-1), "Juan"));
        var r2 = await comoAntes.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado");

        Assert.NotEmpty(r1.Respuestas);                              // el menú del asistente
        Assert.Equal(r2.Respuestas, r1.Respuestas);                  // exactamente lo mismo que sin los campos nuevos
        Assert.Null(r1.Ignorado);
        Assert.Empty(conDatos.Historial.Todos);                      // y el servicio del personal no hizo nada
    }

    [Theory]
    [InlineData(ModosAtencion.Asistente)]
    [InlineData(ModosAtencion.Personal)]
    public async Task Un_mensaje_repetido_se_descarta_en_los_dos_modos(string modo)
    {
        var a = new Arnes(modo);

        var primero = await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", Datos());
        var repetido = await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", Datos());
        var distinto = await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", Datos(id: "OTRO"));

        Assert.Null(primero.Ignorado);
        Assert.Equal("duplicado", repetido.Ignorado);
        Assert.Empty(repetido.Respuestas);
        Assert.Null(distinto.Ignorado);
    }

    [Fact]
    public async Task En_modo_Asistente_un_repetido_no_hace_avanzar_dos_veces_el_menu()
    {
        var a = new Arnes(ModosAtencion.Asistente);
        var primero = await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", Datos(id: "A1"));
        var estadoDespues = a.Esc.Conversaciones.Todas.Values.Single().PasoActual;

        await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", Datos(id: "A1"));         // el reintento de whatsapp-service

        Assert.Equal(estadoDespues, a.Esc.Conversaciones.Todas.Values.Single().PasoActual);
        Assert.Single(a.Esc.Mensajes.Mensajes, m => m.Direccion == "Entrante");
        Assert.NotEmpty(primero.Respuestas);
    }

    [Fact]
    public async Task Si_la_atencion_falla_el_reintento_con_el_mismo_id_si_entra()
    {
        var a = new Arnes(ModosAtencion.Personal);

        // Un teléfono inválido es un ArgumentException (400): no se atendió, así que el id queda libre.
        await Assert.ThrowsAsync<ArgumentException>(() => a.Puerta.ProcesarAsync("12", "hola", "Simulado", Datos(id: "A1")));
        await Assert.ThrowsAsync<ArgumentException>(() => a.Puerta.ProcesarAsync("12", "hola", "Simulado", Datos(id: "A1")));   // y sigue siendo un error, no un "duplicado"

        var bien = await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", Datos(id: "A1"));
        Assert.Null(bien.Ignorado);
    }

    [Fact]
    public async Task Sin_id_no_se_descarta_nada_aunque_se_repita_el_texto()
    {
        var a = new Arnes(ModosAtencion.Personal);

        await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado");
        var otra = await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado");
        var sinId = await a.Puerta.ProcesarAsync("5491155550001", "hola", "Simulado", Datos(id: null));

        Assert.Null(otra.Ignorado);
        Assert.Null(sinId.Ignorado);
        Assert.Equal(3, a.Historial.Todos.Count);
    }
}
