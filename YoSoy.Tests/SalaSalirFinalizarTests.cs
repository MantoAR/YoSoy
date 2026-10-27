using System.Text.RegularExpressions;

// «Salir» y «Finalizar» en las dos salas (PANTALLAS/Pantalla_Paciente/sesion_video.html y PANTALLAS/Pantalla_Profesional/sesion_video.html), leídas como texto. Cómo viaja el aviso entre
// los dos lados (señalización real) lo prueban signaling-server/test/servidor.test.js y video-frontend.test.js; acá se vigila lo que no puede cambiar sin que alguien lo note:
// que haya un botón de cada cosa, que Finalizar del paciente NO toque ningún estado y que guardar el cierre saque al profesional de la sala.
public class SalaSalirFinalizarTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(string rol) => File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", rol, "sesion_video.html")).Replace("\r\n", "\n");
    private static string Paciente => Leer("Pantalla_Paciente");
    private static string Profesional => Leer("Pantalla_Profesional");

    private static string Funcion(string html, string firma)
    {
        var i = html.IndexOf(firma, StringComparison.Ordinal);
        Assert.True(i >= 0, $"no encuentro {firma}");
        var fin = html.IndexOf("\n}\n", i, StringComparison.Ordinal);
        return html[i..(fin + 3)];
    }

    [Theory]
    [InlineData("Pantalla_Paciente")]
    [InlineData("Pantalla_Profesional")]
    public void Las_dos_salas_tienen_un_boton_Salir_y_uno_Finalizar_cada_uno_con_su_funcion(string rol)
    {
        var html = Leer(rol);
        Assert.Single(Regex.Matches(html, @"id=""btn-salir"""));
        Assert.Single(Regex.Matches(html, @"id=""btn-finalizar"""));
        Assert.Matches(@"id=""btn-salir""[^>]*onclick=""salirDeLaSala\(\)""", html);
        Assert.Matches(@"id=""btn-finalizar""[^>]*onclick=""finalizarSesion\(\)""", html);
        Assert.DoesNotContain("Finalizar sesión</button>", html);   // el texto viejo y ambiguo ya no está
        Assert.Contains("function salirDeLaSala()", html);
        Assert.Contains("function finalizarSesion()", html);
        // Todo botón con onclick apunta a una función que existe (nada sin función).
        foreach (Match m in Regex.Matches(html, @"<button[^>]*\sonclick=""([A-Za-z_][A-Za-z0-9_]*)\("))
            Assert.True(Regex.IsMatch(html, $@"function\s+{m.Groups[1].Value}\s*\("), $"el botón llama a {m.Groups[1].Value}(), que no existe");
    }

    [Fact]
    public void Salir_del_paciente_no_avisa_que_la_llamada_termino_y_Finalizar_si()
    {
        var salir = Funcion(Paciente, "function salirDeLaSala() {");
        Assert.Contains("cerrarSesion()", salir);
        Assert.DoesNotContain("terminar()", salir);          // salir = cerrar la conexión; el otro lado ve «salió», no «terminó»
        Assert.Contains("podés volver", Paciente);            // el mensaje le dice que puede volver a entrar

        var finalizar = Funcion(Paciente, "function finalizarSesion() {");
        Assert.Contains("terminar()", finalizar);
        Assert.Contains("alLlamadaTerminada", Paciente);      // y si el que finaliza es el otro, la sala se cierra con un mensaje claro
        Assert.Contains("Tu profesional terminó la videollamada", Paciente);
    }

    [Fact]
    public void Finalizar_del_paciente_no_marca_la_sesion_como_realizada_ni_toca_ningun_estado()
    {
        // El paciente corta la LLAMADA y nada más: no hay ninguna llamada a la API en ese camino. Marcar el turno como Realizado lo hace solo el profesional al guardar el cierre.
        var finalizar = Funcion(Paciente, "function finalizarSesion() {");
        var cierre = Funcion(Paciente, "function cerrarPorFinalizacion(mensaje) {");
        foreach (var codigo in new[] { finalizar, cierre })
        {
            Assert.DoesNotContain("YS.api", codigo);
            Assert.DoesNotContain("fetch(", codigo);
            Assert.DoesNotContain("Realizad", codigo);
        }
        Assert.Contains("TODO-VALIDAR", Paciente);   // la decisión queda marcada para validar

        // Y la sala del profesional, cuando el paciente finaliza, NO guarda nada sola: abre el cierre para que él decida.
        var recibida = Funcion(Profesional, "function llamadaTerminadaPorElPaciente() {");
        Assert.DoesNotContain("YS.api", recibida);
        Assert.DoesNotContain("guardarEnHC", recibida);
        Assert.DoesNotContain("confirmaRealizacion", recibida);
        Assert.Contains("finalizarSesion()", recibida);
        Assert.Contains("TODO-VALIDAR", Profesional);
    }

    [Fact]
    public void El_profesional_al_finalizar_avisa_al_paciente_salvo_que_haya_sido_el_paciente_quien_termino()
    {
        var finalizar = Funcion(Profesional, "async function finalizarSesion() {");
        Assert.Contains("terminoElOtro", finalizar);
        Assert.Contains("control?.terminar()", finalizar);
        Assert.Contains("control?.cerrar()", finalizar);
        Assert.Matches(@"alLlamadaTerminada:\s*\(\)\s*=>\s*llamadaTerminadaPorElPaciente\(\)", Profesional);
    }

    [Fact]
    public void El_profesional_puede_salir_y_volver_a_entrar_mientras_la_sesion_siga_abierta()
    {
        var salir = Funcion(Profesional, "function salirDeLaSala() {");
        Assert.Contains("volverAConexion(", salir);          // vuelve a la pantalla de «Entrar a la sala»
        Assert.DoesNotContain("abrirPanelDeCierre", salir);   // salir NO abre el cierre ni da la sesión por terminada
        Assert.DoesNotContain("salaFinalizada = true", salir);
        Assert.Contains("Entrar a la sala", Profesional);
    }

    [Fact]
    public void Al_guardar_el_cierre_con_exito_el_profesional_vuelve_a_su_panel_salvo_que_falte_el_asistente_de_alta_o_derivacion()
    {
        var guardar = Funcion(Profesional, "async function guardarEnHC() {");
        Assert.Matches(@"siguientePaso\.tipo === 'seguimiento'\) programarSalidaAlPanel\(\)", guardar);

        var volver = Funcion(Profesional, "function volverAlPanel() {");
        Assert.Contains("window.parent.location.hash = '#inicio'", volver);   // la sala corre en un iframe del panel
        Assert.Contains("window.close()", volver);                             // o ventana suelta
        Assert.Contains("clearTimeout(salidaAlPanelTimer)", Profesional);     // si toca algo del cierre, se queda
    }
}
