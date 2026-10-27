// Primeros pasos del paciente: el Inicio guiado, la firma del consentimiento y el formulario «Sobre mí» desde el panel principal (ventanas emergentes),
// sin mandar al paciente a otras pestañas e ir y volver.
//
// El estado de cada paso lo calcula el SERVIDOR con datos reales (GET /pacientes/{id}/primeros-pasos, Services/PrimerosPasos.cs): acá solo se dibuja y se
// ejecuta el único botón de «qué tengo que hacer ahora». Nada se marca a mano.
// Usa las constantes y funciones de dashboard.js (idPaciente, toast, confirmar, mostrarSeccion, confirmarTurnoPaciente...).

// Nombre visible del formulario de admisión. Se cambia en un solo lugar: YS.NOMBRE_FORMULARIO_ADMISION (shared/ui.js).
const NOMBRE_FORMULARIO = () => YS.NOMBRE_FORMULARIO_ADMISION;

const pedirPrimerosPasos = () => YS.api(`/pacientes/${idPaciente}/primeros-pasos`);

// ── Inicio guiado ──────────────────────────────────────────
const CHECK_SVG = `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"></path></svg>`;
const RELOJ_SVG = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path></svg>`;

const ETIQUETA_PASO = { espera: "En revisión", pendiente: "Después", cerrado: "Cerrado" };

// Lo que hace cada botón principal del servidor (AccionTipo en Services/PrimerosPasos.cs).
function ejecutarAccionPaso(tipo, idTurno) {
    if (tipo === "firmar-consentimiento") return abrirFirmaConsentimiento();
    if (tipo === "completar-formulario") return abrirSobreMi();
    if (tipo === "completar-datos") { location.hash = "#perfil"; return; }
    if (tipo === "confirmar-turno") return confirmarTurnoPaciente(idTurno);
    if (tipo === "ver-turnos") { location.hash = "#agenda"; return; }
    if (tipo === "entrar-sesion") return entrarSesionDelPaso();
    if (tipo === "reprogramar") return reprogramarTurnoPasado(idTurno);
    // "responder-propuesta": no tiene botón propio, el panel dibuja la tarjeta con «Me sirve» y «Prefiero otro horario» (htmlTarjetaPropuesta).
}

// ── Propuesta automática de profesional y horario ──────────
// Al aceptar la recepción el formulario, el sistema propone un profesional y un horario y el paciente decide (Services/AsignacionAutomatica.cs).
// «Me sirve» crea el turno confirmado; «Prefiero otro horario» muestra los próximos libres del mismo profesional y permite probar con otro.
function htmlTarjetaPropuesta(pr) {
    if (!pr) return "";
    const vence = pr.expiraEn ? new Date(pr.expiraEn) : null;
    const venceTexto = vence ? `${vence.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit" })} a las ${vence.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false })} hs` : "";
    return `<div class="pp-propuesta" role="group" aria-label="Propuesta de profesional y horario">
        <div class="pp-propuesta-datos">
            <span class="pp-propuesta-titulo">Te propusimos a ${YS.esc(pr.profesional)}${pr.especialidad ? ` <span class="texto-suave">(${YS.esc(pr.especialidad)})</span>` : ""}</span>
            <span class="pp-propuesta-cuando">${YS.esc(pr.cuando)}</span>
        </div>
        <div class="pp-acciones">
            <button type="button" id="btnPropuestaSirve" class="ui-btn ui-btn--primary pp-boton" onclick="confirmarPropuesta(${Number(pr.idPropuesta)})">Me sirve, confirmar</button>
            <button type="button" class="ui-btn pp-boton" onclick="pedirOtraOpcion(${Number(pr.idPropuesta)})">${pr.puedeCambiar ? "Prefiero otro horario" : "No me sirve"}</button>
        </div>
        <span class="texto-suave texto-chico">${pr.puedeCambiar ? "" : "Es tu última propuesta: si no te sirve, la recepción te ayuda a elegir. "}${venceTexto ? `Podés responder hasta el ${YS.esc(venceTexto)}.` : ""}</span>
    </div>`;
}

async function confirmarPropuesta(idPropuesta) {
    const boton = document.getElementById("btnPropuestaSirve");
    if (boton) boton.disabled = true;   // un doble clic no manda dos pedidos (el servidor igual no crea dos turnos)
    try {
        const r = await YS.fetch(`/propuestas-asignacion/${idPropuesta}/aceptar`, { method: "POST" });
        const data = await r.json().catch(() => ({}));
        if (r.ok) toast(data.mensaje || "Listo, tu primer turno quedó confirmado.");
        else toast(data.mensaje || "No pudimos confirmar la propuesta. Probá de nuevo.", "error");
    } catch { toast("No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos.", "error"); }
    mostrarSeccion("inicio");   // se vuelve a dibujar con lo que dice el servidor (turno confirmado, propuesta nueva o «estamos buscando»)
}

