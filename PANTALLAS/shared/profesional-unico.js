/* Elegir profesional cuando el paciente va a pedir un turno (y en el filtro del calendario).
 *
 *   · Con MÁS de uno: el selector de siempre.
 *   · Con EXACTAMENTE uno: no hay nada que elegir, así que no se muestra un desplegable con una única opción: una tarjeta con su nombre y especialidad, ya elegido, y el calendario/horarios siguen
 *     directo. El valor viaja en un <input type="hidden"> con el MISMO id que tendría el selector, así el resto del código (que lee .value) no cambia.
 *   · Con NINGUNO: un mensaje claro de qué hacer, en vez de un selector vacío.
 *
 * Solo arma texto (HTML); no toca el DOM. Se prueba con node (YoSoy.Tests/Pantallas/profesional-unico.prueba.js).
 */
(function (raiz) {
    const YSg = raiz.YS = raiz.YS || {};
    const esc = s => (YSg.esc ? YSg.esc(s) : String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]));

    const validos = lista => (Array.isArray(lista) ? lista : []).filter(p => p && Number.isInteger(p.idProfesional));
    /** "ninguno" | "uno" | "varios" */
    const modo = lista => { const n = validos(lista).length; return n === 0 ? "ninguno" : n === 1 ? "uno" : "varios"; };
    const etiqueta = p => `${esc(p.nombre)}${p.especialidad ? ` — ${esc(p.especialidad)}` : ""}`;

    const MENSAJE_NINGUNO = "Todavía no tenés un profesional asignado. Completá tu formulario «Sobre mí» (menú «Sobre mí») o escribinos a recepción y te ayudamos a conseguir un turno.";

    /** La tarjeta del profesional único, ya elegido. «id» es el del campo que lee el resto del código. */
    function tarjeta(p, id) {
        return `<div class="prof-elegido" role="group" aria-label="Tu profesional">
            <span class="prof-elegido-nombre">${esc(p.nombre)}</span>${p.especialidad ? `<span class="prof-elegido-esp texto-suave">${esc(p.especialidad)}</span>` : ""}
        </div><input type="hidden" id="${esc(id)}" value="${Number(p.idProfesional)}">`;
    }

    function mensajeNinguno() { return `<p class="prof-ninguno texto-suave" role="status">${esc(MENSAJE_NINGUNO)}</p>`; }

    /**
     * El campo de profesional del formulario de solicitud de turno.
     * opciones: { alCambiar: "js a ejecutar en onchange", textoElegir: "Seleccioná un profesional" }
     */
    function campoSolicitud(lista, id, opciones = {}) {
        const v = validos(lista);
        if (v.length === 0) return mensajeNinguno();
        if (v.length === 1) return tarjeta(v[0], id);
        return `<select id="${esc(id)}" class="turno-input"${opciones.alCambiar ? ` onchange="${opciones.alCambiar}"` : ""}>
            <option value="">${esc(opciones.textoElegir || "Seleccioná un profesional")}</option>
            ${v.map(p => `<option value="${Number(p.idProfesional)}">${etiqueta(p)}</option>`).join("")}
        </select>`;
    }

    /** El filtro «Ver disponibilidad de:» del calendario. Con uno, queda elegido; con varios, el selector con «Sin filtro»; con ninguno, el mensaje. */
    function filtroCalendario(lista, id, opciones = {}) {
        const v = validos(lista);
        if (v.length === 0) return `<div class="card">${mensajeNinguno()}</div>`;
        const cuerpo = v.length === 1
            ? `<span class="prof-filtro-etiqueta">Tu profesional:</span>${tarjeta(v[0], id)}`
            : `<label for="${esc(id)}">Ver disponibilidad de:</label>
               <select id="${esc(id)}" class="turno-input"${opciones.alCambiar ? ` onchange="${opciones.alCambiar}"` : ""}>
                   <option value="">— Sin filtro —</option>
                   ${v.map(p => `<option value="${Number(p.idProfesional)}">${etiqueta(p)}</option>`).join("")}
               </select>`;
        return `<div class="card"><div class="filtro-prof">${cuerpo}<div id="diasAtencion" class="dias-atencion"></div></div></div>`;
    }

    YSg.profesionalUnico = { modo, tarjeta, campoSolicitud, filtroCalendario, mensajeNinguno, MENSAJE_NINGUNO };
    if (typeof module !== "undefined" && module.exports) module.exports = YSg.profesionalUnico;
})(typeof window !== "undefined" ? window : globalThis);
