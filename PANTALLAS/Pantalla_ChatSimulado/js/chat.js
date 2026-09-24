// YoSoy · Mensajería: vista interna para seguir las conversaciones del bot con
// los pacientes (modo administrador) y chat de prueba como paciente.
// Actualización por polling; todo el texto se inserta con textContent.
const API = location.protocol.startsWith("http") ? location.origin : "http://localhost:5000";
const POLL_MS = 2500;
const INFO_CADA_MS = 10000;

const ESTADOS = { Activa: "Activa", Urgente: "Urgente", DerivadaHumano: "Derivada", Cerrada: "Cerrada" };
const ICONOS = {
    Bot:     '<path fill="currentColor" d="M12 2a2 2 0 0 1 1 3.7V7h4a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3v-8a3 3 0 0 1 3-3h4V5.7A2 2 0 0 1 12 2zM9 12a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3zm6 0a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z"/>',
    Humano:  '<path fill="currentColor" d="M12 12a5 5 0 1 0 0-10 5 5 0 0 0 0 10zm0 2c-4 0-9 2-9 6v2h18v-2c0-4-5-6-9-6z"/>',
    Sistema: '<path fill="currentColor" d="M12 22a2 2 0 0 0 2-2h-4a2 2 0 0 0 2 2zm6-6V11a6 6 0 0 0-5-5.9V4a1 1 0 0 0-2 0v1.1A6 6 0 0 0 6 11v5l-2 2v1h16v-1l-2-2z"/>'
};
const AUTORES = { Bot: "Bot", Humano: "Equipo YoSoy", Sistema: "Aviso automático", Paciente: "Paciente" };

const $ = id => document.getElementById(id);

let modo            = "admin";
let telefonoActual  = null;     // modo paciente
let idConversacion  = null;
let estadoActual    = null;
let ultimoIdMensaje = 0;
let ultimoDia       = null;
let generacion      = 0;        // cambia al cambiar de conversación: descarta respuestas viejas
let cargandoMsjs    = false;
let conversaciones  = [];
let firmaLista      = "";
let infoCargadaEn   = 0;

// ── Utilidades ──────────────────────────────────────────────
async function api(ruta, opciones = {}) {
    const res = await fetch(API + ruta, { headers: { "Content-Type": "application/json" }, ...opciones });
    if (res.status === 404 && opciones.permitir404) return null;
    if (!res.ok) {
        let mensaje = `Error ${res.status}`;
        try { mensaje = (await res.json()).mensaje || mensaje; } catch { }
        throw new Error(mensaje);
    }
    return res.status === 204 ? null : res.json();
}

function toast(texto, tipo = "ok") {
    const t = $("toast");
    t.textContent = texto;
    t.className = "toast visible" + (tipo === "error" ? " error" : "");
    clearTimeout(toast._timer);
    toast._timer = setTimeout(() => t.className = "toast", 3200);
}

function el(tag, clase, texto) {
    const e = document.createElement(tag);
    if (clase) e.className = clase;
    if (texto != null) e.textContent = texto;
    return e;
}

function icono(origen) {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("aria-hidden", "true");
    svg.innerHTML = ICONOS[origen] || "";   // constante propia, no dato de usuario
    return svg;
}

const mismoDia = (a, b) => a.toDateString() === b.toDateString();

function hora(fecha) {
    return new Date(fecha).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });
}

// "HOY", "AYER" o "23/9"
function etiquetaDia(fecha) {
    const d = new Date(fecha), hoy = new Date(), ayer = new Date();
    ayer.setDate(hoy.getDate() - 1);
    if (mismoDia(d, hoy))  return "HOY";
    if (mismoDia(d, ayer)) return "AYER";
    return `${d.getDate()}/${d.getMonth() + 1}`;
}

// En la lista: hora si es de hoy, "Ayer" o la fecha corta.
function horaLista(fecha) {
    if (!fecha) return "";
    const et = etiquetaDia(fecha);
    return et === "HOY" ? hora(fecha) : et === "AYER" ? "Ayer" : et;
}

function iniciales(nombre) {
    if (!nombre) return "#";
    const partes = nombre.trim().split(/\s+/);
    return ((partes[0]?.[0] || "") + (partes.length > 1 ? partes[partes.length - 1][0] : "")).toUpperCase();
}

function avatar(nombre, id, clase = "avatar") {
    const a = el("div", `${clase} ${nombre ? "c" + ((id % 4) + 1) : "anonimo"}`, iniciales(nombre));
    a.setAttribute("aria-hidden", "true");
    return a;
}

