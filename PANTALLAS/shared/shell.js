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
        configurador: {
            etiqueta: "Panel configurador",
            items: [
                { id: "inicio", texto: "Inicio" },
                {
                    texto: "Usuarios", items: [
                        { id: "usuarios", texto: "Pacientes y profesionales", icono: "pacientes", desc: "Asignaciones, altas y bajas" },
                        { id: "solicitudes", texto: "Solicitudes de registro", icono: "usuario-mas", desc: "Profesionales que pidieron cuenta" },
                        { id: "derivaciones", texto: "Solicitudes de cambio", icono: "flechas", desc: "Pedidos de cambio de profesional" },
                        { id: "recuperacion", texto: "Recuperación de contraseña", icono: "candado", desc: "Pedidos de contraseña temporal" }
                    ]
                },
                {
                    texto: "Clínico", items: [
                        { id: "admisiones", texto: "Admisiones", icono: "portapapeles", desc: "Informes y primera entrevista" },
                        { id: "turnos", texto: "Turnos", icono: "reloj", desc: "Todos los turnos" },
                        { id: "agendas", texto: "Agendas", icono: "agenda", desc: "Horarios de los profesionales" },
                        { id: "evoluciones", texto: "Evoluciones", icono: "tendencia", desc: "Solo lectura" },
                        { id: "eventoscrisis", texto: "Eventos de crisis", icono: "alerta", desc: "Solo lectura" }
                    ]
                },
                { id: "reportes", texto: "Reportes" },
                {
                    texto: "Configuración", items: [
                        { id: "roles", texto: "Roles", icono: "escudo" },
                        { id: "especialidades", texto: "Especialidades", icono: "etiqueta" },
                        { id: "coberturas", texto: "Coberturas", icono: "tarjeta" },
                        { id: "interaccionesia", texto: "Interacciones IA", icono: "ia" }
                    ]
                },
                { id: "auditoria", texto: "Auditoría", titulo: "Log de auditoría" }
            ],
            usuario: []
        }
    };
    const ROL_TEXTO = { paciente: "Paciente", profesional: "Profesional", configurador: "Configurador" };
    const LOGO = "/pantallas/Pantalla_Usuario/Usuario/img/logo_SINFONDO.png";

    let cfg = null, menu = null, actual = null, primeraMarca = true;
    const els = {};
    let resolverUsuario;
    const usuario = new Promise(r => resolverUsuario = r);   // { nombre, detalle } para saludar en Inicio
    const tituloBase = { texto: "YoSoy", contador: 0 };

    const plano = () => {
        const out = [];
        menu.items.forEach(it => it.items ? it.items.forEach(s => out.push({ ...s, grupo: it })) : out.push(it));
        (menu.usuario || []).forEach(u => out.push({ ...u, deUsuario: true }));
        return out;
    };
    const buscar = id => plano().find(i => i.id === id);
    const enlace = item => item.href || (cfg.navegar ? "#" + item.id : YS.rutas[cfg.rol] + "#" + item.id);
    const leerHash = () => {
        const [id, q] = decodeURIComponent(location.hash.slice(1)).split("?");
        return { id, params: Object.fromEntries(new URLSearchParams(q || "")) };
    };

    // ── Construcción de la barra ────────────────────────────────
    function crearBarra() {
        const barra = YS.el("header", { class: "ui-topbar" });
        const inicio = buscar("inicio");
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
                const u = await YS.api(`/usuarios/${YS.sesion.idUsuario()}`);
                nombre = u?.username; detalle = "Configurador";
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
                <ol class="ui-migas" aria-label="Estás en"></ol>
                <div class="ui-encabezado-fila">
                    <div class="ui-encabezado-textos"><h1 class="ui-encabezado-titulo"></h1><p class="ui-encabezado-sub oculto"></p></div>
                    <div class="ui-encabezado-acciones"></div>
                </div>
            </div>`;
        els.migas = enc.querySelector(".ui-migas");
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
        const migas = [];
        if (id !== "inicio") migas.push({ texto: menu.etiqueta, href: enlace(buscar("inicio")) });
        if (item?.grupo) migas.push({ texto: item.grupo.texto });
        if (id !== "inicio") migas.push({ texto: item?.texto || titulo });
        encabezado({ titulo, subtitulo: op.subtitulo || "", migas });
        acciones([]);

        if (cfg.navegar && leerHash().id !== id) {
            const url = location.pathname + location.search + "#" + id;
            if (primeraMarca) history.replaceState(null, "", url); else history.pushState(null, "", url);
        }
        primeraMarca = false;
    }

    function encabezado({ titulo, subtitulo, migas } = {}) {
        if (titulo != null) {
            els.titulo.textContent = titulo;
            tituloBase.texto = titulo ? `${titulo} · YoSoy` : "YoSoy";
            actualizarTitulo();
        }
        if (subtitulo != null) {
            els.sub.textContent = subtitulo;
            els.sub.classList.toggle("oculto", !subtitulo);
        }
        if (migas) {
            els.migas.innerHTML = migas.map((m, i) => {
                const ultimo = i === migas.length - 1;
                return `<li>${m.href && !ultimo ? `<a href="${YS.esc(m.href)}">${YS.esc(m.texto)}</a>` : `<span${ultimo ? ' aria-current="page"' : ""}>${YS.esc(m.texto)}</span>`}</li>`;
            }).join("");
            els.migas.classList.toggle("oculto", !migas.length);
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
                return {
                    noLeidos: sinSala.length,
                    items: sinSala.map(t => ({
                        id: t.idTurno, titulo: "Turno confirmado: falta la sala", noLeido: true,
                        texto: `${t.nombrePaciente} · ${t.fecha} ${t.hora} hs`,
                        accion: { texto: "Generar sala", tipo: "primary", alClick: () => generarSala(t.idTurno) }
                    }))
                };
            },
            vacio: "No hay turnos confirmados esperando sala.",
            recordatorio: n => `Tenés ${n} turno${n === 1 ? "" : "s"} confirmado${n === 1 ? "" : "s"} sin sala de video.`
        }
    };

    const fechaDMY = s => { const [d, m, a] = String(s).split("/").map(Number); return new Date(a, m - 1, d); };

    async function unirseSala(codigo, idNotif) {
        try { await YS.fetch(`/notificaciones/${idNotif}/leida`, { method: "PUT" }); } catch { }
        location.href = `/pantallas/Pantalla_Paciente/sesion_video.html?sala=${encodeURIComponent(codigo)}&idPaciente=${YS.sesion.idPaciente()}`;
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
                if (it.accion) {
                    const acc = YS.el("div", { class: "ui-aviso-acciones" },
                        YS.el("button", { class: "ui-btn ui-btn--sm" + (it.accion.tipo ? " ui-btn--" + it.accion.tipo : ""), type: "button", text: it.accion.texto, on: { click: it.accion.alClick } }));
                    div.querySelector(".ui-aviso-cuerpo").appendChild(acc);
                }
                return div;
            }));
        }
    };

    // ── API pública ─────────────────────────────────────────────
    YS.shell = {
        /**
         * cfg: { rol, navegar(id, params), inicial = "inicio", activo }
         * - navegar: en los paneles (dashboard). El shell lee el #hash y llama a navegar.
         * - activo: en páginas sueltas (consentimiento, sesión de video), qué ítem marcar.
         */
        iniciar(opciones) {
            cfg = { inicial: "inicio", ...opciones };
            // Páginas que se abren desde un link (consentimiento): sin sesión muestran solo la marca
            // y la página explica que hay que ingresar, en vez de mandar directo al login.
            if (cfg.opcional && !localStorage.getItem({ paciente: "idPaciente", profesional: "idProfesional" }[cfg.rol] || "idUsuario")) {
                document.body.classList.add("ui-con-shell");
                els.encabezado = crearEncabezado();
                document.body.prepend(YS.el("header", { class: "ui-topbar" },
                    YS.el("a", { class: "ui-marca", href: YS.rutas.login, html: `<img src="${LOGO}" alt=""><span class="ui-marca-nombre">Yo<b>Soy</b></span>` }),
                    YS.el("a", { class: "ui-btn ui-btn--primary ui-btn--sm", href: YS.rutas.login, text: "Ingresar" })), els.encabezado);
                encabezado({ titulo: cfg.titulo || "", subtitulo: cfg.subtitulo || "", migas: [] });
                resolverUsuario({ nombre: null });
                return false;
            }
            if (!YS.sesion.requerir(cfg.rol)) return false;
            menu = MENUS[cfg.rol];
            document.body.classList.add("ui-con-shell");
            els.barra = crearBarra();
            els.encabezado = crearEncabezado();
            document.body.prepend(els.barra, els.encabezado);
            if (AVISOS[cfg.rol]) campana.iniciar(AVISOS[cfg.rol]);

            if (cfg.navegar) {
                const ir = () => {
                    const { id, params } = leerHash();
                    const destino = buscar(id) && !buscar(id).href ? id : cfg.inicial;
                    if (destino === actual && !Object.keys(params).length) return;
                    cfg.navegar(destino, params);
                };
                window.addEventListener("hashchange", ir);
                ir();
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
        get actual() { return actual; },
        /** Número entre paréntesis en el título de la pestaña (mensajes sin leer). */
        contadorTitulo(n) { tituloBase.contador = n || 0; actualizarTitulo(); },
        campana: { refrescar: () => campana.refrescar(), abrir: () => campana.ctrl?.abrir() },
        generarSala
    };
})();
