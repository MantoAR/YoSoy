/* ═══════════════════════════════════════════════════════════════
   YoSoy · consentimiento de los avisos por WhatsApp (YS.whatsapp)

   Lo comparten el registro de paciente y el perfil. El TEXTO del consentimiento y su versión los sirve la
   API (GET /consentimiento-whatsapp/texto sin sesión, y el estado en GET /consentimiento-whatsapp): lo que se
   ve es exactamente lo que queda registrado con la aceptación.

   Reglas que esta capa respeta:
     · Nada viene tildado por defecto: ni acá, ni por un envío anterior, ni por el navegador.
     · Nada de lo que manda el servidor (títulos, párrafos, mensajes) se inserta como HTML: todo va por textContent.
     · Si no hay texto (el servidor no lo tiene o no contesta), no se ofrece nada: el registro anda igual.
     · El link para escribirle primero a la clínica (contacto.enlace) lo arma la API con el número configurado en el servidor: acá no se
       escribe ningún número ni se arma ningún link. Sin número configurado no se muestra nada.
     · Lo único que el navegador recuerda es una molestia ("ahora no" a la sugerencia de después de un turno), nunca una aceptación.
   Requiere shared/ui.js (YS.fetch, YS.api, YS.modal).
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS = window.YS || {};

    // "Ahora no" a la sugerencia: se guarda la fecha y no se vuelve a ofrecer en este tiempo. Es la única cosa que se guarda en el navegador.
    const CLAVE_AHORA_NO = "ys.wa.ahoraNo";
    const DIAS_SIN_INSISTIR = 30;

    function sugerenciaPospuesta() {
        try {
            const cuando = Date.parse(localStorage.getItem(CLAVE_AHORA_NO) || "");
            return Number.isFinite(cuando) && Date.now() - cuando < DIAS_SIN_INSISTIR * 86400000;
        } catch {
            return false;   // sin almacenamiento (ventana privada, bloqueado): se ofrece igual
        }
    }

    function posponerSugerencia() {
        try { localStorage.setItem(CLAVE_AHORA_NO, new Date().toISOString()); } catch { /* sin almacenamiento: se vuelve a ofrecer, no pasa nada */ }
    }

    // Solo un link https a ese host y sin credenciales: el link viene de la configuración del servidor, pero acá no se confía en nada que
    // se vaya a poner en un href.
    function enlaceDeWhatsApp(valor) {
        if (typeof valor !== "string") return null;
        try {
            const u = new URL(valor);
            return u.protocol === "https:" && u.hostname === "wa.me" && !u.username && !u.password ? u.href : null;
        } catch {
            return null;
        }
    }

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

        /**
         * El bloque "escribinos vos primero" con el link que arma la API (estado.contacto.enlace), o null si la API no mandó uno (no hay
         * número de la clínica configurado) o no es un link de WhatsApp. Abrirlo no manda nada: WhatsApp se abre con un saludo ya escrito
         * y solo se envía si la persona lo manda.
         */
        contacto(estado) {
            const enlace = enlaceDeWhatsApp(estado?.contacto?.enlace);
            if (!enlace) return null;
            return YS.el("div", { class: "wa-contacto" },
                YS.el("p", { class: "texto-suave", text: "Si preferís, escribinos vos primero por WhatsApp: el chat lo abrís vos y te contestamos por ahí." }),
                YS.el("a", { class: "ui-btn", id: "enlaceWhatsApp", href: enlace, target: "_blank", rel: "noopener noreferrer", text: "Abrir WhatsApp" }),
                YS.el("p", { class: "texto-suave texto-chico", text: "Se abre WhatsApp con un saludo ya escrito. No se envía nada hasta que lo mandes vos." }));
        },

        /**
         * Después de pedir o confirmar un turno: si la persona todavía no decidió nada sobre los avisos por WhatsApp (y se pueden activar), se
         * le cuentan, con el link para escribirle primero a la clínica si hay. NUNCA activa nada: "Ver cómo activarlos" lleva al perfil
         * (alActivar), donde se lee el texto y se tilda la casilla. "Ahora no" (o cerrar) lo posterga 30 días. Para quien ya activó, desactivó
         * o tiene los avisos en pausa no se muestra nada. Devuelve true si mostró la sugerencia; nunca lanza.
         */
        async sugerirTrasUnTurno({ alActivar } = {}) {
            try {
                if (sugerenciaPospuesta()) return false;
                const estado = await YS.api("/consentimiento-whatsapp");
                if (!estado || estado.estado !== "sin-decision" || !estado.disponible) return false;

                const cuerpo = YS.el("div", {},
                    YS.el("p", { text: "Si querés, te avisamos de tus turnos por WhatsApp: el día y la hora, nada más. Es opcional y lo podés cambiar cuando quieras." }),
                    YS.whatsapp.contacto(estado));
                const ventana = YS.modal({
                    titulo: "Avisos por WhatsApp", cuerpo, tamano: "chico", enfocarCampo: false,
                    acciones: [
                        { texto: "Ahora no", alClick: c => c.cerrar(false) },
                        { texto: "Ver cómo activarlos", tipo: "primary", foco: true, alClick: c => c.cerrar(true) }
                    ]
                });
                const respuesta = await ventana.resultado;
                if (respuesta === true) alActivar?.();
                else posponerSugerencia();
                return true;
            } catch {
                return false;
            }
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
