/* Provincia con autocompletado (registro de paciente y de profesional, y el perfil del profesional).
 *
 * Es un campo de escritura con sugerencias, NO un desplegable que se abre al hacer clic:
 *   · La lista aparece recién cuando la persona empieza a escribir. «Cor» sugiere Córdoba y Corrientes; no distingue mayúsculas ni tildes («cordoba» encuentra Córdoba).
 *   · Solo provincias de Argentina (las 23 y la Ciudad Autónoma de Buenos Aires). Al elegir una sugerencia se guarda el nombre canónico, con su tilde.
 *   · Teclado: flechas para moverse, Enter para elegir, Escape para cerrar. Roles ARIA de combobox / listbox / option; en el celular se elige tocando la sugerencia.
 *   · Escribir algo que no es una provincia es un error claro: «Elegí una provincia de la lista.» (YS.provincias.error). Los datos viejos ya guardados no se tocan: en el perfil se acepta
 *     el valor que ya tenía la persona aunque no esté en la lista (ver «previo»).
 *
 * LISTA: es la ÚNICA lista del front (la usan los tres formularios). El servidor tiene la suya en Services/Provincias.cs y una prueba verifica que sean idénticas.
 * La lógica pura (filtrar, canonica) se prueba con node (YoSoy.Tests/Pantallas/provincias.prueba.js).
 */
