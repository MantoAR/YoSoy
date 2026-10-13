using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// POST /video/acceso: quién entra a la sala de un turno o de una admisión, con qué token y qué servidores ICE.
// La aplicación es la real (WebApplicationFactory, JWT de verdad, rate limiter y headers de Program.cs); solo el
// repositorio de turnos, la auditoría y el reloj son dobles (ver VideoApp). Con la base real hay una pasada en
// VideoBaseRealTests. El turno de referencia es el 1 (paciente 301 / usuario 31, profesional 201 / usuario 21),
// empieza el martes 4/3/2031 10:00 y dura 30 minutos: la sala abre 09:50 y cierra 11:00.
public class VideoAccesoEndpointsTests
{
    private static async Task<(HttpResponseMessage R, JsonElement J)> Pedir(HttpClient cliente, string origen = "turno", int id = VideoApp.IdTurno)
    {
        var r = await VideoApp.Acceder(cliente, origen, id);
        return (r, await VideoApp.Json(r));
    }

    private static string Cadena(JsonElement j, string propiedad) => j.GetProperty(propiedad).GetString()!;
    private static JsonElement ClaimsDe(JsonElement j) => TokenDeSala.Verificar(Cadena(j, "token"), VideoApp.SecretoSenal);
    private static string[] Nombres(JsonElement j) => j.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    // La respuesta entera (status, headers menos Date y cuerpo) en un texto, para compararla con otra.
    private static async Task<string> Huella(HttpClient cliente, string origen, int id)
    {
        var r = await VideoApp.Acceder(cliente, origen, id);
        var cabeceras = r.Headers.Concat(r.Content.Headers)
            .Where(h => !h.Key.Equals("Date", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}")
            .OrderBy(l => l, StringComparer.Ordinal);
        return $"{(int)r.StatusCode}\n{string.Join("\n", cabeceras)}\n\n{await r.Content.ReadAsStringAsync()}";
    }

    // ---------- Autenticación ----------

    [Fact]
    public async Task Sin_token_da_401()
    {
        using var app = new VideoApp();

        var r = await VideoApp.Acceder(app.SinSesion(), "turno", VideoApp.IdTurno);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Empty(app.Auditoria.Registros);
    }

    [Fact]
    public async Task Con_un_token_que_no_es_un_jwt_da_401()
    {
        using var app = new VideoApp();
        var r = await VideoApp.Acceder(app.ConToken("esto.no.es-un-jwt"), "turno", VideoApp.IdTurno);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // El token de sala se firma con otro secreto que el login: no vale como sesión de la API.
    [Fact]
    public async Task El_token_de_sala_no_sirve_como_sesion_de_la_api()
    {
        using var app = new VideoApp();
        var (r, j) = await Pedir(app.Paciente());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var intento = await VideoApp.Acceder(app.ConToken(Cadena(j, "token")), "turno", VideoApp.IdTurno);

        Assert.Equal(HttpStatusCode.Unauthorized, intento.StatusCode);
    }

    [Fact]
    public async Task El_encabezado_legacy_sin_jwt_no_alcanza_para_entrar()
    {
        using var app = new VideoApp();
        var cliente = app.SinSesion();
        cliente.DefaultRequestHeaders.Add("X-Usuario-Id", "3");

        var r = await VideoApp.Acceder(cliente, "turno", VideoApp.IdTurno);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // La autenticación va antes que la configuración: sin sesión nadie se entera de si el video está o no habilitado.
    [Fact]
    public async Task Sin_sesion_se_contesta_401_aunque_el_video_no_este_configurado()
    {
        using var app = new VideoApp(new() { ["Video:SignalingSecreto"] = "" });

        var r = await VideoApp.Acceder(app.SinSesion(), "turno", VideoApp.IdTurno);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ---------- El pedido ----------

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"origen":"turno"}""")]
    [InlineData("""{"id":1}""")]
    [InlineData("""{"origen":"turno","id":0}""")]
    [InlineData("""{"origen":"turno","id":-4}""")]
    [InlineData("""{"origen":"sala","id":1}""")]
    [InlineData("""{"origen":"","id":1}""")]
    [InlineData("""{"origen":null,"id":1}""")]
    [InlineData("""{"origen":"turno","id":"uno"}""")]
    [InlineData("""{"origen":"turno","id":1.5}""")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("")]
    public async Task Un_pedido_mal_armado_da_400(string cuerpo)
    {
        using var app = new VideoApp();

        var r = await app.Paciente().PostAsync("/video/acceso", new StringContent(cuerpo, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(app.Auditoria.Registros);
    }

    [Theory]
    [InlineData("turno")]
    [InlineData("Turno")]
    [InlineData(" TURNO ")]
    public async Task El_origen_turno_se_reconoce_sin_importar_mayusculas_ni_espacios(string origen)
    {
        using var app = new VideoApp();
        var (r, _) = await Pedir(app.Paciente(), origen, VideoApp.IdTurno);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("admision")]
    [InlineData("Admisión")]
    public async Task El_origen_admision_se_reconoce_con_y_sin_tilde(string origen)
    {
        using var app = new VideoApp();
        var (r, _) = await Pedir(app.Paciente(), origen, VideoApp.IdInforme);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // ---------- Turno: quién entra ----------

    [Fact]
    public async Task El_paciente_del_turno_entra_con_rol_paciente()
    {
        using var app = new VideoApp();

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("paciente", Cadena(j, "rol"));
        Assert.Equal(VideoApp.SalaDelTurno, Cadena(j, "sala"));
        var claims = ClaimsDe(j);
        Assert.Equal("paciente", claims.GetProperty("rol").GetString());
        Assert.Equal(VideoApp.SalaDelTurno, claims.GetProperty("sala").GetString());
        Assert.Equal("31", claims.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task El_profesional_del_turno_entra_con_rol_profesional()
    {
        using var app = new VideoApp();

        var (r, j) = await Pedir(app.Profesional());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("profesional", Cadena(j, "rol"));
        var claims = ClaimsDe(j);
        Assert.Equal("profesional", claims.GetProperty("rol").GetString());
        Assert.Equal("21", claims.GetProperty("sub").GetString());
    }

    // Igual que PUT /turnos/{id}/generar-sala: el Configurador entra a cualquier turno, en el asiento del profesional.
    [Fact]
    public async Task El_configurador_entra_a_cualquier_turno_con_rol_profesional()
    {
        using var app = new VideoApp();

        var (r, j) = await Pedir(app.Configurador());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("profesional", ClaimsDe(j).GetProperty("rol").GetString());
        Assert.Equal("11", ClaimsDe(j).GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Un_ajeno_recibe_exactamente_la_misma_respuesta_que_un_id_que_no_existe()
    {
        // "Existe pero no sos parte" y "no existe" se contestan EXACTAMENTE igual: mismo status, mismos headers y mismo
        // cuerpo (se compara la respuesta entera, no solo el status), así una persona con sesión no puede averiguar qué
        // ids de turno existen. Por dentro siguen siendo dos casos: solo el primero se audita (ver más abajo).
        using var app = new VideoApp();
        var noExiste = await Huella(app.Paciente(), "turno", 999);
        Assert.StartsWith("404\n", noExiste);

        var ajenos = new (string Quien, HttpClient Cliente)[]
        {
            ("otro paciente", app.OtroPaciente()),
            ("otro profesional", app.OtroProfesional()),
            ("paciente sin id de paciente en su jwt", app.Cliente(40, Roles.Paciente)),
            ("profesional con el id de paciente del turno", app.Cliente(41, Roles.Profesional, idPaciente: VideoApp.IdPaciente, idProfesional: 999)),
            ("paciente con el id de profesional del turno", app.Cliente(42, Roles.Paciente, idPaciente: 999, idProfesional: VideoApp.IdProfesional)),
            ("rol desconocido", app.Cliente(50, "otro", VideoApp.IdPaciente, VideoApp.IdProfesional))
        };
        foreach (var (quien, cliente) in ajenos)
        {
            var huella = await Huella(cliente, "turno", VideoApp.IdTurno);
            Assert.True(noExiste == huella,
                $"{quien}: la respuesta no es idéntica a la de un id que no existe.\n--- no existe ---\n{noExiste}\n--- {quien} ---\n{huella}");
        }
    }

    // Tampoco cambia con el estado, el horario ni la sala del turno: el ajeno no se entera de ninguno de los tres.
    [Fact]
    public async Task La_respuesta_a_un_ajeno_no_cambia_con_el_estado_el_horario_ni_la_sala_del_turno()
    {
        using var app = new VideoApp();
        var noExiste = await Huella(app.OtroPaciente(), "turno", 999);

        var confirmadoYEnHorario = await Huella(app.OtroPaciente(), "turno", VideoApp.IdTurno);

        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { Estado = "Cancelado", CodigoSala = null };
        app.Reloj.Ahora = VideoApp.Inicio.AddDays(-30);
        var canceladoSinSalaYMuchoAntes = await Huella(app.OtroPaciente(), "turno", VideoApp.IdTurno);

        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { Estado = "Completado", CodigoSala = "sala con espacios" };
        app.Reloj.Ahora = VideoApp.Inicio.AddDays(30);
        var completadoYMuchoDespues = await Huella(app.OtroProfesional(), "turno", VideoApp.IdTurno);

        Assert.Equal(noExiste, confirmadoYEnHorario);
        Assert.Equal(noExiste, canceladoSinSalaYMuchoAntes);
        Assert.Equal(noExiste, completadoYMuchoDespues);
    }

    [Fact]
    public async Task Un_turno_que_no_existe_da_404_con_un_mensaje_que_no_dice_mas()
    {
        using var app = new VideoApp();

        var (r, j) = await Pedir(app.Paciente(), "turno", 999);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("no-existe", Cadena(j, "codigo"));
        Assert.Equal("No encontramos esa sesión.", Cadena(j, "mensaje"));
        Assert.Equal(new[] { "codigo", "mensaje" }, Nombres(j));
        Assert.Empty(app.Auditoria.Registros);   // un id que no existe no tiene a qué atarse
    }

    [Fact]
    public async Task Una_admision_que_no_existe_da_404()
    {
        using var app = new VideoApp();
        var (r, j) = await Pedir(app.Configurador(), "admision", 999);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("no-existe", Cadena(j, "codigo"));
    }

    // Un cliente no puede elegir la sala: la sala sale del turno por el que se pregunta, y solo si pertenece.
    [Fact]
    public async Task La_sala_es_la_del_turno_pedido_y_solo_para_quienes_pertenecen_a_ese_turno()
    {
        using var app = new VideoApp();
        app.Origen.Turnos[2] = new TurnoParaSala(2, VideoApp.IdOtroPaciente, VideoApp.IdOtroProfesional, "Confirmado",
            VideoApp.Inicio.Date, VideoApp.Inicio.TimeOfDay, "turno-2-otra-sala", 30);

        var (rAjeno, _) = await Pedir(app.Paciente(), "turno", 2);
        var (rPropio, jPropio) = await Pedir(app.OtroPaciente(), "turno", 2);

        Assert.Equal(HttpStatusCode.NotFound, rAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rPropio.StatusCode);
        Assert.Equal("turno-2-otra-sala", Cadena(jPropio, "sala"));
        Assert.Equal("turno-2-otra-sala", ClaimsDe(jPropio).GetProperty("sala").GetString());
    }

    // ---------- Turno: estado y sala ----------

    [Theory]
    [InlineData("Pendiente", "El turno todavía no está confirmado.")]
    [InlineData("Cancelado", "El turno fue cancelado.")]
    [InlineData("Completado", "El turno ya fue realizado.")]
    [InlineData("Ausente", "El turno no está disponible para una videollamada.")]
    public async Task Un_turno_que_no_esta_confirmado_da_409_con_el_motivo(string estado, string mensaje)
    {
        using var app = new VideoApp();
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { Estado = estado };

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("estado-invalido", Cadena(j, "codigo"));
        Assert.Equal(mensaje, Cadena(j, "mensaje"));
        Assert.False(j.TryGetProperty("token", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Un_turno_sin_sala_generada_da_409_y_no_se_crea_la_sala(string? codigo)
    {
        using var app = new VideoApp();
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { CodigoSala = codigo };

        var (r, j) = await Pedir(app.Profesional());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("sala-no-generada", Cadena(j, "codigo"));
        Assert.Equal(codigo, app.Origen.Turnos[1].CodigoSala);   // el endpoint no escribe nada
    }

    [Fact]
    public async Task Un_codigo_de_sala_que_el_signaling_no_aceptaria_da_409_y_no_se_emite_token()
    {
        using var app = new VideoApp();
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { CodigoSala = "sala con espacios" };

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("sala-invalida", Cadena(j, "codigo"));
        Assert.False(j.TryGetProperty("token", out _));
    }

    // ---------- Turno: ventana horaria ----------

    [Theory]
    [InlineData(-86400, 409, "aun-no-abre")]
    [InlineData(-601, 409, "aun-no-abre")]     // un segundo antes de abrir
    [InlineData(-600, 200, "ok")]              // 10 minutos antes: abre justo ahí
    [InlineData(0, 200, "ok")]
    [InlineData(1800, 200, "ok")]              // termina el turno: quedan 30 minutos de gracia
    [InlineData(3599, 200, "ok")]              // un segundo antes de cerrar
    [InlineData(3600, 409, "ya-termino")]      // fin + 30 min: cierra justo ahí
    [InlineData(86400, 409, "ya-termino")]
    public async Task La_ventana_del_turno_abre_10_minutos_antes_y_cierra_30_despues_del_fin(int segundos, int status, string codigo)
    {
        using var app = new VideoApp();
        app.Reloj.Ahora = VideoApp.Inicio.AddSeconds(segundos);

        foreach (var cliente in new[] { app.Paciente(), app.Profesional() })
        {
            var (r, j) = await Pedir(cliente);

            Assert.Equal(status, (int)r.StatusCode);
            if (status == 409) Assert.Equal(codigo, Cadena(j, "codigo"));
            else Assert.Equal(codigo, "ok");
        }
    }

    [Fact]
    public async Task Antes_de_abrir_el_409_dice_cuando_abre()
    {
        using var app = new VideoApp();
        app.Reloj.Ahora = VideoApp.Inicio.AddMinutes(-30);

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("aun-no-abre", Cadena(j, "codigo"));
        Assert.Equal("Todavía no se puede entrar: la sala se abre 10 minutos antes del horario.", Cadena(j, "mensaje"));
        Assert.Equal(VideoApp.Utc(VideoApp.Inicio.AddMinutes(-10)), j.GetProperty("abreUtc").GetDateTimeOffset());
        Assert.False(j.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task Despues_de_cerrar_el_409_dice_que_ya_termino_y_cuando_cerro()
    {
        using var app = new VideoApp();
        app.Reloj.Ahora = VideoApp.Inicio.AddHours(3);

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ya-termino", Cadena(j, "codigo"));
        Assert.Equal("Esta sesión ya terminó.", Cadena(j, "mensaje"));
        Assert.Equal(VideoApp.Utc(VideoApp.Inicio.AddMinutes(60)), j.GetProperty("cerroUtc").GetDateTimeOffset());
    }

    // La ventana usa la duración real del turno, y 10/30 son configurables.
    [Fact]
    public async Task La_ventana_usa_la_duracion_del_turno_y_los_minutos_configurados()
    {
        using var app = new VideoApp(new()
        {
            ["Video:Ventana:MinutosAntes"] = "5",
            ["Video:Ventana:MinutosDespues"] = "15"
        });
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { DuracionMinutos = 50 };   // 10:00 → 10:50, cierra 11:05, abre 09:55

        app.Reloj.Ahora = VideoApp.Inicio.AddSeconds(-301);
        Assert.Equal(HttpStatusCode.Conflict, (await Pedir(app.Paciente())).R.StatusCode);
        app.Reloj.Ahora = VideoApp.Inicio.AddSeconds(-300);
        Assert.Equal(HttpStatusCode.OK, (await Pedir(app.Paciente())).R.StatusCode);
        app.Reloj.Ahora = VideoApp.Inicio.AddMinutes(65).AddSeconds(-1);
        Assert.Equal(HttpStatusCode.OK, (await Pedir(app.Paciente())).R.StatusCode);
        app.Reloj.Ahora = VideoApp.Inicio.AddMinutes(65);
        Assert.Equal(HttpStatusCode.Conflict, (await Pedir(app.Paciente())).R.StatusCode);
    }

    // ---------- El token ----------

    // El cliente puede mandar lo que quiera en el cuerpo: rol, sala y sub salen del servidor.
    [Fact]
    public async Task El_rol_la_sala_y_el_sub_del_token_salen_del_servidor_no_del_pedido()
    {
        using var app = new VideoApp();
        var cliente = app.Paciente();

        var r = await cliente.PostAsJsonAsync("/video/acceso", new
        {
            origen = "turno", id = VideoApp.IdTurno,
            rol = "profesional", sala = "otra-sala", sub = "1", exp = 9999999999L, token = "x", idUsuario = 1
        });
        var j = await VideoApp.Json(r);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var claims = ClaimsDe(j);
        Assert.Equal("paciente", claims.GetProperty("rol").GetString());
        Assert.Equal(VideoApp.SalaDelTurno, claims.GetProperty("sala").GetString());
        Assert.Equal("31", claims.GetProperty("sub").GetString());
        Assert.True(TokenDeSala.Exp(claims) < VideoApp.Utc(VideoApp.Inicio.AddHours(3)));
    }

    [Fact]
    public async Task El_token_vence_al_cerrar_la_ventana_si_eso_llega_antes_que_el_tope()
    {
        using var app = new VideoApp();   // ahora 10:00, cierra 11:00, tope por defecto 2 h → 12:00

        var (_, j) = await Pedir(app.Paciente());

        var claims = ClaimsDe(j);
        Assert.Equal(VideoApp.Utc(VideoApp.Inicio.AddMinutes(60)), TokenDeSala.Exp(claims));
        Assert.Equal(TokenDeSala.Exp(claims), j.GetProperty("expiraUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task El_token_vence_a_ahora_mas_el_maximo_si_la_ventana_es_mas_larga()
    {
        using var app = new VideoApp();
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { DuracionMinutos = 480 };   // cierra a las 18:30

        var (_, j) = await Pedir(app.Paciente());

        Assert.Equal(VideoApp.Utc(VideoApp.Inicio.AddHours(2)), TokenDeSala.Exp(ClaimsDe(j)));
    }

    [Fact]
    public async Task El_maximo_de_vida_del_token_es_configurable()
    {
        using var app = new VideoApp(new() { ["Video:Token:MaxMinutos"] = "15" });

        var (_, j) = await Pedir(app.Paciente());

        Assert.Equal(VideoApp.Utc(VideoApp.Inicio.AddMinutes(15)), TokenDeSala.Exp(ClaimsDe(j)));
    }

    // Entrando sobre el borde, el token vive lo que queda de ventana y ni un segundo más.
    [Fact]
    public async Task Entrando_un_segundo_antes_del_cierre_el_token_vive_un_segundo()
    {
        using var app = new VideoApp();
        app.Reloj.Ahora = VideoApp.Inicio.AddMinutes(60).AddSeconds(-1);

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(VideoApp.Utc(VideoApp.Inicio.AddMinutes(60)), TokenDeSala.Exp(ClaimsDe(j)));
    }

    [Fact]
    public async Task La_respuesta_tiene_la_forma_que_espera_el_frontend()
    {
        using var app = new VideoApp();

        var (_, j) = await Pedir(app.Paciente());

        Assert.Equal(new[] { "expiraUtc", "iceServers", "rol", "sala", "token", "turnDisponible" }, Nombres(j));
        Assert.Equal(JsonValueKind.Array, j.GetProperty("iceServers").ValueKind);
    }

    // ---------- Servidores ICE ----------

    [Fact]
    public async Task La_respuesta_trae_STUN_y_TURN_con_credenciales_del_usuario()
    {
        using var app = new VideoApp();

        var (_, j) = await Pedir(app.Paciente());

        Assert.True(j.GetProperty("turnDisponible").GetBoolean());
        var ice = j.GetProperty("iceServers").EnumerateArray().ToList();
        Assert.Equal(2, ice.Count);

        Assert.Equal(new[] { "urls" }, Nombres(ice[0]));   // el STUN no lleva usuario ni clave
        Assert.Equal("stun:stun.prueba.test:3478", ice[0].GetProperty("urls")[0].GetString());

        Assert.Equal(new[] { "credential", "urls", "username" }, Nombres(ice[1]));
        var urls = ice[1].GetProperty("urls").EnumerateArray().Select(u => u.GetString()).ToArray();
        Assert.Equal(new[] { "turn:turn.prueba.test:3478?transport=udp", "turn:turn.prueba.test:3478?transport=tcp" }, urls);
        var username = Cadena(ice[1], "username");
        Assert.Equal($"{VideoApp.Utc(VideoApp.Inicio.AddHours(2)).ToUnixTimeSeconds()}:31", username);   // TTL de 2 h
        Assert.Equal(TurnCredentialsService.Credencial(VideoApp.SecretoTurn, username), Cadena(ice[1], "credential"));
    }

    // Las credenciales TURN no vencen antes que el token de sala ni antes de que termine la sesión.
    [Fact]
    public async Task Las_credenciales_TURN_no_vencen_antes_que_el_token_ni_que_la_sesion()
    {
        using var app = new VideoApp(new() { ["Video:Turn:TtlMinutos"] = "5", ["Video:Token:MaxMinutos"] = "120" });

        var (_, j) = await Pedir(app.Paciente());

        var turn = j.GetProperty("iceServers").EnumerateArray().Last();
        var venceTurn = long.Parse(Cadena(turn, "username").Split(':')[0]);
        Assert.True(venceTurn >= TokenDeSala.Exp(ClaimsDe(j)).ToUnixTimeSeconds());
        Assert.True(venceTurn >= VideoApp.Utc(VideoApp.Inicio.AddMinutes(60)).ToUnixTimeSeconds());   // cierre de la ventana
    }

    [Fact]
    public async Task Sin_TURN_configurado_la_respuesta_trae_solo_STUN_y_lo_dice()
    {
        using var app = new VideoApp(new() { ["Video:Ice:TurnUrls:0"] = "", ["Video:Ice:TurnUrls:1"] = "" });

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(j.GetProperty("turnDisponible").GetBoolean());
        var ice = Assert.Single(j.GetProperty("iceServers").EnumerateArray());
        Assert.Equal(new[] { "urls" }, Nombres(ice));
        Assert.DoesNotContain("credential", j.GetRawText());
    }

    // Con TURN pero sin un secreto válido de TURN el acceso sigue funcionando (el video por STUN anda en la
    // mayoría de las redes): se entrega sin TURN y el motivo queda en el log.
    [Fact]
    public async Task Con_URLs_de_TURN_pero_sin_secreto_de_TURN_se_entrega_el_acceso_solo_con_STUN_y_se_avisa()
    {
        using var app = new VideoApp(new() { ["Video:TurnSecreto"] = "" });

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(j.GetProperty("turnDisponible").GetBoolean());
        Assert.Single(j.GetProperty("iceServers").EnumerateArray());
        Assert.Contains(app.Logs.Lineas, l => l.StartsWith("Warning|") && l.Contains("Video__TurnSecreto"));
    }

    [Fact]
    public async Task Sin_STUN_configurado_se_usa_el_de_siempre()
    {
        using var app = new VideoApp(new() { ["Video:Ice:StunUrls:0"] = "" });

        var (_, j) = await Pedir(app.Paciente());

        var stun = j.GetProperty("iceServers")[0];
        Assert.Equal("stun:stun.l.google.com:19302", stun.GetProperty("urls")[0].GetString());
    }

    // ---------- Sin secreto de señalización: 503 ----------

    [Theory]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("k7Q-9zX")]
    [InlineData("1234567890123456789012345678901")]   // 31 caracteres: uno menos que el mínimo
    public async Task Sin_un_secreto_de_senalizacion_valido_da_503_generico_y_la_api_sigue_andando(string secreto)
    {
        using var app = new VideoApp(new() { ["Video:SignalingSecreto"] = secreto });

        var (r, j) = await Pedir(app.Paciente());

        await VerificarServicioNoDisponible(app, r, j, secreto);
    }

    [Fact]
    public async Task Sin_la_variable_del_secreto_de_senalizacion_da_503()
    {
        using var app = new VideoApp(new() { ["Video:SignalingSecreto"] = null });

        var (r, j) = await Pedir(app.Paciente());

        await VerificarServicioNoDisponible(app, r, j, null);
    }

    // Un secreto de video igual a Jwt:Key dejaría a quien lo tiene (el proceso de señalización) falsificar un login.
    [Fact]
    public async Task Un_secreto_de_senalizacion_igual_a_Jwt_Key_da_503()
    {
        using var app = new VideoApp(servicios: s =>
            s.AddOptions<VideoOptions>().Configure<IConfiguration>((o, c) => o.SignalingSecreto = c["Jwt:Key"]));

        var (r, j) = await Pedir(app.Paciente());

        await VerificarServicioNoDisponible(app, r, j, app.JwtKey);
        Assert.Contains(app.Logs.Lineas, l => l.Contains("Jwt:Key"));
    }

    private static async Task VerificarServicioNoDisponible(VideoApp app, HttpResponseMessage r, JsonElement j, string? secreto)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("video-no-disponible", Cadena(j, "codigo"));
        Assert.Equal(new[] { "codigo", "mensaje" }, Nombres(j));

        // El mensaje es genérico: no dice qué variable falta ni nada de la configuración.
        var texto = j.GetRawText();
        Assert.DoesNotContain("Video__", texto);
        Assert.DoesNotContain("ecreto", texto);
        Assert.DoesNotContain("Jwt", texto);
        if (!string.IsNullOrWhiteSpace(secreto)) Assert.DoesNotContain(secreto, texto);

        // El motivo real queda en el log para quien administra, sin el valor del secreto.
        Assert.Contains(app.Logs.Lineas, l => l.StartsWith("Warning|") && l.Contains("Video__SignalingSecreto"));
        Assert.Contains(app.Logs.Lineas, l => l.Contains("deshabilitado hasta que se configure"));   // y avisa también al arrancar
        if (!string.IsNullOrWhiteSpace(secreto)) Assert.DoesNotContain(secreto, app.Logs.Todo);

        Assert.Empty(app.Auditoria.Registros);

        // La API arrancó y sigue sirviendo todo lo demás.
        Assert.Equal(HttpStatusCode.OK, (await app.SinSesion().GetAsync("/capacidades")).StatusCode);
    }

    // 32 caracteres es el mínimo que acepta el signaling (y el que exige esta API): con exactamente 32 anda.
    [Fact]
    public async Task Un_secreto_de_exactamente_32_caracteres_alcanza()
    {
        var secreto = new string('s', 32);
        using var app = new VideoApp(new() { ["Video:SignalingSecreto"] = secreto });

        var (r, j) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        TokenDeSala.Verificar(Cadena(j, "token"), secreto);
    }

    // ---------- Admisión ----------

    [Fact]
    public async Task En_la_admision_entran_el_paciente_del_informe_y_el_configurador()
    {
        using var app = new VideoApp();

        var (rPaciente, jPaciente) = await Pedir(app.Paciente(), "admision", VideoApp.IdInforme);
        var (rConfigurador, jConfigurador) = await Pedir(app.Configurador(), "admision", VideoApp.IdInforme);

        Assert.Equal(HttpStatusCode.OK, rPaciente.StatusCode);
        Assert.Equal("paciente", ClaimsDe(jPaciente).GetProperty("rol").GetString());
        Assert.Equal(VideoApp.SalaDeLaAdmision, ClaimsDe(jPaciente).GetProperty("sala").GetString());
        Assert.Equal(HttpStatusCode.OK, rConfigurador.StatusCode);
        Assert.Equal("profesional", ClaimsDe(jConfigurador).GetProperty("rol").GetString());
        Assert.Equal(VideoApp.SalaDeLaAdmision, ClaimsDe(jConfigurador).GetProperty("sala").GetString());
    }

    // La entrevista la hace el Configurador: un profesional no entra a una admisión, ni el paciente de otro informe.
    // Los tres reciben exactamente lo mismo que con un id que no existe.
    [Fact]
    public async Task En_la_admision_un_ajeno_recibe_exactamente_la_misma_respuesta_que_un_id_que_no_existe()
    {
        using var app = new VideoApp();
        var noExiste = await Huella(app.Configurador(), "admision", 999);
        Assert.StartsWith("404\n", noExiste);

        var ajenos = new (string Quien, HttpClient Cliente)[]
        {
            ("profesional", app.Profesional()),
            ("otro profesional", app.OtroProfesional()),
            ("otro paciente", app.OtroPaciente())
        };
        foreach (var (quien, cliente) in ajenos)
        {
            var huella = await Huella(cliente, "admision", VideoApp.IdInforme);
            Assert.True(noExiste == huella,
                $"{quien}: la respuesta no es idéntica a la de un id que no existe.\n--- no existe ---\n{noExiste}\n--- {quien} ---\n{huella}");
        }
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Derivado")]
    [InlineData("Rechazado")]
    public async Task Una_admision_que_no_esta_en_entrevista_da_409(string estado)
    {
        using var app = new VideoApp();
        app.Origen.Admisiones[VideoApp.IdInforme] = app.Origen.Admisiones[VideoApp.IdInforme] with { Estado = estado };

        var (r, j) = await Pedir(app.Paciente(), "admision", VideoApp.IdInforme);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("estado-invalido", Cadena(j, "codigo"));
    }

    [Theory]
    [InlineData(-601, 409, "aun-no-abre")]
    [InlineData(-600, 200, "ok")]
    [InlineData(3599, 200, "ok")]
    [InlineData(3600, 409, "ya-termino")]
    public async Task La_ventana_de_la_admision_es_la_misma_que_la_del_turno(int segundos, int status, string codigo)
    {
        using var app = new VideoApp();
        app.Reloj.Ahora = VideoApp.Inicio.AddSeconds(segundos);

        var (r, j) = await Pedir(app.Configurador(), "admision", VideoApp.IdInforme);

        Assert.Equal(status, (int)r.StatusCode);
        if (status == 409) Assert.Equal(codigo, Cadena(j, "codigo"));
    }

    [Fact]
    public async Task Una_admision_sin_sala_da_409()
    {
        using var app = new VideoApp();
        app.Origen.Admisiones[VideoApp.IdInforme] = app.Origen.Admisiones[VideoApp.IdInforme] with { CodigoSala = null };

        var (r, j) = await Pedir(app.Paciente(), "admision", VideoApp.IdInforme);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("sala-no-generada", Cadena(j, "codigo"));
    }

    // ---------- Auditoría ----------

    [Fact]
    public async Task Los_accesos_y_los_rechazos_se_auditan_con_usuario_rol_origen_id_y_resultado()
    {
        using var app = new VideoApp();

        await Pedir(app.Paciente());                                                // concedido
        await Pedir(app.OtroProfesional());                                         // no es parte: responde 404 uniforme, pero se audita como sin-acceso
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { Estado = "Cancelado" };
        await Pedir(app.Paciente());                                                // estado
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { Estado = "Confirmado", CodigoSala = null };
        await Pedir(app.Profesional());                                             // sin sala
        app.Origen.Turnos[1] = app.Origen.Turnos[1] with { CodigoSala = VideoApp.SalaDelTurno };
        app.Reloj.Ahora = VideoApp.Inicio.AddHours(-2);
        await Pedir(app.Paciente());                                                // todavía no
        app.Reloj.Ahora = VideoApp.Inicio.AddHours(2);
        await Pedir(app.Paciente());                                                // ya terminó
        app.Reloj.Ahora = VideoApp.Inicio;
        await Pedir(app.Configurador(), "admision", VideoApp.IdInforme);            // concedido en admisión

        Assert.Collection(app.Auditoria.Registros,
            r => Assert.Equal((31, "turno", 1, "paciente", "ok"), (r.IdUsuario, r.Origen, r.IdOrigen, r.Rol, r.Resultado.Codigo)),
            r => Assert.Equal((22, "turno", 1, "profesional", "sin-acceso"), (r.IdUsuario, r.Origen, r.IdOrigen, r.Rol, r.Resultado.Codigo)),
            r => Assert.Equal((31, "turno", 1, "paciente", "estado-invalido"), (r.IdUsuario, r.Origen, r.IdOrigen, r.Rol, r.Resultado.Codigo)),
            r => Assert.Equal((21, "turno", 1, "profesional", "sala-no-generada"), (r.IdUsuario, r.Origen, r.IdOrigen, r.Rol, r.Resultado.Codigo)),
            r => Assert.Equal((31, "turno", 1, "paciente", "aun-no-abre"), (r.IdUsuario, r.Origen, r.IdOrigen, r.Rol, r.Resultado.Codigo)),
            r => Assert.Equal((31, "turno", 1, "paciente", "ya-termino"), (r.IdUsuario, r.Origen, r.IdOrigen, r.Rol, r.Resultado.Codigo)),
            r => Assert.Equal((11, "admision", 5, "profesional", "ok"), (r.IdUsuario, r.Origen, r.IdOrigen, r.Rol, r.Resultado.Codigo)));
    }

    // Lo concedido se audita con el rol con el que entró; lo rechazado, con el rol que el usuario tiene en el sistema.
    [Fact]
    public async Task El_configurador_que_entra_a_un_turno_queda_auditado_como_profesional_del_turno()
    {
        using var app = new VideoApp();

        await Pedir(app.Configurador());

        var registro = Assert.Single(app.Auditoria.Registros);
        Assert.Equal(11, registro.IdUsuario);
        Assert.Equal("profesional", registro.Rol);
    }

    [Fact]
    public async Task Los_pedidos_que_no_llegan_a_evaluarse_no_se_auditan()
    {
        using var app = new VideoApp();
        await Pedir(app.Paciente(), "turno", 999);                                        // 404
        await VideoApp.Acceder(app.SinSesion(), "turno", 1);                              // 401
        await app.Paciente().PostAsync("/video/acceso", new StringContent("{}", Encoding.UTF8, "application/json"));   // 400

        Assert.Empty(app.Auditoria.Registros);
    }

    // ---------- Cabeceras ----------

    [Fact]
    public async Task Ninguna_respuesta_de_video_acceso_se_cachea()
    {
        using var app = new VideoApp();
        var respuestas = new List<HttpResponseMessage>
        {
            (await Pedir(app.Paciente())).R,                                           // 200
            (await Pedir(app.OtroPaciente())).R,                                       // 404 (existe, pero no es suyo)
            (await Pedir(app.Paciente(), "turno", 999)).R,                             // 404
            (await app.Paciente().PostAsync("/video/acceso", new StringContent("{}", Encoding.UTF8, "application/json"))),   // 400
        };
        app.Reloj.Ahora = VideoApp.Inicio.AddDays(1);
        respuestas.Add((await Pedir(app.Paciente())).R);                               // 409

        Assert.Equal(new[] { 200, 404, 404, 400, 409 }, respuestas.Select(r => (int)r.StatusCode).ToArray());
        foreach (var r in respuestas) Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task La_respuesta_503_tampoco_se_cachea()
    {
        using var app = new VideoApp(new() { ["Video:SignalingSecreto"] = "" });

        var (r, _) = await Pedir(app.Paciente());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }

    // La cámara y el micrófono quedan permitidos solo para el propio origen (y los iframes del mismo origen).
    [Fact]
    public async Task Permissions_Policy_permite_camara_y_microfono_solo_al_propio_origen()
    {
        using var factory = new WebApplicationFactory<Program>();

        var r = await factory.CreateClient().GetAsync("/capacidades");

        Assert.Equal("camera=(self), microphone=(self)", string.Join(", ", r.Headers.GetValues("Permissions-Policy")));
    }

    // La política tiene que estar en la respuesta del documento que usa la cámara: la página de la sala.
    [Theory]
    [InlineData("/pantallas/Pantalla_Paciente/sesion_video.html")]
    [InlineData("/pantallas/Pantalla_Profesional/sesion_video.html")]
    public async Task Permissions_Policy_sale_en_las_paginas_de_la_sala_de_video(string ruta)
    {
        using var factory = new WebApplicationFactory<Program>();

        var r = await factory.CreateClient().GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("camera=(self), microphone=(self)", string.Join(", ", r.Headers.GetValues("Permissions-Policy")));
    }

    // También en lo que contesta el propio endpoint (aunque sea un rechazo), y sin pisar los demás headers de
    // seguridad: X-Frame-Options sigue en SAMEORIGIN porque la sala va embebida en un iframe del mismo origen. (El 401
    // de un pedido sin sesión lo corta la autorización antes del middleware de headers, igual que pasaba ya con
    // los otros tres headers: es un JSON sin contenido y no un documento donde se use la cámara.)
    [Fact]
    public async Task Permissions_Policy_sale_tambien_en_las_respuestas_del_endpoint_y_no_cambia_los_otros_headers()
    {
        using var app = new VideoApp();

        var r = await VideoApp.Acceder(app.OtroPaciente(), "turno", 1);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("camera=(self), microphone=(self)", string.Join(", ", r.Headers.GetValues("Permissions-Policy")));
        Assert.Equal("SAMEORIGIN", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", r.Headers.GetValues("Referrer-Policy").Single());
    }

    // ---------- Rate limit ----------

    // Política propia "video", por IP real (X-Forwarded-For confiado solo desde el proxy local, ver Program.cs): el
    // pedido siguiente al límite se rechaza, y una IP distinta tiene su propio contador. Cuenta por IP, no por usuario.
    [Fact]
    public async Task Pasado_el_limite_por_ip_se_rechaza_con_429_y_otra_ip_tiene_su_propio_contador()
    {
        using var app = new VideoApp(new() { ["Video:LimitePorMinuto"] = "3" });
        HttpClient DesdeLaIp(HttpClient c, string ip) { c.DefaultRequestHeaders.Add("X-Forwarded-For", ip); return c; }

        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(DesdeLaIp(app.Paciente(), "203.0.113.40"), "turno", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(DesdeLaIp(app.Paciente(), "203.0.113.40"), "turno", 1)).StatusCode);
        // Otro usuario, misma IP: comparte el contador.
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(DesdeLaIp(app.Profesional(), "203.0.113.40"), "turno", 1)).StatusCode);

        var cuarto = await VideoApp.Acceder(DesdeLaIp(app.Paciente(), "203.0.113.40"), "turno", 1);
        Assert.Equal(HttpStatusCode.TooManyRequests, cuarto.StatusCode);

        var otraIp = await VideoApp.Acceder(DesdeLaIp(app.Paciente(), "203.0.113.41"), "turno", 1);
        Assert.Equal(HttpStatusCode.OK, otraIp.StatusCode);
    }

    [Fact]
    public async Task El_limite_por_defecto_deja_pasar_20_pedidos_por_minuto()
    {
        // Sin pisar Video:LimitePorMinuto: es el valor de appsettings.json.
        using var app = new VideoApp(new() { ["Video:LimitePorMinuto"] = null });
        var cliente = app.Paciente();

        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(cliente, "turno", 1)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await VideoApp.Acceder(cliente, "turno", 1)).StatusCode);
    }

    // ---------- Lo que no se loguea ----------

    [Fact]
    public async Task Ni_el_token_ni_las_credenciales_ni_los_secretos_aparecen_en_los_logs()
    {
        using var app = new VideoApp();
        var secretos = new List<string> { VideoApp.SecretoSenal, VideoApp.SecretoTurn };

        foreach (var cliente in new[] { app.Paciente(), app.Profesional(), app.Configurador() })
        {
            var (r, j) = await Pedir(cliente);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            secretos.Add(Cadena(j, "token"));
            secretos.Add(Cadena(j.GetProperty("iceServers")[1], "credential"));
        }
        await Pedir(app.OtroPaciente());   // y un rechazo

        var logs = app.Logs.Todo;
        foreach (var secreto in secretos) Assert.DoesNotContain(secreto, logs);
    }

    // ---------- Configuración ----------

    // Los números de appsettings.json son los mismos que los valores por defecto del código, y los dos secretos no
    // están en el archivo (van por variable de entorno).
    [Fact]
    public void El_appsettings_trae_los_valores_documentados_y_ningun_secreto()
    {
        var raiz = RaizDelRepositorio();
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(raiz, "appsettings.json"), optional: false).Build();
        var enArchivo = new VideoOptions();
        config.GetSection(VideoOptions.Seccion).Bind(enArchivo);
        var porDefecto = new VideoOptions();

        Assert.Null(config["Video:SignalingSecreto"]);
        Assert.Null(config["Video:TurnSecreto"]);
        Assert.Equal(10, enArchivo.Ventana.MinutosAntes);
        Assert.Equal(30, enArchivo.Ventana.MinutosDespues);
        Assert.Equal(120, enArchivo.Token.MaxMinutos);
        Assert.Equal(120, enArchivo.Turn.TtlMinutos);
        Assert.Equal(30, enArchivo.Admision.DuracionMinutos);
        Assert.Equal(30, enArchivo.DuracionPorDefectoMinutos);
        Assert.Equal(20, enArchivo.LimitePorMinuto);
        Assert.Equal(porDefecto.Ventana.MinutosAntes, enArchivo.Ventana.MinutosAntes);
        Assert.Equal(porDefecto.Ventana.MinutosDespues, enArchivo.Ventana.MinutosDespues);
        Assert.Equal(porDefecto.Token.MaxMinutos, enArchivo.Token.MaxMinutos);
        Assert.Equal(porDefecto.Turn.TtlMinutos, enArchivo.Turn.TtlMinutos);
        Assert.Equal(porDefecto.Admision.DuracionMinutos, enArchivo.Admision.DuracionMinutos);
        Assert.Equal(porDefecto.DuracionPorDefectoMinutos, enArchivo.DuracionPorDefectoMinutos);
        Assert.Equal(porDefecto.LimitePorMinuto, enArchivo.LimitePorMinuto);
        Assert.Empty(enArchivo.Ice.StunUrls);   // vacías: el STUN de respaldo está en el código
        Assert.Empty(enArchivo.Ice.TurnUrls);
    }

    // La carpeta del proyecto web (donde están YoSoy.csproj, appsettings.json y signaling-server/), subiendo desde
    // la carpeta de salida de los tests. Se busca YoSoy.csproj y no appsettings.json porque la carpeta de salida
    // también trae una copia de appsettings.json.
    internal static string RaizDelRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "YoSoy.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No encontré YoSoy.csproj subiendo desde " + AppContext.BaseDirectory);
    }
}
