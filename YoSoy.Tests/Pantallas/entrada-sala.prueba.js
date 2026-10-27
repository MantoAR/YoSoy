// Prueba de la lógica del cliente de la entrada a la sala (PANTALLAS/shared/entrada-sala.js y las funciones de Pantalla_Paciente/js/dashboard.js que la usan), con un DOM falso.
// Corre con node (la lanza YoSoy.Tests/EntradaSalaTests.cs). Imprime una línea por verificación y sale con código 1 si alguna falla.
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const raiz = process.argv[2];
let fallas = 0, ok = 0;
const verificar = (cond, msg) => { if (cond) ok++; else { fallas++; console.log("FALLA: " + msg); } };

// ── YS falso ──
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
globalThis.YS = { esc };
let respuestaApi;   // lo que contesta /salas/entrable: un objeto, o una función que lanza
YS.api = async ruta => { verificar(ruta === "/salas/entrable", "pide /salas/entrable (pidió " + ruta + ")"); return typeof respuestaApi === "function" ? respuestaApi() : respuestaApi; };
const sala = require(path.join(raiz, "PANTALLAS", "shared", "entrada-sala.js"));
verificar(YS.entradaSala === sala, "se registra en YS.entradaSala");

const turno = (extra = {}) => ({ idTurno: 77, idPaciente: 5, inicio: "2031-03-04T10:00:00", hora: "10:00", abre: "2031-03-04T09:50:00", horaAbre: "09:50", cierra: "2031-03-04T10:50:00", esHoy: true, contraparte: "Dra. Pérez", ...extra });

// ── estadoDe ──
verificar(sala.estadoDe({ entrable: turno(), proximo: null }).tipo === "entrable", "con turno entrable → entrable");
verificar(sala.estadoDe({ entrable: turno(), proximo: turno({ idTurno: 78 }) }).turno.idTurno === 77, "si hay entrable y próximo gana el entrable");
verificar(sala.estadoDe({ entrable: null, proximo: turno({ esHoy: true }) }).tipo === "hoy", "próximo de hoy → hoy");
verificar(sala.estadoDe({ entrable: null, proximo: turno({ esHoy: false }) }).tipo === "proximo", "próximo de otro día → proximo");
verificar(sala.estadoDe({ entrable: null, proximo: null }).tipo === "ninguno", "sin turnos → ninguno");
for (const rara of [undefined, null, {}, [], "x", 5, { entrable: 3 }, { entrable: { idTurno: "a" } }])
    verificar(sala.estadoDe(rara).tipo === (rara && typeof rara === "object" && "entrable" in rara ? "ninguno" : "error"), "respuesta rara " + JSON.stringify(rara) + " no se toma como turno");