async function responderPropuesta(idPropuesta, cuerpo) {
    try {
        const r = await YS.fetch(`/propuestas-asignacion/${idPropuesta}/rechazar`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(cuerpo) });
        const data = await r.json().catch(() => ({}));
        toast(data.mensaje || (r.ok ? "Listo." : "No pudimos registrar tu respuesta. Probá de nuevo."), r.ok ? undefined : "error");
    } catch { toast("No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos.", "error"); }
    mostrarSeccion("inicio");
}

async function pedirOtraOpcion(idPropuesta) {
    const pr = window._ppActual?.propuesta;
    if (!pr) return;
    if (!pr.puedeCambiar) {
        const ok = await YS.confirmar({ titulo: "Esta propuesta no me sirve", mensaje: "Ya usaste las tres propuestas. Si no te sirve, la recepción se encarga de ayudarte a elegir. ¿Seguimos?", textoConfirmar: "No me sirve" });
        if (ok) await responderPropuesta(idPropuesta, { tipo: "ninguno" });
        return;
    }
    let horarios = [];
    try { horarios = (await YS.api(`/propuestas-asignacion/${idPropuesta}/horarios`)).horarios || []; }
    catch (e) { toast(e.message || "No pudimos buscar otros horarios. Probá de nuevo.", "error"); return; }

    const cuerpo = document.createElement("div");
    cuerpo.innerHTML = `
        <p class="mb-12">Elegí otro horario con <strong>${YS.esc(pr.profesional)}</strong> o probá con otro profesional.</p>
        ${horarios.length
            ? `<div class="pp-horarios">${horarios.map(h => `<button type="button" class="ui-btn pp-horario" data-h="${YS.esc(h.fechaHora)}">${YS.esc(h.cuando)}</button>`).join("")}</div>`
            : `<p class="texto-suave">Ese profesional no tiene más horarios libres en los próximos días. Podés probar con otro profesional.</p>`}`;
    const m = YS.modal({
        titulo: "Prefiero otra opción", cuerpo,
        acciones: [
            { texto: "Probar con otro profesional", alClick: async c => { c.cerrar(null); await responderPropuesta(idPropuesta, { tipo: "otro-profesional" }); } },
            { texto: "Cancelar", alClick: c => c.cerrar(null) }
        ]
    });
    cuerpo.querySelectorAll("[data-h]").forEach(b => b.addEventListener("click", async () => {
        m.cerrar(null);
        await responderPropuesta(idPropuesta, { tipo: "otro-horario", fechaHora: b.dataset.h });
    }));
}

// El turno ya pasó (la sala cerró y no hubo sesión): lleva a Turnos, en el día de ese turno, con la reprogramación abierta.
async function reprogramarTurnoPasado(idTurno) {
    const turnos = await YS.api(`/pacientes/${idPaciente}/turnos-detalle`).catch(() => []);
    const t = turnos.find(x => x.idTurno === idTurno);
    if (!t) { location.hash = "#agenda"; return; }
    window._reprogramarAlEntrar = { idTurno: t.idTurno, idProfesional: t.idProfesional };
    irAlTurno(t.fecha);
}

function entrarSesionDelPaso() {
    const s = window._ppActual?.sesion;
    if (!s || !s.habilitada || s.fase === "cerrada") return;   // el servidor también lo exige (ventana de la sala)
    // Se entra por el turno (no por el código de sala): el servidor genera la sala si el profesional todavía no entró, así el paciente puede esperar.
    location.href = "sesion_video.html?turno=" + encodeURIComponent(s.idTurno) + "&idPaciente=" + idPaciente;
}

function botonPrincipal(pp) {
    const a = pp.principal;
    if (!a) return "";
    if (a.tipo === "responder-propuesta") return htmlTarjetaPropuesta(pp.propuesta);
    if (a.tipo === "entrar-sesion") {
        const s = pp.sesion;
        // Gris hasta 10 minutos antes del turno: la hora es la del servidor (pp.ahora), no la de la computadora del paciente.
        const habilitado = !!(s && s.habilitada);
        const desde = s ? YS.esc(s.habilitaDesde.substring(11, 16)) : "";
        const nota = !s ? "" : habilitado ? "Podés entrar ahora: si tu profesional todavía no llegó, vas a ver «Esperando a tu profesional…» y se conectan solos cuando entre."
            : `Se habilita 10 minutos antes del turno, a las ${desde} hs.`;
        return `<div class="pp-acciones">
            <button type="button" id="btnEntrarSesion" class="ui-btn ${habilitado ? "ui-btn--primary" : ""} pp-boton" ${habilitado ? "" : "disabled aria-disabled=\"true\""}
                    onclick="ejecutarAccionPaso('entrar-sesion')">${YS.esc(a.texto)}</button>
            ${nota ? `<span class="texto-suave texto-chico">${nota}</span>` : ""}
        </div>`;
    }
    return `<div class="pp-acciones">
        <button type="button" class="ui-btn ui-btn--primary pp-boton" onclick="ejecutarAccionPaso('${YS.esc(a.tipo)}', ${a.idTurno ?? "null"})">${YS.esc(a.texto)}</button>
    </div>`;
}

