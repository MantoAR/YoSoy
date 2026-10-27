// Bloque B (prompt 08): la actualización automática del Inicio del paciente (PANTALLAS/shared/refresco.js) con un DOM falso y un reloj simulado. Corre con node (la lanza PantallasNodeTests.cs).
const fs = require("fs");
const path = require("path");
const raiz = process.argv[2];
let ok = 0, fallas = 0;
const verificar = (c, m) => { if (c) ok++; else { fallas++; console.log("FALLA: " + m); } };

globalThis.YS = {};
const refresco = require(path.join(raiz, "PANTALLAS", "shared", "refresco.js"));
verificar(YS.refresco === refresco, "se registra en YS.refresco");

// ── Entorno falso: reloj simulado con una cola de timers, document con visibilidad y window con foco ──
function entornoFalso() {
    const e = { t: 0, timers: new Map(), siguiente: 1, oyentes: { doc: {}, win: {} }, hidden: false };
    e.setTimeout = (f, ms) => { const id = e.siguiente++; e.timers.set(id, { f, en: e.t + ms }); return id; };
    e.clearTimeout = id => e.timers.delete(id);
    e.ahora = () => e.t;
    const oy = grupo => ({ addEventListener: (n, f) => { (e.oyentes[grupo][n] ||= new Set()).add(f); }, removeEventListener: (n, f) => e.oyentes[grupo][n]?.delete(f) });
    e.document = { get hidden() { return e.hidden; }, ...oy("doc") };
    e.window = oy("win");
    e.emitir = (grupo, n) => [...(e.oyentes[grupo][n] || [])].forEach(f => f());
    // Avanza el reloj y dispara los timers vencidos, dejando que las promesas se resuelvan entre uno y otro.
    e.avanzar = async ms => {
        const fin = e.t + ms;
        for (;;) {
            const proximo = [...e.timers.entries()].filter(([, x]) => x.en <= fin).sort((a, b) => a[1].en - b[1].en)[0];
            if (!proximo) break;
            e.timers.delete(proximo[0]); e.t = Math.max(e.t, proximo[1].en);
            proximo[1].f();
            for (let i = 0; i < 10; i++) await Promise.resolve();
        }
        e.t = fin;
        for (let i = 0; i < 10; i++) await Promise.resolve();
    };
    e.activos = () => e.timers.size;
    return e;
}
const vaciar = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };

