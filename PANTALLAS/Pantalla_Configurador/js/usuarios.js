// Configurador › Usuarios (Fase 2, PR 2): UNA lista con todos los tipos de usuario (pacientes, profesionales y equipo), con búsqueda,
// filtros y paginación en el servidor (GET /usuarios/lista), y un menú de acciones por fila. Reemplaza las dos listas de "Cuentas".
// Se carga ANTES de dashboard.js (que al terminar ya navega a la primera sección) y, al ejecutarse, usa lo que ese archivo define
// (mostrarTab, toast, confirmar, abrirModalCrearUsuario, ...).
// Todo lo que muestra son datos de cuenta: nada clínico. El servidor vuelve a controlar cada pedido (solo Configurador).

const USR = {
    q: "", tipo: "todos", estado: "todos", pagina: 1, cantidad: 25,
    total: 0, filas: [], pedido: 0, menu: null
};

const USR_TIPOS = [
    { id: "todos", texto: "Todos" },
    { id: "paciente", texto: "Pacientes" },
    { id: "profesional", texto: "Profesionales" },
    { id: "equipo", texto: "Equipo" }
];
const USR_ESTADOS = [
    { id: "todos", texto: "Todos los estados" },
    { id: "activo", texto: "Activos" },
    { id: "inactivo", texto: "Dados de baja" },
    { id: "pendiente", texto: "Sin confirmar" },
    { id: "bloqueado", texto: "Bloqueados" },
    { id: "sin-cuenta", texto: "Sin cuenta (todavía no tienen usuario)" }
];
const USR_TONO_TIPO = { paciente: "info", profesional: "marca", equipo: "neutral" };
const USR_NOMBRE_TIPO = { paciente: "Paciente", profesional: "Profesional", equipo: "Equipo" };

async function renderListaUsuarios(c, params = {}) {
    // Un enlace con ?paciente=N (por ejemplo "Ver ficha" desde un chat) llega con el paciente ya buscado, por DNI.
    const idPac = parseInt(params.paciente);
    if (idPac) {
        const pac = await YS.fetch(`/pacientes/${idPac}`).then(r => r.ok ? r.json() : null).catch(() => null);
        if (pac?.dni) Object.assign(USR, { q: String(pac.dni), tipo: "todos", estado: "todos", pagina: 1 });
    }
    if (YS.shell.actual !== "usuarios") return;

    YS.shell.acciones([
        { texto: "Registrar usuario", icono: "usuario-mas", tipo: "primary", alClick: () => { location.hash = "#altausuario"; } },
        { texto: "Actualizar", icono: "refrescar", alClick: () => usrCargar() }
    ]);

    const buscador = YS.el("input", { class: "ui-input", type: "search", id: "usrBuscar", value: USR.q,
        placeholder: "Buscar por usuario, nombre, DNI, matrícula o mail", "aria-label": "Buscar usuarios" });
    buscador.addEventListener("input", YS.debounce(() => { USR.q = buscador.value.trim(); USR.pagina = 1; usrCargar(); }, 300));

    const selectEstado = YS.el("select", { class: "ui-select", id: "usrEstado", "aria-label": "Filtrar por estado" });
    selectEstado.innerHTML = USR_ESTADOS.map(e => `<option value="${e.id}">${YS.esc(e.texto)}</option>`).join("");
    selectEstado.value = USR.estado;
    selectEstado.addEventListener("change", () => { USR.estado = selectEstado.value; USR.pagina = 1; usrCargar(); });

    const filtroTipo = YS.el("div", { class: "ui-filtros", role: "group", "aria-label": "Filtrar por tipo", id: "usrTipos" });
    filtroTipo.addEventListener("click", e => {
        const b = e.target.closest("[data-tipo]");
        if (!b) return;
        USR.tipo = b.dataset.tipo; USR.pagina = 1; usrPintarTipos(); usrCargar();
    });

    c.innerHTML = "";
    const barra = YS.el("div", { class: "ui-barra" },
        YS.el("label", { class: "ui-buscador" }, YS.el("span", { html: YS.icono("buscar") }).firstChild, buscador),
        filtroTipo, selectEstado,
        YS.el("span", { class: "ui-barra-espacio" }),
        YS.el("span", { class: "ui-barra-info", id: "usrInfo", "aria-live": "polite" }));
    const panel = YS.el("div", { class: "ui-tabla-panel" });
    panel.innerHTML = `
        <div class="ui-tabla-scroll ui-tabla-scroll--alta" id="usrScroll">
            <table class="ui-tabla ui-tabla--densa" id="usrTabla" aria-busy="false">
                <thead><tr>
                    <th>Usuario</th><th>Tipo</th><th>Estado</th><th>Contacto</th><th class="acciones"><span class="sr-only">Acciones</span></th>
                </tr></thead>
                <tbody id="usrCuerpo"></tbody>
            </table>
        </div>
        <div class="ui-paginacion" id="usrPie"></div>`;
    c.append(barra, panel);
    panel.querySelector("#usrPie").addEventListener("click", e => {
        const b = e.target.closest("[data-pag]");
        if (b && !b.disabled) { USR.pagina += parseInt(b.dataset.pag); usrCargar(); document.getElementById("usrScroll").scrollTop = 0; }
    });
    panel.querySelector("#usrCuerpo").addEventListener("click", e => {
        const b = e.target.closest("[data-menu]");
        if (b) usrAbrirMenu(b, USR.filas[parseInt(b.dataset.menu)]);
    });

    usrPintarTipos();
    await usrCargar(true);
}