function sinTildes(s) {
    return (s || "").toLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "");
}

function badge(estado) {
    return el("span", "badge badge-" + estado, ESTADOS[estado] || estado);
}

// Convierte las URLs http(s) en links sin usar innerHTML.
function agregarTextoConLinks(contenedor, texto) {
    texto.split(/(https?:\/\/[^\s]+)/g).forEach(parte => {
        if (/^https?:\/\//.test(parte)) {
            const a = el("a", null, parte);
            a.href = parte; a.target = "_blank"; a.rel = "noopener noreferrer";
            contenedor.appendChild(a);
        } else if (parte) {
            contenedor.appendChild(document.createTextNode(parte));
        }
    });
}

// ── Modos ───────────────────────────────────────────────────
function cambiarModo(nuevo) {
    modo = nuevo;
    telefonoActual = null;
    document.body.className = "modo-" + modo;
    $("btnModoAdmin").classList.toggle("activo", modo === "admin");
    $("btnModoPaciente").classList.toggle("activo", modo === "paciente");
    $("btnModoAdmin").setAttribute("aria-selected", modo === "admin");
    $("btnModoPaciente").setAttribute("aria-selected", modo === "paciente");
    $("inputMensaje").placeholder = modo === "admin" ? "Responder como equipo de YoSoy" : "Escribí un mensaje";
    reiniciarChat();
    if (modo === "admin") { firmaLista = ""; cargarConversaciones(); }
}

function reiniciarChat() {
    generacion++;
    idConversacion  = null;
    estadoActual    = null;
    ultimoIdMensaje = 0;
    ultimoDia       = null;
    $("app").classList.remove("con-chat");
    $("mensajes").replaceChildren(el("div", "vacio", modo === "admin"
        ? "Elegí una conversación de la lista para ver el hilo."
        : "Ingresá un teléfono de prueba para empezar."));
    $("chatTitulo").textContent    = "Sin conversación";
    $("chatSubtitulo").textContent = modo === "admin" ? "Elegí una conversación de la lista" : "Modo paciente de prueba";
    $("chatAvatar").replaceWith(Object.assign(avatar(null, 0), { id: "chatAvatar" }));
    $("infoContenido").replaceChildren(el("div", "info-vacio", "Elegí una conversación para ver los datos del paciente."));
    $("infoContenido").className = "";
    mostrarEstado(null);
    habilitarEnvio(false);
}

function habilitarEnvio(si) {
    $("inputMensaje").disabled = !si;
    $("btnEnviar").disabled    = !si;
}

// El administrador no puede escribir en una conversación cerrada.
function actualizarEnvio() {
    if (modo === "paciente") { habilitarEnvio(!!telefonoActual); return; }
    const cerrada = estadoActual === "Cerrada";
    habilitarEnvio(!!idConversacion && !cerrada);
    $("inputMensaje").placeholder = cerrada
        ? "La conversación está cerrada"
        : "Responder como equipo de YoSoy";
}

function mostrarEstado(estado) {
    estadoActual = estado;
    const b = $("chatEstado");
    b.classList.toggle("oculto", !estado || modo === "paciente");
    if (estado) { b.textContent = ESTADOS[estado] || estado; b.className = "badge badge-" + estado; }
    $("bannerUrgente").classList.toggle("oculto", !(modo === "admin" && estado === "Urgente"));
    if (modo === "paciente" && estado === "DerivadaHumano")
        $("chatSubtitulo").textContent = "Te está atendiendo una persona del equipo";
    actualizarEnvio();
}

// ── Hilo de mensajes ────────────────────────────────────────
function renderMensaje(m) {
    const cont = $("mensajes");
    cont.querySelector(".vacio")?.remove();

    const dia = etiquetaDia(m.fechaHora);
    if (dia !== ultimoDia) {
        cont.appendChild(el("div", "separador-dia", dia));
        ultimoDia = dia;
    }

    // Administrador: el paciente a la izquierda; bot, equipo y avisos a la derecha.
    // Paciente de prueba: al revés, como lo vería en su teléfono.
    const esDelPaciente = m.direccion === "Entrante";
    const derecha = modo === "admin" ? !esDelPaciente : esDelPaciente;

    const b = el("div", `burbuja ${derecha ? "der" : "izq"} origen-${m.origen}`);
    if (!esDelPaciente) {
        const autor = el("div", "autor");
        autor.append(icono(m.origen), document.createTextNode(AUTORES[m.origen] || m.origen));
        b.appendChild(autor);
    }
    const cuerpo = el("div");
    agregarTextoConLinks(cuerpo, m.texto);
    b.appendChild(cuerpo);
    b.appendChild(el("div", "meta" + (m.estadoEnvio === "Error" ? " error" : ""),
        hora(m.fechaHora) + (m.estadoEnvio === "Error" ? " · no entregado" : "")));
    cont.appendChild(b);
}

