/* Actualización automática liviana de una pantalla (hoy: el Inicio del paciente), sin recargar con F5.
 *
 * La pantalla consulta cada ~15 segundos un endpoint barato (GET /pacientes/{id}/novedades) que devuelve una «huella» del estado. Solo cuando la huella CAMBIA se llama a alCambiar(), que redibuja lo que
 * haga falta. Así lo que hace OTRA persona (recepción acepta el formulario, el sistema propone, el profesional acepta o rechaza) aparece solo, sin parpadeos, y la carga es mínima.
 *
 *  · Con la pestaña oculta NO hay consultas (ni timer); al volver a la pestaña o enfocar la ventana se consulta en el acto.
 *  · Si una consulta falla, el intervalo se duplica (retroceso) hasta un tope; con la primera que sale bien vuelve al normal.
 *  · No se solapan dos consultas, y unico(clave, …) garantiza UN solo refresco por clave: al navegar entre pantallas no quedan timers duplicados.
 *  · La primera consulta solo toma la huella de referencia: no dispara alCambiar.
 *
 *   const r = YS.refresco.unico("paciente", { consultar: () => YS.api("/pacientes/3/novedades"), alCambiar: (nuevo, previo) => {...} });
 *   r.iniciar();  r.detener();  r.forzar() (consulta ya);  r.sincronizar() (consulta ya y toma la huella como referencia, sin avisar: se usa después de una acción propia).
 *
 * Todo lo del navegador (document, window, setTimeout) se puede inyectar con «entorno»: así la lógica se prueba con un DOM falso y un reloj simulado.
 */
(function (raiz) {
    const YSg = raiz.YS = raiz.YS || {};

    const INTERVALO = 15000, TOPE = 120000, FACTOR = 2, ENFOQUE_MINIMO_MS = 2000;

    function crear({ consultar, alCambiar, intervaloMs = INTERVALO, maxIntervaloMs = TOPE, factor = FACTOR, entorno = {} }) {
        const doc = entorno.document || raiz.document;
        const ventana = entorno.window || raiz;
        const poner = entorno.setTimeout || ((f, ms) => raiz.setTimeout(f, ms));
        const quitar = entorno.clearTimeout || (t => raiz.clearTimeout(t));
        const ahora = entorno.ahora || (() => Date.now());

        let activo = false, timer = null, enCurso = false, intervalo = intervaloMs, huella = null, datos = null, ultimaConsulta = -Infinity;
        const huellaDe = d => d && typeof d === "object" && "huella" in d ? String(d.huella) : JSON.stringify(d ?? null);
        const oculta = () => !!(doc && doc.hidden);

        function parar() { if (timer !== null) { quitar(timer); timer = null; } }
        function programar() {
            parar();
            if (!activo || oculta()) return;   // pestaña oculta: nada de timers; al volver se consulta
            timer = poner(ciclo, intervalo);
        }

        async function consultarYComparar(silencioso) {
            if (enCurso) return;
            enCurso = true;
            ultimaConsulta = ahora();
            try {
                const d = await consultar();
                intervalo = intervaloMs;   // salió bien: vuelve al ritmo normal
                const h = huellaDe(d);
                const cambio = huella !== null && h !== huella;
                const previo = datos;
                huella = h; datos = d;
                if (cambio && !silencioso && activo) await alCambiar(d, previo);
            } catch {
                intervalo = Math.min(maxIntervaloMs, Math.round(intervalo * factor));   // retroceso: no se insiste cada 15 s contra un servidor caído
            } finally { enCurso = false; }
        }

        async function ciclo() {
            timer = null;
            if (!activo || oculta()) return;
            await consultarYComparar(false);
            programar();
        }

        const alCambiarVisibilidad = () => { if (!activo) return; if (oculta()) parar(); else forzar(); };
        const alEnfocar = () => { if (activo && !oculta() && ahora() - ultimaConsulta >= ENFOQUE_MINIMO_MS) forzar(); };

        async function forzar() { parar(); await consultarYComparar(false); programar(); }
        async function sincronizar() { parar(); await consultarYComparar(true); programar(); }

        return {
            iniciar() {
                if (activo) return;   // nunca dos timers
                activo = true;
                doc && doc.addEventListener && doc.addEventListener("visibilitychange", alCambiarVisibilidad);
                ventana && ventana.addEventListener && ventana.addEventListener("focus", alEnfocar);
                sincronizar();   // la referencia inicial
            },
            detener() {
                activo = false; parar();
                doc && doc.removeEventListener && doc.removeEventListener("visibilitychange", alCambiarVisibilidad);
                ventana && ventana.removeEventListener && ventana.removeEventListener("focus", alEnfocar);
            },
            forzar, sincronizar,
            estado: () => ({ activo, intervalo, huella, hayTimer: timer !== null, enCurso })
        };
    }

    const registro = {};
    /** Un solo refresco por clave: si ya había uno, se detiene antes de crear el nuevo (no se acumulan timers al volver a armar la pantalla). */
    function unico(clave, opciones) {
        if (registro[clave]) registro[clave].detener();
        return registro[clave] = crear(opciones);
    }

    YSg.refresco = { crear, unico, INTERVALO, TOPE };
    if (typeof module !== "undefined" && module.exports) module.exports = YSg.refresco;
})(typeof window !== "undefined" ? window : globalThis);