function usrPintarTipos() {
    const cont = document.getElementById("usrTipos");
    if (!cont) return;
    cont.innerHTML = USR_TIPOS.map(t => {
        const si = t.id === USR.tipo;
        return `<button type="button" class="ui-filtro${si ? " activo" : ""}" data-tipo="${t.id}" aria-pressed="${si}">${YS.esc(t.texto)}</button>`;
    }).join("");
}

// Pide la página al servidor. Si llegan dos respuestas desordenadas (tecleando rápido), solo vale la del último pedido.
async function usrCargar(primera = false) {
    const mio = ++USR.pedido;
    const tabla = document.getElementById("usrTabla");
    const cuerpo = document.getElementById("usrCuerpo");
    if (!tabla || !cuerpo) return;
    tabla.setAttribute("aria-busy", "true");
    if (primera || !USR.filas.length) cuerpo.innerHTML = `<tr><td colspan="5">${YS.cargando()}</td></tr>`;
    else tabla.classList.add("usr-cargando");
    const consulta = new URLSearchParams({ tipo: USR.tipo, estado: USR.estado, pagina: USR.pagina, cantidad: USR.cantidad });
    if (USR.q) consulta.set("q", USR.q);
    try {
        const res = await YS.fetch(`/usuarios/lista?${consulta}`);
        if (mio !== USR.pedido) return;
        if (!res.ok) throw new Error(await YS.leerMensaje(res, "No se pudo cargar la lista de usuarios."));
        const d = await res.json();
        USR.filas = d.filas; USR.total = d.total; USR.pagina = d.pagina;
        usrPintar(d);
    } catch (e) {
        if (mio !== USR.pedido) return;
        cuerpo.innerHTML = `<tr><td colspan="5">${YS.vacio({ icono: "alerta", titulo: "No se pudo cargar la lista",
            texto: e.message || "Probá de nuevo en unos segundos." })}</td></tr>`;
        document.getElementById("usrPie")?.classList.add("oculto");
    } finally {
        if (mio === USR.pedido) { tabla.setAttribute("aria-busy", "false"); tabla.classList.remove("usr-cargando"); }
    }
}

function usrNombreMostrado(f) { return f.nombre || f.username || "(sin nombre)"; }

function usrChipsEstado(f) {
    if (!f.tieneCuenta) {
        const base = '<span class="ui-chip ui-chip--warn" title="Todavía no tiene un usuario para ingresar">Sin cuenta</span>';
        return f.estado === "Inactivo" ? `${base} <span class="ui-chip ui-chip--neutral">Dado de baja</span>` : base;
    }
    const chips = [chipEstadoCuenta(f.estado === "Pendiente" ? "Pendiente" : f.estado)];
    if (f.bloqueado) {
        const hasta = new Date(f.bloqueadoHasta).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });
        chips.push(`<span class="ui-chip ui-chip--danger" title="Por intentos fallidos. Restablecer la contraseña la desbloquea.">Bloqueada hasta ${YS.esc(hasta)}</span>`);
    }
    if (f.debeCambiarPassword) chips.push('<span class="ui-chip ui-chip--info" title="Tiene una clave temporal: la tiene que cambiar al ingresar">Clave temporal</span>');
    return chips.join(" ");
}

