// Configuración › Asistente de chats (Chats v2): el interruptor GLOBAL de IA de los chats. Solo el Configurador (también la sesión Administración) lo ve y lo cambia; el servidor
// vuelve a controlarlo (GET/PUT /configuracion/chats-ia, 403 para el resto). Arranca APAGADO.
//
// Regla: con el interruptor global apagado, ninguna conversación puede responder con IA, aunque la tenga prendida en su panel. Cada conversación tiene además su propio interruptor
// (en Chats). La IA todavía no existe: este interruptor solo guarda la decisión. El asistente de reglas ("Bot") no depende de él. Se carga antes de dashboard.js, como leads.js.

async function renderChatsIa(c) {
    if (YS.shell.actual !== "chatsia") return;
    let estado;
    try { estado = await YS.api("/configuracion/chats-ia"); }
    catch (e) { c.innerHTML = YS.errorCarga({ texto: e.message || "No se pudo leer el interruptor." }); return; }
    if (YS.shell.actual !== "chatsia") return;

    YS.shell.encabezado({ subtitulo: "Interruptor global de IA de los chats" });
    YS.shell.acciones([]);
    const pintar = (activa) => {
        c.innerHTML = `
            <div class="ui-card">
                <div class="ui-card-header">
                    <h3 class="ui-card-titulo">IA en los chats</h3>
                    <span class="ui-chip ui-chip--${activa ? "ok" : "neutral"}" id="chatsIaEstado">${activa ? "Prendida" : "Apagada"}</span>
                </div>
                <p class="ui-card-sub">Con este interruptor <strong>apagado</strong>, ninguna conversación puede responder con IA, aunque la tenga prendida en su panel de Chats.
                    Con el interruptor <strong>prendido</strong>, solo responden las conversaciones donde además se prendió la IA a mano.</p>
                <div class="ui-campo">
                    <label class="ui-check">
                        <input type="checkbox" id="chatsIaGlobal"${activa ? " checked" : ""}>
                        <span>Permitir que la IA responda en los chats</span>
                    </label>
                </div>
                <p class="ui-card-sub">${estado.implementada ? "" : "<strong>La IA todavía no responde mensajes:</strong> por ahora este interruptor solo guarda la decisión. "}El asistente automático de turnos (el «Bot») no depende de este interruptor y sigue como siempre. Cada cambio queda en la auditoría administrativa.</p>
            </div>`;
        c.querySelector("#chatsIaGlobal").addEventListener("change", cambiar);
    };
    const cambiar = async (ev) => {
        const caja = ev.target;
        const querida = caja.checked;
        if (querida && !await YS.confirmar({
            titulo: "Prender la IA de los chats",
            mensaje: "Las conversaciones donde se prenda la IA van a poder responder con IA cuando exista. Por ahora no responde nada. ¿Prender el interruptor global?",
            textoConfirmar: "Prender"
        })) { caja.checked = false; return; }
        caja.disabled = true;
        try {
            await YS.api("/configuracion/chats-ia", { method: "PUT", json: { activa: querida } });
            YS.toast(querida ? "IA de los chats prendida." : "IA de los chats apagada.");
            pintar(querida);
        } catch (e) {
            caja.checked = !querida;
            caja.disabled = false;
            YS.toast(e.message, "error");
        }
    };
    pintar(!!estado.activa);
}
