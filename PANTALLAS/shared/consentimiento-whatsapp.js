/* ═══════════════════════════════════════════════════════════════
   YoSoy · consentimiento de los avisos por WhatsApp (YS.whatsapp)

   Lo comparten el registro de paciente y el perfil. El TEXTO del consentimiento y su versión los sirve la
   API (GET /consentimiento-whatsapp/texto sin sesión, y el estado en GET /consentimiento-whatsapp): lo que se
   ve es exactamente lo que queda registrado con la aceptación.

   Reglas que esta capa respeta:
     · Nada viene tildado por defecto: ni acá, ni por un envío anterior, ni por el navegador.
     · Nada de lo que manda el servidor (títulos, párrafos, mensajes) se inserta como HTML: todo va por textContent.
     · Si no hay texto (el servidor no lo tiene o no contesta), no se ofrece nada: el registro anda igual.
   Requiere shared/ui.js (YS.fetch).
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS = window.YS || {};

    // Por qué no se puede aceptar ahora (los códigos "motivo" que manda la API).
    const MOTIVOS = {
        "menor-de-edad": "Los avisos por WhatsApp de una persona menor de 18 años los tiene que activar su madre, padre o tutor.",
        "telefono-invalido": "Para activarlos, cargá un celular argentino válido, con código de área (por ejemplo 11 5555-0001).",
        "no-configurado": "Los avisos por WhatsApp no están disponibles en este momento."
    };

    YS.whatsapp = {
        /**
         * El texto vigente, sin sesión (para el registro): { disponible, version, provisorio, titulo, texto[] }.
         * null si el servidor no contesta o no tiene texto: en ese caso no se ofrece la casilla.
         */
        async texto() {
            try {
                const res = await YS.fetch("/consentimiento-whatsapp/texto", { sinRedireccion: true });
                if (!res.ok) return null;
                const t = await res.json();
                return t && t.disponible && t.version && Array.isArray(t.texto) ? t : null;
            } catch {
                return null;
            }
        },

        /**
         * Dibuja el texto dentro de `contenedor` (lo vacía antes): la etiqueta "Texto provisorio" si corresponde, el título y los párrafos.
         * Solo textContent: lo que manda el servidor nunca se interpreta como HTML.
         */
        dibujarTexto(contenedor, t) {
            const partes = [];
            if (t.provisorio) partes.push(YS.el("span", { class: "ui-chip ui-chip--warn ui-chip--sin-punto", text: "Texto provisorio" }));
            if (t.titulo) partes.push(YS.el("h3", { class: "wa-texto-titulo", text: t.titulo }));
            for (const parrafo of t.texto || []) partes.push(YS.el("p", { text: parrafo }));
            partes.push(YS.el("p", { class: "texto-suave texto-chico", text: `Versión del texto: ${t.version}` }));
            contenedor.replaceChildren(...partes);
        },

        /** El mensaje para el paciente de un "motivo" de la API, o null si no se conoce. */
        motivo(codigo) {
            return MOTIVOS[codigo] || null;
        },

        /** Edad en años de una fecha "AAAA-MM-DD" (o null si no hay fecha). */
        edad(fechaISO, hoy = new Date()) {
            const f = YS.fecha.parse(fechaISO);
            if (!f) return null;
            let edad = hoy.getFullYear() - f.getFullYear();
            const mes = hoy.getMonth() - f.getMonth();
            if (mes < 0 || (mes === 0 && hoy.getDate() < f.getDate())) edad--;
            return edad;
        }
    };
})();