function usrPintar(d) {
    const cuerpo = document.getElementById("usrCuerpo");
    document.getElementById("usrInfo").textContent = `${d.total} ${d.total === 1 ? "resultado" : "resultados"}`;
    YS.shell.encabezado({ subtitulo: "Pacientes, profesionales y equipo en una sola lista" });
    if (!d.filas.length) {
        const filtrado = USR.q || USR.tipo !== "todos" || USR.estado !== "todos";
        cuerpo.innerHTML = `<tr><td colspan="5">${YS.vacio(filtrado
            ? { icono: "buscar", titulo: "Sin resultados", texto: "Probá con otra búsqueda o sacá algún filtro." }
            : { icono: "pacientes", titulo: "Todavía no hay usuarios" })}</td></tr>`;
    } else {
        cuerpo.innerHTML = d.filas.map((f, i) => {
            const nombre = usrNombreMostrado(f);
            const sub = [f.username && f.nombre ? f.username : null,
                f.dni ? "DNI " + f.dni : null, f.matricula ? "Mat. " + f.matricula : null].filter(Boolean).join(" · ");
            return `<tr class="${f.estado === "Inactivo" ? "usr-fila-inactiva" : ""}">
                <td><span class="ui-flex">${YS.avatar(nombre, (f.idUsuario ?? "") + f.tipo + (f.idPaciente ?? f.idProfesional ?? ""), "sm")}
                    <span><span class="principal">${YS.esc(nombre)}${f.esUnoMismo ? ' <span class="ui-muted">(vos)</span>' : ""}</span>
                    <span class="secundario">${YS.esc(sub || "—")}</span></span></span></td>
                <td><span class="ui-chip ui-chip--sin-punto ui-chip--${USR_TONO_TIPO[f.tipo] || "neutral"}">${YS.esc(USR_NOMBRE_TIPO[f.tipo] || f.tipo)}</span>
                    ${f.especialidad ? `<span class="secundario">${YS.esc(f.especialidad)}</span>` : ""}</td>
                <td>${usrChipsEstado(f)}</td>
                <td>${celda(f.mail)}</td>
                <td class="acciones"><button type="button" class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono" data-menu="${i}"
                    aria-label="Acciones de ${YS.esc(nombre)}" aria-haspopup="menu" aria-expanded="false">${YS.icono("ajustes")}</button></td>
            </tr>`;
        }).join("");
    }
    const paginas = Math.max(1, d.paginas || 1);
    const desde = d.total ? (d.pagina - 1) * d.cantidad + 1 : 0;
    const pie = document.getElementById("usrPie");
    pie.classList.toggle("oculto", d.total <= d.cantidad);
    pie.innerHTML = `<span>${d.total ? `${desde}–${Math.min(d.pagina * d.cantidad, d.total)} de ${d.total}` : ""}</span>
        <div class="ui-paginacion-botones">
            <button type="button" class="ui-btn ui-btn--sm" data-pag="-1"${d.pagina <= 1 ? " disabled" : ""}>Anterior</button>
            <span class="ui-paginacion-pagina">${d.pagina} / ${paginas}</span>
            <button type="button" class="ui-btn ui-btn--sm" data-pag="1"${d.pagina >= paginas ? " disabled" : ""}>Siguiente</button>
        </div>`;
}

