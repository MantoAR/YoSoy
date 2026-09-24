// Chat simulado de YoSoy: modo paciente (chatear como el paciente) y modo
// administrador (ver conversaciones, responder y cerrar derivaciones).
// Actualización por polling; los textos se insertan siempre con textContent.
const API = location.protocol.startsWith("http") ? location.origin : "http://localhost:5000";
const POLL_MS = 2500;

const AUTORES = { Bot: "Asistente YoSoy", Humano: "Equipo YoSoy", Sistema: "Aviso automático", Paciente: "Paciente" };
const ESTADOS = { Activa: "Activa", DerivadaHumano: "Derivada a una persona", Urgente: "Urgente", Cerrada: "Cerrada" };

const $ = id => document.getElementById(id);

let modo            = "paciente";
let telefonoActual  = null;
let idConversacion  = null;
let ultimoIdMensaje = 0;
let ultimoDia       = null;
let filtroEstado    = "";
let generacion      = 0;       // se incrementa al cambiar de conversación: descarta respuestas viejas
let cargandoMsjs    = false;

// ── Utilidades ──────────────────────────────────────────────
async function api(ruta, opciones = {}) {
    const res = await fetch(API + ruta, {
        headers: { "Content-Type": "application/json" },
        ...opciones
    });
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

function hora(fecha) {
    return new Date(fecha).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });
}

function dia(fecha) {
    return new Date(fecha).toLocaleDateString("es-AR", { weekday: "long", day: "numeric", month: "long" });
}

// Convierte las URLs http(s) en links sin usar innerHTML.
function agregarTextoConLinks(contenedor, texto) {
    texto.split(/(https?:\/\/[^\s]+)/g).forEach(parte => {
        if (/^https?:\/\//.test(parte)) {
            const a = document.createElement("a");
            a.href = parte; a.textContent = parte; a.target = "_blank"; a.rel = "noopener noreferrer";
            contenedor.appendChild(a);
        } else if (parte) {
            contenedor.appendChild(document.createTextNode(parte));
        }
    });
}

// ── Modos ───────────────────────────────────────────────────
function cambiarModo(nuevo) {
    modo = nuevo;
    $("btnModoPaciente").classList.toggle("activo", modo === "paciente");
    $("btnModoAdmin").classList.toggle("activo", modo === "admin");
    $("btnModoPaciente").setAttribute("aria-selected", modo === "paciente");
    $("btnModoAdmin").setAttribute("aria-selected", modo === "admin");
    $("panelPaciente").classList.toggle("oculto", modo !== "paciente");
    $("panelAdmin").classList.toggle("oculto", modo !== "admin");
    $("inputMensaje").placeholder = modo === "admin" ? "Responder como equipo de YoSoy" : "Escribí un mensaje";
    reiniciarChat();
    if (modo === "admin") { cargarConversaciones(); cargarAvisos(); }
}

function reiniciarChat() {
    generacion++;
    idConversacion  = null;
    ultimoIdMensaje = 0;
    ultimoDia       = null;
    const cont = $("mensajes");
    cont.replaceChildren();
    const vacio = document.createElement("div");
    vacio.className = "vacio";
    vacio.textContent = modo === "admin"
        ? "Elegí una conversación de la lista."
        : "Ingresá un teléfono de prueba para empezar.";
    cont.appendChild(vacio);
    $("chatTitulo").textContent    = "Sin conversación";
    $("chatSubtitulo").textContent = modo === "admin" ? "Modo administrador" : "Modo paciente";
    $("chatAvatar").textContent    = "?";
    mostrarEstado(null);
    habilitarEnvio(false);
}

function habilitarEnvio(si) {
    $("inputMensaje").disabled = !si;
    $("btnEnviar").disabled    = !si;
}

function mostrarEstado(estado) {
    const badge = $("chatEstado");
    badge.classList.toggle("oculto", !estado);
    if (estado) {
        badge.textContent = ESTADOS[estado] || estado;
        badge.className = "badge badge-" + estado;
    }
    $("bannerUrgente").classList.toggle("oculto", !(modo === "admin" && estado === "Urgente"));
    $("btnCerrar").classList.toggle("oculto",
        !(modo === "admin" && (estado === "DerivadaHumano" || estado === "Urgente")));
    if (modo === "paciente" && estado === "DerivadaHumano")
        $("chatSubtitulo").textContent = "Te está atendiendo una persona del equipo";
}

