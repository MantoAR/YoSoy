using System.Text.Json;
using System.Text.RegularExpressions;

// Menú del panel de gestión (PANTALLAS/shared/shell.js + PANTALLAS/Pantalla_Configurador/js/dashboard.js): el del Configurador no tiene Inicio, ofrece
// Configuración, Usuarios y Auditoría (y CRM solo si la sesión trae el permiso crm), y deja fuera del menú Grupos, Reglas de derivación, Coberturas,
// Privilegios, Roles y Recuperación de contraseña (su código, sus endpoints y sus datos siguen). El del CRM, el del Profesional y el del Paciente no cambiaron.
// El menú es solo lo que se muestra: cada endpoint vuelve a controlar el permiso en el servidor (lo prueban las suites de acceso). No hay navegador en la suite:
// el menú se arma con el propio shell.js ejecutado con el node de la máquina (si no hay, las pruebas quedan OMITIDAS, no pasan en falso) y la sincronía con
// SECCIONES_POR_MODULO y el aterrizaje se prueba sobre el texto de dashboard.js.
public class ConfiguradorMenuTests
{
    private static readonly string Raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(partes).ToArray()));

    // Carga shell.js en un contexto sin DOM, con la sesión de la prueba, y devuelve el menú de cada módulo (con y sin el grupo de chats).
    private const string Script = """
        const vm = require('vm'), fs = require('fs');
        const sesion = JSON.parse(process.env.SESION);
        const sinDom = () => { throw new Error('esta prueba no debería tocar el DOM'); };
        const YS = {
          sesion: { tienePermiso: p => sesion.permisos.includes(p), tienePrivilegio: p => sesion.privilegios.includes(p), modulos: () => [], modulo: () => '' },
          rutas: {}, esc: s => s, el: sinDom, icono: sinDom
        };
        vm.runInNewContext(fs.readFileSync(process.env.SHELL, 'utf8'), { window: { YS }, console, document: { addEventListener() {} } });
        const salida = {};
        for (const rol of JSON.parse(process.env.ROLES))
          for (const conChats of [false, true]) {
            const m = YS.shell.menuDe(rol, { conChats });
            salida[rol + (conChats ? '+chats' : '')] = m.items.map(i => i.items
              ? { grupo: i.texto, items: i.items.map(s => ({ id: s.id, texto: s.texto })) } : { id: i.id, texto: i.texto });
          }
        process.stdout.write(JSON.stringify(salida));
        """;

    private sealed record Entrada(string? Grupo, string? Id, string? Texto, Entrada[]? Items)
    {
        public IEnumerable<string> Ids => Items is null ? new[] { Id! } : Items.Select(i => i.Id!);
    }

    private static Dictionary<string, Entrada[]> Menus(string[] permisos, string[]? privilegios = null, string[]? roles = null, string? shell = null)
    {
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", Script }, new Dictionary<string, string>
        {
            ["SHELL"] = shell ?? Path.Combine(Raiz, "PANTALLAS", "shared", "shell.js"),
            ["SESION"] = JsonSerializer.Serialize(new { permisos, privilegios = privilegios ?? Array.Empty<string>() }),
            ["ROLES"] = JsonSerializer.Serialize(roles ?? new[] { "configurador", "crm", "profesional", "paciente" })
        });
        Assert.True(codigo == 0, "node falló: " + error);
        var crudo = JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(salida)!;
        return crudo.ToDictionary(kv => kv.Key, kv => kv.Value.Select(e => e.TryGetProperty("grupo", out var g)
            ? new Entrada(g.GetString(), null, null, e.GetProperty("items").EnumerateArray().Select(s => new Entrada(null, s.GetProperty("id").GetString(), s.GetProperty("texto").GetString(), null)).ToArray())
            : new Entrada(null, e.GetProperty("id").GetString(), e.GetProperty("texto").GetString(), null)).ToArray());
    }

    private static string[] Resumen(Entrada[] menu) =>
        menu.Select(e => e.Items is null ? e.Id! : e.Grupo + "[" + string.Join(",", e.Items.Select(i => i.Id)) + "]").ToArray();

    private static readonly string[] Ocultas = { "inicio", "grupos", "reglasderivacion", "coberturas", "privilegios", "roles", "recuperacion" };

    // ───────────── El menú del Configurador ─────────────

    [FactConNodeDelServicioDeWhatsApp]
    public void El_menu_del_Configurador_es_Configuracion_Usuarios_y_Auditoria_sin_Inicio()
    {
        var m = Menus(new[] { "configurador" });

        Assert.Equal(new[] { "Configuración[whatsapp,chatsia,especialidades]", "Usuarios[altausuario,permisos,usuarios,solicitudes]", "auditoria" }, Resumen(m["configurador"]));
        Assert.Equal(new[] { "Configuración", "Usuarios", "Auditoría" }, m["configurador"].Select(e => e.Grupo ?? e.Texto));
        Assert.Equal(new[] { "WhatsApp", "Asistente de chats", "Especialidades" }, m["configurador"][0].Items!.Select(i => i.Texto));
        Assert.Equal(new[] { "Registrar usuario", "Permisos", "Usuarios", "Solicitudes de registro" }, m["configurador"][1].Items!.Select(i => i.Texto));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Las_secciones_ocultas_no_estan_en_el_menu_del_Configurador_con_ninguna_sesion()
    {
        foreach (var permisos in new[] { new[] { "configurador" }, new[] { "configurador", "crm" } })
            foreach (var clave in new[] { "configurador", "configurador+chats" })
            {
                var ids = Menus(permisos)[clave].SelectMany(e => e.Ids).ToArray();
                Assert.All(Ocultas, oculta => Assert.DoesNotContain(oculta, ids));
            }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void El_Configurador_solo_nunca_tiene_chats_ni_grupo_CRM_ni_con_el_permiso_crm_en_la_sesion()
    {
        // Quien tiene Configurador y CRM entra a Administración (otro menú); el menú del Configurador no cambia con lo que traiga la sesión.
        var sinCrm = Menus(new[] { "configurador" }, roles: new[] { "configurador" });
        var conCrm = Menus(new[] { "configurador", "crm" }, roles: new[] { "configurador" });
        foreach (var clave in new[] { "configurador", "configurador+chats" })
        {
            Assert.DoesNotContain("CRM", sinCrm[clave].Select(e => e.Grupo));
            Assert.DoesNotContain("chats", sinCrm[clave].SelectMany(e => e.Ids));
            Assert.Equal(Resumen(sinCrm[clave]), Resumen(conCrm[clave]));
        }
        Assert.Equal(Resumen(sinCrm["configurador"]), Resumen(sinCrm["configurador+chats"]));
    }

    // ───────────── Lo que no cambió ─────────────

    [FactConNodeDelServicioDeWhatsApp]
    public void El_menu_del_CRM_es_el_de_siempre()
    {
        var m = Menus(new[] { "crm" })["crm"];
        Assert.Equal(new[] { "inicio", "Casos[casos,casosderivaciones]" }, Resumen(m));

        var conChats = Menus(new[] { "crm" })["crm+chats"];
        Assert.Equal(new[] { "inicio", "Conversaciones[chats,escalamientos,etiquetas]", "Casos[casos,casosderivaciones]" }, Resumen(conChats));
        Assert.Equal(new[] { "Chats", "Escalamientos", "Etiquetas de chats" }, conChats[1].Items!.Select(i => i.Texto));

        var conAgendas = Menus(new[] { "crm" }, new[] { "gestion_agendas" })["crm+chats"];
        Assert.Equal(new[] { "inicio", "Conversaciones[chats,escalamientos,etiquetas]", "Casos[casos,casosderivaciones]", "Agendas[turnos,agendas]" }, Resumen(conAgendas));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Los_menus_del_Profesional_y_del_Paciente_son_los_de_siempre_y_sin_chats()
    {
        var m = Menus(new[] { "profesional" });

        Assert.Equal(new[] { "inicio", "Agenda[agenda,horarios]", "pacientes", "casos", "sesion", "reportes" }, Resumen(m["profesional"]));
        Assert.Equal(Resumen(m["profesional"]), Resumen(m["profesional+chats"]));   // ni los chats (D2)

        // El paciente, en el orden de la guía de primeros pasos de Inicio.
        var p = Menus(new[] { "paciente" });
        Assert.Equal(new[] { "inicio", "admision", "consentimiento", "Turnos[agenda,profesionales]", "sesion" }, Resumen(p["paciente"]));
        Assert.Equal(Resumen(p["paciente"]), Resumen(p["paciente+chats"]));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void El_menu_del_Profesional_con_Admision_lleva_Admision_despues_de_Inicio()
    {
        var m = Menus(new[] { "profesional", "auditor" }, new[] { "admision" }, new[] { "profesional" })["profesional"];

        Assert.Equal(new[]
        {
            "inicio", "Admisión[admisiones,derivaciones,asignaciones]", "Agenda[agenda,horarios]", "pacientes", "casos", "sesion", "reportes",
            "Auditoría[reportesgenerales,evoluciones,eventoscrisis,interaccionesia,auditoriaclinica]"
        }, Resumen(m));
    }

    // ───────────── Vista única: el menú de Administración (Configurador + CRM) ─────────────

    private static readonly string[] SesionAdministracion = { "configurador", "crm" };

    private static string[] Textos(Entrada e) => e.Items is null ? new[] { e.Texto! } : e.Items.Select(i => i.Texto!).ToArray();

    [FactConNodeDelServicioDeWhatsApp]
    public void El_menu_de_Administracion_es_CRM_Configuracion_Usuarios_y_Auditoria_sin_Inicio()
    {
        var m = Menus(SesionAdministracion, roles: new[] { "administracion" });

        // Sin chats (mensajería no disponible): el grupo CRM solo trae Casos y Derivaciones.
        Assert.Equal(new[] { "CRM[casos,casosderivaciones]", "Configuración[whatsapp,chatsia,especialidades]", "Usuarios[altausuario,permisos,usuarios,solicitudes]", "auditoria" },
            Resumen(m["administracion"]));
        // Con chats: Chats y Leads van PRIMERO dentro de CRM ▾; después Etiquetas, Escalamientos, Casos y Derivaciones.
        var conChats = m["administracion+chats"];
        Assert.Equal(new[] { "CRM[chats,leads,etiquetas,escalamientos,casos,casosderivaciones]", "Configuración[whatsapp,chatsia,especialidades]", "Usuarios[altausuario,permisos,usuarios,solicitudes]", "auditoria" },
            Resumen(conChats));
        Assert.Equal(new[] { "Chats", "Leads", "Etiquetas", "Escalamientos", "Casos", "Derivaciones" }, Textos(conChats[0]));
        Assert.Equal(new[] { "CRM", "Configuración", "Usuarios", "Auditoría" }, conChats.Select(e => e.Grupo ?? e.Texto));
        Assert.Equal(new[] { "WhatsApp", "Asistente de chats", "Especialidades" }, Textos(conChats[1]));
        Assert.Equal(new[] { "Registrar usuario", "Permisos", "Usuarios", "Solicitudes de registro" }, Textos(conChats[2]));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Turnos_y_Agendas_van_dentro_de_CRM_solo_con_el_privilegio_Gestion_de_agendas()
    {
        var con = Menus(SesionAdministracion, new[] { "gestion_agendas" }, new[] { "administracion" })["administracion+chats"];
        Assert.Equal(new[] { "chats", "leads", "etiquetas", "escalamientos", "casos", "casosderivaciones", "turnos", "agendas" }, con[0].Ids);
        Assert.Equal(new[] { "Chats", "Leads", "Etiquetas", "Escalamientos", "Casos", "Derivaciones", "Turnos", "Agendas" }, Textos(con[0]));
        var sin = Menus(SesionAdministracion, roles: new[] { "administracion" })["administracion+chats"];
        Assert.DoesNotContain("turnos", sin.SelectMany(e => e.Ids));
        Assert.DoesNotContain("agendas", sin.SelectMany(e => e.Ids));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Las_secciones_ocultas_tampoco_estan_en_el_menu_de_Administracion()
    {
        foreach (var privilegios in new[] { Array.Empty<string>(), new[] { "gestion_agendas" } })
            foreach (var clave in new[] { "administracion", "administracion+chats" })
            {
                var ids = Menus(SesionAdministracion, privilegios, new[] { "administracion" })[clave].SelectMany(e => e.Ids).ToArray();
                Assert.All(Ocultas, oculta => Assert.DoesNotContain(oculta, ids));
            }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Leads_solo_esta_en_el_menu_de_Administracion_nunca_en_el_del_CRM_puro_ni_en_otro()
    {
        var todos = Menus(SesionAdministracion, new[] { "gestion_agendas" }, new[] { "administracion", "crm", "configurador", "profesional", "paciente" });
        Assert.Contains("leads", todos["administracion+chats"].SelectMany(e => e.Ids));
        foreach (var rol in new[] { "crm", "configurador", "profesional", "paciente" })
            foreach (var clave in new[] { rol, rol + "+chats" })
                Assert.DoesNotContain("leads", todos[clave].SelectMany(e => e.Ids));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void El_menu_del_CRM_puro_es_identico_al_de_antes_de_la_vista_unica_con_todos_sus_textos()
    {
        // La recepcionista (solo CRM): el menú no cambia en nada. Se fija con todos los textos, no solo con los ids.
        var sin = Menus(new[] { "crm" }, roles: new[] { "crm" });
        var conChats = sin["crm+chats"];
        Assert.Equal(new[] { "inicio", "Conversaciones", "Casos" }, conChats.Select(e => e.Id ?? e.Grupo!));
        Assert.Equal(new[] { "Chats", "Escalamientos", "Etiquetas de chats" }, Textos(conChats[1]));
        Assert.Equal(new[] { "Casos", "Derivaciones" }, Textos(conChats[2]));
        Assert.Equal("Inicio", conChats[0].Texto);

        var conAgendas = Menus(new[] { "crm" }, new[] { "gestion_agendas" }, new[] { "crm" })["crm+chats"];
        Assert.Equal(new[] { "inicio", "Conversaciones", "Casos", "Agendas" }, conAgendas.Select(e => e.Id ?? e.Grupo!));
        Assert.Equal(new[] { "Turnos", "Horarios" }, Textos(conAgendas[3]));
        // Con la sesión de un administrador (permisos de CRM + Configurador) el rol "crm" tampoco cambia: lo único que cambia es qué módulo se le da.
        Assert.Equal(Resumen(sin["crm+chats"]), Resumen(Menus(SesionAdministracion, roles: new[] { "crm" })["crm+chats"]));
    }

    // ───────────── Sincronía con dashboard.js ─────────────

    private static string[] SeccionesDe(string modulo, string[] permisos, string[] privilegios)
    {
        // Se evalúa el literal SECCIONES_POR_MODULO de dashboard.js con una sesión simulada.
        var codigoFuente = Leer("PANTALLAS", "Pantalla_Configurador", "js", "dashboard.js");
        var m = Regex.Match(codigoFuente, @"const SECCIONES_POR_MODULO = \{.*?\n\};", RegexOptions.Singleline);
        Assert.True(m.Success, "No encuentro SECCIONES_POR_MODULO en dashboard.js");
        const string script = """
            const YS = { sesion: { tienePermiso: p => JSON.parse(process.env.SESION).permisos.includes(p), tienePrivilegio: p => JSON.parse(process.env.SESION).privilegios.includes(p) } };
            %CODIGO%
            process.stdout.write(JSON.stringify(SECCIONES_POR_MODULO[process.env.MODULO]));
            """;
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", script.Replace("%CODIGO%", m.Value) }, new Dictionary<string, string>
        {
            ["MODULO"] = modulo, ["SESION"] = JsonSerializer.Serialize(new { permisos, privilegios })
        });
        Assert.True(codigo == 0, "node falló: " + error);
        return JsonSerializer.Deserialize<string[]>(salida)!;
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Las_secciones_del_Configurador_coinciden_con_su_menu_y_no_incluyen_las_ocultas()
    {
        foreach (var permisos in new[] { new[] { "configurador" }, new[] { "configurador", "crm" } })
        {
            var menu = Menus(permisos, roles: new[] { "configurador" })["configurador+chats"].SelectMany(e => e.Ids).OrderBy(x => x).ToArray();
            var secciones = SeccionesDe("configurador", permisos, Array.Empty<string>()).OrderBy(x => x).ToArray();
            Assert.Equal(menu, secciones);   // lo que se ve en el menú es exactamente lo que la pantalla acepta
            Assert.All(Ocultas, oculta => Assert.DoesNotContain(oculta, secciones));
        }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Las_secciones_de_Administracion_coinciden_con_su_menu_y_no_incluyen_las_ocultas()
    {
        foreach (var privilegios in new[] { Array.Empty<string>(), new[] { "gestion_agendas" } })
        {
            var menu = Menus(SesionAdministracion, privilegios, new[] { "administracion" })["administracion+chats"].SelectMany(e => e.Ids).OrderBy(x => x).ToArray();
            var secciones = SeccionesDe("administracion", SesionAdministracion, privilegios).OrderBy(x => x).ToArray();
            Assert.Equal(menu, secciones);   // lo que se ve en el menú es exactamente lo que la pantalla acepta
            Assert.All(Ocultas, oculta => Assert.DoesNotContain(oculta, secciones));
            Assert.Contains("leads", secciones);
        }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Las_secciones_del_CRM_coinciden_con_su_menu()
    {
        foreach (var privilegios in new[] { Array.Empty<string>(), new[] { "gestion_agendas" } })
        {
            var menu = Menus(new[] { "crm" }, privilegios, new[] { "crm" })["crm+chats"].SelectMany(e => e.Ids).OrderBy(x => x).ToArray();
            Assert.Equal(menu, SeccionesDe("crm", new[] { "crm" }, privilegios).OrderBy(x => x).ToArray());
        }
    }

    [Fact]
    public void El_aterrizaje_del_Configurador_es_Usuarios_y_un_enlace_a_una_seccion_oculta_cae_ahi()
    {
        var dashboard = Leer("PANTALLAS", "Pantalla_Configurador", "js", "dashboard.js");

        // Al ingresar y ante un #grupos, #recuperacion, #inicio…: el shell lo manda a `inicial` si no está en el menú; mostrarTab, a ATERRIZAJE si no está en SECCIONES.
        Assert.Contains("const ATERRIZAJE = MODULO === \"configurador\" || MODULO === \"administracion\" ? \"usuarios\" : \"inicio\";", dashboard);
        // Administración aterriza en Chats si hay mensajería disponible (lo resuelve el shell) y, si no, en Usuarios.
        Assert.Contains("inicialConChats: MODULO === \"administracion\" ? \"chats\" : undefined", dashboard);
        Assert.Contains("inicial: ATERRIZAJE", dashboard);
        Assert.Contains("if (!SECCIONES.includes(tab)) tab = SECCIONES.includes(ATERRIZAJE) ? ATERRIZAJE", dashboard);
        Assert.Matches(@"usuarios:\s+""Usuarios""", dashboard);   // la pantalla "Cuentas" se llama Usuarios
        Assert.DoesNotMatch(@"usuarios:\s+""Cuentas""", dashboard);

        var shell = Leer("PANTALLAS", "shared", "shell.js");
        Assert.Contains("const destino = buscar(id) && esLocal(buscar(id)) ? id : cfg.inicial;", shell);
        Assert.DoesNotContain(@"texto: ""Cuentas""", shell);
    }

    [Fact]
    public void Sin_Inicio_la_marca_de_la_barra_y_los_grupos_agregados_no_dependen_de_el()
    {
        var shell = Leer("PANTALLAS", "shared", "shell.js");

        // La marca lleva a la primera sección local cuando el menú no tiene Inicio (antes: buscar("inicio") a secas, que daba undefined).
        Assert.Contains("const inicio = buscar(\"inicio\") || buscar(cfg.inicial) || plano().find(i => esLocal(i) && !i.deUsuario);", shell);
        // agregarGrupo ya no inserta en la posición 1 a ciegas.
        Assert.Contains("const posicionInicial = items => items.some(it => it.id === \"inicio\") ? 1 : 0;", shell);
        Assert.DoesNotContain("menu.items.splice(1, 0, grupo)", shell);
        Assert.Contains("menu.items.splice(pos, 0, grupo)", shell);
    }

    [Fact]
    public void Lo_ocultado_sigue_en_el_codigo_y_en_los_endpoints()
    {
        var dashboard = Leer("PANTALLAS", "Pantalla_Configurador", "js", "dashboard.js");

        // No se borró nada: los render* siguen, para volver a mostrarlos con una línea en el menú.
        foreach (var f in new[] { "renderGrupos", "renderReglasDerivacion", "renderPrivilegios", "inicioConfigurador" })
            Assert.Contains("function " + f, dashboard);
        Assert.Contains("\"grupos\"", dashboard);
        Assert.Contains("\"privilegios\"", dashboard);
    }
}
