using System.Text.RegularExpressions;

// Las pantallas de la propuesta automática de profesional y horario, leídas como texto (cómo se comportan en un navegador queda en el guion manual del reporte): lo que no puede
// cambiar sin que alguien lo note.
public class AsignacionAutomaticaPantallaTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(ruta).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void El_paciente_ve_la_tarjeta_de_la_propuesta_con_sus_dos_botones_y_la_ultima_dice_no_me_sirve()
    {
        var js = Leer("PANTALLAS", "Pantalla_Paciente", "js", "primeros_pasos.js");
        Assert.Contains("Me sirve, confirmar", js);
        Assert.Contains("Prefiero otro horario", js);
        Assert.Contains("Probar con otro profesional", js);
        Assert.Contains("No me sirve", js);
        Assert.Contains("a.tipo === \"responder-propuesta\"", js);
        Assert.Contains("/propuestas-asignacion/${idPropuesta}/aceptar", js);
        Assert.Contains("/propuestas-asignacion/${idPropuesta}/rechazar", js);
        Assert.Contains("/propuestas-asignacion/${idPropuesta}/horarios", js);
        // El botón se deshabilita al primer clic: un doble clic no manda dos pedidos.
        Assert.Matches(@"function confirmarPropuesta[\s\S]*?boton\.disabled = true", js);
    }

    [Fact]
    public void La_recepcion_ve_formularios_por_revisar_y_asignaciones_para_resolver_en_el_inicio_y_en_la_lista()
    {
        var gestion = Leer("PANTALLAS", "Pantalla_Configurador", "js", "dashboard.js");
        Assert.Contains("Formularios por revisar", gestion);
        Assert.Contains("Asignaciones para resolver", gestion);
        Assert.Contains("admisiones?filtro=pendientes", gestion);
        Assert.Contains("admisiones?filtro=resolver", gestion);
        Assert.Contains("{ id: \"resolver\", texto: \"Asignaciones para resolver\", pasa: esPorResolver }", gestion);
        Assert.Contains("/proponer-asignacion", gestion);

        var profesional = Leer("PANTALLAS", "Pantalla_Profesional", "js", "nuevo_dashboard.js");
        Assert.Contains("Formularios por revisar", profesional);
        Assert.Contains("#admisiones?filtro=resolver", profesional);
        Assert.DoesNotContain("Pacientes en entrevista", profesional);
    }

    [Fact]
    public void La_campanita_de_la_recepcion_pide_solo_las_no_leidas()
    {
        var bandeja = Leer("PANTALLAS", "Pantalla_ChatSimulado", "js", "bandeja.js");
        Assert.Contains("notificaciones-internas?destinatario=Administrador&soloNoLeidas=true", bandeja);
    }
}