// ── consultar: la red que falla no tira ──
(async () => {
    respuestaApi = { entrable: turno(), proximo: null };
    verificar((await sala.consultar()).tipo === "entrable", "consultar con turno entrable");
    respuestaApi = () => { throw new Error("red"); };
    verificar((await sala.consultar()).tipo === "error", "consultar con error de red → error (no lanza)");
    respuestaApi = undefined;
    verificar((await sala.consultar()).tipo === "error", "consultar con respuesta vacía → error");

    // ── Botón y tarjeta del Inicio ──
    const entrable = sala.estadoDe({ entrable: turno(), proximo: null });
    const boton = sala.htmlBoton(entrable);
    verificar(/onclick="entrarASalaDelTurno\(77\)"/.test(boton) && !/disabled/.test(boton) && boton.includes("Entrar a la sesión") && boton.includes("ui-btn--primary"), "botón habilitado con el id correcto: " + boton);
    const tarjeta = sala.htmlTarjetaInicio(entrable, "<a>enlaces</a>");
    verificar(tarjeta.includes("Tu turno es a las 10:00 con Dra. Pérez."), "texto con hora y profesional");
    verificar(tarjeta.includes("<a>enlaces</a>") && tarjeta.includes("Tu sesión por video"), "conserva los enlaces y el título");

    const hoy = sala.estadoDe({ entrable: null, proximo: turno({ hora: "10:20", horaAbre: "10:10", inicio: "2031-03-04T10:20:00" }) });
    const botonHoy = sala.htmlBoton(hoy);
    verificar(/disabled/.test(botonHoy) && !/onclick/.test(botonHoy), "antes de los 10 min el botón está deshabilitado y sin acción: " + botonHoy);
    verificar(sala.htmlTarjetaInicio(hoy).includes("La sala abre a las 10:10"), "antes de los 10 min dice a qué hora abre");

    for (const [nombre, st] of [["sin turnos", sala.estadoDe({ entrable: null, proximo: null })], ["otro día", sala.estadoDe({ entrable: null, proximo: turno({ esHoy: false }) })], ["error", sala.estadoDe(undefined)]]) {
        verificar(sala.htmlBoton(st) === "", "sin turno entrable (" + nombre + ") no hay botón");
        verificar(sala.htmlTarjetaInicio(st).includes("La sala de video abre 10 minutos antes de cada turno. Entrá desde la Sala de video con tu turno confirmado."), "(" + nombre + ") queda el texto de siempre");
    }
    verificar(!sala.htmlTarjetaInicio(sala.estadoDe({ entrable: turno({ contraparte: "<img onerror=x>" }), proximo: null })).includes("<img"), "el nombre se escapa");

    // ── Menú «Sala de video» del paciente: renderSala de Pantalla_Paciente/js/dashboard.js, cada estado ──
    const fuente = fs.readFileSync(path.join(raiz, "PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js"), "utf8");
    const desde = fuente.indexOf("async function renderSala"), hasta = fuente.indexOf("function entrarSesion()");
    verificar(desde > 0 && hasta > desde, "se encuentra renderSala en dashboard.js");
    const trozo = fuente.slice(desde, hasta);

    async function menu(resp, { seccion = "sesion" } = {}) {
        respuestaApi = resp;
        const reemplazos = [], c = { innerHTML: "(sin tocar)" };
        const ctx = vm.createContext({ YS: { ...YS, shell: { actual: seccion } }, idPaciente: "9", location: { replace: u => reemplazos.push(u), set href(u) { reemplazos.push(u); } } });
        vm.runInContext(trozo + "\nglobalThis.renderSala = renderSala; globalThis.entrarASalaDelTurno = entrarASalaDelTurno;", ctx);
        await ctx.renderSala(c);
        return { html: c.innerHTML, destino: reemplazos, ctx };
    }

    let r = await menu({ entrable: turno(), proximo: null });
    verificar(r.destino.length === 1 && r.destino[0] === "sesion_video.html?turno=77&idPaciente=9", "con turno entrable el menú entra directo a la sala del turno: " + r.destino);
    verificar(!r.html.includes("Código de sala") && r.html.trim() !== "", "con turno entrable no pide código (y no queda en blanco)");

    r = await menu({ entrable: null, proximo: null });
    verificar(r.destino.length === 0 && r.html.includes("No tenés turnos próximos.") && r.html.includes('href="#agenda"'), "sin turnos: mensaje y link a Turnos");
    verificar(r.html.includes("<details") && r.html.includes("Tengo un código de sala") && !/<details[^>]*open/.test(r.html), "el código manual queda colapsado como opción secundaria");
    verificar(r.html.includes('id="codigoSala"') && r.html.includes("entrarSesion()"), "el formulario del código sigue disponible");

    r = await menu({ entrable: null, proximo: turno({ hora: "10:20", horaAbre: "10:10" }) });
    verificar(r.html.includes("No tenés una sesión en este momento. Tu próximo turno es hoy a las 10:20 con Dra. Pérez: la sala abre a las 10:10."), "próximo turno de hoy: " + r.html.slice(0, 400));

    r = await menu({ entrable: null, proximo: turno({ esHoy: false, inicio: "2031-03-06T15:30:00", hora: "15:30" }) });
    verificar(r.html.includes("Tu próximo turno es el 06/03 a las 15:30 con Dra. Pérez."), "próximo turno de otro día");

    for (const [nombre, resp] of [["error de red", () => { throw new Error("net"); }], ["respuesta vacía", undefined], ["respuesta nula", null], ["objeto vacío", {}], ["texto", "<html>"]]) {
        r = await menu(resp);
        verificar(r.destino.length === 0 && r.html.includes("No pudimos consultar tus turnos") && r.html.includes("Reintentar") && r.html.includes("Tengo un código de sala"), "(" + nombre + ") se dice que falló, se puede reintentar y no queda en blanco");
    }

    r = await menu({ entrable: turno(), proximo: null }, { seccion: "inicio" });
    verificar(r.destino.length === 0 && r.html === "(sin tocar)", "si el usuario ya cambió de sección no se redirige ni se pisa la pantalla");

    // ── El Inicio y el paso 7 llaman a la misma función ──
    const pp = fs.readFileSync(path.join(raiz, "PANTALLAS", "Pantalla_Paciente", "js", "primeros_pasos.js"), "utf8");
    verificar(pp.includes("entrarASalaDelTurno(s.idTurno)"), "el paso 7 entra con entrarASalaDelTurno");
    verificar(fuente.includes("YS.entradaSala.htmlTarjetaInicio(sala,") && fuente.includes("function entrarASalaDelTurno(idTurno)"), "el Inicio usa la tarjeta compartida y entrarASalaDelTurno");
    r = await menu({ entrable: turno(), proximo: null });
    r.ctx.entrarASalaDelTurno(5);
    verificar(r.destino.includes("sesion_video.html?turno=5&idPaciente=9"), "entrarASalaDelTurno lleva a la sala por el turno");

    // ── La ruta de retorno del login acepta la sala del recordatorio y nada más ──
    const ui = fs.readFileSync(path.join(raiz, "PANTALLAS", "shared", "ui.js"), "utf8");
    const linea = ui.slice(ui.indexOf("YS.rutaRetornoValida"), ui.indexOf("// ── DOM"));
    const ctx2 = vm.createContext({ YS: {} });
    vm.runInContext("var YS = {};" + linea, ctx2);
    const valida = ctx2.YS.rutaRetornoValida;
    verificar(valida("/pantallas/Pantalla_Paciente/sesion_video.html?turno=123") === "/pantallas/Pantalla_Paciente/sesion_video.html?turno=123", "return a la sala del turno es válido");
    verificar(valida("/reservar/abc-1") === "/reservar/abc-1", "return a la reserva sigue válido");
    for (const mala of ["//evil.com", "https://evil.com", "/pantallas/Pantalla_Paciente/sesion_video.html?turno=1&x=//evil.com", "/pantallas/Pantalla_Paciente/sesion_video.html?turno=a", "/pantallas/Pantalla_Paciente/dashboard.html", null])
        verificar(valida(mala) === null, "return inválido descartado: " + mala);

    console.log(`OK ${ok}, FALLAS ${fallas}`);
    process.exit(fallas ? 1 : 0);
})().catch(e => { console.log("EXCEPCION: " + (e && e.stack || e)); process.exit(1); });