async function cargarMensajes() {
    if (!idConversacion || cargandoMsjs) return;
    cargandoMsjs = true;
    const gen = generacion;
    try {
        const [nuevos, conv] = await Promise.all([
            api(`/mensajeria/conversaciones/${idConversacion}/mensajes?desde=${ultimoIdMensaje}`),
            api(`/mensajeria/conversaciones/${idConversacion}`)
        ]);
        if (gen !== generacion) return;
        const cont = $("mensajes");
        const primeraCarga = ultimoIdMensaje === 0;
        const abajo = cont.scrollHeight - cont.scrollTop - cont.clientHeight < 80;
        nuevos.forEach(m => { renderMensaje(m); ultimoIdMensaje = Math.max(ultimoIdMensaje, m.idMensaje); });
        if (nuevos.length && (primeraCarga || abajo)) cont.scrollTop = cont.scrollHeight;

        const cambioEstado = conv.estado !== estadoActual;
        mostrarEstado(conv.estado);
        if (modo === "admin" && (cambioEstado || Date.now() - infoCargadaEn > INFO_CADA_MS)) cargarInfo();
    } catch (e) {
        console.warn("No se pudieron cargar los mensajes:", e.message);
    } finally {
        cargandoMsjs = false;
    }
}

// ── Columna izquierda (administrador) ───────────────────────
async function cargarConversaciones() {
    if (modo !== "admin") return;
    try {
        conversaciones = await api("/mensajeria/conversaciones");
        const urgentes  = conversaciones.filter(c => c.estado === "Urgente").length;
        const derivadas = conversaciones.filter(c => c.estado === "DerivadaHumano").length;
        $("pillUrgentes").textContent  = `${urgentes} urgente${urgentes === 1 ? "" : "s"}`;
        $("pillDerivadas").textContent = `${derivadas} derivada${derivadas === 1 ? "" : "s"}`;
        $("pillUrgentes").classList.toggle("oculto", urgentes === 0);
        $("pillDerivadas").classList.toggle("oculto", derivadas === 0);
        renderLista();
    } catch (e) {
        console.warn("No se pudo cargar la lista:", e.message);
    }
}

function coincideBusqueda(c, q) {
    if (!q) return true;
    const digitos = q.replace(/\D/g, "").slice(-10);   // mismo criterio que el backend
    return sinTildes(c.nombrePaciente).includes(sinTildes(q))
        || (digitos.length >= 3 && c.telefono.includes(digitos));
}

function renderLista() {
    const q = $("inputBuscar").value.trim();
    const visibles = conversaciones.filter(c => coincideBusqueda(c, q));
    // No redibujar si nada cambió: evita parpadeos y perder el foco con el polling.
    const firma = JSON.stringify([q, idConversacion, visibles]);
    if (firma === firmaLista) return;
    firmaLista = firma;

    const ul = $("listaConversaciones");
    ul.replaceChildren();
    if (!visibles.length) {
        ul.appendChild(el("li", "lista-vacia", q ? "Ninguna conversación coincide con la búsqueda." : "Todavía no hay conversaciones."));
        return;
    }
    visibles.forEach(c => {
        const btn = el("button", "conversacion");
        if (c.idConversacion === idConversacion) btn.classList.add("seleccionada");
        if (c.estado === "Urgente") btn.classList.add("urgente");
        btn.setAttribute("aria-label", `${c.nombrePaciente || c.telefono}, ${ESTADOS[c.estado] || c.estado}`);

        const fila1 = el("div", "conv-fila");
        fila1.append(el("span", "conv-nombre", c.nombrePaciente || c.telefono),
                     el("span", "conv-hora", horaLista(c.fechaUltimoMensaje || c.fechaUltimaActividad)));
        const fila2 = el("div", "conv-fila");
        fila2.append(el("span", "conv-ultimo", c.ultimoMensaje || ""), badge(c.estado));
        const cuerpo = el("div", "conv-cuerpo");
        cuerpo.append(fila1, fila2);

        btn.append(avatar(c.nombrePaciente, c.idConversacion), cuerpo);
        btn.addEventListener("click", () => seleccionarConversacion(c));
        const li = el("li");
        li.appendChild(btn);
        ul.appendChild(li);
    });
}