// ── Mensajes ────────────────────────────────────────────────
function renderMensaje(m) {
    const cont = $("mensajes");
    cont.querySelector(".vacio")?.remove();

    const d = dia(m.fechaHora);
    if (d !== ultimoDia) {
        const sep = document.createElement("div");
        sep.className = "separador-dia";
        sep.textContent = d;
        cont.appendChild(sep);
        ultimoDia = d;
    }

    // En modo paciente, lo que escribe el paciente va a la derecha; en modo
    // administrador, lo que sale del centro (bot, equipo, avisos) va a la derecha.
    const esDelPaciente = m.direccion === "Entrante";
    const derecha = modo === "paciente" ? esDelPaciente : !esDelPaciente;

    const b = document.createElement("div");
    b.className = "burbuja " + (derecha ? "der" : "izq");
    if (m.origen === "Humano")  b.classList.add("humano");
    if (m.origen === "Sistema") b.classList.add("sistema");

    if (!(modo === "paciente" && esDelPaciente)) {
        const autor = document.createElement("div");
        autor.className = "burbuja-autor";
        autor.textContent = AUTORES[m.origen] || m.origen;
        b.appendChild(autor);
    }

    const cuerpo = document.createElement("div");
    agregarTextoConLinks(cuerpo, m.texto);
    b.appendChild(cuerpo);

    const meta = document.createElement("div");
    meta.className = "burbuja-meta" + (m.estadoEnvio === "Error" ? " error" : "");
    meta.textContent = hora(m.fechaHora) + (m.estadoEnvio === "Error" ? " · no entregado" : "");
    b.appendChild(meta);

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
        mostrarEstado(conv.estado);
    } catch (e) {
        console.warn("No se pudieron cargar los mensajes:", e.message);
    } finally {
        cargandoMsjs = false;
    }
}

// ── Modo paciente ───────────────────────────────────────────
async function usarTelefono(ev) {
    ev.preventDefault();
    const tel = $("inputTelefono").value.trim();
    if (tel.replace(/\D/g, "").length < 6) { toast("Ingresá un teléfono válido.", "error"); return; }
    reiniciarChat();
    telefonoActual = tel;
    $("chatTitulo").textContent    = "Chat con YoSoy";
    $("chatSubtitulo").textContent = "Escribiendo como el paciente con el teléfono " + tel;
    $("chatAvatar").textContent    = "Y";
    habilitarEnvio(true);
    $("inputMensaje").focus();
    try {
        const c = await api(`/mensajeria/simulado/conversacion?telefono=${encodeURIComponent(tel)}`, { permitir404: true });
        if (c) { idConversacion = c.idConversacion; await cargarMensajes(); }
        else {
            const vacio = $("mensajes").querySelector(".vacio");
            if (vacio) vacio.textContent = "Todavía no hay mensajes con este número. Escribí \"hola\" para empezar.";
        }
    } catch (e) { toast(e.message, "error"); }
}

// ── Modo administrador ──────────────────────────────────────
async function cargarConversaciones() {
    if (modo !== "admin") return;
    try {
        const lista = await api(`/mensajeria/conversaciones${filtroEstado ? "?estado=" + filtroEstado : ""}`);
        const ul = $("listaConversaciones");
        ul.replaceChildren();
        if (!lista.length) {
            const li = document.createElement("li");
            li.className = "vacio";
            li.style.padding = "16px 0";
            li.textContent = "No hay conversaciones.";
            ul.appendChild(li);
            return;
        }
        lista.forEach(c => {
            const li  = document.createElement("li");
            const btn = document.createElement("button");
            btn.className = "conversacion";
            if (c.idConversacion === idConversacion) btn.classList.add("seleccionada");
            if (c.estado === "Urgente") btn.classList.add("urgente");
            if (c.estado === "DerivadaHumano") btn.classList.add("derivada");

            const avatar = document.createElement("div");
            avatar.className = "avatar";
            avatar.textContent = (c.nombrePaciente || "?").charAt(0).toUpperCase();

            const cuerpo = document.createElement("div");
            cuerpo.className = "conv-cuerpo";
            const fila = document.createElement("div");
            fila.className = "conv-fila";
            const nombre = document.createElement("span");
            nombre.className = "conv-nombre";
            nombre.textContent = c.nombrePaciente || "Sin identificar · " + c.telefono;
            const horaEl = document.createElement("span");
            horaEl.className = "conv-hora";
            horaEl.textContent = hora(c.fechaUltimaActividad);
            fila.append(nombre, horaEl);

            const fila2 = document.createElement("div");
            fila2.className = "conv-fila";
            const ultimo = document.createElement("span");
            ultimo.className = "conv-ultimo";
            ultimo.textContent = c.ultimoMensaje || "";
            fila2.appendChild(ultimo);
            if (c.estado !== "Activa") {
                const badge = document.createElement("span");
                badge.className = "badge badge-" + c.estado;
                badge.textContent = ESTADOS[c.estado] || c.estado;
                fila2.appendChild(badge);
            }

            cuerpo.append(fila, fila2);
            btn.append(avatar, cuerpo);
            btn.addEventListener("click", () => seleccionarConversacion(c));
            li.appendChild(btn);
            ul.appendChild(li);
        });
    } catch (e) {
        console.warn("No se pudo cargar la lista:", e.message);
    }
}

