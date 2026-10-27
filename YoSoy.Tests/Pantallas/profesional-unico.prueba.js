// Bloque D (prompt 08): con un solo profesional no se muestra un desplegable de una opción. Corre con node (la lanza PantallasNodeTests.cs), con un DOM falso.
const fs = require("fs");
const path = require("path");
const vm = require("vm");
const raiz = process.argv[2];
let ok = 0, fallas = 0;
const verificar = (c, m) => { if (c) ok++; else { fallas++; console.log("FALLA: " + m); } };

const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
globalThis.YS = { esc };
const pu = require(path.join(raiz, "PANTALLAS", "shared", "profesional-unico.js"));
verificar(YS.profesionalUnico === pu, "se registra en YS.profesionalUnico");

const ana = { idProfesional: 7, nombre: "Ana Pérez", especialidad: "Psicología" };
const beto = { idProfesional: 9, nombre: "Beto <b>Gil</b>", especialidad: "Psiquiatría" };

// ── modo ──
verificar(pu.modo([]) === "ninguno" && pu.modo(null) === "ninguno" && pu.modo(undefined) === "ninguno" && pu.modo("x") === "ninguno", "cero profesionales (o una respuesta rara) → ninguno");
verificar(pu.modo([ana]) === "uno" && pu.modo([ana, beto]) === "varios", "uno / varios");
verificar(pu.modo([{ nombre: "sin id" }, ana]) === "uno", "una fila sin id no cuenta como profesional");

// ── Solicitud de turno ──
let h = pu.campoSolicitud([ana, beto], "solProf", { alCambiar: "cargarHorasSolicitud('2031-03-05')" });
verificar(h.includes('<select id="solProf"') && (h.match(/<option/g) || []).length === 3 && h.includes('onchange="cargarHorasSolicitud(\'2031-03-05\')"'), "con varios: el selector de siempre (placeholder + 2 opciones)");
verificar(h.includes("Beto &lt;b&gt;Gil&lt;/b&gt; — Psiquiatría") && !h.includes("<b>Gil"), "el nombre se escapa");
h = pu.campoSolicitud([ana], "solProf", { alCambiar: "x()" });
verificar(!h.includes("<select") && !h.includes("<option"), "con uno: no hay desplegable");
verificar(h.includes("Ana Pérez") && h.includes("Psicología") && h.includes("prof-elegido"), "con uno: tarjeta con nombre y especialidad");
verificar(h.includes('<input type="hidden" id="solProf" value="7">'), "con uno: el id queda elegido en un campo con el mismo id (el resto del código lee .value)");
h = pu.campoSolicitud([], "solProf");
verificar(!h.includes("<select") && h.includes("Todavía no tenés un profesional asignado") && h.includes("«Sobre mí»") && h.includes("recepción"), "con cero: mensaje claro con qué hacer");

// ── Filtro del calendario ──
h = pu.filtroCalendario([ana, beto], "selProfCalendario", { alCambiar: "cambiarProfCalendario()" });
verificar(h.includes("Ver disponibilidad de:") && h.includes("— Sin filtro —") && h.includes('id="diasAtencion"'), "filtro con varios: selector con «Sin filtro»");
h = pu.filtroCalendario([ana], "selProfCalendario");
verificar(!h.includes("<select") && !h.includes("Sin filtro") && h.includes("Tu profesional:") && h.includes("Ana Pérez") && h.includes('id="selProfCalendario" value="7"') && h.includes('id="diasAtencion"'), "filtro con uno: tarjeta ya elegida (y queda el lugar de los días de atención)");
h = pu.filtroCalendario([], "selProfCalendario");
verificar(!h.includes("<select") && h.includes("Todavía no tenés un profesional asignado"), "filtro con cero: mensaje");

// ── El Inicio de Turnos del paciente lo usa y sigue directo al calendario/horarios ──
const fuente = fs.readFileSync(path.join(raiz, "PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js"), "utf8");
verificar(fuente.includes("YS.profesionalUnico.filtroCalendario(profesionales") && fuente.includes('YS.profesionalUnico.modo(profesionales) === "uno") cambiarProfCalendario()'), "renderAgenda usa el filtro y, con uno, carga su calendario sin tocar nada");
const desde = fuente.indexOf("async function cargarProfesionalesSolicitud"), hasta = fuente.indexOf("function normalizar(s)");
verificar(desde > 0 && hasta > desde, "se encuentra cargarProfesionalesSolicitud");
const trozo = fuente.slice(desde, hasta);

async function solicitud(profs) {
    const botones = [];
    const sel = { outerHTML: "(select de carga)", disabled: false, innerHTML: "" };
    const llamadas = [];
    const ctx = vm.createContext({
        YS: { ...YS, fetch: async () => ({ json: async () => profs }) }, idPaciente: "1", turnosCalendario: [],
        document: {
            getElementById: id => id === "solProf" ? sel : null,
            querySelector: () => ({ setAttribute: (n, v) => botones.push([n, v]) })
        },
        cargarHorasSolicitud: f => llamadas.push(f)
    });
    vm.runInContext(trozo + "\nglobalThis.cargar = cargarProfesionalesSolicitud;", ctx);
    await ctx.cargar("2031-03-05");
    return { html: sel.outerHTML, botones, llamadas };
}
(async () => {
    let r = await solicitud([ana]);
    verificar(r.html.includes('type="hidden" id="solProf" value="7"') && r.llamadas.length === 1 && r.llamadas[0] === "2031-03-05", "solicitud con uno: tarjeta + pide los horarios de ese día sin que la persona toque nada");
    r = await solicitud([ana, beto]);
    verificar(r.html.includes("<select") && r.llamadas.length === 0, "solicitud con varios: selector y espera la elección");
    r = await solicitud([]);
    verificar(r.html.includes("Todavía no tenés un profesional") && r.llamadas.length === 0 && r.botones.some(([n]) => n === "disabled"), "solicitud con cero: mensaje y el botón de confirmar queda deshabilitado");
    r = await solicitud("no es una lista");
    verificar(r.html.includes("Todavía no tenés un profesional") && r.llamadas.length === 0, "una respuesta rara se trata como cero, sin romper");

    const html = fs.readFileSync(path.join(raiz, "PANTALLAS", "Pantalla_Paciente", "dashboard.html"), "utf8");
    verificar(html.includes("../shared/profesional-unico.js"), "dashboard.html carga shared/profesional-unico.js");

    console.log(`OK ${ok}, FALLAS ${fallas}`);
    process.exit(fallas ? 1 : 0);
})().catch(e => { console.log("EXCEPCION: " + (e && e.stack || e)); process.exit(1); });
