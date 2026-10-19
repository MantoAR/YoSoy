using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// El cuadro de respuesta de la bandeja de chats (CRM > Chats) decía siempre "Enviar por: Chat simulado", aun con Mensajeria:CanalActivo=Baileys, donde las respuestas salen por
// WhatsApp. Ahora lo informa /capacidades (canalActivo: el Nombre del canal por el que salen las respuestas, "Simulado" o "WhatsApp"), el shell se lo pasa a la bandeja y la
// bandeja lo muestra. La insignia de canal de cada conversación de la lista sale de la conversación (canal), no de un texto fijo.
//
// Las pruebas de /capacidades no abren una conexión ni tocan la base: solo se arma la aplicación y se pide /capacidades. Las de la pantalla LEEN el código (no lo ejecutan): el
// comportamiento real de la interfaz se mira con un navegador contra la API (ver el reporte de la rama).
public class ChatEtiquetaCanalTests
{
    private const string ClaveJwt = "clave-de-prueba-de-la-etiqueta-del-canal-0123456789-abcdef";

    // UseSetting (no ConfigureAppConfiguration): igual que CanalActivoTests y ModoAtencionTests, para que llegue antes de que se arme el contenedor.
    private static WebApplicationFactory<Program> Fabrica(string? canalActivo = null, string? chatsHabilitados = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", ClaveJwt);
            b.UseSetting("Mensajeria:WhatsApp:Secreto", "secreto-de-prueba-de-la-etiqueta-del-canal");
            if (canalActivo is not null) b.UseSetting("Mensajeria:CanalActivo", canalActivo);
            if (chatsHabilitados is not null) b.UseSetting("Mensajeria:ChatSimuladoHabilitado", chatsHabilitados);
        });

    // ───────────── /capacidades ─────────────

    [Fact]
    public async Task Capacidades_dice_que_el_canal_activo_es_Simulado_por_defecto()
    {
        using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.True(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal("Simulado", r.GetProperty("canalActivo").GetString());
    }

    [Fact]
    public async Task Capacidades_dice_que_el_canal_activo_es_WhatsApp_con_Baileys()
    {
        using var fabrica = Fabrica("Baileys");
        using var cliente = fabrica.CreateClient();

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.True(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal("WhatsApp", r.GetProperty("canalActivo").GetString());
    }

    [Theory]
    [InlineData("Simulado")]
    [InlineData("Baileys")]
    public async Task El_canal_activo_es_el_Nombre_del_canal_por_el_que_salen_las_respuestas(string canalActivo)
    {
        // Lo que dice /capacidades tiene que ser el IMessageChannel que usan las respuestas (el que arma AddMensajeria), no un valor deducido de la configuración por otro camino.
        using var fabrica = Fabrica(canalActivo);
        using var cliente = fabrica.CreateClient();

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.Equal(fabrica.Services.GetRequiredService<IMessageChannel>().Nombre, r.GetProperty("canalActivo").GetString());
    }

    [Fact]
    public async Task Con_los_chats_apagados_capacidades_no_informa_el_canal()
    {
        using var fabrica = Fabrica("Baileys", chatsHabilitados: "false");
        using var cliente = fabrica.CreateClient();

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.False(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("canalActivo").ValueKind);
    }

    [Fact]
    public async Task Un_pedido_por_proxy_ve_los_chats_como_remotos_y_no_se_le_informa_el_canal()
    {
        using var fabrica = Fabrica("Baileys");
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.False(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("canalActivo").ValueKind);
    }

    // ───────────── La pantalla ─────────────

    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Bandeja => File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "Pantalla_ChatSimulado", "js", "bandeja.js"));
    private static string Shell => File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "shared", "shell.js"));

    [Fact]
    public void El_selector_Enviar_por_muestra_el_canal_activo_y_no_uno_fijo()
    {
        var bandeja = Bandeja;

        Assert.Contains("<option value=\"${YS.esc(est.canalActivo || \"\")}\">${YS.esc(textoCanal(est.canalActivo) || \"Canal activo\")}</option>", bandeja);
        Assert.DoesNotContain("<option value=\"Simulado\">Chat simulado</option>", bandeja);
    }

    [Fact]
    public void El_canal_activo_llega_a_la_bandeja_desde_capacidades_por_el_shell_y_se_guarda_al_iniciar()
    {
        Assert.Contains("canalActivo: caps?.canalActivo", Shell);
        Assert.Contains("est.canalActivo = typeof opciones.canalActivo === \"string\" ? opciones.canalActivo : null;", Bandeja);
    }

    [Fact]
    public void La_insignia_de_canal_de_cada_conversacion_sale_de_la_conversacion_y_no_dice_siempre_Chat_simulado()
    {
        var bandeja = Bandeja;

        Assert.Contains("class=\"chats-conv-canal\" title=\"${YS.esc(textoCanal(c.canal))}\"", bandeja);
        Assert.DoesNotContain("title=\"Chat simulado\"", bandeja);
    }

    [Fact]
    public void El_texto_Chat_simulado_esta_en_un_solo_lugar_y_es_el_rotulo_del_canal_Simulado()
    {
        // Si alguien lo vuelve a escribir a mano en otro lado, vuelve el rótulo equivocado para los chats que salen por WhatsApp.
        var bandeja = Bandeja;

        Assert.Single(Regex.Matches(bandeja, "\"Chat simulado\""));
        Assert.Contains("const CANAL_TEXTO = { Simulado: \"Chat simulado\" };", bandeja);
        Assert.Contains("const textoCanal = canal => CANAL_TEXTO[canal] || canal || \"\";", bandeja);
    }
}
