/* ═══════════════════════════════════════════════════════════════
   YoSoy · Traspasos por confirmar (CRM › Derivaciones)

   Cuando un profesional deriva el caso de un paciente a otro desde el cierre de una sesión, el profesional de destino acepta o rechaza. Si ACEPTA, el sistema NO cambia
   ningún vínculo: crea esta tarea para Admisión (CRM), que es quien ya puede reasignar casos. Al tocar «Confirmar traspaso» se usa la reasignación de casos de siempre
   (con motivo e historial). Si el destino RECHAZA, la tarea pide decidir: el caso sigue con quien lo tenía y Admisión la da por resuelta cuando resolvió cómo seguir.
   Lo llama renderCasosDerivaciones (dashboard.js) al final de la lista de derivaciones por confirmar.
   ═══════════════════════════════════════════════════════════════ */
async function renderTraspasosPorConfirmar(contenedor) {
    let lista;
    try { lista = await YS.api("/derivaciones/traspasos-pendientes"); }
    catch { return; }   // sin permiso o sin la migración: la pantalla de siempre queda como está
    if (YS.shell.actual !== "casosderivaciones") return;

    const previa = contenedor.querySelector("#traspasos-por-confirmar");
    if (previa) previa.remove();
    const seccion = YS.el("section", { id: "traspasos-por-confirmar", class: "mt-16", "aria-label": "Traspasos por confirmar" });
    seccion.appendChild(YS.el("h3", { text: `Traspasos por confirmar (${lista.length})` }));
    seccion.appendChild(YS.el("p", { class: "texto-suave", text: "Derivaciones entre profesionales que ya respondió el profesional de destino. El vínculo del paciente no cambia hasta que confirmes el traspaso." }));

    if (!lista.length) {
        seccion.appendChild(YS.el("p", { class: "texto-suave", text: "No hay traspasos pendientes." }));
        contenedor.appendChild(seccion);
        return;
    }

    const filas = lista.map(d => {
        const aceptada = d.estado === "Aceptada por el destino";
        const fila = YS.el("div", { class: "ui-flex", style: "gap:12px;align-items:center;flex-wrap:wrap;padding:8px 0;border-top:1px solid var(--border)" });
        fila.appendChild(YS.el("div", { style: "flex:1;min-width:260px" },
            YS.el("strong", { text: d.nombrePaciente }),
            YS.el("div", { class: "texto-chico ui-muted", text: `De ${d.nombreOrigen} a ${d.nombreDestino} · ${YS.fecha.corta(d.fecha)}` }),
            YS.el("div", { class: "texto-chico", text: aceptada ? `${d.nombreDestino} aceptó la derivación: falta tu confirmación.` : `${d.nombreDestino} rechazó la derivación: decidí cómo seguir (el caso sigue con ${d.nombreOrigen}).` })));
        fila.appendChild(aceptada
            ? YS.el("button", { class: "ui-btn ui-btn--sm ui-btn--ok", type: "button", text: "Confirmar traspaso", on: { click: () => confirmarTraspaso(d) } })
            : YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", text: "Dar por resuelta", on: { click: () => resolverDerivacionRechazada(d) } }));
        return fila;
    });
    filas.forEach(f => seccion.appendChild(f));
    contenedor.appendChild(seccion);
}

async function confirmarTraspaso(d) {
    const ok = await YS.confirmar({
        titulo: "Confirmar traspaso",
        mensaje: `¿Confirmar el traspaso de ${d.nombrePaciente} de ${d.nombreOrigen} a ${d.nombreDestino}? El caso y el vínculo del paciente pasan a ${d.nombreDestino}, con motivo e historial.`,
        textoConfirmar: "Confirmar traspaso"
    });
    if (!ok) return;
    try {
        await YS.api(`/derivaciones/${d.idDerivacion}/traspasar`, { method: "POST" });
        YS.toast("Traspaso confirmado. Se avisó a los dos profesionales.");
    } catch (e) { YS.toast(e.message, "error"); }
    mostrarTab("casosderivaciones");
}

async function resolverDerivacionRechazada(d) {
    const ok = await YS.confirmar({
        titulo: "Dar por resuelta",
        mensaje: `¿Ya decidiste cómo seguir con ${d.nombrePaciente}? El caso sigue a cargo de ${d.nombreOrigen}; si querés pasarlo a otro profesional, usá «Reasignar» en Casos.`,
        textoConfirmar: "Dar por resuelta"
    });
    if (!ok) return;
    try {
        await YS.api(`/derivaciones/${d.idDerivacion}/resolver`, { method: "POST" });
        YS.toast("Derivación resuelta.");
    } catch (e) { YS.toast(e.message, "error"); }
    mostrarTab("casosderivaciones");
}