function inicioPacienteNuevo(pp, tarjetaConsentimiento, tarjetaProximo) {
    window._ppActual = pp;
    const actual = pp.pasos.find(p => p.clave === pp.pasoActual) || pp.pasos.find(p => p.estado !== "hecho") || pp.pasos[pp.pasos.length - 1];
    const indiceActual = pp.pasos.indexOf(actual);
    const prof = pp.profesional;

    const filas = pp.pasos.map((p, i) => {
        const n = i + 1;
        if (p.estado === "hecho") return `
            <li class="pp-fila pp-fila--hecha">
                <span class="pp-num pp-num--hecho">${CHECK_SVG}</span>
                <span class="pp-fila-titulo">${YS.esc(p.titulo)}</span>
                <span class="pp-fila-estado pp-fila-estado--ok">Hecho</span>
            </li>`;
        if (p === actual) return `
            <li class="pp-fila pp-fila--actual" aria-current="step">
                <span class="pp-num pp-num--actual">${n}</span>
                <span class="pp-fila-titulo">${YS.esc(p.titulo)}</span>
                <span class="pp-fila-estado pp-fila-estado--actual">Ahora</span>
            </li>`;
        return `
            <li class="pp-fila pp-fila--bloqueada">
                <span class="pp-num">${n}</span>
                <span class="pp-fila-titulo">${YS.esc(p.titulo)}</span>
                <span class="pp-fila-estado">${YS.esc(ETIQUETA_PASO[p.estado] || "Después")}</span>
            </li>`;
    }).join("");

    // Un paso que le toca al equipo no tiene botón: dice qué está pasando. Con el ingreso cerrado, el motivo.
    const cuerpo = pp.cerrado
        ? `<div class="pp-aviso">${RELOJ_SVG}<span>${YS.esc(pp.mensaje)}${pp.motivoCierre ? ` <strong>Motivo:</strong> ${YS.esc(pp.motivoCierre)}` : ""}</span></div>`
        : pp.principal ? botonPrincipal(pp)
        : `<div class="pp-aviso">${RELOJ_SVG}<span>${YS.esc(actual.detalle)}</span></div>`;

    return `
        <div class="ui-stats">
            <button type="button" class="ui-stat ui-stat--marca" onclick="document.getElementById('pp-proximo').scrollIntoView({ behavior: 'smooth' })">
                <span class="ui-stat-etiqueta">${YS.icono("check")}Tu proceso</span>
                <span class="ui-stat-valor">Paso ${indiceActual + 1} de ${pp.total}</span>
                <span class="ui-stat-pie">${YS.esc(actual.titulo)}</span>
            </button>
            ${tarjetaConsentimiento}
            <a class="ui-stat" href="#profesionales">
                <span class="ui-stat-etiqueta">${YS.icono("pacientes")}Tu profesional</span>
                <span class="ui-stat-valor">${prof ? YS.esc(prof.nombre) : "—"}</span>
                <span class="ui-stat-pie">${prof ? YS.esc(prof.especialidad || "") : "Se asigna después de la revisión"}</span>
            </a>
            ${tarjetaProximo}
        </div>
        <div class="inicio-columnas inicio-columnas--parejas">
            <section class="card" id="pp-proximo" aria-labelledby="pp-proximo-titulo">
                <div class="titulo-tarjeta" id="pp-proximo-titulo">${pp.cerrado ? "Tu ingreso" : "Qué tenés que hacer ahora"}</div>
                <div class="pp-actual">
                    <span class="pp-num pp-num--actual pp-num--grande">${indiceActual + 1}</span>
                    <div class="pp-cuerpo">
                        <span class="pp-titulo-grande">${YS.esc(actual.titulo)}</span>
                        ${pp.cerrado || !pp.principal || pp.principal.tipo === "responder-propuesta" ? "" : `<span class="pp-texto">${YS.esc(actual.detalle)}</span>`}
                        ${cuerpo}
                    </div>
                </div>
            </section>
            <section class="card" aria-labelledby="pp-lista-titulo">
                <div class="pp-lista-cabecera">
                    <div class="titulo-tarjeta" id="pp-lista-titulo">Tus primeros pasos</div>
                    <span class="texto-suave texto-chico">${pp.hechos} de ${pp.total} hechos</span>
                </div>
                <div class="pp-barra" role="progressbar" aria-label="Avance de los primeros pasos" aria-valuemin="0" aria-valuemax="${pp.total}" aria-valuenow="${pp.hechos}">
                    <div style="width: ${Math.round(pp.hechos / pp.total * 100)}%"></div>
                </div>
                <ol class="pp-filas">${filas}</ol>
            </section>
        </div>`;
}