async function seleccionarConversacion(c) {
    reiniciarChat();
    idConversacion = c.idConversacion;
    $("chatTitulo").textContent    = c.nombrePaciente || "Paciente sin identificar";
    $("chatSubtitulo").textContent = "Tel. " + c.telefono + " · canal " + c.canal;
    $("chatAvatar").textContent    = (c.nombrePaciente || "?").charAt(0).toUpperCase();
    habilitarEnvio(true);
    await cargarMensajes();
    cargarConversaciones();
}

async function cerrarDerivacion() {
    if (!idConversacion) return;
    try {
        await api(`/mensajeria/conversaciones/${idConversacion}/cerrar`, { method: "PUT" });
        toast("Conversación cerrada. El bot vuelve a atender.");
        await cargarMensajes();
        cargarConversaciones();
    } catch (e) { toast(e.message, "error"); }
}

async function cargarAvisos() {
    if (modo !== "admin") return;
    try {
        const avisos = await api("/mensajeria/notificaciones-internas");
        const noLeidos = avisos.filter(a => !a.leida).length;
        const contador = $("contadorAvisos");
        contador.textContent = noLeidos;
        contador.classList.toggle("oculto", noLeidos === 0);

        const cont = $("listaAvisos");
        cont.replaceChildren();
        if (!avisos.length) {
            const p = document.createElement("div");
            p.className = "ayuda";
            p.textContent = "No hay avisos.";
            cont.appendChild(p);
        }
        avisos.slice(0, 30).forEach(a => {
            const div = document.createElement("div");
            div.className = "aviso" + (a.leida ? "" : " no-leido") + (a.tipo === "Urgencia" ? " urgencia" : "");
            const titulo = document.createElement("div");
            titulo.className = "aviso-titulo";
            titulo.textContent = a.titulo;
            const msj = document.createElement("div");
            msj.textContent = a.mensaje;
            const pie = document.createElement("div");
            pie.className = "aviso-pie";
            const info = document.createElement("span");
            info.textContent = `Para: ${a.destinatario} · ${new Date(a.fechaCreacion).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short" })}`;
            pie.appendChild(info);
            if (!a.leida) {
                const btn = document.createElement("button");
                btn.textContent = "Marcar leído";
                btn.addEventListener("click", async () => {
                    try { await api(`/mensajeria/notificaciones-internas/${a.idNotificacionInterna}/leida`, { method: "PUT" }); cargarAvisos(); }
                    catch (e) { toast(e.message, "error"); }
                });
                pie.appendChild(btn);
            }
            div.append(titulo, msj, pie);
            cont.appendChild(div);
        });
    } catch (e) {
        console.warn("No se pudieron cargar los avisos:", e.message);
    }
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
                method: "POST",
                body: JSON.stringify({ telefono: telefonoActual, texto })
            });
            idConversacion = r.idConversacion;
        } else {
            await api(`/mensajeria/conversaciones/${idConversacion}/responder`, {
                method: "POST",
                body: JSON.stringify({ texto })
            });
        }
        await cargarMensajes();
        if (modo === "admin") cargarConversaciones();
    } catch (e) {
        input.value = texto;
        toast(e.message, "error");
    } finally {
        habilitarEnvio(true);
        input.focus();
    }
}

// ── Inicio ──────────────────────────────────────────────────
$("btnModoPaciente").addEventListener("click", () => cambiarModo("paciente"));
$("btnModoAdmin").addEventListener("click", () => cambiarModo("admin"));
$("formTelefono").addEventListener("submit", usarTelefono);
$("formMensaje").addEventListener("submit", enviar);
$("btnCerrar").addEventListener("click", cerrarDerivacion);
$("btnAvisos").addEventListener("click", () => {
    const lista = $("listaAvisos");
    const abrir = lista.classList.contains("oculto");
    lista.classList.toggle("oculto", !abrir);
    $("btnAvisos").setAttribute("aria-expanded", abrir);
});
document.querySelectorAll(".filtro").forEach(b => b.addEventListener("click", () => {
    document.querySelectorAll(".filtro").forEach(x => x.classList.toggle("activo", x === b));
    filtroEstado = b.dataset.estado;
    cargarConversaciones();
}));

setInterval(() => {
    cargarMensajes();
    if (modo === "admin") { cargarConversaciones(); cargarAvisos(); }
}, POLL_MS);

reiniciarChat();