async function seleccionarConversacion(c) {
    reiniciarChat();
    idConversacion = c.idConversacion;
    $("app").classList.add("con-chat");
    $("chatTitulo").textContent    = c.nombrePaciente || "Paciente sin identificar";
    $("chatSubtitulo").textContent = "Tel. " + c.telefono;
    $("chatAvatar").replaceWith(Object.assign(avatar(c.nombrePaciente, c.idConversacion), { id: "chatAvatar" }));
    firmaLista = "";
    renderLista();
    await cargarMensajes();
    cargarInfo();
}

// ── Columna derecha: datos del paciente ─────────────────────
function dato(etiqueta, valor) {
    const d = el("div", "dato");
    d.append(el("span", null, etiqueta), el("span", valor ? null : "sin-dato", valor || "Sin cargar"));
    return d;
}

function seccion(titulo, ...hijos) {
    const s = el("section", "info-seccion");
    s.append(el("h3", null, titulo), ...hijos);
    return s;
}

async function cargarInfo() {
    if (modo !== "admin" || !idConversacion) return;
    const gen = generacion;
    infoCargadaEn = Date.now();
    let info;
    try { info = await api(`/mensajeria/conversaciones/${idConversacion}/paciente`); }
    catch (e) { console.warn("No se pudieron cargar los datos del paciente:", e.message); return; }
    if (gen !== generacion) return;

    const cont = $("infoContenido");
    cont.className = "";
    const p = info.paciente;

    const cab = el("div", "info-cabecera");
    cab.append(avatar(p?.nombre, info.idConversacion),
               el("div", "info-nombre", p?.nombre || "Paciente sin identificar"),
               badge(info.estado));

    const partes = [cab];
    if (!p) {
        partes.push(seccion("Contacto", dato("Teléfono", info.telefono)),
                    el("p", "info-vacio", "El bot todavía no pudo identificar a este paciente (no coincidió el teléfono ni el DNI)."));
    } else {
        partes.push(seccion("Datos", dato("DNI", p.dni), dato("Teléfono", p.telefono || info.telefono)));

        const ul = el("ul", "lista-profesionales");
        info.profesionales.forEach(pr => {
            const li = el("li", null, pr.nombre);
            li.appendChild(el("small", null, pr.especialidad));
            ul.appendChild(li);
        });
        partes.push(seccion(info.profesionales.length === 1 ? "Profesional" : "Profesionales",
            info.profesionales.length ? ul : el("div", "sin-dato", "Sin profesionales asignados")));

        const t = info.proximoTurno;
        let turno;
        if (t) {
            const m = new Date(t.momento);
            const fecha = `${m.toLocaleDateString("es-AR", { weekday: "long" })} ${m.getDate()}/${m.getMonth() + 1}`;
            turno = el("div", "turno-card");
            turno.append(el("div", "turno-fecha", `${fecha} · ${hora(t.momento)}`),
                         el("div", null, `Con ${t.nombreProfesional}`),
                         el("div", null, `${t.modalidad} · ${t.estado}`));
        } else {
            turno = el("div", "sin-dato", "Sin turno próximo");
        }
        partes.push(seccion("Próximo turno", turno));
    }

    if (info.estado === "Urgente" || info.estado === "DerivadaHumano") {
        const acc = el("div", "info-acciones");
        const btn = el("button", "btn-resolver" + (info.estado === "Urgente" ? " urgente" : ""),
            info.estado === "Urgente" ? "Marcar como resuelta" : "Cerrar derivación");
        btn.addEventListener("click", cerrarDerivacion);
        acc.append(btn, el("p", "nota", "La conversación pasa a Cerrada y el bot vuelve a atender si el paciente escribe de nuevo."));
        partes.push(acc);
    }
    cont.replaceChildren(...partes);
}

async function cerrarDerivacion() {
    if (!idConversacion) return;
    if (!confirm("¿Cerrar esta conversación? El bot vuelve a atender si el paciente escribe de nuevo.")) return;
    try {
        await api(`/mensajeria/conversaciones/${idConversacion}/cerrar`, { method: "PUT" });
        toast("Conversación cerrada.");
        await cargarMensajes();
        cargarInfo();
        cargarConversaciones();
    } catch (e) { toast(e.message, "error"); }
}