(function (raiz) {
    const YSg = raiz.YS = raiz.YS || {};

    const LISTA = [
        "Buenos Aires", "Catamarca", "Chaco", "Chubut", "Ciudad Autónoma de Buenos Aires", "Córdoba", "Corrientes", "Entre Ríos", "Formosa", "Jujuy", "La Pampa", "La Rioja", "Mendoza", "Misiones",
        "Neuquén", "Río Negro", "Salta", "San Juan", "San Luis", "Santa Cruz", "Santa Fe", "Santiago del Estero", "Tierra del Fuego Antártida e Islas del Atlántico Sur", "Tucumán"
    ];
    const MENSAJE_ERROR = "Elegí una provincia de la lista.";

    /** Minúsculas y sin tildes ni espacios de más: «  CÓRDOBA » → «cordoba». */
    const normalizar = s => String(s ?? "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/\s+/g, " ").trim();
    const NORMALIZADAS = LISTA.map(normalizar);

    /** Las provincias que coinciden con lo escrito: el nombre empieza con el texto (o, con 3 letras o más, alguna de sus palabras: «aires» encuentra Buenos Aires). Vacío = ninguna (la lista no se muestra hasta que se escribe). */
    function filtrar(texto) {
        const t = normalizar(texto);
        if (!t) return [];
        return LISTA.filter((_, i) => NORMALIZADAS[i].startsWith(t) || (t.length >= 3 && NORMALIZADAS[i].split(" ").some(p => p.startsWith(t))));
    }

    /** El nombre canónico (con tilde) si el texto ES una provincia (sin importar mayúsculas ni tildes); null si no. */
    function canonica(texto) {
        const i = NORMALIZADAS.indexOf(normalizar(texto));
        return i < 0 ? null : LISTA[i];
    }

    /** Mensaje de error de un valor escrito, o null si está bien. Vacío es válido acá (si es obligatorio lo dice «requerido»). «previo»: un valor ya guardado antes, que se acepta tal cual. */
    function error(texto, previo) {
        const t = String(texto ?? "").trim();
        if (!t) return null;
        if (canonica(t)) return null;
        if (previo && t === String(previo).trim()) return null;
        return MENSAJE_ERROR;
    }

    let contador = 0;
    /**
     * Convierte un <input type="text"> en un combobox con sugerencias. Devuelve { destruir() }.
     * opciones: { documento } (para pruebas).
     */
    function combo(input, opciones = {}) {
        const doc = opciones.documento || input.ownerDocument || raiz.document;
        const id = (input.id || "provincia") + "-lista-" + (++contador);
        const lista = doc.createElement("ul");
        lista.id = id; lista.setAttribute("role", "listbox"); lista.className = "ui-combo-lista"; lista.hidden = true;
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-expanded", "false");
        input.setAttribute("aria-controls", id);
        input.setAttribute("autocomplete", "off");   // la lista propia reemplaza la del navegador
        input.insertAdjacentElement("afterend", lista);
        if (input.parentElement) input.parentElement.classList.add("ui-combo");

        let opcionesActuales = [], activa = -1;

        function cerrar() { lista.hidden = true; input.setAttribute("aria-expanded", "false"); input.removeAttribute("aria-activedescendant"); activa = -1; }
        function marcarActiva(i) {
            activa = i;
            [...lista.children].forEach((li, k) => { li.setAttribute("aria-selected", k === i ? "true" : "false"); li.classList.toggle("ui-combo-activa", k === i); });
            if (i >= 0) input.setAttribute("aria-activedescendant", lista.children[i].id); else input.removeAttribute("aria-activedescendant");
        }
        let eligiendo = false;   // el «input» que se avisa al elegir no tiene que volver a abrir la lista
        function elegir(nombre) {
            input.value = nombre;
            cerrar();
            eligiendo = true;
            try {
                input.dispatchEvent(new (raiz.Event || Event)("change", { bubbles: true }));
                input.dispatchEvent(new (raiz.Event || Event)("input", { bubbles: true }));
            } finally { eligiendo = false; }
        }
        function abrir() {
            opcionesActuales = filtrar(input.value);
            lista.replaceChildren();
            if (!opcionesActuales.length) { cerrar(); return; }   // nada que sugerir (o campo vacío): la lista no se muestra
            opcionesActuales.forEach((nombre, k) => {
                const li = doc.createElement("li");
                li.id = id + "-" + k; li.setAttribute("role", "option"); li.setAttribute("aria-selected", "false"); li.textContent = nombre; li.className = "ui-combo-opcion";
                // mousedown/pointerdown (no click): el campo todavía no perdió el foco, así que la elección no se pierde en el blur.
                li.addEventListener("mousedown", e => { e.preventDefault(); elegir(nombre); });
                li.addEventListener("touchstart", e => { e.preventDefault(); elegir(nombre); }, { passive: false });
                lista.appendChild(li);
            });
            lista.hidden = false; input.setAttribute("aria-expanded", "true"); marcarActiva(-1);
        }

        const alEscribir = () => { if (!eligiendo) abrir(); };
        const alTeclear = e => {
            if (e.key === "ArrowDown" || e.key === "Down") { e.preventDefault(); if (lista.hidden) abrir(); if (opcionesActuales.length) marcarActiva((activa + 1) % opcionesActuales.length); }
            else if (e.key === "ArrowUp" || e.key === "Up") { e.preventDefault(); if (opcionesActuales.length) marcarActiva(activa <= 0 ? opcionesActuales.length - 1 : activa - 1); }
            else if (e.key === "Enter") { if (!lista.hidden && activa >= 0) { e.preventDefault(); elegir(opcionesActuales[activa]); } else if (!lista.hidden && opcionesActuales.length === 1) { e.preventDefault(); elegir(opcionesActuales[0]); } }
            else if (e.key === "Escape" || e.key === "Esc") { if (!lista.hidden) { e.preventDefault(); cerrar(); } }
        };
        // Al salir del campo: si lo escrito ES una provincia (por ejemplo «cordoba»), se deja con su nombre canónico; si no, queda para que la validación lo marque.
        const alSalir = () => { const c = canonica(input.value); if (c) input.value = c; cerrar(); };

        input.addEventListener("input", alEscribir);
        input.addEventListener("keydown", alTeclear);
        input.addEventListener("blur", alSalir);
        return { destruir() { input.removeEventListener("input", alEscribir); input.removeEventListener("keydown", alTeclear); input.removeEventListener("blur", alSalir); lista.remove(); } };
    }

    YSg.provincias = { LISTA, MENSAJE_ERROR, normalizar, filtrar, canonica, error, combo };
    if (typeof module !== "undefined" && module.exports) module.exports = YSg.provincias;
})(typeof window !== "undefined" ? window : globalThis);
