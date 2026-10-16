using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// Mejora de chats y centrado: pertenencia de conversaciones (cierre del alcance abierto en
// A1 - ver CAMBIOS_AGUS.md, "Pertenencia de conversaciones"). Corre contra una base de datos, igual que
// ChatsGestionTests. Crea sus propios datos (DatosDePrueba): el paciente, dos profesionales con su
// usuario, el vínculo PacienteProfesional del paciente con el profesional A (el caso "bot-only") y los
// usuarios Configurador y paciente, además de las conversaciones de prueba con teléfono único. Antes
// suponía los profesionales 1 y 2, el paciente 1 con su vínculo y los usuarios 1 a 4 de la base de desarrollo.
//
// No repite la matriz 401/403/404/200 endpoint por endpoint: todos pasan por el mismo punto
// de decisión (VisibilidadConversacion.Puede/Filtrar, ver el grep que lista cada MapGet/Put/
// Post/Delete junto a su llamada). Prueba ese punto de decisión a fondo contra un puñado de
// endpoints representativos (lista, detalle, notas, notificaciones) más las dos acciones que
// tienen lógica propia además de la pertenencia (tomar atómico, asignar/devolver).
[Trait("Categoria", "Integracion")]
public class PertenenciaConversacionTests : IAsyncLifetime
{
    private DatosDePrueba _datos = null!;
    private int IdProfesionalA;   // asociado al paciente en PacienteProfesional
    private int IdProfesionalB;   // no asociado al paciente
    private int IdPaciente;
    private string _nombreDelPaciente = null!;

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _a = null!, _b = null!, _configurador = null!, _sinToken = null!, _paciente = null!;
    private static int _contador;

    public Task InitializeAsync()
    {
        _datos = new DatosDePrueba();
        // Un apellido propio del paciente: la prueba de auditoría comprueba que el nombre NO sale en el detalle.
        _nombreDelPaciente = "Juan Garcíaprueba " + _datos.Marca;
        IdPaciente = _datos.Paciente(_nombreDelPaciente);
        IdProfesionalA = _datos.Profesional();
        IdProfesionalB = _datos.Profesional();
        _datos.PacienteProfesional(IdPaciente, IdProfesionalA);

        // IdUsuario de usuarios reales (LogAuditoria.IdUsuario tiene FK a Usuario - con un id inventado,
        // el INSERT de auditoría fallaría y Auditar/AuditarConIds lo tragarían en silencio, sin avisar).
        var usuarioA = _datos.UsuarioDeProfesional(IdProfesionalA);
        var usuarioB = _datos.UsuarioDeProfesional(IdProfesionalB);
        var usuarioConfigurador = _datos.Usuario();
        var usuarioPaciente = _datos.UsuarioDePaciente(IdPaciente);

        _factory = new WebApplicationFactory<Program>();
        var emisor = _factory.Services.GetRequiredService<JwtEmisorService>();
        _a = ClienteCon(emisor.Emitir(usuarioA, Roles.Profesional, idProfesional: IdProfesionalA));
        _b = ClienteCon(emisor.Emitir(usuarioB, Roles.Profesional, idProfesional: IdProfesionalB));
        _configurador = ClienteCon(emisor.Emitir(usuarioConfigurador, Roles.Configurador));
        _paciente = ClienteCon(emisor.Emitir(usuarioPaciente, Roles.Paciente, idPaciente: IdPaciente));
        _sinToken = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _datos.Dispose();   // las filas propias y, en cascada, las conversaciones y la auditoría que cuelgan de ellas
        return Task.CompletedTask;
    }