// ── Menú de acciones de cada fila ────────────────────────────
// Se dibuja fuera de la tabla (pegado al botón) para que el scroll de la tabla no lo recorte.
function usrAcciones(f) {
    const nombre = usrNombreMostrado(f);
    const items = [];
    const activa = f.estado === "Activo";
    if (f.tieneCuenta) {
        if (activa && !f.esUnoMismo)
            items.push({ icono: "candado", texto: "Restablecer contraseña", ayuda: "Genera una clave temporal", alClick: () => usrRestablecer(f) });
        if (f.tipo !== "paciente")
            items.push({ icono: "escudo", texto: "Permisos", ayuda: "Módulos y privilegios", alClick: () => {
                window._ultimosAccesos = [{ idUsuario: f.idUsuario, username: f.username }];
                abrirEditorPermisos(f.idUsuario);
            } });
        if (f.estado === "Pendiente")
            items.push({ icono: "enviar", texto: "Reenviar invitación", alClick: () => {
                window._ultimosAccesos = [{ idUsuario: f.idUsuario, username: f.username }];
                reenviarInvitacion(f.idUsuario);
            } });
    } else if (f.tipo === "profesional" && f.estado === "Activo") {
        items.push({ icono: "usuario-mas", texto: "Crear usuario", ayuda: "Para que pueda ingresar", alClick: () => usrCrearUsuario(f) });
    }
    if (f.tipo === "paciente" && f.idPaciente && f.estado !== "Inactivo")
        items.push({ icono: "pacientes", texto: "Asignar profesionales", ayuda: "Quién atiende a este paciente", alClick: () => usrAsignar(f) });
    if (f.tipo === "profesional" && f.linkPublicoActivo)
        items.push({ icono: "externo", texto: "Apagar link público", alClick: () => usrApagarLink(f) });
    if (f.tipo === "paciente" && f.idPaciente) {
        items.push("sep");
        items.push(f.estado === "Inactivo"
            ? { icono: "check", texto: "Dar de alta", alClick: () => usrCambiarEstado(f, "PUT", `/pacientes/${f.idPaciente}/alta`, `¿Dar de alta a ${nombre}?`, "Dar de alta", "Paciente dado de alta.", "No se pudo dar de alta al paciente.") }
            : { icono: "papelera", texto: "Dar de baja", peligro: true, alClick: () => usrCambiarEstado(f, "DELETE", `/pacientes/${f.idPaciente}`, `¿Dar de baja a ${nombre}?`, "Dar de baja", "Paciente dado de baja.", "No se pudo dar de baja al paciente.", true) });
    } else if (f.tipo === "profesional" && f.idProfesional) {
        items.push("sep");
        items.push(f.estado === "Inactivo"
            ? { icono: "check", texto: "Dar de alta", alClick: () => usrCambiarEstado(f, "PUT", `/profesionales/${f.idProfesional}/alta`, `¿Dar de alta a ${nombre}?`, "Dar de alta", "Profesional dado de alta.", "No se pudo dar de alta al profesional.") }
            : { icono: "papelera", texto: "Dar de baja", peligro: true, alClick: () => usrCambiarEstado(f, "DELETE", `/profesionales/${f.idProfesional}`, `¿Dar de baja a ${nombre}?`, "Dar de baja", "Profesional dado de baja.", "No se pudo dar de baja al profesional.", true) });
    }
    while (items.length && items[0] === "sep") items.shift();
    if (items.length && items[items.length - 1] === "sep") items.pop();
    return items;
}

