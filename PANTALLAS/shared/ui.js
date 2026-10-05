/* ═══════════════════════════════════════════════════════════════
   YoSoy · capa JS compartida (window.YS)

   Lo que antes cada pantalla repetía por su cuenta: fetch con manejo de
   errores, toasts, confirmaciones, fechas, avatares, estados de turno,
   validación de formularios y menús desplegables. Sin dependencias.
   Ejemplos de uso en shared/guia-componentes.html.
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS = window.YS || {};

    // ── Configuración ───────────────────────────────────────────
    // Servido por la propia API (http://localhost:5000/pantallas/...) la base es el
    // mismo origen; abierto como archivo suelto cae al puerto de desarrollo.
    YS.API = location.protocol.startsWith("http") ? location.origin : "http://localhost:5000";
    YS.rutas = {
        login: "/pantallas/index.html",
        paciente: "/pantallas/Pantalla_Paciente/dashboard.html",
        profesional: "/pantallas/Pantalla_Profesional/nuevo_dashboard.html",
        configurador: "/pantallas/Pantalla_Configurador/dashboard.html",
        chats: "/pantallas/Pantalla_ChatSimulado/"
    };

    // ── DOM ─────────────────────────────────────────────────────
    const ESC = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };
    /** Escapa texto para insertarlo en HTML (nombres, motivos, mensajes del servidor). */
    YS.esc = s => String(s ?? "").replace(/[&<>"']/g, c => ESC[c]);

    /** Crea un elemento: YS.el("button", { class: "ui-btn", text: "Hola", on: { click: fn } }, hijo...) */
    YS.el = (tag, attrs = {}, ...hijos) => {
        const e = document.createElement(tag);
        for (const [k, v] of Object.entries(attrs || {})) {
            if (v == null || v === false) continue;
            if (k === "class") e.className = v;
            else if (k === "text") e.textContent = v;
            else if (k === "html") e.innerHTML = v;
            else if (k === "on") for (const [ev, fn] of Object.entries(v)) e.addEventListener(ev, fn);
            else if (k === "dataset") Object.assign(e.dataset, v);
            else e.setAttribute(k, v === true ? "" : v);
        }
        for (const h of hijos.flat()) if (h != null && h !== false) e.append(h instanceof Node ? h : String(h));
        return e;
    };

    // Íconos de trazo (24×24). Constantes propias: nunca llevan datos de usuario.
    const ICONOS = {
        inicio: '<path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6h-6v6H4a1 1 0 0 1-1-1z"/>',
        agenda: '<rect x="3" y="4.5" width="18" height="16.5" rx="2"/><path d="M3 9.5h18M8 2.5v4M16 2.5v4"/>',
        reloj: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
        pacientes: '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c.6-3.5 3.2-5.5 6.5-5.5s5.9 2 6.5 5.5M16 4.8a3.3 3.3 0 0 1 0 6.4M18.5 14.8c1.7.7 2.8 2.4 3 5.2"/>',
        usuario: '<circle cx="12" cy="8" r="4"/><path d="M4 21c.8-4 4-6.5 8-6.5s7.2 2.5 8 6.5"/>',
        "usuario-mas": '<circle cx="10" cy="8" r="4"/><path d="M3 20c.7-3.7 3.5-6 7-6 1.4 0 2.7.4 3.8 1M18 14v6M15 17h6"/>',
        video: '<rect x="2.5" y="6" width="13" height="12" rx="2"/><path d="m15.5 10.5 6-3.5v10l-6-3.5"/>',
        reportes: '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
        documento: '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6M8 13h8M8 17h5"/>',
        firma: '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6M8.5 15l2 2 4.5-4.5"/>',
        portapapeles: '<rect x="5" y="3.5" width="14" height="18" rx="2"/><path d="M9 3.5v2h6v-2M9 11h6M9 15h4"/>',
        salir: '<path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3M10 17l5-5-5-5M15 12H3"/>',
        campana: '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.94 1.94 0 0 0 3.4 0"/>',
        chat: '<path d="M21 11.5a8.4 8.4 0 0 1-9 8.4 9.3 9.3 0 0 1-3.9-.9L3 20.5l1.5-4.6A8.1 8.1 0 0 1 3 11.5a8.4 8.4 0 0 1 9-8.4 8.4 8.4 0 0 1 9 8.4z"/>',
        chevron: '<path d="m6 9 6 6 6-6"/>',
        "chevron-izq": '<path d="m15 18-6-6 6-6"/>',
        "chevron-der": '<path d="m9 18 6-6-6-6"/>',
        buscar: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
        cerrar: '<path d="M18 6 6 18M6 6l12 12"/>',
        mas: '<path d="M12 5v14M5 12h14"/>',
        check: '<path d="M20 6 9 17l-5-5"/>',
        "check-doble": '<path d="M18 6 7 17l-5-5M22 10l-7.5 7.5L13 16"/>',
        alerta: '<path d="M12 3 2.5 20h19z"/><path d="M12 10v4M12 17h.01"/>',
        info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
        escudo: '<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/>',
        etiqueta: '<path d="M20.6 13.4 13.4 20.6a2 2 0 0 1-2.8 0L3 13V3h10l7.6 7.6a2 2 0 0 1 0 2.8z"/><path d="M7.5 7.5h.01"/>',
        tarjeta: '<rect x="2.5" y="5" width="19" height="14" rx="2"/><path d="M2.5 10h19"/>',
        ia: '<path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/>',
        lista: '<path d="M8 6h13M8 12h13M8 18h13M3.5 6h.01M3.5 12h.01M3.5 18h.01"/>',
        tendencia: '<path d="M3 17l6-6 4 4 8-8M15 7h6v6"/>',
        flechas: '<path d="M4 7h13l-3-3M20 17H7l3 3"/>',
        candado: '<rect x="4" y="10.5" width="16" height="10" rx="2"/><path d="M8 10.5V7a4 4 0 0 1 8 0v3.5"/>',
        refrescar: '<path d="M21 12a9 9 0 1 1-2.6-6.4M21 4v5h-5"/>',
        expandir: '<path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7"/>',
        contraer: '<path d="M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7"/>',
        enviar: '<path d="m22 2-7 20-4-9-9-4z"/><path d="M22 2 11 13"/>',
        rayo: '<path d="M13 2 3 14h9l-1 8 10-12h-9z"/>',
        sonido: '<path d="M11 5 6 9H2v6h4l5 4zM15.5 8.5a5 5 0 0 1 0 7M19 5a10 10 0 0 1 0 14"/>',
        "sin-sonido": '<path d="M11 5 6 9H2v6h4l5 4zM22 9l-6 6M16 9l6 6"/>',
        teclado: '<rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10"/>',
        "panel-der": '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M15 4v16"/>',
        bot: '<rect x="4" y="8" width="16" height="12" rx="3"/><path d="M12 8V4M9 13h.01M15 13h.01M9 17h6"/>',
        telefono: '<path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1.9.3 1.8.6 2.7a2 2 0 0 1-.5 2.1L8 9.8a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.5 2.7.6a2 2 0 0 1 1.7 2z"/>',
        externo: '<path d="M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
        "flecha-abajo": '<path d="M12 5v14M19 12l-7 7-7-7"/>',
        ajustes: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
        bandeja: '<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1z"/>',
        nube: '<path d="M17.5 19H9a7 7 0 1 1 6.7-9h1.8a4.5 4.5 0 1 1 0 9z"/>',
        "sin-conexion": '<path d="M2 2l20 20M8.5 16.5a5 5 0 0 1 7 0M2 8.8a15 15 0 0 1 4.2-2.7M10.7 5.1A15 15 0 0 1 22 8.8M5 12.9a10 10 0 0 1 5.2-2.8M16.8 11.2a10 10 0 0 1 2.2 1.7M12 20h.01"/>',
        lapiz: '<path d="M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/>',
        papelera: '<path d="M3 6h18M8 6V4h8v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6"/>',
        descargar: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3"/>',
        copiar: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
        corazon: '<path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1 1.1L12 21l7.8-7.5 1-1.1a5.5 5.5 0 0 0 0-7.8z"/>'
    };
    /** SVG de trazo por nombre: YS.icono("agenda") */
    YS.icono = (nombre, clase = "") =>
        `<svg class="${clase}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${ICONOS[nombre] || ""}</svg>`;

    // ── Texto y personas ────────────────────────────────────────
    /** Minúsculas sin tildes: para buscar y para comparar nombres de días ("Miércoles" = "miercoles"). */
    YS.normalizar = s => String(s ?? "").toLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "").trim();
    YS.capitalizar = s => { s = String(s ?? ""); return s.charAt(0).toUpperCase() + s.slice(1); };
    YS.nombreCompleto = p => p ? [p.nombre, p.apellido].filter(Boolean).join(" ").trim() : "";
    YS.iniciales = nombre => {
        const partes = String(nombre ?? "").trim().split(/\s+/).filter(Boolean);
        if (!partes.length) return "?";
        return ((partes[0][0] || "") + (partes.length > 1 ? partes[partes.length - 1][0] : "")).toUpperCase();
    };
    /** Color estable por persona (c1..c6), para que el mismo paciente tenga siempre el mismo avatar. */
    YS.colorDe = semilla => {
        const s = String(semilla ?? "");
        let h = 0;
        for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
        return "c" + ((h % 6) + 1);
    };
    YS.avatar = (nombre, semilla, tamano = "") => {
        const t = tamano ? " ui-avatar--" + tamano : "";
        return nombre
            ? `<span class="ui-avatar ${YS.colorDe(semilla ?? nombre)}${t}" aria-hidden="true">${YS.esc(YS.iniciales(nombre))}</span>`
            : `<span class="ui-avatar ui-avatar--anonimo${t}" aria-hidden="true">#</span>`;
    };

    // ── Días de la semana ───────────────────────────────────────
    // La base guarda el día como texto y no siempre igual ("lunes", "Lunes", "Miercoles").
    const DIAS = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"];
    YS.dias = {
        nombres: DIAS,
        /** Número de día (0 = domingo) a partir del texto guardado, o -1 si no se reconoce. */
        numero: texto => DIAS.findIndex(d => YS.normalizar(d) === YS.normalizar(texto)),
        /** Orden lunes → domingo, para listar horarios. */
        orden: texto => { const n = YS.dias.numero(texto); return n < 0 ? 99 : (n + 6) % 7; },
        /** Texto prolijo ("lunes" → "Lunes"). */
        nombre: texto => { const n = YS.dias.numero(texto); return n < 0 ? String(texto ?? "") : DIAS[n]; }
    };

    // ── Fechas ──────────────────────────────────────────────────
    const MESES_CORTOS = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];
    const DIAS_CORTOS = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"];
    const dosD = n => String(n).padStart(2, "0");
    YS.fecha = {
        /** Date local. "2026-10-05" se toma como fecha local (new Date() la tomaría en UTC y en Argentina caería el día anterior). */
        parse(v) {
            if (v instanceof Date) return v;
            if (v == null || v === "") return null;
            if (/^\d{4}-\d{2}-\d{2}$/.test(v)) { const [a, m, d] = v.split("-").map(Number); return new Date(a, m - 1, d); }
            const d = new Date(v);
            return isNaN(d) ? null : d;
        },
        aISO(v) { const d = YS.fecha.parse(v); return d ? `${d.getFullYear()}-${dosD(d.getMonth() + 1)}-${dosD(d.getDate())}` : ""; },
        mismoDia(a, b) { a = YS.fecha.parse(a); b = YS.fecha.parse(b); return !!a && !!b && a.toDateString() === b.toDateString(); },
        esHoy(v) { return YS.fecha.mismoDia(v, new Date()); },
        esAyer(v) { const a = new Date(); a.setDate(a.getDate() - 1); return YS.fecha.mismoDia(v, a); },
        /** "14:05" */
        hora(v) { const d = YS.fecha.parse(v); return d ? `${dosD(d.getHours())}:${dosD(d.getMinutes())}` : ""; },
        /** "09:30:00" (TimeSpan del backend) → "09:30" */
        horaTexto(h) { return h ? String(h).substring(0, 5) : ""; },
        /** "05/10/2026" */
        corta(v) { const d = YS.fecha.parse(v); return d ? `${dosD(d.getDate())}/${dosD(d.getMonth() + 1)}/${d.getFullYear()}` : ""; },
        /** "5 oct" */
        diaMes(v) { const d = YS.fecha.parse(v); return d ? `${d.getDate()} ${MESES_CORTOS[d.getMonth()]}` : ""; },
        /** "lunes 5 de octubre" (+ " de 2026" si se pide o si no es este año) */
        larga(v, conAnio) {
            const d = YS.fecha.parse(v);
            if (!d) return "";
            const txt = d.toLocaleDateString("es-AR", { weekday: "long", day: "numeric", month: "long" });
            return (conAnio || d.getFullYear() !== new Date().getFullYear()) ? `${txt} de ${d.getFullYear()}` : txt;
        },
        /** Para listas tipo chat: hoy → "14:05", ayer → "Ayer", esta semana → "lun", si no "05/10" (o "05/10/25"). */
        relativa(v) {
            const d = YS.fecha.parse(v);
            if (!d) return "";
            if (YS.fecha.esHoy(d)) return YS.fecha.hora(d);
            if (YS.fecha.esAyer(d)) return "Ayer";
            const dias = (new Date().setHours(0, 0, 0, 0) - new Date(d).setHours(0, 0, 0, 0)) / 864e5;
            if (dias > 0 && dias < 7) return YS.capitalizar(DIAS_CORTOS[d.getDay()]);
            const base = `${dosD(d.getDate())}/${dosD(d.getMonth() + 1)}`;
            return d.getFullYear() === new Date().getFullYear() ? base : `${base}/${String(d.getFullYear()).slice(2)}`;
        },
        /** "recién", "hace 5 min", "hace 2 h", "ayer", "hace 3 días", o la fecha corta. */
        hace(v) {
            const d = YS.fecha.parse(v);
            if (!d) return "";
            const s = (Date.now() - d.getTime()) / 1000;
            if (s < 60) return "recién";
            if (s < 3600) return `hace ${Math.floor(s / 60)} min`;
            if (YS.fecha.esHoy(d)) return `hace ${Math.floor(s / 3600)} h`;
            if (YS.fecha.esAyer(d)) return "ayer";
            if (s < 7 * 86400) return `hace ${Math.max(2, Math.round(s / 86400))} días`;
            return YS.fecha.corta(d);
        },
        /** Separador de día en hilos: "Hoy", "Ayer", "Lunes 29 de septiembre". */
        separador(v) {
            const d = YS.fecha.parse(v);
            if (!d) return "";
            if (YS.fecha.esHoy(d)) return "Hoy";
            if (YS.fecha.esAyer(d)) return "Ayer";
            return YS.capitalizar(YS.fecha.larga(d));
        }
    };

    // ── Estados de turno: mismos textos y colores en toda la app ──
    const ESTADOS_TURNO = { 1: "Pendiente", 2: "Confirmado", 3: "Cancelado", 4: "Realizado" };
    YS.turno = {
        texto(estado) { return typeof estado === "number" ? (ESTADOS_TURNO[estado] ?? "—") : (estado || "—"); },
        /** pendiente | propuesto | confirmado | cancelado | realizado | rechazado */
        tono(estado, { propuesto = false } = {}) {
            const t = YS.normalizar(YS.turno.texto(estado));
            if (t.startsWith("pendiente")) return propuesto ? "propuesto" : "pendiente";
            if (t.startsWith("confirm")) return "confirmado";
            if (t.startsWith("cancel")) return "cancelado";
            if (t.startsWith("realiz")) return "realizado";
            if (t.startsWith("rechaz")) return "rechazado";
            return "cancelado";
        },
        chip(estado, opciones = {}) {
            const texto = opciones.texto || YS.turno.texto(estado);
            return `<span class="ui-chip estado-turno--${YS.turno.tono(estado, opciones)}">${YS.esc(texto)}</span>`;
        }
    };

    // ── Sesión ──────────────────────────────────────────────────
    YS.sesion = {
        rol: () => localStorage.getItem("rol"),
        idUsuario: () => parseInt(localStorage.getItem("idUsuario")) || null,
        idPaciente: () => parseInt(localStorage.getItem("idPaciente")) || null,
        idProfesional: () => parseInt(localStorage.getItem("idProfesional")) || null,
        /** Corta la carga de la pantalla si no hay sesión del rol indicado y vuelve al login. */
        requerir(rol) {
            const ids = { paciente: "idPaciente", profesional: "idProfesional", configurador: "idUsuario" };
            const okRol = YS.sesion.rol() === rol || (!YS.sesion.rol() && localStorage.getItem(ids[rol]));
            if (okRol && localStorage.getItem(ids[rol])) return true;
            location.replace(YS.rutas.login);
            return false;
        },
        async cerrar({ preguntar = true } = {}) {
            if (preguntar && !await YS.confirmar({
                titulo: "Cerrar sesión", mensaje: "¿Querés cerrar la sesión en este equipo?", textoConfirmar: "Cerrar sesión"
            })) return;
            localStorage.clear();
            location.href = YS.rutas.login;
        },
        /** La API respondió 401: la sesión no es válida (con JWT, porque venció). */
        expirada() {
            if (YS.sesion._expirando) return;
            YS.sesion._expirando = true;
            YS.toast("Tu sesión ya no es válida. Ingresá de nuevo.", "warn");
            setTimeout(() => { localStorage.clear(); location.href = YS.rutas.login; }, 1600);
        }
    };

    // ── HTTP ────────────────────────────────────────────────────
    YS.mensajeHttp = s =>
        s === 0   ? "No se pudo conectar con el servidor. Revisá la conexión e intentá de nuevo." :
        s === 400 ? "Revisá los datos ingresados." :
        s === 403 ? "No tenés permiso para hacer esto." :
        s === 404 ? "No se encontró lo que buscabas." :
        s === 405 ? "Esta acción no está disponible en el servidor." :
        s === 409 ? "No se pudo completar por un conflicto con otros datos." :
        s >= 500  ? "El servidor tuvo un problema. Probá de nuevo en un rato." : `Error ${s}`;

    /**
     * fetch con la base de la API, el header X-Usuario-Id y JSON. Devuelve el Response
     * (igual que fetch) para que cada pantalla siga decidiendo según el status.
     * opciones.json: objeto a mandar como cuerpo JSON.
     */
    YS.fetch = async (ruta, opciones = {}) => {
        const url = /^https?:/i.test(ruta) ? ruta : YS.API + ruta;
        const { json, sinRedireccion, ...resto } = opciones;
        const headers = new Headers(resto.headers || {});
        const idU = localStorage.getItem("idUsuario");
        if (idU && !headers.has("X-Usuario-Id")) headers.set("X-Usuario-Id", idU);
        if (json !== undefined) { resto.body = JSON.stringify(json); headers.set("Content-Type", "application/json"); }
        else if (typeof resto.body === "string" && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
        let res;
        try {
            res = await fetch(url, { ...resto, headers });
        } catch (e) {
            if (e.name === "AbortError") throw e;
            const err = new Error(YS.mensajeHttp(0));
            err.status = 0; err.sinConexion = true;
            throw err;
        }
        if (res.status === 401 && !sinRedireccion) YS.sesion.expirada();
        return res;
    };

    /** El campo "mensaje" que manda el backend en los errores (400, 403, 409 de RN-02, etc.). */
    YS.leerMensaje = async (res, porDefecto) => {
        try { const d = await res.clone().json(); if (d && (d.mensaje || d.message)) return d.mensaje || d.message; } catch { }
        try { const t = (await res.clone().text()).trim(); if (t && t.length < 240 && !t.includes("\n") && !t.startsWith("<")) return t; } catch { }
        return porDefecto ?? YS.mensajeHttp(res.status);
    };

    /**
     * JSON o error con mensaje legible. El Error trae .status y .datos (cuerpo del error).
     * opciones.permitir404: devuelve null en vez de fallar (para "no hay datos").
     */
    YS.api = async (ruta, opciones = {}) => {
        const { permitir404, ...resto } = opciones;
        const res = await YS.fetch(ruta, resto);
        if (res.status === 404 && permitir404) return null;
        if (!res.ok) {
            const err = new Error(await YS.leerMensaje(res));
            err.status = res.status;
            try { err.datos = await res.clone().json(); } catch { err.datos = null; }
            throw err;
        }
        if (res.status === 204) return null;
        const txt = await res.text();
        return txt ? JSON.parse(txt) : null;
    };

    // ── Toasts ──────────────────────────────────────────────────
    /**
     * YS.toast("Turno confirmado")  ·  YS.toast("No se pudo", "error")
     * opciones: { duracion (ms, 0 = queda fijo), accion: { texto, alClick } }
     */
    YS.toast = (mensaje, tipo = "ok", opciones = {}) => {
        if (typeof opciones === "number") opciones = { duracion: opciones };
        let cont = document.getElementById("toast-container");
        if (!cont) {
            cont = YS.el("div", { id: "toast-container", "aria-live": "polite" });
            document.body.appendChild(cont);
        }
        const iconos = { ok: "✔", error: "✖", warn: "!", info: "i" };
        const t = YS.el("div", { class: `toast toast-${tipo}`, role: tipo === "error" ? "alert" : "status" },
            YS.el("span", { class: "toast-icono", "aria-hidden": "true", text: iconos[tipo] ?? "i" }),
            YS.el("span", { class: "toast-texto", text: mensaje }));
        let timer = null;
        const cerrar = () => {
            if (t.classList.contains("saliendo")) return;
            clearTimeout(timer);
            t.classList.add("saliendo");
            t.addEventListener("animationend", () => t.remove());
            setTimeout(() => t.remove(), 400);
        };
        if (opciones.accion) {
            t.appendChild(YS.el("button", {
                class: "toast-accion", type: "button", text: opciones.accion.texto,
                on: { click: () => { cerrar(); opciones.accion.alClick?.(); } }
            }));
        }
        t.appendChild(YS.el("button", { class: "toast-cerrar", type: "button", "aria-label": "Cerrar aviso", text: "×", on: { click: cerrar } }));
        cont.appendChild(t);
        const duracion = opciones.duracion ?? (tipo === "error" ? 6000 : opciones.accion ? 6000 : 3500);
        const programar = () => { if (duracion > 0) timer = setTimeout(cerrar, duracion); };
        t.addEventListener("mouseenter", () => clearTimeout(timer));
        t.addEventListener("mouseleave", programar);
        programar();
        return { cerrar, elemento: t };
    };

    // ── Modales ─────────────────────────────────────────────────
    const ENFOCABLES = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
    let idModal = 0;
    const pilaModales = [];

    /**
     * Modal accesible (foco atrapado, Esc, clic afuera, devuelve el foco al cerrar).
     * YS.modal({ titulo, cuerpo: nodo | html, tamano: "chico" | "grande" | "xl",
     *            acciones: [{ texto, tipo: "primary" | "danger-solido" | "ghost", alClick(ctrl), foco }],
     *            cerrable: true, alCerrar(resultado) })
     * Devuelve { elemento, cuerpo, cerrar(resultado), resultado: Promise }.
     */
    YS.modal = (op = {}) => {
        const previo = document.activeElement;
        const uid = "ui-modal-" + (++idModal);
        const cerrable = op.cerrable !== false;
        const fondo = YS.el("div", { class: "ui-modal-fondo" });
        const modal = YS.el("div", {
            class: "ui-modal" + (op.tamano ? " ui-modal--" + op.tamano : "") + (op.titulo ? "" : " ui-modal--sin-cabecera") + (op.clase ? " " + op.clase : ""),
            role: op.rol || "dialog", "aria-modal": "true", tabindex: "-1",
            "aria-labelledby": op.titulo ? uid + "-t" : null, "aria-label": op.titulo ? null : (op.etiqueta || "Diálogo")
        });
        let resolver;
        const ctrl = { elemento: modal, resultado: new Promise(r => resolver = r) };
        ctrl.cerrar = (resultado) => {
            if (ctrl.cerrado) return;
            ctrl.cerrado = true;
            document.removeEventListener("keydown", teclas, true);
            fondo.remove();
            pilaModales.splice(pilaModales.indexOf(ctrl), 1);
            if (!pilaModales.length) document.body.classList.remove("ui-sin-scroll");
            if (previo && document.contains(previo)) previo.focus?.();
            op.alCerrar?.(resultado);
            resolver(resultado);
        };
        if (op.titulo) {
            const cab = YS.el("div", { class: "ui-modal-cabecera" },
                YS.el("h2", { class: "ui-modal-titulo", id: uid + "-t", text: op.titulo }));
            if (cerrable) cab.appendChild(YS.el("button", {
                class: "ui-btn ui-btn--ghost ui-btn--icono ui-modal-cerrar", type: "button", "aria-label": "Cerrar",
                html: YS.icono("cerrar"), on: { click: () => ctrl.cerrar(null) }
            }));
            modal.appendChild(cab);
        }
        const cuerpo = YS.el("div", { class: "ui-modal-cuerpo" });
        if (op.cuerpo instanceof Node) cuerpo.appendChild(op.cuerpo);
        else if (op.cuerpo != null) cuerpo.innerHTML = op.cuerpo;
        modal.appendChild(cuerpo);
        ctrl.cuerpo = cuerpo;
        let botonFoco = null;
        if (op.acciones?.length) {
            const pie = YS.el("div", { class: "ui-modal-pie" });
            op.acciones.forEach(a => {
                const b = YS.el("button", {
                    class: "ui-btn" + (a.tipo ? " ui-btn--" + a.tipo : ""), type: "button", text: a.texto, id: a.id || null,
                    on: {
                        click: async () => {
                            if (!a.alClick) { ctrl.cerrar(null); return; }
                            const r = a.alClick(ctrl, b);
                            if (r instanceof Promise) { b.classList.add("cargando"); try { await r; } finally { b.classList.remove("cargando"); } }
                        }
                    }
                });
                if (a.foco) botonFoco = b;
                pie.appendChild(b);
            });
            modal.appendChild(pie);
            ctrl.pie = pie;
        }
        const teclas = e => {
            if (pilaModales[pilaModales.length - 1] !== ctrl) return;
            if (e.key === "Escape" && cerrable) { e.stopPropagation(); e.preventDefault(); ctrl.cerrar(null); }
            if (e.key === "Tab") {
                const f = [...modal.querySelectorAll(ENFOCABLES)].filter(x => x.offsetParent !== null);
                if (!f.length) { e.preventDefault(); modal.focus(); return; }
                const primero = f[0], ultimo = f[f.length - 1];
                if (e.shiftKey && document.activeElement === primero) { e.preventDefault(); ultimo.focus(); }
                else if (!e.shiftKey && document.activeElement === ultimo) { e.preventDefault(); primero.focus(); }
            }
        };
        document.addEventListener("keydown", teclas, true);
        let abajoEnFondo = false;
        fondo.addEventListener("mousedown", e => { abajoEnFondo = e.target === fondo; });
        fondo.addEventListener("click", e => { if (cerrable && abajoEnFondo && e.target === fondo) ctrl.cerrar(null); });
        fondo.appendChild(modal);
        document.body.appendChild(fondo);
        document.body.classList.add("ui-sin-scroll");
        pilaModales.push(ctrl);
        const campo = cuerpo.querySelector("input:not([type=hidden]):not([readonly]), textarea, select");
        (op.enfocarCampo !== false && campo ? campo : botonFoco || modal).focus();
        return ctrl;
    };

    /** Reemplazo de confirm(): YS.confirmar({ titulo, mensaje, textoConfirmar, peligro }) → Promise<bool> */
    YS.confirmar = (op) => {
        if (typeof op === "string") op = { mensaje: op };
        const cuerpo = YS.el("p", { class: "ui-confirmar-texto", text: op.mensaje || "" });
        const m = YS.modal({
            titulo: op.titulo || null, cuerpo, tamano: "chico", rol: "alertdialog",
            etiqueta: op.mensaje, enfocarCampo: false,
            acciones: [
                { texto: op.textoCancelar || "Cancelar", id: op.idCancelar, alClick: c => c.cerrar(false) },
                { texto: op.textoConfirmar || "Aceptar", id: op.idConfirmar, tipo: op.peligro ? "danger-solido" : "primary", foco: true, alClick: c => c.cerrar(true) }
            ]
        });
        return m.resultado.then(r => r === true);
    };

    /** Reemplazo de alert(): YS.avisar({ titulo, mensaje }) */
    YS.avisar = (op) => {
        if (typeof op === "string") op = { mensaje: op };
        const m = YS.modal({
            titulo: op.titulo || null, tamano: "chico", rol: "alertdialog", etiqueta: op.mensaje, enfocarCampo: false,
            cuerpo: YS.el("p", { text: op.mensaje || "" }),
            acciones: [{ texto: op.textoBoton || "Entendido", tipo: "primary", foco: true, alClick: c => c.cerrar(true) }]
        });
        return m.resultado.then(() => undefined);
    };

    /** Reemplazo de prompt() con validación en el campo. Devuelve el texto o null si se cancela. */
    YS.pedirTexto = (op = {}) => {
        const id = "ui-pedir-" + (++idModal);
        const campo = YS.el("div", { class: "ui-campo" },
            YS.el("label", { for: id, text: op.etiqueta || "Texto" }),
            op.multilinea
                ? YS.el("textarea", { id, class: "ui-textarea", rows: 4, placeholder: op.placeholder || null, maxlength: op.maximo || null })
                : YS.el("input", { id, class: "ui-input", type: "text", placeholder: op.placeholder || null, maxlength: op.maximo || null }));
        const cuerpo = YS.el("div", {}, op.mensaje ? YS.el("p", { text: op.mensaje }) : null, campo);
        campo.querySelector("input, textarea").value = op.valor || "";
        const m = YS.modal({
            titulo: op.titulo || "Completá el dato", cuerpo, tamano: op.multilinea ? null : "chico",
            acciones: [
                { texto: "Cancelar", alClick: c => c.cerrar(null) },
                {
                    texto: op.textoConfirmar || "Aceptar", tipo: op.peligro ? "danger-solido" : "primary",
                    alClick: c => {
                        const v = document.getElementById(id).value.trim();
                        if (op.requerido !== false && !v) { YS.form.marcar(id, op.mensajeRequerido || "Este dato es obligatorio."); return; }
                        c.cerrar(v);
                    }
                }
            ]
        });
        if (!op.multilinea) document.getElementById(id).addEventListener("keydown", e => {
            if (e.key === "Enter") { e.preventDefault(); m.pie.lastChild.click(); }
        });
        return m.resultado.then(v => (typeof v === "string" ? v : null));
    };

    // ── Formularios: validación visible en el campo ─────────────
    const elCampo = x => typeof x === "string" ? document.getElementById(x) : x;
    YS.form = {
        /** Marca el campo como inválido y muestra el mensaje debajo. */
        marcar(campoOId, mensaje) {
            const input = elCampo(campoOId);
            if (!input) return;
            const cont = input.closest(".ui-campo") || input.parentElement;
            cont.classList.add("invalido");
            input.setAttribute("aria-invalid", "true");
            const idErr = (input.id || ("campo-" + (++idModal))) + "-error";
            let err = cont.querySelector(".ui-campo-error");
            if (!err) {
                err = YS.el("div", { class: "ui-campo-error", id: idErr });
                const ayuda = cont.querySelector(".ui-campo-ayuda");
                ayuda ? ayuda.before(err) : cont.appendChild(err);
            }
            err.textContent = mensaje;
            input.setAttribute("aria-describedby", err.id);
            if (!input.dataset.uiLimpia) {
                input.dataset.uiLimpia = "1";
                const limpiar = () => YS.form.limpiar(input);
                input.addEventListener("input", limpiar);
                input.addEventListener("change", limpiar);
            }
        },
        limpiar(campoOId) {
            const input = elCampo(campoOId);
            if (!input) return;
            const cont = input.closest(".ui-campo") || input.parentElement;
            cont.classList.remove("invalido");
            input.removeAttribute("aria-invalid");
            cont.querySelector(".ui-campo-error")?.remove();
        },
        limpiarTodo(contenedor) {
            (elCampo(contenedor) || document).querySelectorAll("[aria-invalid=true]").forEach(i => YS.form.limpiar(i));
        },
        /**
         * reglas: [{ id, requerido: true | "mensaje", patron: /re/, mensajePatron, minimo, maximo,
         *            validar: valor => "mensaje de error" | null }]
         * Marca todos los campos con error, enfoca el primero y devuelve true si está todo bien.
         */
        validar(reglas) {
            let primero = null;
            for (const r of reglas) {
                const input = elCampo(r.id);
                if (!input) continue;
                const v = input.type === "checkbox" ? (input.checked ? "1" : "") : String(input.value ?? "").trim();
                let error = null;
                if (r.requerido && !v) error = typeof r.requerido === "string" ? r.requerido : "Este dato es obligatorio.";
                else if (v && r.patron && !r.patron.test(v)) error = r.mensajePatron || "El formato no es válido.";
                else if (v && r.minimo && v.length < r.minimo) error = r.mensajeMinimo || `Tiene que tener al menos ${r.minimo} caracteres.`;
                else if (v && r.maximo && v.length > r.maximo) error = `No puede superar los ${r.maximo} caracteres.`;
                else if (r.validar) error = r.validar(v, input) || null;
                if (error) { YS.form.marcar(input, error); primero ??= input; }
                else YS.form.limpiar(input);
            }
            primero?.focus();
            return !primero;
        }
    };

    // ── Estados de pantalla (devuelven HTML para los templates) ─
    YS.cargando = (texto = "Cargando…") =>
        `<div class="ui-cargando" role="status"><span class="ui-spinner" aria-hidden="true"></span>${YS.esc(texto)}</div>`;
    /** accion: { texto, onclick: "funcion()" } (template con onclick, como el resto del código) */
    YS.vacio = ({ icono = "bandeja", titulo = "", texto = "", accion = null, clase = "" } = {}) =>
        `<div class="ui-vacio-estado ${clase}">
            <div class="ui-vacio-icono">${YS.icono(icono)}</div>
            ${titulo ? `<div class="ui-vacio-titulo">${YS.esc(titulo)}</div>` : ""}
            ${texto ? `<p class="ui-vacio-texto">${YS.esc(texto)}</p>` : ""}
            ${accion ? `<button class="ui-btn ui-btn--sm" type="button" onclick="${YS.esc(accion.onclick)}">${YS.esc(accion.texto)}</button>` : ""}
        </div>`;
    YS.errorCarga = ({ titulo = "No se pudo cargar", texto = "Revisá la conexión e intentá de nuevo.", reintentar = null } = {}) =>
        YS.vacio({ icono: "sin-conexion", titulo, texto, clase: "ui-vacio-estado--error", accion: reintentar ? { texto: "Reintentar", onclick: reintentar } : null });
    YS.esqueleto = {
        lista: (n = 4) => `<div aria-hidden="true">${Array.from({ length: n }, () =>
            `<div class="ui-esqueleto-fila"><span class="ui-esqueleto ui-esqueleto--avatar"></span><div><span class="ui-esqueleto ui-esqueleto--titulo"></span><span class="ui-esqueleto ui-esqueleto--corto"></span></div></div>`).join("")}</div>`,
        bloques: (n = 3) => `<div class="ui-stats" aria-hidden="true">${Array.from({ length: n }, () => `<span class="ui-esqueleto ui-esqueleto--bloque"></span>`).join("")}</div>`,
        tabla: (filas = 5) => `<div class="ui-esqueleto-tabla" aria-hidden="true">${Array.from({ length: filas }, () => `<span class="ui-esqueleto"></span>`).join("")}</div>`
    };

    // ── Listados: tabla densa con búsqueda, filtros, orden y paginación ──
    /**
     * Todo en el navegador, sobre las filas que ya trajo la pantalla.
     * YS.tabla(contenedor, {
     *   filas, columnas: [{ titulo, html: fila => "...", clase, orden: fila => valor }],
     *   buscar: fila => "texto donde buscar", placeholder,
     *   filtros: [{ id, texto, pasa: fila => bool }], filtro: "id inicial",
     *   porPagina: 25 (0 = sin paginar), vacio: { icono, titulo, texto }, unidad: ["turno", "turnos"],
     *   barra: [nodos extra a la derecha], ordenInicial: { col, desc }
     * }) → { actualizar(filas), estado }
     * Las celdas son HTML: los datos del servidor van con YS.esc.
     */
    YS.tabla = (contenedor, op) => {
        const est = {
            filas: op.filas || [], q: "", filtro: op.filtro || op.filtros?.[0]?.id || null, pagina: 1,
            col: op.ordenInicial?.col ?? null, desc: !!op.ordenInicial?.desc
        };
        const porPagina = op.porPagina ?? 25;
        contenedor.innerHTML = "";
        const barra = YS.el("div", { class: "ui-barra" });
        let input = null;
        if (op.buscar) {
            input = YS.el("input", { class: "ui-input", type: "search", placeholder: op.placeholder || "Buscar…", "aria-label": op.placeholder || "Buscar" });
            const lupa = YS.el("span", { html: YS.icono("buscar") }).firstChild;
            barra.appendChild(YS.el("label", { class: "ui-buscador" }, lupa, input));
            input.addEventListener("input", YS.debounce(() => { est.q = YS.normalizar(input.value); est.pagina = 1; dibujar(); }, 120));
        }
        const filtros = op.filtros?.length ? YS.el("div", { class: "ui-filtros", role: "group", "aria-label": "Filtrar" }) : null;
        if (filtros) {
            barra.appendChild(filtros);
            filtros.addEventListener("click", e => {
                const b = e.target.closest("[data-filtro]");
                if (!b) return;
                est.filtro = b.dataset.filtro; est.pagina = 1; dibujar();
            });
        }
        barra.appendChild(YS.el("span", { class: "ui-barra-espacio" }));
        const info = YS.el("span", { class: "ui-barra-info", "aria-live": "polite" });
        barra.appendChild(info);
        (op.barra || []).forEach(n => barra.appendChild(n));
        const panel = YS.el("div", { class: "ui-tabla-panel" });
        const scroll = YS.el("div", { class: "ui-tabla-scroll ui-tabla-scroll--alta" });
        const tabla = YS.el("table", { class: "ui-tabla ui-tabla--densa" });
        const thead = YS.el("thead"), tbody = YS.el("tbody");
        tabla.append(thead, tbody); scroll.appendChild(tabla);
        const pie = YS.el("div", { class: "ui-paginacion" });
        panel.append(scroll, pie);
        contenedor.append(barra, panel);

        thead.addEventListener("click", e => {
            const b = e.target.closest("[data-col]");
            if (!b) return;
            const col = parseInt(b.dataset.col);
            est.desc = est.col === col ? !est.desc : false;
            est.col = col; dibujar();
        });
        pie.addEventListener("click", e => {
            const b = e.target.closest("[data-pag]");
            if (b) { est.pagina += parseInt(b.dataset.pag); dibujar(); scroll.scrollTop = 0; }
        });

        function dibujar() {
            const pasaFiltro = f => !op.filtros || !est.filtro || (op.filtros.find(x => x.id === est.filtro)?.pasa(f) ?? true);
            const pasaBusqueda = f => !est.q || YS.normalizar(op.buscar(f)).includes(est.q);
            let vis = est.filas.filter(f => pasaFiltro(f) && pasaBusqueda(f));
            const c = op.columnas[est.col];
            if (c?.orden) {
                vis = vis.slice().sort((a, b) => {
                    const va = c.orden(a), vb = c.orden(b);
                    const r = va < vb ? -1 : va > vb ? 1 : 0;
                    return est.desc ? -r : r;
                });
            }
            if (filtros) {
                filtros.innerHTML = op.filtros.map(fl => {
                    const n = est.filas.filter(x => fl.pasa(x) && pasaBusqueda(x)).length;
                    const si = fl.id === est.filtro;
                    return `<button type="button" class="ui-filtro${si ? " activo" : ""}" data-filtro="${fl.id}" aria-pressed="${si}">${YS.esc(fl.texto)}<span class="ui-contador ui-contador--suave">${n}</span></button>`;
                }).join("");
            }
            thead.innerHTML = "<tr>" + op.columnas.map((col, i) => {
                const sort = est.col === i ? (est.desc ? "descending" : "ascending") : null;
                const flecha = est.col === i ? (est.desc ? " ↓" : " ↑") : "";
                return `<th class="${col.clase || ""}"${sort ? ` aria-sort="${sort}"` : ""}>${col.orden
                    ? `<button type="button" class="ui-th-orden" data-col="${i}">${YS.esc(col.titulo)}${flecha}</button>`
                    : YS.esc(col.titulo || "")}</th>`;
            }).join("") + "</tr>";
            const paginas = porPagina ? Math.max(1, Math.ceil(vis.length / porPagina)) : 1;
            est.pagina = Math.min(Math.max(1, est.pagina), paginas);
            const desde = porPagina ? (est.pagina - 1) * porPagina : 0;
            const pagina = porPagina ? vis.slice(desde, desde + porPagina) : vis;
            if (!pagina.length) {
                const v = est.filas.length
                    ? { icono: "buscar", titulo: "Sin resultados", texto: "Probá con otra búsqueda o sacá el filtro." }
                    : (op.vacio || { icono: "bandeja", titulo: "No hay registros para mostrar" });
                tbody.innerHTML = `<tr><td colspan="${op.columnas.length}">${YS.vacio(v)}</td></tr>`;
            } else {
                tbody.innerHTML = pagina.map(f => `<tr>${op.columnas.map(col => `<td class="${col.clase || ""}">${col.html(f)}</td>`).join("")}</tr>`).join("");
            }
            info.textContent = `${vis.length} ${vis.length === 1 ? (op.unidad?.[0] || "resultado") : (op.unidad?.[1] || "resultados")}`;
            pie.classList.toggle("oculto", !porPagina || vis.length <= porPagina);
            pie.innerHTML = `<span>${vis.length ? `${desde + 1}–${Math.min(desde + porPagina, vis.length)} de ${vis.length}` : ""}</span>
                <div class="ui-paginacion-botones">
                    <button type="button" class="ui-btn ui-btn--sm" data-pag="-1"${est.pagina <= 1 ? " disabled" : ""}>Anterior</button>
                    <span class="ui-paginacion-pagina">${est.pagina} / ${paginas}</span>
                    <button type="button" class="ui-btn ui-btn--sm" data-pag="1"${est.pagina >= paginas ? " disabled" : ""}>Siguiente</button>
                </div>`;
        }
        dibujar();
        return { actualizar(filas) { est.filas = filas || []; dibujar(); }, estado: est, buscador: input };
    };

    // ── Menús desplegables accesibles ───────────────────────────
    const abiertos = new Set();
    /**
     * Conecta un botón con un panel desplegable: aria-expanded, clic afuera, Esc,
     * flechas para moverse entre ítems. Solo uno abierto a la vez.
     */
    YS.desplegable = (boton, panel, op = {}) => {
        const items = () => [...panel.querySelectorAll(op.items || ".ui-desplegable-item, [role=menuitem]")].filter(i => !i.disabled && i.offsetParent !== null);
        const ctrl = {
            abierto: () => panel.classList.contains("abierto"),
            abrir(enfocar = false) {
                abiertos.forEach(c => c !== ctrl && c.cerrar());
                panel.classList.add("abierto");
                boton.setAttribute("aria-expanded", "true");
                abiertos.add(ctrl);
                op.alAbrir?.();
                if (enfocar) items()[0]?.focus();
            },
            cerrar(devolverFoco = false) {
                if (!ctrl.abierto()) return;
                panel.classList.remove("abierto");
                boton.setAttribute("aria-expanded", "false");
                abiertos.delete(ctrl);
                op.alCerrar?.();
                if (devolverFoco) boton.focus();
            },
            alternar() { ctrl.abierto() ? ctrl.cerrar() : ctrl.abrir(); },
            _contiene: t => panel.contains(t) || boton.contains(t)
        };
        boton.setAttribute("aria-expanded", "false");
        boton.setAttribute("aria-haspopup", op.tipo || "true");
        boton.addEventListener("click", e => { e.stopPropagation(); ctrl.alternar(); });
        boton.addEventListener("keydown", e => {
            if (e.key === "ArrowDown") { e.preventDefault(); ctrl.abrir(true); }
        });
        panel.addEventListener("keydown", e => {
            const lista = items();
            const i = lista.indexOf(document.activeElement);
            if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); ctrl.cerrar(true); }
            else if (e.key === "ArrowDown") { e.preventDefault(); lista[(i + 1) % lista.length]?.focus(); }
            else if (e.key === "ArrowUp") { e.preventDefault(); lista[(i - 1 + lista.length) % lista.length]?.focus(); }
            else if (e.key === "Home") { e.preventDefault(); lista[0]?.focus(); }
            else if (e.key === "End") { e.preventDefault(); lista[lista.length - 1]?.focus(); }
            else if (e.key === "Tab") ctrl.cerrar();
        });
        panel.addEventListener("click", e => {
            if (op.cerrarAlElegir !== false && e.target.closest(".ui-desplegable-item, [role=menuitem]")) ctrl.cerrar();
        });
        return ctrl;
    };
    document.addEventListener("click", e => {
        abiertos.forEach(c => { if (!c._contiene?.(e.target)) c.cerrar(); });
    });
    document.addEventListener("keydown", e => {
        if (e.key === "Escape" && abiertos.size && !pilaModales.length) abiertos.forEach(c => c.cerrar());
    });

    // ── Varios ──────────────────────────────────────────────────
    YS.debounce = (fn, ms = 250) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };
    YS.copiar = async (texto, aviso = "Copiado al portapapeles") => {
        try { await navigator.clipboard.writeText(texto); YS.toast(aviso); }
        catch { YS.toast("No se pudo copiar. Seleccioná el texto y copialo a mano.", "warn"); }
    };
    /** Lee un parámetro de la URL (?sala=...) */
    YS.param = nombre => new URLSearchParams(location.search).get(nombre);
})();