// ── Avisos internos (campanita del header) ──────────────────
async function cargarAvisos() {
    if (modo !== "admin") return;
    try {
        const avisos = await api("/mensajeria/notificaciones-internas");
        const noLeidos = avisos.filter(a => !a.leida).length;
        $("contadorAvisos").textContent = noLeidos;
        $("contadorAvisos").classList.toggle("oculto", noLeidos === 0);
        if ($("panelAvisos").classList.contains("oculto")) return;

        const cont = $("panelAvisos");
        cont.replaceChildren();
        if (!avisos.length) cont.appendChild(el("div", "info-vacio", "No hay avisos."));
        avisos.slice(0, 30).forEach(a => {
            const div = el("div", "aviso" + (a.leida ? "" : " no-leido") + (a.tipo === "Urgencia" ? " urgencia" : ""));
            const pie = el("div", "aviso-pie");
            pie.appendChild(el("span", null, `Para: ${a.destinatario} · ${new Date(a.fechaCreacion).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short" })}`));
            if (!a.leida) {
                const btn = el("button", null, "Marcar leído");
                btn.addEventListener("click", async () => {
                    try { await api(`/mensajeria/notificaciones-internas/${a.idNotificacionInterna}/leida`, { method: "PUT" }); cargarAvisos(); }
                    catch (e) { toast(e.message, "error"); }
                });
                pie.appendChild(btn);
            }
            div.append(el("div", "aviso-titulo", a.titulo), el("div", null, a.mensaje), pie);
            cont.appendChild(div);
        });
    } catch (e) {
        console.warn("No se pudieron cargar los avisos:", e.message);
    }
}

// ── Modo paciente de prueba ─────────────────────────────────
async function usarTelefono(ev) {
    ev.preventDefault();
    const tel = $("inputTelefono").value.trim();
    if (tel.replace(/\D/g, "").length < 6) { toast("Ingresá un teléfono válido.", "error"); return; }
    reiniciarChat();
    telefonoActual = tel;
    $("app").classList.add("con-chat");
    $("chatTitulo").textContent    = "Chat con YoSoy";
    $("chatSubtitulo").textContent = "Escribiendo como el paciente con el teléfono " + tel;
    $("chatAvatar").replaceWith(Object.assign(avatar("YoSoy", 0), { id: "chatAvatar" }));
    actualizarEnvio();
    $("inputMensaje").focus();
    try {
        const c = await api(`/mensajeria/simulado/conversacion?telefono=${encodeURIComponent(tel)}`, { permitir404: true });
        if (c) { idConversacion = c.idConversacion; await cargarMensajes(); }
        else $("mensajes").querySelector(".vacio").textContent = "Todavía no hay mensajes con este número. Escribí \"hola\" para empezar.";
    } catch (e) { toast(e.message, "error"); }
}

// ── Envío ───────────────────────────────────────────────────
async function enviar(ev) {
    ev.preventDefault();
    const input = $("inputMensaje");
    const texto = input.value.trim();
    if (!texto) return;
    input.value = "";
    habilitarEnvio(false);
    try {
        if (modo === "paciente") {
            const r = await api("/mensajeria/simulado/entrante", {
                method: "POST", body: JSON.stringify({ telefono: telefonoActual, texto })
            });
            idConversacion = r.idConversacion;
        } else {
            await api(`/mensajeria/conversaciones/${idConversacion}/responder`, {
                method: "POST", body: JSON.stringify({ texto })
            });
        }
        await cargarMensajes();
        if (modo === "admin") cargarConversaciones();
    } catch (e) {
        input.value = texto;
        toast(e.message, "error");
    } finally {
        actualizarEnvio();
        if (!input.disabled) input.focus();
    }
}

// ── Inicio ──────────────────────────────────────────────────
$("btnModoAdmin").addEventListener("click", () => cambiarModo("admin"));
$("btnModoPaciente").addEventListener("click", () => cambiarModo("paciente"));
$("formTelefono").addEventListener("submit", usarTelefono);
$("formMensaje").addEventListener("submit", enviar);
$("inputBuscar").addEventListener("input", renderLista);
$("btnVolver").addEventListener("click", () => $("app").classList.remove("con-chat"));
$("btnInfo").addEventListener("click", () => $("colInfo").classList.add("abierta"));
$("btnCerrarInfo").addEventListener("click", () => $("colInfo").classList.remove("abierta"));
$("btnAvisos").addEventListener("click", () => {
    const panel = $("panelAvisos");
    const abrir = panel.classList.contains("oculto");
    panel.classList.toggle("oculto", !abrir);
    $("btnAvisos").setAttribute("aria-expanded", abrir);
    if (abrir) cargarAvisos();
});

setInterval(() => {
    cargarMensajes();
    if (modo === "admin") { cargarConversaciones(); cargarAvisos(); }
}, POLL_MS);

cambiarModo("admin");
cargarAvisos();