function usrAbrirMenu(boton, f) {
    USR.menu?.cerrar();
    const items = usrAcciones(f);
    const panel = YS.el("div", { class: "ui-desplegable usr-menu", role: "menu", "aria-label": `Acciones de ${usrNombreMostrado(f)}` });
    panel.innerHTML = items.length ? items.map((it, i) => it === "sep" ? '<div class="ui-desplegable-sep" role="separator"></div>' : `
        <button type="button" role="menuitem" class="ui-desplegable-item${it.peligro ? " ui-desplegable-item--peligro" : ""}" data-i="${i}">
            ${YS.icono(it.icono)}<span class="ui-desplegable-texto">${YS.esc(it.texto)}${it.ayuda ? `<small>${YS.esc(it.ayuda)}</small>` : ""}</span>
        </button>`).join("")
        : '<div class="ui-desplegable-titulo">Sin acciones disponibles</div>';
    panel.addEventListener("click", e => {
        const b = e.target.closest("[data-i]");
        if (b) items[parseInt(b.dataset.i)].alClick();
    });
    document.body.appendChild(panel);
    // Pegado al botón, abriendo hacia abajo o hacia arriba según el lugar que haya.
    const r = boton.getBoundingClientRect();
    panel.style.position = "fixed";
    panel.style.left = "auto";
    panel.style.right = Math.max(8, window.innerWidth - r.right) + "px";
    panel.classList.add("abierto");
    const alto = panel.offsetHeight;
    panel.style.top = (r.bottom + 4 + alto > window.innerHeight ? Math.max(8, r.top - alto - 4) : r.bottom + 4) + "px";
    panel.classList.remove("abierto");
    const ctrl = YS.desplegable(boton, panel, { tipo: "menu", alCerrar: () => { panel.remove(); if (USR.menu === ctrl) USR.menu = null; } });
    USR.menu = ctrl;
    ctrl.abrir(true);
    // Este clic ya lo atendió el botón: se evita que el cierre-al-hacer-clic-afuera lo vuelva a cerrar de inmediato.
    window.addEventListener("scroll", () => ctrl.cerrar(), { once: true, capture: true });
}

// ── Dar de baja o de alta (los endpoints de siempre) ─────────
function usrCambiarEstado(f, metodo, ruta, pregunta, textoConfirmar, ok, error, peligro = false) {
    return cambiarEstadoUsuario({ url: `${API}${ruta}`, metodo, pregunta, titulo: textoConfirmar, textoConfirmar, peligro, ok, error });
}