    private HttpClient ClienteCon(string token)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    // Crea una conversación de prueba (teléfono único vía bot) y la deja en el estado pedido.
    // idPaciente/idProfesionalAsignado se escriben directo por SQL: son más rápidos y más
    // claros acá que simular todo el flujo del bot/Tomar para llegar a cada estado.
    private async Task<int> CrearConversacion(string estado, int? idPaciente = null, int? idProfesionalAsignado = null)
    {
        var telefono = "1188" + DateTime.Now.Ticks.ToString()[^6..] + Interlocked.Increment(ref _contador);
        var entrante = await _sinToken.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono, texto = "hola, prueba de pertenencia" });
        entrante.EnsureSuccessStatusCode();
        var conv = await _sinToken.GetFromJsonAsync<JsonElement>($"/mensajeria/simulado/conversacion?telefono={telefono}");
        var id = conv.GetProperty("idConversacion").GetInt32();

        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand(
            "UPDATE Conversacion SET Estado = @Estado, IdPaciente = @IdPaciente, IdProfesionalAsignado = @IdProf WHERE IdConversacion = @Id", con);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@IdPaciente", (object?)idPaciente ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdProf", (object?)idProfesionalAsignado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task BorrarConversacion(params int[] ids)
    {
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM LogAuditoria WHERE Entidad = 'Conversacion' AND IdEntidad = @Id",
            "DELETE FROM NotificacionInterna WHERE IdConversacion = @Id",
            "DELETE FROM NotaInternaConversacion WHERE IdConversacion = @Id",
            "DELETE FROM ConversacionEtiqueta WHERE IdConversacion = @Id",
            "DELETE FROM MensajeChat WHERE IdConversacion = @Id",
            "DELETE FROM Conversacion WHERE IdConversacion = @Id"
        })
            foreach (var id in ids)
            {
                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@Id", id);
                await cmd.ExecuteNonQueryAsync();
            }
    }

    [Fact]
    public async Task Conversacion_asignada_a_uno_es_404_para_el_otro_profesional_y_200_para_el_dueno_y_configurador()
    {
        var id = await CrearConversacion(EstadoConversacion.DerivadaHumano, idProfesionalAsignado: IdProfesionalA);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await _a.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _b.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _configurador.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _sinToken.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            // Mensajes, notas y notificaciones de esa conversación tampoco se le revelan a B.
            Assert.Equal(HttpStatusCode.NotFound, (await _b.GetAsync($"/mensajeria/conversaciones/{id}/mensajes")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _b.GetAsync($"/mensajeria/conversaciones/{id}/notas")).StatusCode);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Conversacion_escalada_sin_asignar_es_visible_para_cualquier_profesional()
    {
        var id = await CrearConversacion(EstadoConversacion.Urgente);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await _a.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _b.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _configurador.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Conversacion_bot_only_solo_la_ve_el_equipo_tratante_del_paciente()
    {
        // Activa (nunca escaló), del paciente - cuyo único profesional asociado es A.
        var id = await CrearConversacion(EstadoConversacion.Activa, idPaciente: IdPaciente);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await _a.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _b.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _configurador.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Lista_y_contador_de_cada_profesional_no_incluyen_lo_asignado_al_otro()
    {
        var idDeA = await CrearConversacion(EstadoConversacion.DerivadaHumano, idProfesionalAsignado: IdProfesionalA);
        var idDeB = await CrearConversacion(EstadoConversacion.DerivadaHumano, idProfesionalAsignado: IdProfesionalB);
        try
        {
            var vistaPorA = await _a.GetFromJsonAsync<JsonElement[]>("/mensajeria/conversaciones");
            var idsParaA = vistaPorA!.Select(c => c.GetProperty("idConversacion").GetInt32()).ToHashSet();
            Assert.Contains(idDeA, idsParaA);
            Assert.DoesNotContain(idDeB, idsParaA);

            var vistaPorB = await _b.GetFromJsonAsync<JsonElement[]>("/mensajeria/conversaciones");
            var idsParaB = vistaPorB!.Select(c => c.GetProperty("idConversacion").GetInt32()).ToHashSet();
            Assert.Contains(idDeB, idsParaB);
            Assert.DoesNotContain(idDeA, idsParaB);
        }
        finally { await BorrarConversacion(idDeA, idDeB); }
    }

    [Fact]
    public async Task Tomar_una_conversacion_sin_asignar_la_deja_visible_solo_para_quien_la_tomo()
    {
        var id = await CrearConversacion(EstadoConversacion.DerivadaHumano);
        try
        {
            var tomar = await _a.PutAsync($"/mensajeria/conversaciones/{id}/tomar", null);
            Assert.Equal(HttpStatusCode.NoContent, tomar.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await _a.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _b.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);

            // Volver a tomarla (ya es de A) es idempotente, no un error.
            Assert.Equal(HttpStatusCode.NoContent, (await _a.PutAsync($"/mensajeria/conversaciones/{id}/tomar", null)).StatusCode);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Tomar_al_mismo_tiempo_gana_uno_solo_y_el_otro_recibe_409()
    {
        var id = await CrearConversacion(EstadoConversacion.Urgente);
        try
        {
            var tareaA = _a.PutAsync($"/mensajeria/conversaciones/{id}/tomar", null);
            var tareaB = _b.PutAsync($"/mensajeria/conversaciones/{id}/tomar", null);
            var respuestaA = await tareaA;
            var respuestaB = await tareaB;

            var codigos = new[] { respuestaA.StatusCode, respuestaB.StatusCode };
            Assert.Contains(HttpStatusCode.NoContent, codigos);
            Assert.Contains(HttpStatusCode.Conflict, codigos);

            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var cmd = new SqlCommand("SELECT IdProfesionalAsignado FROM Conversacion WHERE IdConversacion = @Id", con);
            cmd.Parameters.AddWithValue("@Id", id);
            var dueno = (int)(await cmd.ExecuteScalarAsync())!;
            Assert.True(dueno == IdProfesionalA || dueno == IdProfesionalB);
            // El que ganó es, justamente, el que recibió 204.
            var ganadorEsA = respuestaA.StatusCode == HttpStatusCode.NoContent;
            Assert.Equal(ganadorEsA ? IdProfesionalA : IdProfesionalB, dueno);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Devolver_libera_la_conversacion_y_vuelve_a_ser_de_cualquiera()
    {
        var id = await CrearConversacion(EstadoConversacion.DerivadaHumano, idProfesionalAsignado: IdProfesionalA);
        try
        {
            var negadoParaB = await _b.PutAsJsonAsync($"/mensajeria/conversaciones/{id}/devolver", new { motivoCategoria = "Otro" });
            Assert.Equal(HttpStatusCode.NotFound, negadoParaB.StatusCode);   // B ni se entera de que existe

            var devuelta = await _a.PutAsJsonAsync($"/mensajeria/conversaciones/{id}/devolver", new { motivoCategoria = "ProfesionalNoDisponible" });
            Assert.Equal(HttpStatusCode.NoContent, devuelta.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await _a.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _b.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Reasignar_por_configurador_mueve_la_visibilidad_al_nuevo_profesional()
    {
        var id = await CrearConversacion(EstadoConversacion.DerivadaHumano, idProfesionalAsignado: IdProfesionalA);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await _a.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _b.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);

            var reasignar = await _configurador.PutAsJsonAsync($"/mensajeria/conversaciones/{id}/asignar",
                new { idProfesional = IdProfesionalB, motivoCategoria = "CargaEquilibrada" });
            Assert.Equal(HttpStatusCode.NoContent, reasignar.StatusCode);

            // La visibilidad se mueve: A deja de verla, B pasa a verla - sin dueño doble.
            Assert.Equal(HttpStatusCode.NotFound, (await _a.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _b.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Notificaciones_internas_no_revelan_avisos_de_conversaciones_de_otro_profesional()
    {
        var id = await CrearConversacion(EstadoConversacion.DerivadaHumano, idProfesionalAsignado: IdProfesionalA);
        try
        {
            using (var con = Conexion.ObtenerConexion())
            {
                await con.OpenAsync();
                using var cmd = new SqlCommand(@"
                    INSERT INTO NotificacionInterna (Destinatario, IdConversacion, Tipo, Titulo, Mensaje)
                    VALUES ('Administrador', @Id, 'DerivacionHumano', 'Aviso de prueba', 'Mensaje de prueba')", con);
                cmd.Parameters.AddWithValue("@Id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            var vistoPorA = await _a.GetFromJsonAsync<JsonElement[]>("/mensajeria/notificaciones-internas?destinatario=Administrador");
            Assert.Contains(vistoPorA!, n => n.GetProperty("idConversacion").GetInt32() == id);

            var vistoPorB = await _b.GetFromJsonAsync<JsonElement[]>("/mensajeria/notificaciones-internas?destinatario=Administrador");
            Assert.DoesNotContain(vistoPorB!, n => n.TryGetProperty("idConversacion", out var v) && v.ValueKind == JsonValueKind.Number && v.GetInt32() == id);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Apertura_queda_auditada_solo_con_ids_sin_nombre_del_paciente()
    {
        var id = await CrearConversacion(EstadoConversacion.Activa, idPaciente: IdPaciente);
        try
        {
            var abrir = await _a.GetAsync($"/mensajeria/conversaciones/{id}/paciente");
            abrir.EnsureSuccessStatusCode();

            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 Detalle FROM LogAuditoria
                WHERE Entidad = 'Conversacion' AND IdEntidad = @Id AND Accion = 'Apertura de conversación de chat'
                ORDER BY IdLog DESC", con);
            cmd.Parameters.AddWithValue("@Id", id);
            var detalle = (string?)await cmd.ExecuteScalarAsync();
            Assert.NotNull(detalle);
            Assert.Contains("#" + id, detalle);
            Assert.Contains($"paciente #{IdPaciente}", detalle);
            Assert.DoesNotContain("García", detalle);
            Assert.DoesNotContain(_nombreDelPaciente, detalle);
        }
        finally { await BorrarConversacion(id); }
    }

    [Fact]
    public async Task Rol_paciente_no_entra_al_modulo_administrador()
    {
        var id = await CrearConversacion(EstadoConversacion.Urgente);
        try { Assert.Equal(HttpStatusCode.Forbidden, (await _paciente.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode); }
        finally { await BorrarConversacion(id); }
    }
}
