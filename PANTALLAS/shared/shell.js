/* ═══════════════════════════════════════════════════════════════
   YoSoy · shell común de las pantallas con sesión

   Arma la barra superior según el rol: logo, menú con submenús,
   campana de avisos, menú de usuario y el encabezado de página (migas,
   título y acciones). Navegación por #hash: el menú son links, así que
   funcionan el botón Atrás, abrir en otra pestaña y los accesos directos.

   Uso en un panel:  YS.shell.iniciar({ rol: "profesional", navegar: (id, params) => activarTab(id) })
   Uso en una página suelta:  YS.shell.iniciar({ rol: "paciente", activo: "consentimiento" })
   Necesita shared/ui.js cargado antes.
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS;
    if (!YS) { console.error("shell.js necesita shared/ui.js"); return; }

    // ── Menús por rol: lo que cada rol ya puede hacer hoy ───────
    // Grupos de Configuración y Usuarios y la Auditoría: los comparten el menú del Configurador y el de Administración (una sola definición).
    const GRUPO_CONFIGURACION = {
        texto: "Configuración", items: [
            // Ítem de WhatsApp: vincular el número es una configuración independiente del chat
            // simulado (los avisos y las respuestas por WhatsApp pueden usarse con el simulado apagado).
            { id: "whatsapp", texto: "WhatsApp", titulo: "WhatsApp", icono: "telefono", desc: "Estado de la conexión y código QR" },
            // Chats v2: el interruptor GLOBAL de IA de los chats (arranca apagado). Solo el Configurador; el CRM puro no tiene Configuración.
            { id: "chatsia", texto: "Asistente de chats", titulo: "Asistente de chats", icono: "ia", desc: "Interruptor global de IA de los chats" },
            { id: "especialidades", texto: "Especialidades", icono: "etiqueta" }
        ]
    };
    const GRUPO_USUARIOS = {
        texto: "Usuarios", items: [
            { id: "altausuario", texto: "Registrar usuario", icono: "usuario-mas", desc: "Alta de alguien del equipo; le llega un mail para confirmar", titulo: "Registrar usuario del equipo" },
            { id: "permisos", texto: "Permisos", icono: "escudo", desc: "Módulos, permisos y privilegios de cada usuario" },
            { id: "usuarios", texto: "Usuarios", icono: "pacientes", desc: "Todos los usuarios: contraseña, permisos, asignaciones y bajas" },
            { id: "solicitudes", texto: "Solicitudes de registro", icono: "usuario-mas", desc: "Profesionales que pidieron cuenta" }
        ]
    };
    const ITEM_AUDITORIA = { id: "auditoria", texto: "Auditoría", titulo: "Auditoría administrativa" };

    const MENUS = {
        profesional: {
            etiqueta: "Panel profesional",
            items: [
                { id: "inicio", texto: "Inicio", titulo: "Inicio" },
                {
                    texto: "Agenda", items: [
                        { id: "agenda", texto: "Calendario", icono: "agenda", desc: "Turnos del día, la semana o el mes" },
                        { id: "horarios", texto: "Mis horarios de trabajo", icono: "reloj", desc: "Días y franjas en que atendés" }
                    ]
                },
                { id: "pacientes", texto: "Pacientes", titulo: "Mis pacientes" },
                { id: "casos", texto: "Mis casos", titulo: "Mis casos", icono: "portapapeles", desc: "Casos asignados a vos" },
                { id: "sesion", texto: "Sala de video", titulo: "Sala de video" },
                { id: "reportes", texto: "Reportes" }
            ],
            usuario: [{ id: "perfil", texto: "Mi perfil", icono: "usuario" }]
        },
        paciente: {
            etiqueta: "Mi espacio",
            items: [
                { id: "inicio", texto: "Inicio" },
                {
                    texto: "Turnos", items: [
                        { id: "agenda", texto: "Mis turnos", icono: "agenda", desc: "Pedir, cancelar o reprogramar" },
                        { id: "profesionales", texto: "Mis profesionales", icono: "pacientes", desc: "Tu equipo y cambio de profesional" }
                    ]
                },
                { id: "sesion", texto: "Sala de video" },
                { id: "admision", texto: "Admisión", titulo: "Informe de admisión" },
                { id: "consentimiento", texto: "Consentimiento", titulo: "Consentimiento informado", href: "/pantallas/Pantalla_Paciente/consentimiento.html" }
            ],
            usuario: [{ id: "perfil", texto: "Mis datos", icono: "usuario" }]
        },
        // Roles y permisos: el Configurador administra usuarios, permisos,
        // configuración y la auditoría administrativa. Nada clínico ni operativo.
        // Sin "Inicio" ni resumen: se aterriza en Usuarios (ver `inicial` en dashboard.js). Quedan
        // fuera del menú (su código, sus endpoints y sus datos siguen): Grupos, Reglas de derivación,
        // Coberturas, Privilegios (se asignan desde Permisos), Roles y Recuperación de contraseña.
        // Los chats del CRM no son de este módulo: quien tiene Configurador y CRM entra a Administración (ver más abajo).
        configurador: {
            etiqueta: "Panel configurador",
            items: [GRUPO_CONFIGURACION, GRUPO_USUARIOS, ITEM_AUDITORIA],
            usuario: []
        },
        // Roles y permisos: el grupo CRM que tenía el Configurador pasa a su propio módulo. Las
        // conversaciones (Chats, Escalamientos, Etiquetas) se suman cuando la mensajería está
        // disponible (GRUPO_CHATS); Agendas, solo con el privilegio Gestión de agendas.
        // Vista única (Administración = Configurador + CRM): CRM ▾, Configuración ▾, Usuarios ▾ y Auditoría, sin Inicio. Los ítems de chats (Chats, Leads,
        // Etiquetas, Escalamientos) se suman al principio del grupo CRM cuando la mensajería está disponible (ver grupoDeChats); Turnos y Agendas, con el
        // privilegio Gestión de agendas (ver menuPara). Grupos, Reglas de derivación, Coberturas, Privilegios, Roles, Recuperación e Inicio siguen fuera del menú.
        administracion: {
            etiqueta: "Administración",
            items: [
                {
                    texto: "CRM", items: [
                        { id: "casos", texto: "Casos", icono: "portapapeles", desc: "Casos derivados a un grupo y profesional" },
                        { id: "casosderivaciones", texto: "Derivaciones", icono: "flechas", desc: "Pendientes de confirmar un grupo y profesional" }
                    ]
                },
                GRUPO_CONFIGURACION,
                GRUPO_USUARIOS,
                ITEM_AUDITORIA
            ],
            usuario: []
        },
        crm: {
            etiqueta: "CRM y atención al cliente",
            items: [
                { id: "inicio", texto: "Inicio" },
                {
                    texto: "Casos", items: [
                        { id: "casos", texto: "Casos", icono: "portapapeles", desc: "Casos derivados a un grupo y profesional" },
                        // id "casosderivaciones", no "derivaciones": ese id es Admisión > Solicitudes
                        // de cambio (tabla Derivacion, otro significado — ver CAMBIOS_AGUS.md, Fase 2).
                        { id: "casosderivaciones", texto: "Derivaciones", icono: "flechas", desc: "Pendientes de confirmar un grupo y profesional" }
                    ]
                }
            ],
            usuario: []
        }
    };
    // Conversaciones del módulo CRM: se agregan al menú solo si la mensajería está disponible
    // (mismo control que el botón de chats), ver agregarGrupoChats().
    const GRUPO_CHATS = {
        texto: "Conversaciones", items: [
            { id: "chats", texto: "Chats", icono: "chat", desc: "Conversaciones de WhatsApp con el equipo" },
            { id: "escalamientos", texto: "Escalamientos", icono: "alerta", desc: "Pidieron hablar con una persona del equipo" },
            { id: "etiquetas", texto: "Etiquetas de chats", icono: "etiqueta", desc: "Catálogo de etiquetas para conversaciones" }
        ]
    };
    // Administración: los ítems de conversaciones que se suman AL PRINCIPIO del grupo CRM (Chats y Leads primero).
    const ITEMS_CHATS_ADMINISTRACION = {
        texto: "CRM", enGrupo: true, items: [
            { id: "chats", texto: "Chats", icono: "chat", desc: "Conversaciones de WhatsApp con el equipo" },
            { id: "leads", texto: "Leads", icono: "pacientes", desc: "Todas las conversaciones: pacientes y personas sin identificar" },
            { id: "etiquetas", texto: "Etiquetas", icono: "etiqueta", desc: "Catálogo de etiquetas para conversaciones" },
            { id: "escalamientos", texto: "Escalamientos", icono: "alerta", desc: "Pidieron hablar con una persona del equipo" }
        ]
    };
    const GRUPO_AGENDAS = {
        texto: "Agendas", items: [
            { id: "turnos", texto: "Turnos", icono: "reloj", desc: "Turnos de todos los profesionales" },
            { id: "agendas", texto: "Horarios", icono: "agenda", desc: "Horarios de trabajo de los profesionales" }
        ]
    };
    // Módulo Profesional: lo que suma cada permiso o privilegio. Estas secciones viven en el
    // panel de gestión (pagina: "gestion"); el resto del menú, en el panel profesional.
    const GRUPO_ADMISION = {
        texto: "Admisión", items: [
            { id: "admisiones", texto: "Admisiones", icono: "portapapeles", desc: "Informes y primera entrevista", pagina: "gestion" },
            { id: "derivaciones", texto: "Solicitudes de cambio", icono: "flechas", desc: "Pedidos de cambio de profesional", pagina: "gestion" },
            { id: "asignaciones", texto: "Asignaciones", icono: "pacientes", desc: "Qué profesional atiende a cada paciente", pagina: "gestion" }
        ]
    };
    const GRUPO_AUDITOR = {
        texto: "Auditoría", items: [
            { id: "reportesgenerales", texto: "Reportes", icono: "reportes", desc: "Atenciones de toda la organización", titulo: "Reportes de la organización", pagina: "gestion" },
            { id: "evoluciones", texto: "Evoluciones", icono: "tendencia", desc: "Todas, solo lectura", pagina: "gestion" },
            { id: "eventoscrisis", texto: "Eventos de crisis", icono: "alerta", desc: "Todos, solo lectura", pagina: "gestion" },
            { id: "interaccionesia", texto: "Interacciones IA", icono: "ia", desc: "Todas, solo lectura", pagina: "gestion" },
            { id: "auditoriaclinica", texto: "Accesos clínicos", icono: "lista", desc: "Quién abrió qué historia clínica", titulo: "Auditoría clínica", pagina: "gestion" }
        ]
    };

    // El menú de cada módulo según los permisos y privilegios de la sesión. Es solo lo que se
    // muestra: cada endpoint vuelve a controlar el permiso en el servidor.
    function menuPara(rol) {
        const base = MENUS[rol];
        if (!base) return null;
        const items = base.items.map(it => it.items ? { ...it, items: [...it.items] } : { ...it });
        if (rol === "profesional") {
            const pagina = it => it.items ? { ...it, items: it.items.map(s => ({ pagina: "profesional", ...s })) } : { pagina: "profesional", ...it };
            const propios = items.map(pagina);
            if (YS.sesion.tienePrivilegio("admision")) propios.push(GRUPO_ADMISION);
            if (YS.sesion.tienePermiso("auditor")) propios.push(GRUPO_AUDITOR);
            return { ...base, items: propios };
        }
        if (rol === "crm" && YS.sesion.tienePrivilegio("gestion_agendas")) items.push(GRUPO_AGENDAS);
        if (rol === "administracion" && YS.sesion.tienePrivilegio("gestion_agendas")) {
            // Turnos y Agendas van dentro del grupo CRM, después de Derivaciones.
            const crm = items.find(it => it.texto === "CRM");
            crm.items.push({ ...GRUPO_AGENDAS.items[0] }, { ...GRUPO_AGENDAS.items[1], texto: "Agendas" });
        }
        return { ...base, items };
    }
    // El grupo de conversaciones que corresponde a este módulo, o null si no tiene chats: solo el módulo CRM y Administración (Configurador +
    // CRM). Ni el profesional ni el Configurador solo (el servidor responde 403).
    function grupoDeChats(rol) {
        if (rol === "crm") return GRUPO_CHATS;
        // Administración: los chats entran DENTRO del grupo CRM (enGrupo), primero Chats y Leads.
        if (rol === "administracion") return ITEMS_CHATS_ADMINISTRACION;
        return null;
    }
    // Dónde entra un grupo agregado "al principio": después de Inicio si el menú lo tiene, o primero si no.
    const posicionInicial = items => items.some(it => it.id === "inicio") ? 1 : 0;
    const ROL_TEXTO = { paciente: "Paciente", profesional: "Profesional", configurador: "Configurador", crm: "CRM y atención al cliente", administracion: "Administración" };
    const LOGO = "/pantallas/Pantalla_Usuario/Usuario/img/logo_SINFONDO.png";

    let cfg = null, menu = null, actual = null, primeraMarca = true;
    let hashMostrado = null;   // el #hash de lo que está en pantalla (sección + parámetros)
    const els = {};
    let resolverUsuario;
    const usuario = new Promise(r => resolverUsuario = r);   // { nombre, detalle } para saludar en Inicio
    let capacidades = Promise.resolve({ mensajeria: false });
    const tituloBase = { texto: "YoSoy", contador: 0 };

    const plano = () => {
        const out = [];
        menu.items.forEach(it => it.items ? it.items.forEach(s => out.push({ ...s, grupo: it })) : out.push(it));
        (menu.usuario || []).forEach(u => out.push({ ...u, deUsuario: true }));
        return out;
    };
    const buscar = id => plano().find(i => i.id === id);
    // Roles y permisos: un ítem puede vivir en otra pantalla del mismo módulo (pagina: "gestion" o
    // "profesional"); ahí el link va a esa pantalla en vez de cambiar el #hash de esta.
    const esLocal = item => !item.href && (!item.pagina || item.pagina === cfg.pagina);
    const enlace = item => item.href
        || (!esLocal(item) ? YS.rutas[item.pagina] + "#" + item.id
        : cfg.navegar ? "#" + item.id : YS.rutas[cfg.rol] + "#" + item.id);
    const leerHash = () => {
        const [id, q] = decodeURIComponent(location.hash.slice(1)).split("?");
        return { id, params: Object.fromEntries(new URLSearchParams(q || "")) };
    };

    // ── Construcción de la barra ────────────────────────────────
    function crearBarra() {
        const barra = YS.el("header", { class: "ui-topbar" });
        // Sin "Inicio" en el menú (Configurador), la marca lleva al aterrizaje de esta pantalla (cfg.inicial).
        const inicio = buscar("inicio") || buscar(cfg.inicial) || plano().find(i => esLocal(i) && !i.deUsuario);
        barra.appendChild(YS.el("a", {
            class: "ui-marca", href: enlace(inicio), "aria-label": "YoSoy, ir al inicio",
            html: `<img src="${LOGO}" alt=""><span class="ui-marca-nombre">Yo<b>Soy</b></span>`
        }));

        // Menú horizontal
        const nav = YS.el("nav", { class: "ui-menu", "aria-label": "Secciones" });
        const ul = YS.el("ul", { role: "menubar" });
        menu.items.forEach(it => ul.appendChild(it.items ? crearGrupo(it) : YS.el("li", { role: "none" },
            YS.el("a", { class: "ui-menu-item", role: "menuitem", href: enlace(it), dataset: { id: it.id }, text: it.texto }))));
        nav.appendChild(ul);
        barra.appendChild(nav);
        teclasMenu(ul);

        // Menú compacto para ventanas angostas (todo en un desplegable)
        const compacto = YS.el("div", { class: "ui-menu-compacto" });
        const btnCompacto = YS.el("button", { class: "ui-menu-item", type: "button", html: `Menú ${YS.icono("chevron", "ui-chevron")}` });
        const panelCompacto = YS.el("div", { class: "ui-desplegable", role: "menu" });
        menu.items.forEach(it => {
            if (it.items) {
                panelCompacto.appendChild(YS.el("div", { class: "ui-desplegable-titulo", text: it.texto }));
                it.items.forEach(s => panelCompacto.appendChild(itemDesplegable(s)));
            } else panelCompacto.appendChild(itemDesplegable(it));
        });
        compacto.append(btnCompacto, panelCompacto);
        YS.desplegable(btnCompacto, panelCompacto, { tipo: "menu" });
        barra.appendChild(compacto);

        // Derecha: botones (chats, campana) y usuario
        const derecha = YS.el("div", { class: "ui-topbar-derecha" });
        els.botones = YS.el("div", { class: "ui-flex ui-topbar-botones" });
        derecha.append(els.botones, YS.el("span", { class: "ui-topbar-sep", "aria-hidden": "true" }), crearUsuario());
        barra.appendChild(derecha);
        return barra;
    }

    function itemDesplegable(s) {
        return YS.el("a", {
            class: "ui-desplegable-item", role: "menuitem", href: enlace(s), dataset: { id: s.id },
            html: (s.icono ? YS.icono(s.icono) : "") +
                `<span class="ui-desplegable-texto">${YS.esc(s.texto)}${s.desc ? `<small>${YS.esc(s.desc)}</small>` : ""}</span>`
        });
    }

    function crearGrupo(grupo) {
        const li = YS.el("li", { role: "none", class: "ui-menu-grupo" });
        const btn = YS.el("button", {
            class: "ui-menu-item", type: "button", role: "menuitem",
            html: `${YS.esc(grupo.texto)} ${YS.icono("chevron", "ui-chevron")}`
        });
        const panel = YS.el("div", { class: "ui-desplegable", role: "menu", "aria-label": grupo.texto });
        grupo.items.forEach(s => panel.appendChild(itemDesplegable(s)));
        li.append(btn, panel);
        const ctrl = YS.desplegable(btn, panel, { tipo: "menu" });
        // También abre al pasar el mouse (con una pequeña demora para no abrir de pasada)
        let tAbrir, tCerrar, abiertoPorHover = 0;
        li.addEventListener("pointerenter", e => {
            if (e.pointerType !== "mouse") return;
            clearTimeout(tCerrar);
            tAbrir = setTimeout(() => { if (!ctrl.abierto()) { ctrl.abrir(); abiertoPorHover = Date.now(); } }, 90);
        });
        li.addEventListener("pointerleave", e => {
            if (e.pointerType !== "mouse") return;
            clearTimeout(tAbrir);
            tCerrar = setTimeout(() => ctrl.cerrar(), 220);
        });
        btn.addEventListener("click", e => {
            // El clic justo después de abrirlo con el mouse no lo vuelve a cerrar.
            if (Date.now() - abiertoPorHover < 450) { e.stopImmediatePropagation(); ctrl.abrir(); }
        }, true);
        grupo._btn = btn;
        return li;
    }

    // Flechas izquierda/derecha entre los ítems de primer nivel
    function teclasMenu(ul) {
        ul.addEventListener("keydown", e => {
            if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
            const tops = [...ul.querySelectorAll(":scope > li > .ui-menu-item")];
            const i = tops.indexOf(document.activeElement);
            if (i < 0) return;
            e.preventDefault();
            tops[(i + (e.key === "ArrowRight" ? 1 : -1) + tops.length) % tops.length].focus();
        });
    }

    function crearUsuario() {
        const cont = YS.el("div", { class: "ui-usuario-menu" });
        const btn = YS.el("button", { class: "ui-usuario-btn", type: "button", "aria-label": "Menú de usuario" });
        btn.innerHTML = `${YS.avatar(ROL_TEXTO[cfg.rol], cfg.rol)}<span class="ui-usuario-texto"><span class="ui-usuario-nombre">${ROL_TEXTO[cfg.rol]}</span><span class="ui-usuario-rol">${ROL_TEXTO[cfg.rol]}</span></span>${YS.icono("chevron", "ui-chevron")}`;
        const panel = YS.el("div", { class: "ui-desplegable ui-desplegable--derecha", role: "menu" });
        els.usuarioCabecera = YS.el("div", { class: "ui-desplegable-cabecera" });
        panel.appendChild(els.usuarioCabecera);
        (menu.usuario || []).forEach(u => panel.appendChild(itemDesplegable(u)));
        if ((menu.usuario || []).length) panel.appendChild(YS.el("div", { class: "ui-desplegable-sep" }));
        // Etapa 2: quien tiene más de un módulo (por ejemplo, Profesional y Configurador)
        // cambia sin volver a iniciar sesión. El servidor vuelve a comprobar el permiso.
        const otrosModulos = cfg.rol === "paciente" ? [] : YS.sesion.modulos().filter(m => m.clave !== YS.sesion.modulo());
        if (otrosModulos.length) {
            panel.appendChild(YS.el("button", {
                class: "ui-desplegable-item", type: "button", role: "menuitem",
                html: YS.icono("flechas") + '<span class="ui-desplegable-texto">Cambiar de módulo</span>',
                on: { click: () => YS.shell.elegirModulo(otrosModulos) }
            }));
            panel.appendChild(YS.el("div", { class: "ui-desplegable-sep" }));
        }
        panel.appendChild(YS.el("button", {
            class: "ui-desplegable-item ui-desplegable-item--peligro", type: "button", role: "menuitem",
            html: YS.icono("salir") + '<span class="ui-desplegable-texto">Cerrar sesión</span>',
            on: { click: () => YS.sesion.cerrar() }
        }));
        cont.append(btn, panel);
        YS.desplegable(btn, panel, { tipo: "menu" });
        els.usuarioBtn = btn;
        mostrarUsuario(ROL_TEXTO[cfg.rol]);
        cargarNombre();
        return cont;
    }

    function mostrarUsuario(nombre, detalle) {
        els.usuarioBtn.querySelector(".ui-avatar").outerHTML = YS.avatar(nombre, nombre);
        els.usuarioBtn.querySelector(".ui-usuario-nombre").textContent = nombre;
        els.usuarioCabecera.innerHTML =
            `<div class="texto-fuerte">${YS.esc(nombre)}</div><div class="texto-chico ui-muted">${YS.esc(detalle || ROL_TEXTO[cfg.rol])}</div>`;
    }

    // Nombre del usuario para la barra (se guarda en la sesión del navegador para no pedirlo en cada pantalla)
    async function cargarNombre() {
        const clave = `ys-nombre-${cfg.rol}-${YS.sesion.idUsuario()}`;
        try {
            const guardado = JSON.parse(sessionStorage.getItem(clave) || "null");
            if (guardado) { mostrarUsuario(guardado.nombre, guardado.detalle); resolverUsuario(guardado); return; }
        } catch { }
        try {
            let nombre = null, detalle = null;
            if (cfg.rol === "profesional") {
                const p = await YS.api(`/profesionales/${YS.sesion.idProfesional()}`);
                nombre = YS.nombreCompleto(p); detalle = p?.matricula ? `Profesional · ${p.matricula}` : null;
            } else if (cfg.rol === "paciente") {
                const p = await YS.api(`/pacientes/${YS.sesion.idPaciente()}`);
                nombre = YS.nombreCompleto(p);
            } else {
                // Roles y permisos: /sesion/yo sirve para cualquier módulo (CRM no puede leer /usuarios).
                const u = await YS.api("/sesion/yo");
                nombre = u?.username; detalle = ROL_TEXTO[cfg.rol];
            }
            if (!nombre) { resolverUsuario({ nombre: null }); return; }
            mostrarUsuario(nombre, detalle);
            resolverUsuario({ nombre, detalle });
            try { sessionStorage.setItem(clave, JSON.stringify({ nombre, detalle })); } catch { }
        } catch { resolverUsuario({ nombre: null }); /* sin nombre queda el rol */ }
    }

    function crearEncabezado() {
        const enc = YS.el("div", { class: "ui-encabezado" });
        enc.innerHTML = `
            <div class="ui-encabezado-interior">
                <div class="ui-encabezado-fila">
                    <div class="ui-encabezado-textos"><h1 class="ui-encabezado-titulo"></h1><p class="ui-encabezado-sub oculto"></p></div>
                    <div class="ui-encabezado-acciones"></div>
                </div>
            </div>`;
        els.titulo = enc.querySelector(".ui-encabezado-titulo");
        els.sub = enc.querySelector(".ui-encabezado-sub");
        els.acciones = enc.querySelector(".ui-encabezado-acciones");
        return enc;
    }

    // ── Sección activa ──────────────────────────────────────────
    function marcar(id, op = {}) {
        actual = id;
        const item = buscar(id);
        els.barra.querySelectorAll("[data-id]").forEach(a => {
            const si = a.dataset.id === id;
            a.classList.toggle("activo", si);
            si ? a.setAttribute("aria-current", "page") : a.removeAttribute("aria-current");
        });
        menu.items.forEach(g => g._btn?.classList.toggle("activo", !!g.items.find(s => s.id === id)));

        const titulo = op.titulo || item?.titulo || item?.texto || "";
        encabezado({ titulo, subtitulo: op.subtitulo || "" });
        acciones([]);

        if (cfg.navegar && leerHash().id !== id) {
            const url = location.pathname + location.search + "#" + id;
            if (primeraMarca) history.replaceState(null, "", url); else history.pushState(null, "", url);
        }
        if (cfg.navegar) hashMostrado = location.hash;
        primeraMarca = false;
    }

    // Sin migas de pan a propósito: la navegación se entiende por el menú superior (su ítem
    // activo queda resaltado en marcar(), con un mecanismo aparte de este). Lo único que
    // queda acá es el título de la pantalla y, si la pantalla lo pidió, un subtítulo corto.
    function encabezado({ titulo, subtitulo } = {}) {
        if (titulo != null) {
            els.titulo.textContent = titulo;
            tituloBase.texto = titulo ? `${titulo} · YoSoy` : "YoSoy";
            actualizarTitulo();
        }
        if (subtitulo != null) {
            els.sub.textContent = subtitulo;
            els.sub.classList.toggle("oculto", !subtitulo);
        }
    }

    /** Botones del encabezado: [{ texto, tipo: "primary"|"danger"|..., icono, alClick, id, titulo }] o nodos. */
    function acciones(lista = []) {
        els.acciones.replaceChildren(...lista.map(a => {
            if (a instanceof Node) return a;
            return YS.el("button", {
                class: "ui-btn" + (a.tipo ? " ui-btn--" + a.tipo : ""), type: "button", id: a.id || null, title: a.titulo || null,
                html: (a.icono ? YS.icono(a.icono) : "") + YS.esc(a.texto), on: { click: e => a.alClick?.(e) }
            });
        }));
    }

    function actualizarTitulo() {
        document.title = (tituloBase.contador ? `(${tituloBase.contador}) ` : "") + tituloBase.texto;
    }

    // ── Botones de la barra (campana y chats) ───────────────────
    /** Agrega un botón de ícono a la derecha. Devuelve { boton, contador(n, peligro) }. */
    function boton({ id, icono, etiqueta, alClick, antes = false }) {
        const b = YS.el("button", { class: "ui-btn-icono", type: "button", id, "aria-label": etiqueta, title: etiqueta, html: YS.icono(icono) });
        if (alClick) b.addEventListener("click", alClick);
        antes ? els.botones.prepend(b) : els.botones.appendChild(b);
        return {
            boton: b,
            contador(n, peligro = false) {
                let c = b.querySelector(".ui-contador");
                if (!n) { c?.remove(); b.setAttribute("aria-label", etiqueta); return; }
                if (!c) { c = YS.el("span", { class: "ui-contador", "aria-hidden": "true" }); b.appendChild(c); }
                c.textContent = n > 99 ? "99+" : String(n);
                c.classList.toggle("ui-contador--peligro", !!peligro);
                b.setAttribute("aria-label", `${etiqueta}, ${n} sin leer`);
            }
        };
    }

    // ── Campana de avisos por rol ───────────────────────────────
    const AVISOS = {
        // Notificaciones del paciente (mismo comportamiento que la campanita anterior:
        // al abrir la casilla se marcan como leídas).
        paciente: {
            titulo: "Notificaciones",
            async cargar() {
                const lista = await YS.api(`/pacientes/${YS.sesion.idPaciente()}/notificaciones`);
                return {
                    noLeidos: lista.filter(n => n.leida === false).length,
                    items: lista.map(n => ({
                        id: n.idNotificacion, titulo: n.titulo, texto: n.mensaje, fecha: n.fecha, noLeido: n.leida === false,
                        accion: n.link ? { texto: "Unirse / Ver sala", alClick: () => unirseSala(n.link, n.idNotificacion) } : null
                    }))
                };
            },
            async alAbrir(datos) {
                for (const it of datos.items.filter(i => i.noLeido)) {
                    try { await YS.fetch(`/notificaciones/${it.id}/leida`, { method: "PUT" }); } catch { }
                }
                document.dispatchEvent(new CustomEvent("ys:avisos-leidos"));
                return true;   // vuelve a cargar el contador
            },
            vacio: "No tenés notificaciones."
        },
        // Turnos confirmados por el paciente que todavía no tienen sala (antes eran avisos flotantes).
        profesional: {
            titulo: "Avisos",
            async cargar() {
                const lista = await YS.api(`/profesionales/${YS.sesion.idProfesional()}/turnos-confirmados`);
                const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
                const sinSala = lista.filter(t => !t.codigoSala && fechaDMY(t.fecha) >= hoy);
                // Derivaciones internas: lo que me toca responder y los avisos de derivación / traspaso (si la API todavía no los tiene, la campanita sigue como siempre).
                const clinicos = await YS.api(`/profesionales/${YS.sesion.idProfesional()}/avisos-clinicos`).catch(() => ({ avisos: [], porResponder: [] }));
                const porResponder = (clinicos.porResponder || []).map(d => ({
                    id: "deriv-" + d.idDerivacion, titulo: "Derivación pendiente de tu respuesta", noLeido: true,
                    texto: `${d.nombreOrigen} te derivó el caso de ${d.nombrePaciente}. Motivo: ${d.motivo || "—"}${d.observaciones ? ` · Observaciones: ${d.observaciones}` : ""}`,
                    fecha: d.fecha,
                    acciones: [
                        { texto: "Aceptar", tipo: "primary", alClick: () => responderDerivacion(d.idDerivacion, true) },
                        { texto: "Rechazar", alClick: () => responderDerivacion(d.idDerivacion, false) }
                    ]
                }));
                // «derivacion-pendiente» ya figura arriba con sus botones: acá solo el resto (enviada, aceptada, rechazada, traspaso confirmado).
                const avisos = (clinicos.avisos || []).filter(a => a.tipo !== "derivacion-pendiente").map(a => ({
                    id: "aviso-" + a.idAviso, titulo: a.titulo, texto: a.mensaje, fecha: a.fecha ? String(a.fecha).slice(0, 10) : null, noLeido: a.leida === false,
                    accion: a.leida === false ? { texto: "Entendido", alClick: () => marcarAvisoClinico(a.idAviso) } : null
                }));
                return {
                    noLeidos: sinSala.length + porResponder.length + avisos.filter(a => a.noLeido).length,
                    items: [
                        ...porResponder,
                        ...avisos,
                        ...sinSala.map(t => ({
                            id: t.idTurno, titulo: "Turno confirmado: falta la sala", noLeido: true,
                            texto: `${t.nombrePaciente} · ${t.fecha} ${t.hora} hs`,
                            accion: { texto: "Generar sala", tipo: "primary", alClick: () => generarSala(t.idTurno) }
                        }))
                    ]
                };
            },
            vacio: "No hay avisos por ahora.",
            recordatorio: n => `Tenés ${n} aviso${n === 1 ? "" : "s"} sin leer (turnos sin sala o derivaciones).`
        }
    };

    const fechaDMY = s => { const [d, m, a] = String(s).split("/").map(Number); return new Date(a, m - 1, d); };

    async function unirseSala(codigo, idNotif) {
        try { await YS.fetch(`/notificaciones/${idNotif}/leida`, { method: "PUT" }); } catch { }
        location.href = `/pantallas/Pantalla_Paciente/sesion_video.html?sala=${encodeURIComponent(codigo)}&idPaciente=${YS.sesion.idPaciente()}`;
    }

    // El profesional de destino acepta o rechaza una derivación. Aceptar NO cambia ningún vínculo: Admisión confirma el traspaso después.
    async function responderDerivacion(idDerivacion, acepta) {
        let motivo = null;
        if (!acepta && YS.pedirTexto) {
            motivo = await YS.pedirTexto({ titulo: "Rechazar la derivación", mensaje: "Admisión va a decidir cómo seguir. Contale el motivo (opcional).", etiqueta: "Motivo", multilinea: true, maximo: 300, textoConfirmar: "Rechazar" });
            if (motivo === null) return;   // canceló
        }
        try {
            await YS.api(`/derivaciones/${idDerivacion}/responder`, { method: "PUT", json: { acepta, motivo: motivo || null } });
            YS.toast(acepta ? "Aceptaste la derivación. Admisión va a confirmar el traspaso." : "Rechazaste la derivación. Se avisó a Admisión y al profesional que derivó.");
            campana.refrescar();
        } catch (e) { YS.toast(e.message, "error"); }
    }

    async function marcarAvisoClinico(idAviso) {
        try { await YS.fetch(`/avisos-clinicos/${idAviso}/leida`, { method: "PUT" }); } catch { }
        campana.refrescar();
    }

    async function generarSala(idTurno) {
        try {
            const r = await YS.api(`/turnos/${idTurno}/generar-sala`, { method: "PUT" });
            YS.toast(`Sala generada: ${r.codigoSala}. El paciente fue notificado.`);
            campana.refrescar();
            document.dispatchEvent(new CustomEvent("ys:sala-generada", { detail: { idTurno, codigoSala: r.codigoSala } }));
        } catch (e) { YS.toast(e.message, "error"); }
    }

    const campana = {
        _datos: null,
        iniciar(def) {
            this.def = def;
            const cont = YS.el("div", { class: "ui-campana" });
            const b = boton({ id: "btnCampana", icono: "campana", etiqueta: def.titulo });
            b.boton.remove();
            this.panel = YS.el("div", { class: "ui-desplegable ui-desplegable--derecha ui-avisos", role: "dialog", "aria-label": def.titulo });
            this.panel.innerHTML = `<div class="ui-avisos-cabecera">${YS.esc(def.titulo)}</div><div class="ui-avisos-lista"></div>`;
            cont.append(b.boton, this.panel);
            els.botones.appendChild(cont);
            this.b = b;
            this.lista = this.panel.querySelector(".ui-avisos-lista");
            this.ctrl = YS.desplegable(b.boton, this.panel, {
                tipo: "dialog", cerrarAlElegir: false, items: "button, a",
                alAbrir: () => this.abrir()
            });
            this.refrescar(true);
            // Sin websockets: se revisa cada minuto, y nunca con la pestaña oculta.
            setInterval(() => { if (!document.hidden && !this.ctrl.abierto()) this.refrescar(); }, 60000);
            document.addEventListener("visibilitychange", () => { if (!document.hidden) this.refrescar(); });
        },
        async refrescar(primera = false) {
            try {
                this._datos = await this.def.cargar();
                this.b.contador(this._datos.noLeidos);
                if (this.ctrl.abierto()) this.dibujar();
                if (primera && this._datos.noLeidos && this.def.recordatorio) {
                    const clave = "ys-recordatorio-" + cfg.rol;
                    if (!sessionStorage.getItem(clave)) {
                        sessionStorage.setItem(clave, "1");
                        YS.toast(this.def.recordatorio(this._datos.noLeidos), "info", { accion: { texto: "Ver", alClick: () => this.ctrl.abrir() } });
                    }
                }
            } catch { this.b.contador(0); }
        },
        async abrir() {
            this.lista.innerHTML = YS.cargando();
            if (!this._datos) await this.refrescar();
            this.dibujar();
            if (this.def.alAbrir && this._datos?.noLeidos) {
                const datos = this._datos;
                if (await this.def.alAbrir(datos)) { this.b.contador(0); }
            }
        },
        dibujar() {
            const d = this._datos;
            if (!d) { this.lista.innerHTML = YS.errorCarga({ texto: "No se pudieron cargar los avisos." }); return; }
            if (!d.items.length) { this.lista.innerHTML = YS.vacio({ icono: "campana", texto: this.def.vacio }); return; }
            this.lista.replaceChildren(...d.items.map(it => {
                const div = YS.el("div", { class: "ui-aviso" + (it.noLeido ? " no-leido" : "") + (it.urgente ? " urgente" : "") });
                div.innerHTML = `<span class="ui-aviso-punto" aria-hidden="true"></span>
                    <div class="ui-aviso-cuerpo">
                        <div class="ui-aviso-titulo">${YS.esc(it.titulo)}</div>
                        <div>${YS.esc(it.texto)}</div>
                        ${it.fecha ? `<div class="ui-aviso-fecha">${YS.esc(it.fecha)}</div>` : ""}
                    </div>`;
                const acciones = it.acciones || (it.accion ? [it.accion] : []);
                if (acciones.length) {
                    div.querySelector(".ui-aviso-cuerpo").appendChild(YS.el("div", { class: "ui-aviso-acciones" },
                        acciones.map(a => YS.el("button", { class: "ui-btn ui-btn--sm" + (a.tipo ? " ui-btn--" + a.tipo : ""), type: "button", text: a.texto, on: { click: a.alClick } }))));
                }
                return div;
            }));
        }
    };

    // La bandeja vive en Pantalla_ChatSimulado (con el módulo apagado esa carpeta responde 404).
    // caps: lo que dijo /capacidades ({ mensajeria, modoAtencion, canalActivo }); el modo le dice a la bandeja cómo rotular y el canal, por dónde salen las respuestas.
    function cargarBandeja(caps) {
        const opciones = { ...(cfg.chats || {}), modoAtencion: caps?.modoAtencion, canalActivo: caps?.canalActivo };
        if (YS.bandeja) { YS.bandeja.iniciar(opciones); return; }
        document.head.appendChild(YS.el("link", { rel: "stylesheet", href: YS.rutas.chats + "css/bandeja.css" }));
        // El módulo compartido que traduce el estado de WhatsApp para el aviso de la bandeja; si no carga, la bandeja funciona igual, sin ese aviso.
        const cargar = (src, alCargar) => { const s = YS.el("script", { src }); s.onload = alCargar; s.onerror = alCargar; document.body.appendChild(s); };
        const conBandeja = () => cargar(YS.rutas.chats + "js/bandeja.js", () => YS.bandeja?.iniciar(opciones));
        if (YS.whatsappEstado) conBandeja(); else cargar("/pantallas/shared/whatsapp-estado.js", conBandeja);
    }

    // Suma ítems AL PRINCIPIO de un grupo que ya está dibujado (Administración: Chats y Leads dentro de CRM ▾), en la barra y en el menú compacto.
    function agregarItemsAlGrupo(grupoItems) {
        const grupo = menu.items.find(it => it.items && it.texto === grupoItems.texto);
        if (!grupo || grupoItems.items.every(s => grupo.items.some(x => x.id === s.id))) return;
        grupo.items.unshift(...grupoItems.items);
        const panel = grupo._btn?.nextElementSibling;
        [...grupoItems.items].reverse().forEach(s => panel?.insertBefore(itemDesplegable(s), panel.firstChild));
        const compacto = els.barra.querySelector(".ui-menu-compacto .ui-desplegable");
        const titulo = compacto && [...compacto.children].find(n => n.classList.contains("ui-desplegable-titulo") && n.textContent === grupo.texto);
        [...grupoItems.items].reverse().forEach(s => titulo?.after(itemDesplegable(s)));
    }

    // Suma un grupo al menú ya dibujado, sin reconstruir la barra (las conversaciones llegan
    // después, cuando /capacidades confirma que la mensajería está disponible).
    function agregarGrupo(grupo, alPrincipio = false) {
        if (menu.items.includes(grupo)) return;
        const ul = els.barra.querySelector(".ui-menu > ul");
        const panelCompacto = els.barra.querySelector(".ui-menu-compacto .ui-desplegable");
        const frag = document.createDocumentFragment();
        frag.appendChild(YS.el("div", { class: "ui-desplegable-titulo", text: grupo.texto }));
        grupo.items.forEach(s => frag.appendChild(itemDesplegable(s)));
        if (alPrincipio) {
            // después de "Inicio", que queda siempre primero (o primero de todo si el menú no tiene Inicio)
            const pos = posicionInicial(menu.items);
            menu.items.splice(pos, 0, grupo);
            ul?.children[pos] ? ul.insertBefore(crearGrupo(grupo), ul.children[pos]) : ul?.appendChild(crearGrupo(grupo));
            panelCompacto?.children[pos] ? panelCompacto.insertBefore(frag, panelCompacto.children[pos]) : panelCompacto?.appendChild(frag);
        } else {
            menu.items.push(grupo);
            ul?.appendChild(crearGrupo(grupo));
            panelCompacto?.appendChild(frag);
        }
    }

    // ── API pública ─────────────────────────────────────────────
    YS.shell = {
        /**
         * cfg: { rol, navegar(id, params), inicial = "inicio", activo }
         * - navegar: en los paneles (dashboard). El shell lee el #hash y llama a navegar.
         * - activo: en páginas sueltas (consentimiento, sesión de video), qué ítem marcar.
         */
        iniciar(opciones) {
            cfg = { inicial: "inicio", ...opciones };
            // En qué pantalla estamos (roles y permisos): el panel profesional o el de gestión, que
            // comparten el módulo Profesional (Admisión y Auditoría viven en el de gestión).
            cfg.pagina = cfg.pagina || (cfg.rol === "profesional" ? "profesional" : "gestion");
            // Páginas que se abren desde un link (consentimiento): sin sesión muestran solo la marca
            // y la página explica que hay que ingresar, en vez de mandar directo al login.
            if (cfg.opcional && !localStorage.getItem({ paciente: "idPaciente", profesional: "idProfesional" }[cfg.rol] || "idUsuario")) {
                document.body.classList.add("ui-con-shell");
                els.encabezado = crearEncabezado();
                document.body.prepend(YS.el("header", { class: "ui-topbar" },
                    YS.el("a", { class: "ui-marca", href: YS.rutas.login, html: `<img src="${LOGO}" alt=""><span class="ui-marca-nombre">Yo<b>Soy</b></span>` }),
                    YS.el("a", { class: "ui-btn ui-btn--primary ui-btn--sm", href: YS.rutas.login, text: "Ingresar" })), els.encabezado);
                encabezado({ titulo: cfg.titulo || "", subtitulo: cfg.subtitulo || "" });
                resolverUsuario({ nombre: null });
                return false;
            }
            // Vista única: una sesión guardada antes de que existiera Administración se actualiza UNA vez por carga (ver YS.sesion.actualizarSesion).
            if (cfg.rol !== "paciente" && YS.sesion.sesionAntigua()) {
                YS.sesion.actualizarSesion().then(ok => { if (ok) location.reload(); });
                return false;
            }
            if (!YS.sesion.requerir(cfg.rol)) return false;
            menu = menuPara(cfg.rol);
            // Si "inicio" no está en esta pantalla (Admisión/Auditoría del profesional), se
            // arranca en la primera sección local.
            if (cfg.navegar && !(buscar(cfg.inicial) && esLocal(buscar(cfg.inicial))))
                cfg.inicial = plano().find(i => esLocal(i) && !i.deUsuario)?.id || cfg.inicial;
            // Si la URL ya pedía una sección que un menú async todavía no agregó (ver más abajo,
            // "Chats: solo para..."), ir() de más abajo no la va a encontrar y cae a cfg.inicial,
            // lo que además reescribe el hash de la URL (marcar() → history.replaceState) ANTES de
            // que el reintento pueda mirarlo: guardar el pedido original acá, una sola vez, evita
            // depender de un location.hash que ya puede estar pisado para cuando se usa.
            const hashInicial = leerHash();
            document.body.classList.add("ui-con-shell");
            els.barra = crearBarra();
            els.encabezado = crearEncabezado();
            document.body.prepend(els.barra, els.encabezado);
            if (AVISOS[cfg.rol]) campana.iniciar(AVISOS[cfg.rol]);

            // Chats: SOLO para el módulo CRM (sección propia, con Chats, Escalamientos y Etiquetas) y el Configurador que trae el permiso crm (grupo CRM). Los profesionales no ven ningún chat ni reciben sus avisos
            // (decisión D2 del chat para el personal): no tienen botón Chats ni cajón, y el servidor les contesta 403 en todo /mensajeria. El Configurador sin el
            // módulo CRM tampoco (403); quien tiene los dos módulos ve Chats al trabajar en CRM. Solo si la mensajería está disponible para este pedido:
            // /capacidades usa el mismo control que el 404 de /mensajeria; con el módulo apagado no se agrega nada al menú ni se carga la bandeja.
            const grupoChats = grupoDeChats(cfg.rol);
            if (grupoChats) {
                capacidades = YS.api("/capacidades").catch(() => ({ mensajeria: false }));
                capacidades.then(c => {
                    if (!c?.mensajeria) return;
                    if (grupoChats.enGrupo) agregarItemsAlGrupo(grupoChats); else agregarGrupo(grupoChats, true);
                    // Si la URL ya pedía una sección de este grupo antes de que esto
                    // resolviera, el menú todavía no la tenía y ir() cayó a cfg.inicial
                    // (y de paso pisó la URL con "#inicio", ver hashInicial más arriba):
                    // reintentar con el id que se pidió en realidad, no con el hash actual.
                    if (cfg.navegar && grupoChats.items.some(it => it.id === hashInicial.id) && actual !== hashInicial.id)
                        cfg.navegar(hashInicial.id, hashInicial.params);
                    cargarBandeja(c);
                });
            }

            if (cfg.navegar) {
                // Se compara el #hash completo: de #turnos?paciente=N a #turnos es la misma
                // sección, pero hay que volver a dibujarla sin el filtro.
                const ir = () => {
                    if (location.hash === hashMostrado) return;
                    const { id, params } = leerHash();
                    const destino = buscar(id) && esLocal(buscar(id)) ? id : cfg.inicial;
                    hashMostrado = location.hash;
                    cfg.navegar(destino, params);
                };
                window.addEventListener("hashchange", ir);
                // Aterrizaje de Administración: Chats si la mensajería está disponible, si no la sección de siempre (Usuarios). Sin #hash pedido, se espera
                // a saberlo (un instante) para no dibujar Usuarios y saltar a Chats.
                if (grupoChats && cfg.inicialConChats && !hashInicial.id)
                    capacidades.then(c => { if (c?.mensajeria) cfg.inicial = cfg.inicialConChats; }).finally(ir);
                else ir();
            } else if (cfg.activo) {
                marcar(cfg.activo, { titulo: cfg.titulo, subtitulo: cfg.subtitulo });
            }
            document.dispatchEvent(new CustomEvent("ys:shell-listo", { detail: { rol: cfg.rol } }));
            return true;
        },
        /** La pantalla avisa qué sección mostró (actualiza menú, migas, título y #hash). */
        marcar,
        encabezado,
        acciones,
        boton,
        get rol() { return cfg?.rol; },
        /** Promise con { nombre, detalle } del usuario (el mismo que muestra la barra). */
        usuario,
        /** Promise con lo que muestra la interfaz según el servidor: { mensajeria }. */
        get capacidades() { return capacidades; },
        get actual() { return actual; },
        /** Número entre paréntesis en el título de la pestaña (mensajes sin leer). */
        contadorTitulo(n) { tituloBase.contador = n || 0; actualizarTitulo(); },
        campana: { refrescar: () => campana.refrescar(), abrir: () => campana.ctrl?.abrir(), cerrar: () => campana.ctrl?.cerrar() },
        /** Para módulos que traen sus propios avisos (la bandeja de chats en el configurador). */
        instalarCampana(def) { if (!campana.def) campana.iniciar(def); },
        generarSala,
        /** El menú de un módulo según la sesión (con conChats, el grupo de conversaciones ya agregado). Solo lectura: lo usan las pruebas. */
        menuDe(rol, { conChats = false } = {}) {
            const m = menuPara(rol);
            if (!m) return null;
            const g = conChats ? grupoDeChats(rol) : null;
            if (g?.enGrupo) {
                const items = m.items.map(it => it.texto === g.texto && it.items ? { ...it, items: [...g.items, ...it.items] } : it);
                return { ...m, items };
            }
            return g ? { ...m, items: [...m.items.slice(0, posicionInicial(m.items)), g, ...m.items.slice(posicionInicial(m.items))] } : m;
        },
        /** Etapa 2: diálogo para pasar a otro módulo (lo usa el menú del usuario y en_construccion.html). */
        elegirModulo: elegirModuloDialogo
    };

    function elegirModuloDialogo(opciones) {
        const lista = YS.el("div", { class: "ys-modulos" });
        const ctrl = YS.modal({ titulo: "Cambiar de módulo", cuerpo: lista, tamano: "chico" });
        opciones.forEach(m => lista.appendChild(YS.el("button", {
            class: "ys-modulo-opcion", type: "button",
            html: `<span class="texto-fuerte">${YS.esc(m.nombre)}</span><span class="texto-chico ui-muted">${YS.esc(m.descripcion || "")}</span>`,
            on: {
                click: async (e) => {
                    const b = e.currentTarget;
                    b.classList.add("cargando");
                    try { await YS.sesion.elegirModulo(m.clave); }
                    catch (err) { b.classList.remove("cargando"); YS.toast(err.message, "error"); }
                }
            }
        })));
        return ctrl;
    }
})();