async function usrApagarLink(f) {
    if (!await confirmar(`¿Apagar el link público de reserva de ${usrNombreMostrado(f)}? Deja de funcionar al instante.`,
        { titulo: "Link público", textoConfirmar: "Apagar", peligro: true })) return;
    try {
        const res = await YS.fetch(`${API}/profesionales/${f.idProfesional}/link-publico`, {
            method: "PUT", headers: auditorHeaders(), body: JSON.stringify({ activo: false })
        });
        if (res.ok) { toast("Link público apagado."); usrCargar(); }
        else toast(await YS.leerMensaje(res, "No se pudo cambiar el link público."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

// ── Crear el usuario de un profesional sin cuenta ────────────
function usrCrearUsuario(f) {
    const partes = (f.nombre || "").trim().split(/\s+/);
    abrirModalCrearUsuario(f.idProfesional, { nombre: partes[0] || "", apellido: partes.slice(1).join(" "), mail: f.mail || "" });
    // La sugerencia sale del nombre y del apellido (la ficha de la lista trae el nombre entero).
    const campo = document.getElementById("campoUsuarioNombre");
    if (campo) campo.value = sugerirUsuario(partes[0], partes.slice(1).join(" "));
}

// ── Restablecer la contraseña ────────────────────────────────
async function usrRestablecer(f) {
    const nombre = usrNombreMostrado(f);
    const cuerpo = YS.el("div", { html: `
        <p>Vas a generar una <strong>clave temporal</strong> para <strong>${YS.esc(nombre)}</strong>${f.username ? ` (<code>${YS.esc(f.username)}</code>)` : ""}.</p>
        <ul class="usr-lista-aviso">
            <li>Su clave actual deja de servir y, si la cuenta estaba bloqueada, se desbloquea.</li>
            <li>La clave se muestra <strong>una sola vez</strong>, en la próxima pantalla. Se la pasás a la persona por un medio seguro.</li>
            <li>Al ingresar con ella, la persona va a tener que elegir una clave nueva.</li>
            <li>Si ya tenía una sesión abierta, esa sesión no se cierra.</li>
        </ul>` });
    const m = YS.modal({ titulo: "Restablecer contraseña", cuerpo, tamano: "chico", acciones: [
        { texto: "Cancelar" },
        { texto: "Restablecer", tipo: "danger-solido", foco: true, alClick: async ctrl => {
            let res;
            try { res = await YS.fetch(`/usuarios/${f.idUsuario}/restablecer-password`, { method: "POST" }); }
            catch { toast("No se pudo conectar con el servidor.", "error"); return; }
            if (!res.ok) { toast(await YS.leerMensaje(res, "No se pudo restablecer la contraseña."), "error"); return; }
            const d = await res.json();
            ctrl.cerrar(true);
            usrMostrarClave(nombre, d);
            usrCargar();
        } }
    ] });
    return m.resultado;
}

// La clave solo vive en este modal: no se escribe en el almacenamiento del navegador y desaparece del DOM al cerrarlo.
function usrMostrarClave(nombre, d) {
    let copiada = false;
    const cuerpo = YS.el("div", { html: `
        <p>Clave temporal de <strong>${YS.esc(nombre)}</strong>${d.username ? ` (<code>${YS.esc(d.username)}</code>)` : ""}:</p>
        <div class="usr-clave" role="group" aria-label="Clave temporal">
            <code id="usrClave" data-secreto="1">${YS.esc(d.contrasenaTemporal)}</code>
            <button type="button" class="ui-btn ui-btn--sm" id="usrCopiar">${YS.icono("copiar")} Copiar</button>
        </div>
        <div class="ui-alerta ui-alerta--warn mt-12" role="status"><span><strong>Se ve una sola vez.</strong> Cuando cierres esta ventana no hay forma de volver a verla: si se pierde, hay que restablecerla de nuevo.</span></div>
        <p class="texto-chico ui-muted mt-12">La persona ingresa con su usuario y esta clave, y en ese momento elige una clave nueva. Queda registrado en la auditoría (sin la clave).</p>` });
    const copiar = cuerpo.querySelector("#usrCopiar");
    copiar.addEventListener("click", async () => {
        await YS.copiar(d.contrasenaTemporal, "Clave copiada");
        copiada = true;
    });
    YS.modal({ titulo: "Clave temporal", cuerpo, cerrable: false, enfocarCampo: false, acciones: [
        { texto: "Listo", tipo: "primary", foco: true, alClick: async ctrl => {
            if (!copiada && !await confirmar("¿Ya copiaste o anotaste la clave? Después no se puede volver a ver.",
                { titulo: "Antes de cerrar", textoConfirmar: "Sí, cerrar" })) return;
            ctrl.cerrar(true);
        } }
    ] });
}

// ── Asignar profesionales a un paciente ──────────────────────
async function usrAsignar(f) {
    const nombre = usrNombreMostrado(f);
    const cuerpo = YS.el("div", { class: "usr-asignar" });
    let datos = null;
    const elegidos = new Set();
    let filtro = "";

    const cargar = async () => {
        const res = await YS.fetch(`/usuarios/pacientes/${f.idPaciente}/profesionales`);
        if (!res.ok) throw new Error(await YS.leerMensaje(res, "No se pudieron leer los profesionales asignados."));
        datos = await res.json();
        pintar();
    };

    const llamar = async (metodo, ids) => {
        const res = await YS.fetch(`/usuarios/pacientes/${f.idPaciente}/profesionales`, { method: metodo, json: { idProfesionales: ids } });
        if (!res.ok) { toast(await YS.leerMensaje(res, "No se pudo guardar el cambio."), "error"); return null; }
        return res.json();
    };

    const pintar = () => {
        const asignados = datos.asignados;
        const ya = new Set(asignados.map(p => p.idProfesional));
        const libres = datos.disponibles.filter(p => !ya.has(p.idProfesional));
        [...elegidos].forEach(id => { if (!libres.some(p => p.idProfesional === id)) elegidos.delete(id); });
        const q = YS.normalizar(filtro);
        const visibles = libres.filter(p => !q || YS.normalizar([p.nombre, p.matricula, p.especialidad].join(" ")).includes(q));
        const linea = p => `<span class="usr-prof-texto"><span class="principal">${YS.esc(p.nombre)}</span>
            <span class="secundario">${YS.esc([p.matricula ? "Mat. " + p.matricula : null, p.especialidad].filter(Boolean).join(" · ") || "—")}</span></span>`;

        cuerpo.innerHTML = `
            <section aria-labelledby="usrAsignados">
                <h3 class="usr-subtitulo" id="usrAsignados">Profesionales asignados <span class="ui-contador ui-contador--suave">${asignados.length}</span></h3>
                ${asignados.length ? `<ul class="usr-prof-lista">${asignados.map(p => `
                    <li class="usr-prof">${linea(p)}
                        <button type="button" class="ui-btn ui-btn--sm ui-btn--danger" data-quitar="${p.idProfesional}" aria-label="Quitar a ${YS.esc(p.nombre)}">Quitar</button>
                    </li>`).join("")}</ul>`
                    : '<p class="ui-muted">Todavía no tiene profesionales asignados.</p>'}
            </section>
            <section aria-labelledby="usrAgregar">
                <h3 class="usr-subtitulo" id="usrAgregar">Agregar profesionales</h3>
                <label class="ui-buscador"><span aria-hidden="true">${YS.icono("buscar")}</span>
                    <input type="search" class="ui-input" id="usrBuscarProf" placeholder="Buscar por nombre, matrícula o especialidad" value="${YS.esc(filtro)}" aria-label="Buscar profesionales"></label>
                ${visibles.length ? `<ul class="usr-prof-lista usr-prof-lista--elegir">${visibles.map(p => `
                    <li><label class="usr-prof usr-prof--elegir"><input type="checkbox" data-elegir="${p.idProfesional}"${elegidos.has(p.idProfesional) ? " checked" : ""}>${linea(p)}</label></li>`).join("")}</ul>`
                    : `<p class="ui-muted">${libres.length ? "Ningún profesional coincide con la búsqueda." : "No hay más profesionales activos para asignar."}</p>`}
                <div class="usr-asignar-pie"><button type="button" class="ui-btn ui-btn--primary" id="usrAsignarBtn"${elegidos.size ? "" : " disabled"}>
                    ${elegidos.size ? `Asignar ${elegidos.size} seleccionado${elegidos.size === 1 ? "" : "s"}` : "Asignar seleccionados"}</button></div>
            </section>`;

        const buscar = cuerpo.querySelector("#usrBuscarProf");
        buscar.addEventListener("input", YS.debounce(() => {
            filtro = buscar.value; pintar();
            const nuevo = cuerpo.querySelector("#usrBuscarProf"); nuevo.focus(); nuevo.setSelectionRange(nuevo.value.length, nuevo.value.length);
        }, 150));
        cuerpo.querySelectorAll("[data-elegir]").forEach(ch => ch.addEventListener("change", () => {
            const id = parseInt(ch.dataset.elegir);
            ch.checked ? elegidos.add(id) : elegidos.delete(id);
            const b = cuerpo.querySelector("#usrAsignarBtn");
            b.disabled = !elegidos.size;
            b.textContent = elegidos.size ? `Asignar ${elegidos.size} seleccionado${elegidos.size === 1 ? "" : "s"}` : "Asignar seleccionados";
        }));
        cuerpo.querySelector("#usrAsignarBtn").addEventListener("click", async e => {
            e.currentTarget.classList.add("cargando");
            const r = await llamar("POST", [...elegidos]);
            if (r) { toast(`Asignado${r.asignados === 1 ? "" : "s"}: ${r.asignados}.`); elegidos.clear(); await cargar().catch(err => toast(err.message, "error")); }
            else e.currentTarget.classList.remove("cargando");
        });
        cuerpo.querySelectorAll("[data-quitar]").forEach(b => b.addEventListener("click", async () => {
            const id = parseInt(b.dataset.quitar);
            const prof = datos.asignados.find(p => p.idProfesional === id);
            if (!await confirmar(`¿Quitar a ${prof?.nombre ?? "este profesional"} de ${nombre}? Deja de verlo entre sus pacientes.`,
                { titulo: "Quitar profesional", textoConfirmar: "Quitar", peligro: true })) return;
            const r = await llamar("DELETE", [id]);
            if (r) { toast("Profesional quitado."); await cargar().catch(err => toast(err.message, "error")); }
        }));
    };

    cuerpo.innerHTML = YS.cargando();
    YS.modal({ titulo: `Profesionales de ${nombre}`, cuerpo, acciones: [{ texto: "Cerrar", tipo: "primary", alClick: c => c.cerrar() }] });
    try { await cargar(); }
    catch (e) { cuerpo.innerHTML = `<div class="ui-alerta ui-alerta--danger"><span>${YS.esc(e.message)}</span></div>`; }
}
