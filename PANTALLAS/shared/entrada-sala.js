/* Entrada a la sala de video desde el panel (paciente y profesional). Un solo lugar para lo que antes cada pantalla resolvía por su cuenta.
 *
 * «El turno entrable» lo decide el SERVIDOR (GET /salas/entrable → EstadoDeSala.Entrable, la misma regla que da o niega el acceso a la sala): el Confirmado cuya sala está abierta
 * (desde 10 minutos antes hasta el cierre de la ventana); con más de uno, el más cercano. Acá no se calcula ninguna hora: solo se interpreta la respuesta y se dibuja.
 *
 * Estados que devuelve estadoDe(): { tipo: "entrable" | "hoy" | "proximo" | "ninguno" | "error", turno }
 *   entrable  hay un turno con la sala abierta ahora (turno = esa fila).
 *   hoy       el próximo turno es hoy pero la sala todavía no abrió.
 *   proximo   el próximo turno es otro día.
 *   ninguno   no hay turnos próximos.
 *   error     no se pudo consultar (red, 500, respuesta vacía o rara): nunca se deja la pantalla en blanco.
 */
(function (raiz) {
    const YSg = raiz.YS = raiz.YS || {};
    const esc = s => (YSg.esc ? YSg.esc(s) : String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]));

    const esTurno = t => !!t && typeof t === "object" && Number.isInteger(t.idTurno) && typeof t.hora === "string";

    /** Respuesta de GET /salas/entrable → estado. Cualquier cosa que no sea la forma esperada es "error" (no se adivina). */
    function estadoDe(resp) {
        if (!resp || typeof resp !== "object" || !("entrable" in resp || "proximo" in resp)) return { tipo: "error", turno: null };
        if (esTurno(resp.entrable)) return { tipo: "entrable", turno: resp.entrable };
        if (esTurno(resp.proximo)) return { tipo: resp.proximo.esHoy ? "hoy" : "proximo", turno: resp.proximo };
        return { tipo: "ninguno", turno: null };
    }

    /** Pregunta al servidor. No lanza: si falla, devuelve el estado "error". */
    async function consultar() {
        try { return estadoDe(await YSg.api("/salas/entrable")); }
        catch { return { tipo: "error", turno: null }; }
    }

    // "2031-03-04T10:00:00" → "04/03" (sin toLocale*: según el navegador cambia el formato).
    const diaMes = iso => { const m = /^\d{4}-(\d{2})-(\d{2})/.exec(String(iso || "")); return m ? `${m[2]}/${m[1]}` : ""; };

    /** La página de la sala de un turno (relativa a la carpeta de quien la usa: las dos pantallas tienen su sesion_video.html). */
    function urlSala(idTurno, idPaciente) {
        return "sesion_video.html?turno=" + encodeURIComponent(idTurno) + (idPaciente ? "&idPaciente=" + encodeURIComponent(idPaciente) : "");
    }

    /** Frase corta de un estado, para el Inicio. rol: "paciente" | "profesional". */
    function frase(estado, rol = "paciente") {
        const t = estado.turno;
        const con = t && t.contraparte ? ` con ${t.contraparte}` : "";
        if (estado.tipo === "entrable") return `Tu turno es a las ${t.hora}${con}.`;
        if (estado.tipo === "hoy") return `La sala abre a las ${t.horaAbre}.`;
        return rol === "paciente"
            ? "La sala de video abre 10 minutos antes de cada turno. Entrá desde la Sala de video con tu turno confirmado."
            : "La sala de video abre 10 minutos antes de cada turno.";
    }

    /**
     * El botón de entrar al turno, para el Inicio y los primeros pasos: el MISMO dibujo en los dos. Habilitado solo con el turno entrable; "hoy" lo muestra gris con la hora en que abre.
     * entrar = nombre de la función global que se llama con (idTurno).
     */
    function htmlBoton(estado, { entrar = "entrarASalaDelTurno", texto = "Entrar a la sesión" } = {}) {
        if (estado.tipo === "entrable")
            return `<button type="button" class="ui-btn ui-btn--primary pp-boton" onclick="${esc(entrar)}(${Number(estado.turno.idTurno)})">${esc(texto)}</button>`;
        if (estado.tipo === "hoy")
            return `<button type="button" class="ui-btn pp-boton" disabled aria-disabled="true">${esc(texto)}</button>`;
        return "";
    }

    /** La tarjeta «Tu sesión por video» del Inicio del paciente. enlaces = el HTML de los links de siempre (se muestran debajo en cualquier caso). */
    function htmlTarjetaInicio(estado, enlaces = "") {
        const boton = htmlBoton(estado);
        return `
        <section class="card" aria-labelledby="inicio-sesion-titulo">
            <div class="titulo-tarjeta" id="inicio-sesion-titulo">Tu sesión por video</div>
            <p class="inicio-prof-nota">${esc(frase(estado))}</p>
            ${boton ? `<div class="pp-acciones">${boton}</div>` : ""}
            ${enlaces}
        </section>`;
    }

    /**
     * El menú «Sala de video» del paciente cuando NO se entra directo (no hay turno entrable): un mensaje claro y, aparte, el código manual como opción secundaria colapsada.
     * formCodigo = el HTML del formulario del código. Nunca devuelve un texto vacío: es lo que se dibuja si el servidor falla, no contesta o contesta algo raro.
     */
    function htmlSinSesion(estado, formCodigo = "") {
        const t = estado.turno;
        const con = t && t.contraparte ? ` con ${t.contraparte}` : "";
        let mensaje;
        if (estado.tipo === "hoy") mensaje = `No tenés una sesión en este momento. Tu próximo turno es hoy a las ${t.hora}${con}: la sala abre a las ${t.horaAbre}.`;
        else if (estado.tipo === "proximo") mensaje = `No tenés una sesión en este momento. Tu próximo turno es el ${diaMes(t.inicio)} a las ${t.hora}${con}.`;
        else if (estado.tipo === "error") mensaje = "No pudimos consultar tus turnos. Probá de nuevo en unos segundos.";
        else mensaje = "No tenés turnos próximos.";
        return `
        <div class="card sesion-entrada">
            <div class="titulo-tarjeta">Sala de video</div>
            <p class="texto-fuerte mb-12" id="sala-mensaje">${esc(mensaje)}</p>
            <div class="inicio-prof-enlaces mb-12">
                <a href="#agenda">${estado.tipo === "ninguno" ? "Pedir un turno en Turnos" : "Ver mis turnos"}</a>
                ${estado.tipo === "error" ? `<a href="#sesion" onclick="mostrarSeccion('sesion'); return false;">Reintentar</a>` : ""}
            </div>
            ${formCodigo ? `<details class="sesion-codigo"><summary>Tengo un código de sala</summary>${formCodigo}</details>` : ""}
        </div>`;
    }

    YSg.entradaSala = { estadoDe, consultar, urlSala, frase, htmlBoton, htmlTarjetaInicio, htmlSinSesion };
    if (typeof module !== "undefined" && module.exports) module.exports = YSg.entradaSala;
})(typeof window !== "undefined" ? window : globalThis);