// Si el botón de entrar a la sesión está gris, se vuelve a dibujar el Inicio cuando el servidor diga que ya es hora (sin que el paciente recargue).
let _timerSesion = null;
function programarHabilitacionDeLaSesion(pp) {
    clearTimeout(_timerSesion);
    const s = pp?.sesion;
    if (!s || s.habilitada) return;
    const falta = new Date(s.habilitaDesde) - new Date(pp.ahora);   // las dos horas son del servidor: la diferencia no depende de la computadora del paciente
    if (falta > 0 && falta < 6 * 60 * 60 * 1000)
        _timerSesion = setTimeout(() => { if (YS.shell.actual === "inicio") mostrarSeccion("inicio"); }, falta + 1000);
}

// ── Firma del consentimiento (ventana emergente en el panel) ───
// El texto legal todavía no está validado (Consentimiento:MarcaProvisoria): la marca viene de la API y se quita dejándola vacía en la configuración.
const TEXTO_LEGAL_HTML = `
    <div class="texto-legal" tabindex="0" aria-label="Texto del consentimiento">
        <p class="placeholder-legal">[COMPLETAR CON TEXTO LEGAL VALIDADO]</p>
        <p class="nota-borrador">Este espacio muestra el texto del consentimiento informado para la atención psicológica por telemedicina. El texto definitivo tiene que ser redactado y validado por el equipo antes de usar la plataforma con pacientes reales.</p>
    </div>`;

const fechaHoraConstancia = valor => {
    const d = new Date(valor);
    return `${d.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" })} a las ${d.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false })} hs`;
};
const versionReal = v => !!v && v !== "sin-validar";

