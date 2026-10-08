// YoSoy · Pantalla de chats: la bandeja del equipo en modo página (js/bandeja.js, la misma
// que se abre desde el botón Chats) y el simulador del lado del paciente, que reemplaza al
// "modo paciente" de la pantalla anterior. Polling solo con la pestaña visible; todo el
// texto se inserta con textContent.
(function () {
    "use strict";
    const $ = id => document.getElementById(id);
    const POLL_MS = 2500;
    const AUTOR = { Bot: "Asistente YoSoy", Humano: "Equipo YoSoy", Sistema: "Aviso automático" };

    const listo = YS.shell.iniciar({
        rol: "configurador", activo: "chats", titulo: "Chats",
        subtitulo: "Conversaciones de WhatsApp con el asistente y el equipo",
        chats: { modo: "pagina", contenedor: $("vistaBandeja") }
    });
    if (!listo) return;

    // ── Bandeja | Simulador (toggle en el encabezado y #simulador en la URL) ──
    const toggle = YS.el("div", { class: "ui-segmentado", role: "group", "aria-label": "Vista" },
        YS.el("button", { type: "button", "data-vista": "bandeja", text: "Bandeja" }),
        YS.el("button", { type: "button", "data-vista": "simulador", text: "Simulador del paciente" }));
    toggle.addEventListener("click", e => {
        const b = e.target.closest("[data-vista]");
        if (b) { history.replaceState(null, "", b.dataset.vista === "simulador" ? "#simulador" : "#"); mostrarVista(b.dataset.vista); }
    });
    YS.shell.acciones([toggle]);

    function mostrarVista(vista) {
        toggle.querySelectorAll("[data-vista]").forEach(b => b.setAttribute("aria-pressed", b.dataset.vista === vista));
        $("vistaBandeja").classList.toggle("oculto", vista !== "bandeja");
        $("vistaSimulador").classList.toggle("oculto", vista !== "simulador");
        if (vista === "simulador") ($("inputTelefono").value ? $("simTexto") : $("inputTelefono")).focus();
    }
    mostrarVista(location.hash === "#simulador" ? "simulador" : "bandeja");

    // Desde otro equipo con Mensajeria:SoloLocal la página puede verse, pero el módulo no responde.
    YS.shell.capacidades.then(c => {
        if (c?.mensajeria) return;
        toggle.remove();
        $("vistaBandeja").classList.add("oculto");
        $("vistaSimulador").classList.add("oculto");
        $("noDisponible").classList.remove("oculto");
        $("noDisponible").innerHTML = YS.vacio({ icono: "candado", titulo: "La mensajería no está disponible desde este equipo",
            texto: "El módulo está apagado o solo responde a pedidos hechos desde la misma máquina del servidor (Mensajeria:SoloLocal)." });
    });

    // ── Simulador del paciente ──────────────────────────────────
    let telefono = null, idConversacion = null, ultimoId = 0, estado = null, previo = null;
    let generacion = 0, cargando = false, timer = null;
    $("simEnviar").innerHTML = YS.icono("enviar");

    function reiniciar() {
        generacion++;
        idConversacion = null; ultimoId = 0; estado = null; previo = null;
        $("simMensajes").replaceChildren();
    }

    function vacio(texto) {
        $("simMensajes").innerHTML = `<div class="simulador-vacio">${YS.esc(texto)}</div>`;
    }

    function habilitar(si) {
        $("simTexto").disabled = !si;
        $("simEnviar").disabled = !si;
    }

    async function usarTelefono(e) {
        e.preventDefault();
        if (!YS.form.validar([{ id: "inputTelefono", requerido: "Ingresá un teléfono.",
            validar: v => v.replace(/\D/g, "").length < 6 ? "Ingresá un teléfono válido (al menos 6 números)." : null }])) return;
        reiniciar();
        telefono = $("inputTelefono").value.trim();
        $("simEstado").textContent = "Escribiendo como el paciente con el teléfono " + telefono;
        habilitar(true);
        $("simTexto").focus();
        try {
            const c = await YS.api(`/mensajeria/simulado/conversacion?telefono=${encodeURIComponent(telefono)}`, { permitir404: true });
            if (c) { idConversacion = c.idConversacion; await cargar(); }
            else vacio('Todavía no hay mensajes con este número. Escribí "hola" para empezar.');
        } catch (err) { YS.toast(err.message, "error"); }
        programar();
    }

    function burbuja(m) {
        const cont = $("simMensajes");
        cont.querySelector(".simulador-vacio")?.remove();
        const fecha = YS.fecha.parse(m.fechaHora);
        if (!previo || !YS.fecha.mismoDia(previo.fechaHora, fecha))
            cont.appendChild(YS.el("div", { class: "bm-dia", role: "separator" }, YS.el("span", { text: YS.fecha.separador(fecha) })));
        const propio = m.direccion === "Entrante";   // lo que escribió "el paciente"
        const seguido = previo && previo.origen === m.origen && YS.fecha.mismoDia(previo.fechaHora, fecha);
        const b = YS.el("div", { class: `sm ${propio ? "sm--propio" : "sm--yosoy sm--" + (m.origen || "").toLowerCase()}${seguido ? " sm--seguido" : ""}` });
        if (!propio && !seguido) b.appendChild(YS.el("div", { class: "sm-autor", text: AUTOR[m.origen] || m.origen }));
        const texto = YS.el("div", { class: "sm-texto" });
        String(m.texto ?? "").split(/(https?:\/\/[^\s]+)/g).forEach(parte => {
            if (/^https?:\/\//.test(parte)) texto.appendChild(YS.el("a", { href: parte, target: "_blank", rel: "noopener noreferrer", text: parte }));
            else if (parte) texto.appendChild(document.createTextNode(parte));
        });
        b.append(texto, YS.el("div", { class: "sm-meta", text: YS.fecha.hora(fecha) + (m.estadoEnvio === "Error" ? " · no entregado" : "") }));
        cont.appendChild(b);
        previo = m;
    }

    async function cargar() {
        if (!idConversacion || cargando) return;
        cargando = true;
        const gen = generacion;
        try {
            const [nuevos, conv] = await Promise.all([
                YS.api(`/mensajeria/conversaciones/${idConversacion}/mensajes?desde=${ultimoId}`),
                YS.api(`/mensajeria/conversaciones/${idConversacion}`)
            ]);
            if (gen !== generacion) return;
            const cont = $("simMensajes");
            const abajo = cont.scrollHeight - cont.scrollTop - cont.clientHeight < 80;
            nuevos.forEach(m => { burbuja(m); ultimoId = Math.max(ultimoId, m.idMensaje); });
            if (nuevos.length && (abajo || ultimoId === nuevos[nuevos.length - 1].idMensaje)) cont.scrollTop = cont.scrollHeight;
            if (conv.estado !== estado) {
                estado = conv.estado;
                $("simEstado").textContent = estado === "DerivadaHumano" ? "Te está atendiendo una persona del equipo"
                    : estado === "Urgente" ? "El equipo de YoSoy fue avisado"
                    : "Escribiendo como el paciente con el teléfono " + telefono;
            }
        } catch { /* se reintenta en la próxima vuelta */ }
        finally { cargando = false; }
    }

    async function enviar(e) {
        e.preventDefault();
        const t = $("simTexto");
        const texto = t.value.trim();
        if (!texto || !telefono) return;
        t.value = ""; ajustar();
        habilitar(false);
        try {
            const r = await YS.api("/mensajeria/simulado/entrante", { method: "POST", json: { telefono, texto } });
            idConversacion = r.idConversacion;
            await cargar();
        } catch (err) {
            t.value = texto; ajustar();
            YS.toast(err.message, "error");
        } finally {
            habilitar(true);
            t.focus();
        }
    }

    function ajustar() {
        const t = $("simTexto");
        t.style.height = "auto";
        t.style.height = Math.min(t.scrollHeight, 120) + "px";
    }

    function programar() {
        clearTimeout(timer);
        timer = setTimeout(async () => {
            if (!document.hidden && !$("vistaSimulador").classList.contains("oculto")) await cargar();
            programar();
        }, POLL_MS);
    }

    $("formTelefono").addEventListener("submit", usarTelefono);
    $("simComposer").addEventListener("submit", enviar);
    $("simTexto").addEventListener("input", ajustar);
    $("simTexto").addEventListener("keydown", e => {
        if (e.key === "Enter" && !e.shiftKey && !e.isComposing) { e.preventDefault(); $("simComposer").requestSubmit(); }
    });
    vacio("Ingresá un teléfono de prueba para empezar.");
})();
