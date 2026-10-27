using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

// Bloque E (prompt 08): la provincia es una de las 24 de la lista en los registros NUEVOS (paciente y profesional); lo viejo no se toca.
public class ProvinciasTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(ruta).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void La_lista_del_servidor_y_la_del_front_son_la_misma()
    {
        var js = Leer("PANTALLAS", "shared", "provincias.js");
        var bloque = Regex.Match(js, @"const LISTA = \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        var delFront = Regex.Matches(bloque, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(24, delFront.Count);
        Assert.Equal(Provincias.Lista, delFront);
    }

    [Theory]
    [InlineData("Córdoba", "Córdoba")]
    [InlineData("cordoba", "Córdoba")]
    [InlineData("  CÓRDOBA ", "Córdoba")]
    [InlineData("ciudad autonoma de buenos aires", "Ciudad Autónoma de Buenos Aires")]
    [InlineData("rio negro", "Río Negro")]
    [InlineData("tucuman", "Tucumán")]
    [InlineData("Cor", null)]
    [InlineData("Cba", null)]
    [InlineData("Madrid", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Canonica_devuelve_el_nombre_con_tilde_o_nada(string? texto, string? esperado) => Assert.Equal(esperado, Provincias.Canonica(texto));

    private sealed class Repo : IProfesionalRegistroRepositorio
    {
        public List<Profesional> Guardados { get; } = new();
        public bool ExisteEspecialidad(int idEspecialidad) => true;
        public bool ExisteMatricula(string matricula, int? excluirIdProfesional = null) => false;
        public bool ExisteDni(string dniNormalizado, int? excluirIdProfesional = null) => false;
        public Profesional? ObtenerPorId(int idProfesional) => Guardados.FirstOrDefault(p => p.IdProfesional == idProfesional);
        public int Insertar(Profesional p) { p.IdProfesional = Guardados.Count + 1; Guardados.Add(p); return p.IdProfesional; }
        public void Modificar(Profesional p) { Guardados.RemoveAll(x => x.IdProfesional == p.IdProfesional); Guardados.Add(p); }
    }

    private static Profesional Solicitud(string? provincia) => new() { Nombre = "Ana", Apellido = "Paz", Matricula = "MP-1", Dni = "20111222", IdEspecialidad = 1, Provincia = provincia, Estado = "Pendiente" };

    [Fact]
    public void El_registro_nuevo_de_profesional_rechaza_una_provincia_que_no_es_de_la_lista()
    {
        var repo = new Repo();
        var r = new RegistroProfesionalService(repo).Registrar(Solicitud("Madrid"));
        Assert.False(r.Ok);
        Assert.Equal(400, r.StatusCode);
        Assert.Equal("Elegí una provincia de la lista.", r.Mensaje);
        Assert.Empty(repo.Guardados);
    }

    [Fact]
    public void El_registro_nuevo_guarda_el_nombre_canonico_aunque_se_escriba_sin_tilde()
    {
        var repo = new Repo();
        Assert.True(new RegistroProfesionalService(repo).Registrar(Solicitud("cordoba")).Ok);
        Assert.Equal("Córdoba", Assert.Single(repo.Guardados).Provincia);
    }

    [Fact]
    public void Sin_provincia_el_registro_sigue_andando_y_la_edicion_no_toca_datos_viejos()
    {
        var repo = new Repo();
        var servicio = new RegistroProfesionalService(repo);
        Assert.True(servicio.Registrar(Solicitud(null)).Ok);
        Assert.True(servicio.Registrar(Solicitud("  ")).Ok);   // vacío no es un valor: no se valida (los obligatorios los pide la pantalla)

        // Un profesional con una provincia vieja de texto libre puede seguir guardando su perfil: la edición no valida la provincia.
        var viejo = new Profesional { IdProfesional = 50, Nombre = "Vieja", Apellido = "Ficha", Matricula = "MP-50", Dni = "20999888", IdEspecialidad = 1, Provincia = "Cba", Estado = "Activo" };
        repo.Guardados.Add(viejo);
        var r = servicio.Modificar(50, new Profesional { IdProfesional = 50, Nombre = "Vieja", Apellido = "Ficha", Matricula = "MP-50", Dni = "20999888", IdEspecialidad = 1, Provincia = "Cba", Estado = "Activo" });
        Assert.True(r.Ok, r.Mensaje);
        Assert.Equal("Cba", repo.Guardados.Single(p => p.IdProfesional == 50).Provincia);   // no se migra ni se corrige nada
    }

    [Fact]
    public async Task El_registro_nuevo_de_paciente_rechaza_una_provincia_que_no_es_de_la_lista()
    {
        using var fabrica = new WebApplicationFactory<Program>();
        var r = await fabrica.CreateClient().PostAsJsonAsync("/pacientes", new { nombre = "Ana", dni = "30111222", provincia = "Madrid", usuario = "ana.prueba", contrasena = "ClaveDePrueba123", mail = "ana@example.test" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Elegí una provincia de la lista.", await r.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Pantalla_Usuario/Usuario/registro.html", "../../shared/provincias.js")]
    [InlineData("Pantalla_Usuario/Usuario/registro_profesional.html", "../../shared/provincias.js")]
    [InlineData("Pantalla_Profesional/nuevo_dashboard.html", "../shared/provincias.js")]
    public void Las_pantallas_que_cargan_la_provincia_usan_el_campo_con_sugerencias(string pagina, string script)
    {
        Assert.Contains(script, Leer(("PANTALLAS/" + pagina).Split('/')));
    }

    [Fact]
    public void Los_tres_formularios_arman_el_combobox_y_validan_con_la_misma_regla()
    {
        var app = Leer("PANTALLAS", "Pantalla_Usuario", "Usuario", "js", "app.js");
        Assert.Contains("YS.provincias.combo(campo)", app);
        Assert.Contains("YS.provincias.error(v)", app);
        Assert.Contains("YS.provincias.canonica(valor(\"provincia\"))", app);
        var prof = Leer("PANTALLAS", "Pantalla_Usuario", "Usuario", "registro_profesional.html");
        Assert.Contains("YS.provincias.combo(document.getElementById(\"provincia\"))", prof);
        Assert.Contains("YS.provincias.error(v)", prof);
        var perfil = Leer("PANTALLAS", "Pantalla_Profesional", "js", "nuevo_dashboard.js");
        Assert.Contains("YS.provincias.combo(document.getElementById('pf-provincia'))", perfil);
        Assert.Contains("YS.provincias.error(v, (window._profActual || {}).provincia)", perfil);   // el valor viejo ya guardado se acepta
        // Ningún formulario deja la provincia como texto libre sin sugerencias.
        Assert.DoesNotContain("<select id=\"provincia\"", Leer("PANTALLAS", "Pantalla_Usuario", "Usuario", "registro.html"));
    }
}