function htmlConstanciaConsentimiento(fechaFirma, version, venceEl) {
    return `
        <div class="firmado constancia-consentimiento" role="status">
            <div class="firmado-icono" aria-hidden="true">${YS.icono("check")}</div>
            <div>
                <h3 class="constancia-titulo">Consentimiento firmado</h3>
                <dl class="constancia-datos">
                    <div><dt>Fecha</dt><dd>${YS.esc(new Date(fechaFirma).toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" }))}</dd></div>
                    <div><dt>Hora</dt><dd>${YS.esc(new Date(fechaFirma).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false }))} hs</dd></div>
                    <div><dt>Versión del texto</dt><dd>${versionReal(version) ? YS.esc(version) : "Texto provisorio (sin versión validada)"}</dd></div>
                    ${venceEl ? `<div><dt>Vigente hasta</dt><dd>${YS.esc(new Date(venceEl).toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" }))}</dd></div>` : ""}
                </dl>
            </div>
        </div>`;
}

async function abrirFirmaConsentimiento() {
    let estado;
    try { estado = await YS.api(`/pacientes/${idPaciente}/consentimiento-telemedicina`); }
    catch (e) { toast("No pudimos consultar tu consentimiento. Probá de nuevo.", "error"); return; }

    const cuerpo = document.createElement("div");
    const motivoTexto = {
        vencido: "Tu consentimiento venció (pasó más de un año desde que lo firmaste). Leelo y firmalo de nuevo para seguir.",
        "version-anterior": "El texto del consentimiento cambió desde la última vez que lo firmaste. Leelo y volvé a firmarlo para seguir."
    }[estado.motivo] || "";
    const tutores = estado.esMenor ? (estado.tutores || []) : [];
    const marca = estado.marcaProvisoria ? `<p class="nota-borrador" role="note">${YS.esc(estado.marcaProvisoria)}</p>` : "";

    const pintarFirmado = (fecha, version, venceEl) => {
        cuerpo.innerHTML = htmlConstanciaConsentimiento(fecha, version, venceEl) + marca +
            `<p class="texto-suave mt-12">Listo: ya podés confirmar tus turnos. Guardamos la fecha y la hora de tu firma. Podés revisarlo o revocarlo cuando quieras desde <strong>Consentimiento</strong> en el menú.</p>`;
    };

    const m = YS.modal({
        titulo: "Consentimiento informado", tamano: "grande", cuerpo,
        acciones: [{ texto: "Cerrar", alClick: c => c.cerrar(null) }],
        alCerrar: () => { if (YS.shell.actual === "inicio") mostrarSeccion("inicio"); }
    });

    if (estado.firmado) { pintarFirmado(estado.fechaFirma, estado.versionFirmada, estado.venceEl); return; }

    cuerpo.innerHTML = `
        ${motivoTexto ? `<p class="ui-alerta ui-alerta--warn mb-12" role="status"><span>${YS.esc(motivoTexto)}</span></p>` : ""}
        ${TEXTO_LEGAL_HTML}
        ${marca}
        ${versionReal(estado.versionTexto) ? `<p class="version">Versión del texto: <span>${YS.esc(estado.versionTexto)}</span></p>` : ""}
        ${tutores.length ? `<div class="campo-form ui-campo mt-12"><label for="ppSelTutor">Como sos menor de edad, lo firma tu madre, padre o tutor. ¿Quién firma?</label>
            <select id="ppSelTutor" class="ui-select">${tutores.map(t => `<option value="${t.idTutor}">${YS.esc(t.nombre)}${t.vinculo ? " (" + YS.esc(t.vinculo) + ")" : ""}</option>`).join("")}</select></div>` : ""}
        ${estado.esMenor && !tutores.length ? `<p class="ui-alerta ui-alerta--danger mt-12" role="alert"><span>Tu ficha no tiene cargado a ningún tutor. Pedile a la recepción que lo cargue para poder firmar.</span></p>` : ""}
        <p id="ppErrorFirma" class="ui-alerta ui-alerta--danger mt-12 oculto" role="alert"></p>
        <p class="texto-suave texto-chico mt-12">Al aceptar se guarda la fecha, la hora y la versión de tu firma. Es necesario para que te asignemos un profesional y se confirmen tus turnos.</p>`;
    const boton = YS.el("button", { class: "ui-btn ui-btn--primary", type: "button", text: "Leo y acepto" });
    m.pie.insertBefore(boton, m.pie.firstChild);
    boton.addEventListener("click", async () => {
        const err = cuerpo.querySelector("#ppErrorFirma");
        err?.classList.add("oculto");
        boton.disabled = true;
        try {
            const r = await YS.fetch(`/pacientes/${idPaciente}/consentimiento-telemedicina`, {
                method: "POST", headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ acepto: true, version: estado.versionTexto, idTutor: tutores.length ? Number(cuerpo.querySelector("#ppSelTutor").value) : null })
            });
            const data = await r.json().catch(() => ({}));
            if (r.status === 201 || (r.status === 409 && data.fechaFirma)) {
                boton.remove();
                pintarFirmado(data.fechaFirma, data.versionFirmada ?? estado.versionTexto, null);
                return;
            }
            if (err) { err.textContent = data.mensaje || "No se pudo registrar la firma. Probá de nuevo."; err.classList.remove("oculto"); }
        } catch {
            if (err) { err.textContent = "No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos."; err.classList.remove("oculto"); }
        } finally { boton.disabled = false; }
    });
}

// ── «Sobre mí»: el formulario de admisión ──────────────────
const DIAS_FORM = [["lunes", "Lunes"], ["martes", "Martes"], ["miercoles", "Miércoles"], ["jueves", "Jueves"], ["viernes", "Viernes"], ["sabado", "Sábado"], ["domingo", "Domingo"]];
const FRANJAS_FORM = [["manana", "Mañana (8 a 12 hs)"], ["tarde", "Tarde (12 a 18 hs)"], ["noche", "Noche (18 a 22 hs)"], ["cualquiera", "Cualquier horario"]];

// previo: lo que el paciente ya había cargado (cuando la recepción se lo devolvió).
async function htmlFormularioSobreMi(previo) {
    const [especialidades, profesionales] = await Promise.all([
        YS.api("/especialidades").catch(() => []),
        YS.api("/profesionales/elegibles").catch(() => [])
    ]);
    const sel = (valor, ref) => valor === ref ? " selected" : "";
    const opts = especialidades.filter(e => e.estado === "Activo")
        .map(e => `<option value="${e.idEspecialidad}"${sel(previo?.idEspecialidad, e.idEspecialidad)}>${YS.esc(e.nombre)}</option>`).join("");
    const texto = (id, etiqueta, filas, valor, placeholder = "") => `
        <div class="ui-campo ui-campo--ancho"><label for="${id}">${etiqueta}</label>
            <textarea id="${id}" class="ui-textarea" rows="${filas}" maxlength="1000" placeholder="${placeholder}">${YS.esc(valor || "")}</textarea></div>`;
    const diasPrevios = previo?.horarioPreferido?.dias || [];
    const idsPrevios = previo?.profesionalesPreferidos?.ids || [];
    const igual = previo?.profesionalesPreferidos?.cualquiera === true;

    return `
        <form id="formAdmision" novalidate>
            <p id="admError" class="ui-alerta ui-alerta--danger mb-12 oculto" role="alert"></p>
            <div class="ui-form-grid">
                <div class="ui-campo ui-campo--ancho"><label for="admEspecialidad">Especialidad solicitada *</label>
                    <select id="admEspecialidad" class="ui-select"><option value="">Elegí una especialidad</option>${opts}</select></div>
                ${texto("admMotivo", "Motivo de consulta *", 3, previo?.motivoConsulta, "Contanos brevemente por qué buscás atención")}
                <div class="ui-campo"><label for="admTratAnterior">¿Hiciste algún tratamiento antes?</label>
                    <select id="admTratAnterior" class="ui-select"><option value="false">No</option><option value="true"${previo?.tratamientoAnterior ? " selected" : ""}>Sí</option></select></div>
                <div class="ui-campo${previo?.tratamientoAnterior ? "" : " oculto"}" id="campoTratDetalle"><label for="admTratDetalle">¿Cuál?</label>
                    <input type="text" id="admTratDetalle" class="ui-input" maxlength="300" value="${YS.esc(previo?.tratamientoAnteriorDetalle || "")}"></div>
                <div class="ui-campo"><label for="admMedicacion">¿Tomás alguna medicación?</label>
                    <select id="admMedicacion" class="ui-select"><option value="false">No</option><option value="true"${previo?.recibeMedicacion ? " selected" : ""}>Sí</option></select></div>
                <div class="ui-campo${previo?.recibeMedicacion ? "" : " oculto"}" id="campoMedDetalle"><label for="admMedDetalle">¿Cuál?</label>
                    <input type="text" id="admMedDetalle" class="ui-input" maxlength="300" value="${YS.esc(previo?.medicacionDetalle || "")}"></div>
            </div>

            <div class="ui-form-seccion">¿Cuándo y con quién te queda mejor?</div>
            <p class="texto-suave mb-12">Lo tenemos en cuenta al elegir tu profesional. Son opcionales.</p>
            <fieldset class="grupo-opciones" id="admHorario">
                <legend>Días que podés</legend>
                <div class="grupo-opciones-lista">${DIAS_FORM.map(([v, t]) => `<label class="ui-check"><input type="checkbox" name="admDia" value="${v}"${diasPrevios.includes(v) ? " checked" : ""}> <span>${t}</span></label>`).join("")}</div>
                <div class="ui-campo mt-8"><label for="admFranja">Franja horaria</label>
                    <select id="admFranja" class="ui-select"><option value="">Sin preferencia</option>${FRANJAS_FORM.map(([v, t]) => `<option value="${v}"${sel(previo?.horarioPreferido?.franja, v)}>${t}</option>`).join("")}</select></div>
            </fieldset>
            <fieldset class="grupo-opciones mt-12" id="admProfesionales">
                <legend>Profesionales que preferís</legend>
                <label class="ui-check"><input type="checkbox" id="admProfIgual"${igual ? " checked" : ""}> <span>Me da igual</span></label>
                <div class="grupo-opciones-lista grupo-opciones-lista--columna" id="admProfLista">${profesionales.length
                    ? profesionales.map(p => `<label class="ui-check"><input type="checkbox" name="admProf" value="${p.idProfesional}"${idsPrevios.includes(p.idProfesional) ? " checked" : ""}${igual ? " disabled" : ""}> <span>${YS.esc(p.nombre)}${p.especialidad ? ` · <span class="texto-suave">${YS.esc(p.especialidad)}</span>` : ""}</span></label>`).join("")
                    : `<span class="texto-suave">Por ahora no hay profesionales para elegir: lo decide la recepción.</span>`}</div>
            </fieldset>

            <div class="ui-form-seccion">Conociéndote mejor</div>
            <p class="texto-suave mb-12">Estas preguntas nos ayudan a darte una atención personalizada. Son opcionales.</p>
            <div class="ui-form-grid">
                ${texto("admGustos", "Gustos e intereses", 3, previo?.gustosIntereses, "Actividades que disfrutás, hobbies, música, películas o libros, deportes, qué te ayuda a relajarte, algo que quieras aprender")}
                ${texto("admPreferencias", "Preferencias durante la atención", 3, previo?.preferenciasAtencion, "Cómo preferís que se dirijan a vos, algo que te genere ansiedad en una consulta, apoyos que necesites")}
                <div class="ui-campo"><label for="admTipoEntrevista">Tipo de atención preferida</label>
                    <select id="admTipoEntrevista" class="ui-select">${["Sin preferencia", "Individual", "Familiar", "Grupal"].map(v => `<option value="${v}"${sel(previo?.tipoEntrevista, v)}>${v}</option>`).join("")}</select></div>
                ${texto("admRedApoyo", "Red de apoyo", 2, previo?.redApoyo, "¿Con quién convivís? ¿Contás con familiares o personas de confianza que te acompañen?")}
                ${texto("admObjetivos", "¿Qué esperás lograr con el tratamiento?", 2, previo?.objetivosPaciente)}
            </div>
        </form>`;
}

function enlazarFormularioSobreMi(alEnviar) {
    // "¿Cuál?" aparece solo si se contesta que sí
    const alternar = (select, campo) => document.getElementById(select).addEventListener("change", e =>
        document.getElementById(campo).classList.toggle("oculto", e.target.value !== "true"));
    alternar("admTratAnterior", "campoTratDetalle");
    alternar("admMedicacion", "campoMedDetalle");
    // «Me da igual» deja sin efecto la lista de profesionales puntuales
    document.getElementById("admProfIgual").addEventListener("change", e => {
        document.querySelectorAll('input[name="admProf"]').forEach(c => { c.disabled = e.target.checked; if (e.target.checked) c.checked = false; });
    });
    document.getElementById("formAdmision").addEventListener("submit", e => { e.preventDefault(); alEnviar(); });
}

function leerFormularioSobreMi() {
    const valor = id => document.getElementById(id).value.trim() || null;
    const tratamiento = document.getElementById("admTratAnterior").value === "true";
    const medicacion = document.getElementById("admMedicacion").value === "true";
    const dias = [...document.querySelectorAll('input[name="admDia"]:checked')].map(c => c.value);
    const franja = document.getElementById("admFranja").value;
    const igual = document.getElementById("admProfIgual").checked;
    const ids = [...document.querySelectorAll('input[name="admProf"]:checked')].map(c => parseInt(c.value));
    return {
        idPaciente: parseInt(idPaciente),
        idEspecialidad: parseInt(document.getElementById("admEspecialidad").value),
        motivoConsulta: valor("admMotivo"),
        tratamientoAnterior: tratamiento,
        tratamientoAnteriorDetalle: tratamiento ? valor("admTratDetalle") : null,
        recibeMedicacion: medicacion,
        medicacionDetalle: medicacion ? valor("admMedDetalle") : null,
        gustosIntereses: valor("admGustos"),
        preferenciasAtencion: valor("admPreferencias"),
        tipoEntrevista: document.getElementById("admTipoEntrevista").value,
        redApoyo: valor("admRedApoyo"),
        objetivosPaciente: valor("admObjetivos"),
        estado: "Pendiente",
        // Sin días ni franja no se manda nada; sin elegir profesionales ni «me da igual», tampoco.
        horarioPreferido: dias.length || franja ? { dias, franja: franja || "cualquiera" } : null,
        profesionalesPreferidos: igual ? { cualquiera: true, ids: [] } : ids.length ? { cualquiera: false, ids } : null
    };
}

// Manda el formulario: nuevo (POST) o, si la recepción lo devolvió, el mismo de nuevo (PUT). Devuelve true si salió bien.
async function enviarSobreMi(reenvio) {
    const valido = YS.form.validar([
        { id: "admEspecialidad", requerido: "Elegí la especialidad." },
        { id: "admMotivo", requerido: "Contanos el motivo de consulta." }
    ]);
    if (!valido) return false;
    const error = document.getElementById("admError");
    error?.classList.add("oculto");
    const body = leerFormularioSobreMi();
    try {
        const r = await YS.fetch(reenvio ? `/pacientes/${idPaciente}/informe-admision` : "/informes-admision", {
            method: reenvio ? "PUT" : "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body)
        });
        if (r.ok) return true;
        const msg = await YS.leerMensaje(r, "No se pudo enviar el formulario.");
        if (error) { error.textContent = msg; error.classList.remove("oculto"); error.scrollIntoView({ block: "nearest" }); }
        else toast(msg, "error");
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    }
    return false;
}

// Ventana emergente en el panel principal: sin cambiar de pestaña.
async function abrirSobreMi() {
    let previo = null;
    try { previo = await YS.api(`/pacientes/${idPaciente}/informe-admision`, { permitir404: true }); } catch { /* sin datos previos */ }
    if (previo && previo.estado !== "Devuelto") {
        toast(previo.estado === "Pendiente" ? "Tu formulario ya está enviado y en revisión." : "Tu formulario ya fue enviado.", "info");
        return;
    }
    const reenvio = !!previo;
    const cuerpo = document.createElement("div");
    cuerpo.innerHTML = YS.cargando();
    const m = YS.modal({
        titulo: NOMBRE_FORMULARIO(), tamano: "xl", cuerpo,
        acciones: [
            { texto: "Cancelar", alClick: c => c.cerrar(null) },
            {
                texto: reenvio ? "Enviar de nuevo" : "Enviar", tipo: "primary", id: "btnEnviarAdmision",
                alClick: async c => { if (await enviarSobreMi(reenvio)) { toast("Recibimos tu formulario. La recepción lo va a revisar."); c.cerrar(true); } }
            }
        ],
        alCerrar: () => { if (YS.shell.actual === "inicio" || YS.shell.actual === "admision") mostrarSeccion(YS.shell.actual); }
    });
    const aclaracion = reenvio && previo.aclaracionRecepcion
        ? `<p class="ui-alerta ui-alerta--warn mb-12" role="status"><span><strong>La recepción pidió una aclaración:</strong> ${YS.esc(previo.aclaracionRecepcion)}</span></p>` : "";
    cuerpo.innerHTML = `<p class="texto-suave mb-12">Completá este formulario para que el equipo pueda conocerte y asignarte el profesional adecuado. Los campos con * son obligatorios.</p>` +
        aclaracion + await htmlFormularioSobreMi(reenvio ? previo : null);
    enlazarFormularioSobreMi(() => document.getElementById("btnEnviarAdmision").click());
}

// La pestaña del menú: en qué etapa está el formulario, o el formulario si todavía no lo mandó.
function htmlEstadoSobreMi(inf) {
    const nombre = NOMBRE_FORMULARIO();
    let titulo, texto, estado = inf.estado, boton = "";
    if (inf.estado === "Devuelto") {
        titulo = "La recepción necesita que completes algo";
        texto = `${inf.aclaracionRecepcion ? `<strong>Qué falta:</strong> ${YS.esc(inf.aclaracionRecepcion)}<br>` : ""}Completá «${YS.esc(nombre)}» de nuevo y lo revisamos otra vez.`;
        boton = `<button type="button" class="ui-btn ui-btn--primary ui-btn--lg mt-16" onclick="abrirSobreMi()">Completar de nuevo</button>`;
    } else if (inf.estado === "Aprobado") {
        titulo = "Aceptamos tu formulario";
        texto = "La recepción ya lo revisó. Ahora estamos eligiendo tu profesional: te avisamos por la campanita y por mail apenas esté asignado.";
    } else if (inf.estado === "Derivado") {
        titulo = "Ya tenés profesional asignado";
        texto = inf.nombreProfesionalDerivado
            ? `Te asignamos a <strong>${YS.esc(inf.nombreProfesionalDerivado)}</strong>. Ya podés pedir tu primer turno en Turnos o esperar su propuesta.`
            : "Te asignamos un profesional. Ya podés ver tus turnos en Turnos.";
    } else if (inf.estado === "Otro recurso") {
        titulo = "Tu ingreso se cerró";
        texto = `Después de revisar tu formulario, la recepción te orientó hacia otro recurso.${inf.motivoCierre ? ` <strong>Motivo:</strong> ${YS.esc(inf.motivoCierre)}` : ""}`;
    } else {
        // Pendiente y los estados viejos de la entrevista (que ya no forma parte del recorrido): para el paciente, en revisión.
        estado = "En revisión";
        titulo = `Tu formulario «${nombre}» ya fue enviado`;
        texto = "La recepción lo está revisando. No tenés que hacer nada por ahora: te avisamos por la campanita y por mail.";
    }
    const realizada = inf.estado === "Derivado";
    return `
        <div class="card card--angosta${realizada ? " admision-realizada" : ""}">
            ${realizada ? `<div class="admision-realizada-icono" aria-hidden="true">${YS.icono("check")}</div>` : ""}
            <div class="admision-cuerpo">
            <div class="titulo-tarjeta${realizada ? " admision-realizada-titulo" : ""}">${titulo}</div>
            <p class="mb-12">Estado actual: <span class="panel-estado-badge estado-${YS.esc(String(estado).replace(/\s+/g, "-"))}">${YS.esc(estado)}</span></p>
            <p class="mb-12">${texto}</p>
            ${boton}
            <a class="ui-btn ${boton ? "" : "ui-btn--primary "}ui-btn--lg mt-16" href="#inicio">Volver al inicio</a>
            </div>
        </div>`;
}

async function renderSobreMi(c) {
    let inf;
    try { inf = await YS.api(`/pacientes/${idPaciente}/informe-admision`, { permitir404: true }); }
    catch (e) { c.innerHTML = YS.errorCarga({ texto: e.message, reintentar: "mostrarSeccion('admision')" }); return; }
    if (YS.shell.actual !== "admision") return;
    if (inf) { c.innerHTML = htmlEstadoSobreMi(inf); return; }

    c.innerHTML = `
        <div class="card card--angosta form-admision">
            <p class="texto-suave mb-16">Completá este formulario para que el equipo pueda conocerte y asignarte el profesional adecuado. Los campos con * son obligatorios.</p>
            ${await htmlFormularioSobreMi(null)}
            <div class="ui-form-acciones">
                <a class="ui-btn" href="#inicio">Volver al inicio</a>
                <button type="button" class="ui-btn ui-btn--primary" id="btnEnviarAdmision">Enviar</button>
            </div>
        </div>`;
    if (YS.shell.actual !== "admision") return;
    const enviar = async () => {
        const boton = document.getElementById("btnEnviarAdmision");
        boton.disabled = true;
        try { if (await enviarSobreMi(false)) { toast("Recibimos tu formulario. La recepción lo va a revisar."); mostrarSeccion("admision"); } }
        finally { boton.disabled = false; }
    };
    enlazarFormularioSobreMi(enviar);
    document.getElementById("btnEnviarAdmision").addEventListener("click", enviar);
}