(async () => {
    // ── 1. Referencia inicial y cambio detectado ──
    {
        const e = entornoFalso(); let huella = "a", llamadas = 0; const cambios = [];
        const r = refresco.crear({ consultar: async () => { llamadas++; return { huella, noLeidas: 0 }; }, alCambiar: (n, p) => cambios.push([n.huella, p?.huella]), entorno: e });
        r.iniciar(); await vaciar();
        verificar(llamadas === 1 && cambios.length === 0, "la primera consulta solo toma la referencia, no avisa un cambio");
        verificar(e.activos() === 1, "queda un solo timer programado");
        await e.avanzar(14999); verificar(llamadas === 1, "antes de los 15 s no vuelve a consultar");
        await e.avanzar(1); verificar(llamadas === 2 && cambios.length === 0, "a los 15 s consulta; misma huella: no redibuja (sin parpadeos)");
        huella = "b";
        await e.avanzar(15000);
        verificar(cambios.length === 1 && cambios[0][0] === "b" && cambios[0][1] === "a", "cambia la huella: alCambiar(nuevo, previo) exactamente una vez");
        await e.avanzar(15000); verificar(cambios.length === 1, "la huella ya aceptada no vuelve a avisar");
        r.detener(); verificar(e.activos() === 0, "detener limpia el timer");
    }

    // ── 2. Pestaña oculta: pausa; al volver consulta en el acto ──
    {
        const e = entornoFalso(); let huella = "a", llamadas = 0; const cambios = [];
        const r = refresco.crear({ consultar: async () => { llamadas++; return { huella }; }, alCambiar: n => cambios.push(n.huella), entorno: e });
        r.iniciar(); await vaciar();
        e.hidden = true; e.emitir("doc", "visibilitychange");
        verificar(e.activos() === 0, "con la pestaña oculta no queda ningún timer");
        const antes = llamadas;
        await e.avanzar(10 * 60 * 1000);
        verificar(llamadas === antes, "oculta durante 10 minutos: cero consultas");
        huella = "b"; e.hidden = false; e.emitir("doc", "visibilitychange"); await vaciar();
        verificar(llamadas === antes + 1 && cambios.length === 1, "al volver a la pestaña consulta de inmediato y detecta el cambio");
        verificar(e.activos() === 1, "y retoma el ritmo normal");
        // Enfocar la ventana también refresca (si pasaron más de 2 s de la última consulta).
        huella = "c"; await e.avanzar(5000); e.emitir("win", "focus"); await vaciar();
        verificar(cambios.length === 2 && cambios[1] === "c", "al enfocar la ventana refresca");
        const n = llamadas; e.emitir("win", "focus"); await vaciar();
        verificar(llamadas === n, "dos foco seguidos no consultan dos veces");
        r.detener();
    }

    // ── 3. Retroceso ante errores ──
    {
        const e = entornoFalso(); let falla = false, llamadas = 0;
        const r = refresco.crear({ consultar: async () => { llamadas++; if (falla) throw new Error("red"); return { huella: "a" }; }, alCambiar: () => {}, entorno: e });
        r.iniciar(); await vaciar();
        falla = true;
        await e.avanzar(15000); verificar(r.estado().intervalo === 30000, "primer error: el intervalo pasa a 30 s");
        await e.avanzar(30000); verificar(r.estado().intervalo === 60000, "segundo error: 60 s");
        await e.avanzar(60000); verificar(r.estado().intervalo === 120000, "tercero: 120 s");
        await e.avanzar(120000); verificar(r.estado().intervalo === 120000, "tope de 120 s");
        const n = llamadas; await e.avanzar(119000); verificar(llamadas === n, "con el servidor caído no se insiste cada 15 s");
        falla = false; await e.avanzar(1000); await vaciar();
        verificar(r.estado().intervalo === 15000, "al volver a andar, el intervalo vuelve a 15 s");
        r.detener();
    }

    // ── 4. Sin timers duplicados ──
    {
        const e = entornoFalso();
        const mk = () => refresco.unico("paciente", { consultar: async () => ({ huella: "a" }), alCambiar: () => {}, entorno: e });
        const a = mk(); a.iniciar(); await vaciar();
        const b = mk(); b.iniciar(); await vaciar();   // se vuelve a armar la pantalla
        verificar(e.activos() === 1, "volver a armar el refresco no acumula timers (hay " + e.activos() + ")");
        verificar(e.activos() === 1 && a.estado().activo === false && b.estado().activo === true, "el viejo quedó detenido y solo el nuevo está activo");
        b.iniciar(); verificar(e.activos() === 1, "iniciar dos veces el mismo no duplica");
        b.detener();
        verificar(e.oyentes.doc.visibilitychange.size === 0 && e.oyentes.win.focus.size === 0, "detener quita los oyentes de visibilidad y foco");
    }

    // ── 5. Sin consultas solapadas y sincronizar sin avisar ──
    {
        const e = entornoFalso(); let huella = "a", llamadas = 0, soltar; const cambios = [];
        const r = refresco.crear({ consultar: () => { llamadas++; return new Promise(res => { soltar = () => res({ huella }); }); }, alCambiar: n => cambios.push(n.huella), entorno: e });
        r.iniciar(); await vaciar(); soltar(); await vaciar();
        r.forzar(); r.forzar(); await vaciar();
        verificar(llamadas === 2, "dos forzar seguidos con una consulta en curso: una sola (" + llamadas + ")");
        soltar(); await vaciar();
        huella = "b"; const s = r.sincronizar(); await vaciar(); soltar(); await s; await vaciar();
        verificar(cambios.length === 0, "sincronizar (tras una acción propia) toma la referencia sin avisar un cambio");
        r.detener();
    }

    // ── 6. El Inicio del paciente lo usa como pide el prompt ──
    {
        const dash = fs.readFileSync(path.join(raiz, "PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js"), "utf8");
        verificar(dash.includes('YS.refresco.unico("paciente"') && (dash.match(/YS\.refresco\.unico\(/g) || []).length === 1, "un único refresco del paciente");
        verificar(dash.includes("/novedades") && dash.includes("YS.shell.campana.refrescar()"), "consulta las novedades y refresca la campanita");
        verificar(dash.includes("async function renderInicio(c, silencioso = false)") && dash.includes("html !== _htmlInicio"), "el redibujado automático es silencioso y solo si el HTML cambió");
        verificar(dash.includes("!document.getElementById(\"formAdmision\")"), "no le pisa el formulario «Sobre mí» mientras lo escribe");
        verificar(dash.includes("refrescoPaciente?.sincronizar()"), "tras una acción propia vuelve a tomar la referencia");
        const html = fs.readFileSync(path.join(raiz, "PANTALLAS", "Pantalla_Paciente", "dashboard.html"), "utf8");
        verificar(html.includes("../shared/refresco.js"), "dashboard.html carga shared/refresco.js");
    }

    console.log(`OK ${ok}, FALLAS ${fallas}`);
    process.exit(fallas ? 1 : 0);
})().catch(e => { console.log("EXCEPCION: " + (e && e.stack || e)); process.exit(1); });
