const API = YS.API;   // mismo origen que la página (shared/ui.js)

// Avisos y confirmaciones del sistema de diseño (shared/ui.js)
function confirmar(msg, opciones = {}) {
    return YS.confirmar({ mensaje: msg, ...opciones });
}

function toast(msg, tipo = "ok", duracion = 3500) {
    YS.toast(msg, tipo, duracion);
}

const idPaciente = localStorage.getItem("idPaciente");
if (!idPaciente) window.location.href = "../index.html";

let calAnio  = new Date().getFullYear();
let calMes   = new Date().getMonth();
let turnosCalendario = [];
let turnoSeleccionado = null;

let _modoReprogramar = false;
let _idTurnoReprog   = null;
let _reprogAgenda    = [];
let _reprogFechaSeleccionada = null;
let _agendaProfCalendario = [];
let _idProfCalendario = null;

const MESES   = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
const DIAS    = ["Lun","Mar","Mié","Jue","Vie","Sáb","Dom"];
const DIAS_ES = { "Lunes":1,"Martes":2,"Miércoles":3,"Jueves":4,"Viernes":5,"Sábado":6,"Domingo":0 };
const DIA_DB_NUM = { "lunes":1,"martes":2,"miercoles":3,"jueves":4,"viernes":5,"sabado":6,"domingo":0 };

// ── NAVEGACIÓN ──────────────────────────────────────────────
// Cada sección del menú (shared/shell.js) se dibuja en #contenido.
function mostrarSeccion(id) {
    const secciones = ["inicio", "agenda", "profesionales", "perfil", "sesion", "admision"];
    if (!secciones.includes(id)) id = "inicio";
    YS.shell.marcar(id);

    const c = document.getElementById("contenido");
    c.innerHTML = YS.cargando();

    if      (id === "inicio")        renderInicio(c);
    else if (id === "agenda")        renderAgenda(c);
    else if (id === "profesionales") renderProfesionales(c);
    else if (id === "perfil")        renderPerfil(c);
    else if (id === "sesion")        c.innerHTML = renderSesion();
    else if (id === "admision")      renderAdmision(c);
}

async function cerrarSesion() {
    await YS.sesion.cerrar();
}

// ── INICIO ─────────────────────────────────────────────────
async function renderInicio(c) {
    YS.shell.usuario.then(u => {
        if (YS.shell.actual === "inicio" && u.nombre)
            YS.shell.encabezado({ titulo: `Hola, ${u.nombre.split(" ")[0]}`, subtitulo: YS.capitalizar(YS.fecha.larga(new Date())) });
    });
    c.innerHTML = YS.esqueleto.bloques(3) + YS.esqueleto.lista(3);
    try {
        const pedir = (ruta, porDefecto, opciones) => YS.api(ruta, opciones).catch(() => porDefecto);
        const FALLA = {};   // lo que no se pudo consultar: sin todos los datos no se arma la guía de primeros pasos
        const [res, turnos, consentimiento, perfil, informe, profesionales, whatsapp] = await Promise.all([
            YS.fetch(`/pacientes/${idPaciente}/proximo-turno`),   // 404 = no tiene turnos próximos
            pedir(`/pacientes/${idPaciente}/turnos-detalle`, FALLA),
            pedir(`/pacientes/${idPaciente}/consentimiento-telemedicina`, null),
            pedir(`/pacientes/${idPaciente}`, FALLA),
            pedir(`/pacientes/${idPaciente}/informe-admision`, FALLA, { permitir404: true }),   // null = todavía no lo mandó
            pedir(`/pacientes/${idPaciente}/mis-profesionales`, FALLA),
            pedir("/consentimiento-whatsapp", null)
        ]);
        if (YS.shell.actual !== "inicio") return;   // el usuario ya cambió de sección
        const t = res.status === 404 ? null : await res.json();

        const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
        const listaTurnos = turnos === FALLA ? [] : turnos;
        const proximos = listaTurnos
            .filter(x => x.estadoDescripcion !== "Cancelado" && x.estadoDescripcion !== "Realizado" && YS.fecha.parse(x.fecha) >= hoy)
            .sort((a, b) => YS.fecha.parse(a.fecha) - YS.fecha.parse(b.fecha) || String(a.hora).localeCompare(String(b.hora)));
        const porConfirmar = proximos.filter(propuestoPorProfesional);

        const tarjetaConsentimiento = consentimiento == null
            ? `<div class="ui-stat"><span class="ui-stat-etiqueta">${YS.icono("firma")}Consentimiento</span><span class="ui-stat-valor">—</span><span class="ui-stat-pie">No se pudo consultar</span></div>`
            : consentimiento.firmado
                ? `<a class="ui-stat ui-stat--ok" href="consentimiento.html"><span class="ui-stat-etiqueta">${YS.icono("firma")}Consentimiento</span><span class="ui-stat-valor">Firmado</span><span class="ui-stat-pie">El ${YS.esc(YS.fecha.corta(consentimiento.fechaFirma))}</span></a>`
                : `<a class="ui-stat ui-stat--warn" href="consentimiento.html"><span class="ui-stat-etiqueta">${YS.icono("firma")}Consentimiento</span><span class="ui-stat-valor">Sin firmar</span><span class="ui-stat-pie">Hace falta para confirmar tus turnos</span></a>`;

        const proximoHtml = !t
            ? `<div class="card">${YS.vacio({ icono: "agenda", titulo: "No tenés turnos próximos", texto: "Podés pedir uno desde Mis turnos eligiendo un día en el calendario.", accion: { texto: "Ir a Mis turnos", onclick: "location.hash = '#agenda'" } })}</div>`
            : `
            <div class="card proximo-turno">
                <p class="proximo-etiqueta">Próximo turno</p>
                <div class="proximo-fecha">${YS.esc(YS.fecha.larga(t.fecha, true))}</div>
                <div class="proximo-hora">${YS.esc(t.hora.substring(0, 5))} hs</div>
                <div class="proximo-datos">
                    <div>
                        <div class="etiqueta">Profesional</div>
                        <div class="valor">${YS.esc(t.nombreProfesional)}</div>
                    </div>
                    <div>
                        <div class="etiqueta">Especialidad</div>
                        <div class="valor">${YS.esc(t.especialidad)}</div>
                    </div>
                    <div>
                        <div class="etiqueta">Modalidad</div>
                        <div class="valor">${YS.esc(t.modalidad ?? "-")}</div>
                    </div>
                    <div>
                        <div class="etiqueta">Estado</div>
                        <div class="valor"><span class="panel-estado-badge estado-${YS.esc(t.estadoDescripcion)}">${YS.esc(t.estadoDescripcion)}</span></div>
                    </div>
                </div>
                ${propuestoPorProfesional(t) ? `
                <div class="proximo-propuesta">
                    <span>Tu profesional te propuso este turno. Confirmalo para que quede agendado.</span>
                    <button class="btn-accion btn-guardar" onclick="confirmarTurnoPaciente(${t.idTurno})">Confirmar turno</button>
                </div>` : ""}
            </div>`;

        const listaHtml = proximos.length
            ? `<ul class="inicio-turnos">${proximos.slice(0, 5).map(x => `
                <li>
                    <div class="inicio-turno-fecha"><span>${YS.fecha.parse(x.fecha).getDate()}</span>${YS.esc(YS.fecha.parse(x.fecha).toLocaleString("es-AR", { month: "short" }))}</div>
                    <div class="inicio-turno-datos">
                        <div class="texto-fuerte">${YS.esc(x.hora.substring(0, 5))} hs · ${YS.esc(x.nombreProfesional)}</div>
                        <div class="texto-suave texto-chico">${YS.esc(x.especialidad)}${x.modalidad ? " · " + YS.esc(x.modalidad) : ""}</div>
                    </div>
                    ${propuestoPorProfesional(x)
                        ? `<span class="panel-estado-badge estado-propuesto">Por confirmar</span>`
                        : `<span class="panel-estado-badge estado-${YS.esc(x.estadoDescripcion)}">${YS.esc(x.estadoDescripcion)}</span>`}
                </li>`).join("")}</ul>`
            : YS.vacio({ icono: "agenda", texto: "Todavía no tenés turnos agendados." });

        // Paciente nuevo: mientras le falte algún paso, Inicio lo guía (ver armarPrimerosPasos). Si algo no se pudo
        // consultar, no se adivina: queda el Inicio de siempre.
        const guia = [turnos, perfil, informe, profesionales].includes(FALLA) || consentimiento == null ? null
            : armarPrimerosPasos({ perfil, consentimiento, informe, profesionales, turnos: listaTurnos, proximo: t, whatsapp });
        const inicioHtml = `
            <div class="ui-stats">
                <a class="ui-stat" href="#agenda">
                    <span class="ui-stat-etiqueta">${YS.icono("agenda")}Turnos próximos</span>
                    <span class="ui-stat-valor">${proximos.length}</span>
                    <span class="ui-stat-pie">Ver el calendario</span>
                </a>
                <a class="ui-stat${porConfirmar.length ? " ui-stat--marca" : ""}" href="#agenda">
                    <span class="ui-stat-etiqueta">${YS.icono("check")}Por confirmar</span>
                    <span class="ui-stat-valor">${porConfirmar.length}</span>
                    <span class="ui-stat-pie">${porConfirmar.length ? "Te los propuso tu profesional" : "Nada pendiente de tu parte"}</span>
                </a>
                ${tarjetaConsentimiento}
            </div>
            <div class="inicio-columnas">
                ${proximoHtml}
                <div class="card">
                    <div class="titulo-tarjeta">Tus próximos turnos</div>
                    ${listaHtml}
                </div>
            </div>`;
        // Con profesional asignado (pasos 6 y 7) los turnos ya importan: van debajo de la guía.
        c.innerHTML = !guia ? inicioHtml : guia.html + (guia.paso >= 6 ? `<div class="pp-turnos">${inicioHtml}</div>` : "");
    } catch {
        c.innerHTML = YS.errorCarga({ reintentar: "mostrarSeccion('inicio')" });
    }
}

// ── PRIMEROS PASOS (paciente nuevo) ────────────────────────
// El orden que sigue alguien recién dado de alta, con lo que la API ya devuelve: nada nuevo en el servidor. Un paso está
// hecho por lo que hay en la base, no porque se lo haya visitado. Quien ya tiene profesional asignado (por ejemplo, llegó
// por el link público de su profesional) no necesita la admisión: los pasos 3 a 5 cuentan como hechos. Con los siete
// hechos devuelve null y Inicio vuelve a ser el de siempre.
function armarPrimerosPasos({ perfil, consentimiento, informe, profesionales, turnos, proximo, whatsapp }) {
    const conProfesional = profesionales.length > 0;
    const conWhatsApp = !!whatsapp && !YS.whatsapp.apagado(whatsapp);
    const enEntrevista = !!informe && /entrevista/i.test(String(informe.estado || ""));
    const ir = destino => destino.startsWith("#") ? `location.hash='${destino}'` : `location.href='${destino}'`;

    const pasos = [
        { titulo: "Completá tus datos", hecho: !!(perfil.fechaNacimiento && perfil.telefono), destino: "#perfil",
          texto: "Cargá tu fecha de nacimiento y tu teléfono." + (conWhatsApp ? " Ahí también podés activar, si querés, los avisos por WhatsApp." : ""),
          boton: "Ir a Mis datos", nota: "Podés cambiarlos cuando quieras." },
        { titulo: "Firmá el consentimiento de telemedicina", hecho: !!consentimiento.firmado, destino: "consentimiento.html",
          texto: "Es el permiso para atenderte por video. Lo necesitás para confirmar cualquier turno, así que conviene firmarlo ahora.",
          boton: "Leer y firmar", nota: "Lo podés volver a leer en Consentimiento." },
        { titulo: "Contanos sobre vos", hecho: !!informe || conProfesional, destino: "#admision",
          texto: "Completá el informe de admisión. Con lo que nos cuentes, el equipo elige el profesional adecuado para vos.",
          boton: "Completar el informe de admisión", nota: "Los campos con * son obligatorios." },
        { titulo: "Entrevista de admisión", hecho: conProfesional, destino: "#admision", espera: true,
          requiere: informe ? null : "Se habilita cuando envíes tu informe de admisión.",
          texto: "Una charla corta por video con el equipo de admisión, para conocerte antes de asignarte un profesional.",
          aviso: enEntrevista
              ? "Tu entrevista ya está programada. Te llegó un aviso a la campanita con el día, la hora y el botón para entrar."
              : "Tu informe está en revisión. No tenés que hacer nada por ahora: cuando tengamos la fecha de la entrevista te avisamos por la campanita y por mail." },
        { titulo: "Conocé a tu profesional", hecho: conProfesional, destino: "#profesionales",
          requiere: "Te lo asignamos después de la entrevista de admisión.",
          texto: "Ya tenés profesional asignado. Podés ver su especialidad y, si alguna vez lo necesitás, pedir un cambio.",
          boton: "Ver Mis profesionales", nota: "" },
        { titulo: "Pedí tu primer turno", hecho: turnos.some(x => x.estadoDescripcion !== "Cancelado"), destino: "#agenda",
          requiere: conProfesional ? null : "Se habilita cuando tengas profesional asignado.",
          texto: "Elegí un día y un horario libre de tu profesional. Si tu profesional te propone un turno, solo tenés que confirmarlo.",
          boton: "Pedir un turno", nota: "" },
        { titulo: "Tu primera sesión", hecho: turnos.some(x => x.estadoDescripcion === "Realizado"), destino: "#sesion",
          requiere: turnos.some(x => x.estadoDescripcion !== "Cancelado") ? null : "Se habilita cuando tengas tu primer turno.",
          texto: "El día del turno entrás a la Sala de video. Antes de conectarte elegís si aceptás o no la lectura de tus expresiones.",
          boton: "Ir a la Sala de video",
          nota: proximo ? `Tu turno: ${YS.fecha.larga(proximo.fecha, true)} a las ${String(proximo.hora).substring(0, 5)} hs. La sala abre 10 minutos antes.` : "La sala abre 10 minutos antes del horario." }
    ];

    const indiceActual = pasos.findIndex(p => !p.hecho);
    if (indiceActual === -1) return null;
    const paso = indiceActual + 1;
    const hechos = pasos.filter(p => p.hecho).length;

    const check = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"></path></svg>`;
    const candado = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"></rect><path d="M8 11V7a4 4 0 0 1 8 0v4"></path></svg>`;
    const flecha = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12h14"></path><path d="m12 5 7 7-7 7"></path></svg>`;
    const reloj = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path></svg>`;

    const filas = pasos.map((p, i) => {
        const n = i + 1;
        if (p.hecho) return `
            <li class="pp-paso pp-paso--hecho">
                <span class="pp-num pp-num--hecho">${check}</span>
                <span class="pp-titulo">${YS.esc(p.titulo)}</span>
                <span class="pp-chip">Hecho</span>
                <a href="${p.destino}" class="pp-ver" aria-label="Ver: ${YS.esc(p.titulo)}">Ver</a>
            </li>`;
        if (i === indiceActual) return `
            <li class="pp-paso pp-paso--actual" aria-current="step">
                <span class="pp-num pp-num--actual">${n}</span>
                <div class="pp-cuerpo">
                    <span class="pp-etiqueta">Tu próximo paso</span>
                    <span class="pp-titulo-grande">${YS.esc(p.titulo)}</span>
                    <span class="pp-texto">${YS.esc(p.texto)}</span>
                    ${p.espera
                        ? `<div class="pp-aviso">${reloj}<span>${YS.esc(p.aviso)}</span></div>`
                        : `<div class="pp-acciones">
                               <button type="button" class="ui-btn ui-btn--primary pp-boton" onclick="${ir(p.destino)}">${YS.esc(p.boton)}${flecha}</button>
                               ${p.nota ? `<span class="texto-suave texto-chico">${YS.esc(p.nota)}</span>` : ""}
                           </div>`}
                </div>
            </li>`;
        // Sin un requisito pendiente (los pasos 1 a 3 nunca tienen) se puede hacer ya, en cualquier orden; si no, el candado dice de qué depende.
        if (!p.requiere && p.espera) return `
            <li class="pp-paso pp-paso--pendiente">
                <span class="pp-num">${n}</span>
                <div class="pp-cuerpo-chico">
                    <span class="pp-titulo">${YS.esc(p.titulo)}</span>
                    <span class="texto-suave texto-chico">${YS.esc(p.aviso)}</span>
                </div>
            </li>`;
        if (!p.requiere) return `
            <li class="pp-paso pp-paso--pendiente">
                <span class="pp-num">${n}</span>
                <div class="pp-cuerpo-chico">
                    <span class="pp-titulo">${YS.esc(p.titulo)}</span>
                    <span class="texto-suave texto-chico">Podés hacerlo cuando quieras.</span>
                </div>
                <a href="${p.destino}" class="pp-ver" aria-label="${YS.esc(p.boton)}">Ir</a>
            </li>`;
        return `
            <li class="pp-paso pp-paso--pendiente">
                <span class="pp-num">${n}</span>
                <div class="pp-cuerpo-chico">
                    <span class="pp-titulo">${YS.esc(p.titulo)}</span>
                    <span class="texto-suave texto-chico">${YS.esc(p.requiere)}</span>
                </div>
                <span class="pp-candado" role="img" aria-label="Todavía no disponible">${candado}</span>
            </li>`;
    }).join("");

    const html = `
        <section class="pp" aria-label="Tus primeros pasos en YoSoy">
            <p class="pp-intro texto-suave">Estos son tus primeros pasos en YoSoy. Te mostramos siempre cuál sigue.</p>
            <div class="card pp-progreso">
                <div class="pp-progreso-fila">
                    <span class="texto-fuerte">Paso ${paso} de ${pasos.length}</span>
                    <span class="texto-suave texto-chico">${hechos === 1 ? "1 paso hecho" : `${hechos} pasos hechos`}</span>
                </div>
                <div class="pp-barra" role="progressbar" aria-label="Avance de los primeros pasos" aria-valuemin="0" aria-valuemax="${pasos.length}" aria-valuenow="${hechos}">
                    <div style="width: ${Math.round(hechos / pasos.length * 100)}%"></div>
                </div>
            </div>
            <div class="pp-columnas">
                <ol class="pp-lista">${filas}</ol>
                <aside class="pp-lateral">
                    <div class="card">
                        <div class="titulo-tarjeta">Cómo sigue todo</div>
                        <p class="pp-lateral-texto">Con tu informe de admisión, el equipo elige el profesional adecuado para vos. Después de una entrevista corta por video, te lo asignamos y ya podés pedir turnos.</p>
                    </div>
                    ${conWhatsApp ? `
                    <div class="card">
                        <div class="titulo-tarjeta">Avisos por WhatsApp</div>
                        <p class="pp-lateral-texto">Es opcional. Si los activás, te avisamos solo el día y la hora de tus turnos.</p>
                        <a href="#perfil" class="pp-enlace">Configurar en Mis datos</a>
                    </div>` : ""}
                </aside>
            </div>
        </section>`;
    return { paso, html };
}

// ── AGENDA / CALENDARIO ────────────────────────────────────
async function renderAgenda(c) {
    try {
        const res = await YS.fetch(`/pacientes/${idPaciente}/turnos-detalle`);
        turnosCalendario = await res.json();
    } catch {
        turnosCalendario = [];
    }

    // Cargar profesionales del paciente
    let profesionales = [];
    try {
        profesionales = await YS.fetch(`/pacientes/${idPaciente}/mis-profesionales`).then(r => r.json());
    } catch {}

    if (YS.shell.actual !== "agenda") return;
    c.innerHTML = `
        ${profesionales.length ? `
        <div class="card">
            <div class="filtro-prof">
                <label for="selProfCalendario">Ver disponibilidad de:</label>
                <select id="selProfCalendario" class="turno-input" onchange="cambiarProfCalendario()">
                    <option value="">— Sin filtro —</option>
                    ${profesionales.map(p => `<option value="${p.idProfesional}">${YS.esc(p.nombre)} — ${YS.esc(p.especialidad)}</option>`).join("")}
                </select>
                <div id="diasAtencion" class="dias-atencion"></div>
            </div>
        </div>` : ""}

        <div class="card">
            <div class="cal-nav">
                <button onclick="cambiarMes(-1)">&#8249;</button>
                <span class="cal-mes" id="calTitulo"></span>
                <button onclick="cambiarMes(1)">&#8250;</button>
            </div>
            <div class="cal-grid" id="calGrid"></div>
            <div class="cal-leyenda">
                <span class="ui-chip ui-chip--ok">Turno confirmado</span>
                <span class="ui-chip ui-chip--warn">Pendiente</span>
                <span class="ui-chip ui-chip--marca">Días que atiende el profesional</span>
                <span class="ui-chip ui-chip--neutral">Sin disponibilidad</span>
            </div>
        </div>
        <div class="panel-turno" id="panelTurno"></div>
    `;

    dibujarCalendario();
}

async function cambiarProfCalendario() {
    const sel = document.getElementById("selProfCalendario");
    _idProfCalendario = parseInt(sel.value) || null;
    _agendaProfCalendario = [];

    const diasDiv = document.getElementById("diasAtencion");

    if (!_idProfCalendario) {
        diasDiv.textContent = "";
        dibujarCalendario();
        return;
    }

    try {
        const agenda = await YS.fetch(`/profesionales/${_idProfCalendario}/agenda`).then(r => r.json());
        _agendaProfCalendario = agenda.filter(a => a.disponible);
    } catch {}

    if (_agendaProfCalendario.length) {
        const dias = _agendaProfCalendario.map(a => a.dia).join(", ");
        diasDiv.textContent = `Atiende los: ${dias}`;
    } else {
        diasDiv.textContent = "Sin días configurados";
    }

    dibujarCalendario();
}

function cambiarMes(delta) {
    calMes += delta;
    if (calMes > 11) { calMes = 0;  calAnio++; }
    if (calMes < 0)  { calMes = 11; calAnio--; }
    dibujarCalendario();
    if (!_modoReprogramar)
        document.getElementById("panelTurno").classList.remove("visible");
}

function dibujarCalendario() {
    document.getElementById("calTitulo").textContent = `${MESES[calMes]} ${calAnio}`;

    const grid = document.getElementById("calGrid");
    const hoy  = new Date();
    const primerDia  = new Date(calAnio, calMes, 1);
    const diasEnMes  = new Date(calAnio, calMes + 1, 0).getDate();
    const inicioCelda = (primerDia.getDay() + 6) % 7; // Lunes = 0

    const turnosPorDia = {};
    turnosCalendario.forEach(t => {
        const f = new Date(t.fecha);
        if (f.getFullYear() === calAnio && f.getMonth() === calMes) {
            const d = f.getDate();
            if (!turnosPorDia[d]) turnosPorDia[d] = [];
            turnosPorDia[d].push(t);
        }
    });

    let html = DIAS.map(d => `<div class="cal-header">${d}</div>`).join("");

    for (let i = 0; i < inicioCelda; i++)
        html += `<div class="cal-dia vacio"></div>`;

    for (let dia = 1; dia <= diasEnMes; dia++) {
        const tieneT    = !!turnosPorDia[dia];
        const pendiente = tieneT && turnosPorDia[dia].some(t => t.estadoDescripcion === "Pendiente");
        const esHoy     = dia === hoy.getDate() && calMes === hoy.getMonth() && calAnio === hoy.getFullYear();
        const esPasado  = new Date(calAnio, calMes, dia) < new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
        const fechaD    = new Date(calAnio, calMes, dia);
        const esSeleccionado = _reprogFechaSeleccionada &&
            fechaD.toDateString() === _reprogFechaSeleccionada.toDateString();

        let clases, onclick;
        if (_modoReprogramar) {
            const enAgenda  = _reprogAgenda.some(a => DIA_DB_NUM[normalizar(a.dia)] === fechaD.getDay());
            const ocupado   = tieneT && turnosPorDia[dia].some(t => t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado");
            const valido    = !esPasado && enAgenda && !ocupado;
            clases  = ["cal-dia", esPasado ? "pasado" : valido ? "reprog-valido" : "reprog-invalido", esSeleccionado ? "reprog-seleccionado" : ""].join(" ").trim();
            onclick = valido ? `onclick="seleccionarDiaReprog(${dia})"` : "";

            
        } else {
            const soloCancelado = tieneT && turnosPorDia[dia].every(t => t.estadoDescripcion === "Cancelado");

            // Si hay profesional seleccionado, resaltar días disponibles
            let esDisponibleProf = false;
            if (_idProfCalendario && _agendaProfCalendario.length) {
                esDisponibleProf = _agendaProfCalendario.some(
                    a => DIA_DB_NUM[normalizar(a.dia)] === fechaD.getDay()
                );
            }

            let claseT;
            if (tieneT) {
                claseT = soloCancelado ? "con-turno-cancelado" : pendiente ? "con-turno-pendiente" : "con-turno";
            } else if (esPasado) {
                claseT = "pasado";
            } else if (_idProfCalendario && esDisponibleProf) {
                claseT = "dia-disponible-prof";
            } else if (_idProfCalendario && !esDisponibleProf) {
                claseT = "dia-no-disponible-prof";
            } else {
                claseT = "futuro";
            }

            clases  = ["cal-dia", claseT, esHoy ? "hoy" : ""].join(" ").trim();
            onclick = !esPasado ? `onclick="mostrarPanelDia(${dia})"` : "";
        }



        html += `<div class="${clases}" ${onclick}>${dia}</div>`;
    }

    grid.innerHTML = html;
}

let _diaSeleccionado = null;

async function mostrarPanelDia(dia) {
    _diaSeleccionado = dia;
    document.querySelectorAll(".cal-dia").forEach(el => el.classList.remove("seleccionado"));
    document.querySelectorAll(".cal-dia").forEach(el => {
        if (el.textContent.trim() === String(dia)) el.classList.add("seleccionado");
    });
    const turnosDia = turnosCalendario.filter(t => {
        const f = new Date(t.fecha);
        return f.getFullYear() === calAnio && f.getMonth() === calMes && f.getDate() === dia;
    });

    const fechaStr = `${calAnio}-${String(calMes + 1).padStart(2,"0")}-${String(dia).padStart(2,"0")}`;
    const panel = document.getElementById("panelTurno");
    panel.classList.add("visible");

    let html = "";

    if (turnosDia.length) {
        turnoSeleccionado = turnosDia[0];
        const t = turnoSeleccionado;
        const hora = t.hora.substring(0, 5);
        const puedeAccionar = t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado";

        html += `
            <div class="panel-top${t.estadoDescripcion === 'Cancelado' ? ' panel-cancelado' : ''}">
                <div class="panel-hora-bloque">
                    <div class="panel-hora">${hora}</div>
                    <div class="panel-hora-label">hs</div>
                </div>
                <div class="panel-info-principal">
                    <div class="panel-profesional">${YS.esc(t.nombreProfesional)}</div>
                    <div class="panel-especialidad">${YS.esc(t.especialidad)}</div>
                </div>
                ${propuestoPorProfesional(t)
                    ? `<span class="panel-estado-badge estado-propuesto">Por confirmar</span>`
                    : `<span class="panel-estado-badge estado-${YS.esc(t.estadoDescripcion)}">${YS.esc(t.estadoDescripcion)}</span>`}
            </div>
            <div class="panel-chips">
                <div class="chip"><span class="chip-label">Modalidad</span><span class="chip-val">${YS.esc(t.modalidad ?? "-")}</span></div>
                ${t.motivo ? `<div class="chip chip-wide"><span class="chip-label">Motivo</span><span class="chip-val">${YS.esc(t.motivo)}</span></div>` : ""}
            </div>
            ${propuestoPorProfesional(t) ? `<p class="panel-aviso">Tu profesional te propuso este turno. Confirmalo para que quede agendado, o reprogramalo si no te queda bien.</p>` : ""}
            ${puedeAccionar ? `
            <div class="panel-acciones">
                ${propuestoPorProfesional(t) ? `<button class="btn-accion btn-guardar" onclick="confirmarTurnoPaciente(${t.idTurno})">Confirmar turno</button>` : ""}
                <button class="btn-accion btn-cancelar" onclick="cancelarTurno(${t.idTurno})">Cancelar turno</button>
                <button class="btn-accion btn-reprogramar" id="btnReprogramar" onclick="abrirFormReprogramar(${t.idTurno}, ${t.idProfesional})">Reprogramar</button>
            </div>
            <div class="form-reprogramar" id="formReprogramar">
                <p class="reprog-instruccion">Seleccioná el nuevo día en el calendario</p>
                <div id="reprogDetalles" class="oculto">
                    <div class="reprog-dia-seleccionado" id="reprogDiaLabel"></div>
                    <div class="form-fila mt-12">
                        <div class="form-grupo form-grupo--ancho">
                            <label>Hora</label>
                            <select id="reprogHora" class="turno-input"></select>
                        </div>
                        <div class="form-grupo form-grupo--ancho">
                            <label>Motivo</label>
                            <input type="text" id="reprogMotivo" class="turno-input" placeholder="Motivo del cambio...">
                        </div>
                    </div>
                    <button class="btn-accion btn-guardar mt-12" onclick="confirmarReprogramar(${t.idTurno})">Confirmar reprogramación</button>
                </div>
                <button class="btn-accion btn-cancelar mt-12" onclick="cancelarModoReprogramar()">Cancelar reprogramación</button>
            </div>` : ""}
            <div class="panel-separador"></div>`;
    }

    const tieneActivo = turnosDia.some(t => t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado");

    html += !tieneActivo ? `<div class="panel-solicitar-bloque">
        <button class="btn-solicitar-dia" onclick="abrirFormSolicitud('${fechaStr}')">+ Solicitar turno para este día</button>
    </div>
    <div class="form-solicitar-dia" id="formSolicitar">
        <div class="form-fila">
            <div class="form-grupo form-grupo--ancho">
                <label>Profesional</label>
                <select id="solProf" class="turno-input" onchange="cargarHorasSolicitud('${fechaStr}')">
                    <option value="">Cargando...</option>
                </select>
            </div>
            <div class="form-grupo">
                <label>Hora</label>
                <select id="solHora" class="turno-input" disabled><option value="">Elegí un profesional</option></select>
            </div>
        </div>
        <div class="form-grupo mt-8">
            <label>Motivo (opcional)</label>
            <input type="text" id="solMotivo" class="turno-input" placeholder="Escribí el motivo...">
        </div>
        <button class="btn-accion btn-guardar mt-12" onclick="confirmarSolicitudDia('${fechaStr}')">Confirmar solicitud</button>
    </div>` : "";

    panel.innerHTML = html;
    if (!tieneActivo) cargarProfesionalesSolicitud(fechaStr);
}

async function abrirFormSolicitud(fechaStr) {
    document.getElementById("formSolicitar").classList.toggle("visible");
}

async function cargarProfesionalesSolicitud(fechaStr) {
    // Si ya hay turno activo ese día, no permitir solicitar otro
    const fechaDate = new Date(fechaStr + "T12:00:00").toDateString();
    const yaOcupado = turnosCalendario.some(t =>
        new Date(t.fecha).toDateString() === fechaDate &&
        t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado"
    );
    const sel = document.getElementById("solProf");
    if (!sel) return;
    if (yaOcupado) {
        sel.innerHTML = `<option value="">Ya tenés un turno ese día</option>`;
        sel.disabled = true;
        document.querySelector(".btn-accion.btn-guardar[onclick*='confirmarSolicitudDia']")?.setAttribute("disabled", true);
        return;
    }
    const profs = await YS.fetch(`/pacientes/${idPaciente}/mis-profesionales`).then(r => r.json()).catch(() => []);
    sel.innerHTML = profs.length
        ? `<option value="">Seleccioná un profesional</option>` + profs.map(p => `<option value="${p.idProfesional}">${YS.esc(p.nombre)} — ${YS.esc(p.especialidad)}</option>`).join("")
        : `<option value="">Sin profesionales asignados</option>`;
}

function normalizar(s) {
    return (s ?? "").toLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "");
}

// Fecha y hora de hoy en Argentina: es contra lo que valida el servidor ("Ese horario ya pasó"). El servidor sigue
// siendo quien decide; esto solo evita ofrecer en el combo horarios de hoy que ya pasaron.
function ahoraEnArgentina() {
    const partes = new Intl.DateTimeFormat("en-CA", {
        timeZone: "America/Argentina/Buenos_Aires", year: "numeric", month: "2-digit", day: "2-digit",
        hour: "2-digit", minute: "2-digit", hourCycle: "h23"
    }).formatToParts(new Date());
    const v = tipo => partes.find(p => p.type === tipo).value;
    return { fecha: `${v("year")}-${v("month")}-${v("day")}`, minutos: Number(v("hour")) * 60 + Number(v("minute")) };
}

// Minutos del día que ya pasaron, si fechaStr (YYYY-MM-DD) es hoy en Argentina; -1 si es otro día (no hay nada pasado).
function minutosPasadosHoy(fechaStr) {
    const ahora = ahoraEnArgentina();
    return fechaStr === ahora.fecha ? ahora.minutos : -1;
}

async function cargarHorasSolicitud(fechaStr) {
    const idProf = parseInt(document.getElementById("solProf").value);
    const sel    = document.getElementById("solHora");
    if (!idProf) { sel.innerHTML = `<option value="">Elegí un profesional</option>`; sel.disabled = true; return; }

    const fecha  = new Date(fechaStr + "T12:00:00");
    const diaSem = fecha.getDay();
    const agenda = await YS.fetch(`/profesionales/${idProf}/agenda`).then(r => r.json()).catch(() => []);
    const slot   = agenda.find(a => a.disponible && DIA_DB_NUM[normalizar(a.dia)] === diaSem);

    if (!slot) { sel.innerHTML = `<option value="">Sin disponibilidad ese día</option>`; sel.disabled = true; return; }

    const [hI, mI] = slot.horaInicio.substring(0,5).split(":").map(Number);
    const [hF, mF] = slot.horaFin.substring(0,5).split(":").map(Number);

    // Usar el intervalo configurado por el médico (por defecto 30 min)
    const intervalo = slot.intervaloMinutos ?? 30;

    // Obtener turnos ya ocupados ese día para ese profesional
    let turnosOcupados = [];
    try {
        const r = await YS.fetch(`/turnos/ocupados?idProfesional=${idProf}&fecha=${fechaStr}`);
        if (r.ok) turnosOcupados = await r.json();
    } catch {}

    // No hay anticipación mínima: se puede pedir para dentro de unos minutos. Lo único que no se ofrece es lo que ya pasó
    // (solo cuenta si el día elegido es hoy).
    const minutosPasados = minutosPasadosHoy(fechaStr);

    const opciones = [];
    let cur = hI * 60 + mI;
    while (cur + intervalo <= hF * 60 + mF) {
        const hh = String(Math.floor(cur/60)).padStart(2,"0");
        const mm = String(cur%60).padStart(2,"0");
        const horaStr = `${hh}:${mm}`;
        const ocupado = turnosOcupados.some(t => t.hora?.substring(0,5) === horaStr);
        if (!ocupado && cur > minutosPasados) {
            opciones.push(`<option value="${horaStr}">${horaStr}</option>`);
        }
        cur += intervalo;
    }
    sel.innerHTML = opciones.length ? opciones.join("") : `<option value="">Sin horarios disponibles</option>`;
    sel.disabled  = !opciones.length;
}

async function confirmarSolicitudDia(fechaStr) {
    const idProf    = parseInt(document.getElementById("solProf").value);
    const hora      = document.getElementById("solHora").value;
    const modalidad = "Virtual";
    const motivo    = document.getElementById("solMotivo").value || null;

    if (!idProf) { toast("Seleccioná un profesional.", "warn"); return; }
    if (!hora)   { toast("Seleccioná un horario.", "warn"); return; }

    const res = await YS.fetch(`/turnos/solicitar`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:    parseInt(idPaciente),
            idProfesional: idProf,
            fecha:         new Date(fechaStr + "T12:00:00").toISOString(),
            hora:          hora + ":00",
            modalidad,
            motivo
        })
    });

    if (res.ok) {
        toast("Turno solicitado. El profesional lo confirmará.");
        const r = await YS.fetch(`/pacientes/${idPaciente}/turnos-detalle`);
        turnosCalendario = await r.json();
        dibujarCalendario();
        document.getElementById("panelTurno").classList.remove("visible");
        sugerirAvisosWhatsApp();
    } else toast(await YS.leerMensaje(res, "No se pudo solicitar el turno."), "error");
}

function toggleReprogramar() {
    document.getElementById("formReprogramar")?.classList.toggle("visible");
}

async function cancelarTurno(id) {
    if (!await confirmar("¿Cancelar este turno?", { titulo: "Cancelar turno", textoConfirmar: "Cancelar turno", textoCancelar: "Volver", peligro: true })) return;
    const rCancel = await YS.fetch(`/turnos/${id}/cancelar?idPaciente=${idPaciente}`, { method: "PUT" });
    if (!rCancel.ok) { toast(await YS.leerMensaje(rCancel, "No se pudo cancelar el turno."), "error"); return; }
    const res = await YS.fetch(`/pacientes/${idPaciente}/turnos-detalle`);
    turnosCalendario = await res.json();
    dibujarCalendario();
    document.getElementById("panelTurno").classList.remove("visible");
}

// ── REPROGRAMAR ────────────────────────────────────────────
async function abrirFormReprogramar(idTurno, idProf) {
    const form = document.getElementById("formReprogramar");
    const abriendo = !form.classList.contains("visible");
    form.classList.toggle("visible");
    if (!abriendo) { cancelarModoReprogramar(); return; }

    _idTurnoReprog = idTurno;
    _reprogFechaSeleccionada = null;

    try {
        const agenda = await YS.fetch(`/profesionales/${idProf}/agenda`).then(r => r.json());
        _reprogAgenda = agenda.filter(a => a.disponible);
    } catch { _reprogAgenda = []; }

    _modoReprogramar = true;
    dibujarCalendario();
}

function seleccionarDiaReprog(dia) {
    _reprogFechaSeleccionada = new Date(calAnio, calMes, dia);
    dibujarCalendario();

    const slot = _reprogAgenda.find(a => DIA_DB_NUM[normalizar(a.dia)] === _reprogFechaSeleccionada.getDay());

    const label = _reprogFechaSeleccionada.toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long" });
    document.getElementById("reprogDiaLabel").textContent = label;

    const sel = document.getElementById("reprogHora");
    if (slot) {
        const [hI, mI] = slot.horaInicio.substring(0,5).split(":").map(Number);
        const [hF, mF] = slot.horaFin.substring(0,5).split(":").map(Number);
        // Igual que al solicitar: sin anticipación mínima, pero no se ofrece lo que ya pasó hoy (el servidor lo rechaza, y
        // reprogramar cancela primero el turno anterior).
        const f = _reprogFechaSeleccionada;
        const fechaStr = `${f.getFullYear()}-${String(f.getMonth() + 1).padStart(2,"0")}-${String(f.getDate()).padStart(2,"0")}`;
        const minutosPasados = minutosPasadosHoy(fechaStr);
        const opciones = [];
        let cur = hI * 60 + mI;
        while (cur + 60 <= hF * 60 + mF) {
            const hh = String(Math.floor(cur/60)).padStart(2,"0");
            const mm = String(cur%60).padStart(2,"0");
            if (cur > minutosPasados) opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
            cur += 60;
        }
        sel.innerHTML = opciones.join("") || `<option value="">Sin horarios</option>`;
    } else {
        sel.innerHTML = `<option value="">Sin horarios ese día</option>`;
    }

    document.getElementById("reprogDetalles").classList.remove("oculto");
}

function cancelarModoReprogramar() {
    _modoReprogramar = false;
    _idTurnoReprog   = null;
    _reprogFechaSeleccionada = null;
    _reprogAgenda    = [];
    dibujarCalendario();
    document.getElementById("formReprogramar")?.classList.remove("visible");
}

async function confirmarReprogramar(idTurno) {
    const hora   = document.getElementById("reprogHora").value;
    const motivo = document.getElementById("reprogMotivo").value || null;
    if (!_reprogFechaSeleccionada) { toast("Seleccioná un día en el calendario.", "warn"); return; }
    if (!hora) { toast("Seleccioná un horario.", "warn"); return; }

    const fechaNueva = _reprogFechaSeleccionada;

    // Reprogramar es cancelar el turno actual y pedir el nuevo. Como se puede pedir para dentro de unos minutos, el
    // horario elegido puede haber pasado mientras se decidía: el servidor lo rechazaría DESPUÉS de cancelar y la
    // persona se quedaría sin turno. Se comprueba antes de cancelar.
    const fechaElegida = `${fechaNueva.getFullYear()}-${String(fechaNueva.getMonth() + 1).padStart(2,"0")}-${String(fechaNueva.getDate()).padStart(2,"0")}`;
    const [horaElegida, minElegido] = hora.split(":").map(Number);
    if (horaElegida * 60 + minElegido <= minutosPasadosHoy(fechaElegida)) {
        toast("Ese horario ya pasó. Elegí otro y volvé a confirmar; tu turno actual sigue como estaba.", "warn");
        return;
    }

    const rCancel = await YS.fetch(`/turnos/${idTurno}/cancelar?idPaciente=${idPaciente}`, { method: "PUT" });
    if (!rCancel.ok) { toast(await YS.leerMensaje(rCancel, "No se pudo cancelar el turno anterior."), "error"); return; }

    const rNuevo = await YS.fetch(`/turnos/solicitar`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:    parseInt(idPaciente),
            idProfesional: turnoSeleccionado.idProfesional,
            fecha:         new Date(fechaNueva.getFullYear(), fechaNueva.getMonth(), fechaNueva.getDate(), 12).toISOString(),
            hora:          hora + ":00",
            modalidad:     "Virtual",
            motivo
        })
    });
    if (!rNuevo.ok) { toast(await YS.leerMensaje(rNuevo, "No se pudo pedir el nuevo horario."), "error"); return; }

    _modoReprogramar = false;
    _reprogFechaSeleccionada = null;
    _reprogAgenda = [];

    const res = await YS.fetch(`/pacientes/${idPaciente}/turnos-detalle`);
    turnosCalendario = await res.json();
    dibujarCalendario();
    document.getElementById("panelTurno").classList.remove("visible");
    toast("Turno reprogramado. Quedará pendiente de confirmación.");
}

// ── MIS PROFESIONALES ──────────────────────────────────────
async function renderProfesionales(c) {
    let lista;
    try { lista = await YS.api(`/pacientes/${idPaciente}/mis-profesionales`); }
    catch (e) { c.innerHTML = YS.errorCarga({ texto: e.message, reintentar: "mostrarSeccion('profesionales')" }); return; }
    if (YS.shell.actual !== "profesionales") return;
    window._misProfesionales = lista;

    if (!lista.length) {
        c.innerHTML = `<div class="card">${YS.vacio({ icono: "pacientes", titulo: "Todavía no tenés profesionales asignados",
            texto: "Cuando el equipo de admisión te asigne uno, va a aparecer acá." })}</div>`;
        return;
    }
    c.innerHTML = `<div class="prof-lista">${lista.map(p => `
        <div class="prof-card">
            <div class="prof-cabecera">
                ${YS.avatar(p.nombre, "p" + p.idProfesional)}
                <div>
                    <div class="prof-nombre">${YS.esc(p.nombre)}</div>
                    <div class="prof-especialidad">${YS.esc(p.especialidad || "")}</div>
                </div>
            </div>
            ${p.mail     ? `<div class="prof-dato">${YS.esc(p.mail)}</div>`     : ""}
            ${p.telefono ? `<div class="prof-dato">${YS.esc(p.telefono)}</div>` : ""}
            <button class="ui-btn ui-btn--sm prof-cambio" onclick="abrirModalCambio(${p.idProfesional}, ${p.idEspecialidad})">Solicitar cambio de profesional</button>
        </div>`).join("")}</div>`;
}

// ── SOLICITAR CAMBIO ────────────────────────────────────────
// El motivo es opcional, como antes. El configurador elige el nuevo profesional al aceptar.
async function abrirModalCambio(idProfOrigen, idEsp, nombreProf = null) {
    nombreProf ??= (window._misProfesionales || []).find(p => p.idProfesional === idProfOrigen)?.nombre ?? "tu profesional";
    const motivo = await YS.pedirTexto({
        titulo: "Solicitar cambio de profesional",
        mensaje: `Profesional actual: ${nombreProf}. Tu solicitud será revisada por el equipo y te avisaremos cuando sea procesada.`,
        etiqueta: "Motivo (opcional)", placeholder: "Contanos por qué querés cambiar de profesional…",
        multilinea: true, maximo: 500, requerido: false, textoConfirmar: "Enviar solicitud"
    });
    if (motivo === null) return;   // canceló
    await enviarSolicitudCambio(idProfOrigen, motivo || null);
}

async function enviarSolicitudCambio(idProfOrigen, motivo) {
    try {
        const res = await YS.fetch(`/derivaciones`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                idPaciente:           parseInt(idPaciente),
                idProfesionalOrigen:  idProfOrigen,
                idProfesionalDestino: idProfOrigen, // el configurador elige el destino al aceptar
                motivo
            })
        });
        if (res.ok) toast("Solicitud enviada. El equipo la va a revisar.");
        else toast(await YS.leerMensaje(res, "No se pudo enviar la solicitud."), "error");
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    }
}

// ── MI PERFIL ──────────────────────────────────────────────
// Las mismas reglas que el registro (Pantalla_Usuario/Usuario/js/app.js)
const RE_DNI   = /^\d{7,9}$/;
const RE_MAIL  = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const RE_TEL   = /^[\d\s+()-]{6,20}$/;
// Un campo opcional que estaba vacío (null) y sigue vacío se manda como null, no como ""
const opcional = (valor, original) => valor === '' && original == null ? null : valor;

async function renderPerfil(c) {
    let p;
    try { p = await YS.api(`/pacientes/${idPaciente}`); }
    catch (e) { c.innerHTML = YS.errorCarga({ texto: e.message, reintentar: "mostrarSeccion('perfil')" }); return; }
    if (YS.shell.actual !== "perfil") return;
    window._perfilPaciente = p;
    const fn = p.fechaNacimiento ? p.fechaNacimiento.substring(0, 10) : "";
    const campo = (id, etiqueta, valor, extra = "", ancho = false) => `
        <div class="ui-campo${ancho ? " ui-campo--ancho" : ""}"><label for="${id}">${etiqueta}</label>
            <input class="ui-input" id="${id}" value="${YS.esc(valor ?? "")}"${extra}></div>`;

    c.innerHTML = `
        <div class="card perfil-form">
            <form id="formPerfil" novalidate>
                <div class="ui-form-grid">
                    ${campo("pNombre", "Nombre y apellido", p.nombre, ' autocomplete="name" maxlength="100"')}
                    ${campo("pDni", "DNI", p.dni, ' inputmode="numeric" maxlength="9"')}
                    <div class="ui-campo"><label for="pFechaNac">Fecha de nacimiento</label>
                        <input class="ui-input" type="date" id="pFechaNac" value="${fn}" max="${YS.fecha.aISO(new Date())}"></div>
                    ${campo("pTelefono", "Teléfono", p.telefono, ' type="tel" autocomplete="tel" maxlength="20"')}
                    ${campo("pMail", "Mail", p.mail, ' type="email" autocomplete="email" maxlength="100"')}
                    ${campo("pDomicilio", "Domicilio", p.domicilio, ' autocomplete="street-address" maxlength="150"', true)}
                </div>
                <div class="ui-form-acciones">
                    <button type="submit" class="ui-btn ui-btn--primary" id="btnGuardarPerfil">Guardar cambios</button>
                </div>
            </form>
        </div>
        <div class="card perfil-form oculto" id="avisosWhatsApp" aria-live="polite"><p class="texto-suave">Cargando tus avisos por WhatsApp…</p></div>`;
    document.getElementById("formPerfil").addEventListener("submit", e => { e.preventDefault(); guardarPerfil(p.idPaciente); });
    renderAvisosWhatsApp();
}

async function guardarPerfil(id) {
    const hoy = YS.fecha.aISO(new Date());
    const valido = YS.form.validar([
        { id: "pNombre", requerido: "Ingresá tu nombre y apellido.", minimo: 3 },
        { id: "pDni", requerido: "Ingresá tu DNI.", patron: RE_DNI, mensajePatron: "El DNI tiene que tener entre 7 y 9 números, sin puntos." },
        { id: "pFechaNac", validar: v => v && v > hoy ? "La fecha de nacimiento no puede ser futura." : null },
        { id: "pTelefono", patron: RE_TEL, mensajePatron: "Usá solo números, espacios, + o guiones." },
        { id: "pMail", patron: RE_MAIL, mensajePatron: "Ingresá un mail válido." }
    ]);
    if (!valido) return;

    const valor = id_ => document.getElementById(id_).value.trim();
    const body = {
        idPaciente:      id,
        nombre:          valor("pNombre"),
        dni:             valor("pDni"),
        fechaNacimiento: valor("pFechaNac") || null,
        domicilio:       opcional(valor("pDomicilio"), window._perfilPaciente?.domicilio),
        telefono:        opcional(valor("pTelefono"), window._perfilPaciente?.telefono),
        mail:            opcional(valor("pMail"), window._perfilPaciente?.mail),
        estado:          window._perfilPaciente?.estado || "Activo"
    };
    const boton = document.getElementById("btnGuardarPerfil");
    boton.disabled = true;
    try {
        const res = await YS.fetch(`/pacientes/${id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });
        if (res.ok) {
            window._perfilPaciente = { ...window._perfilPaciente, ...body };
            toast("Tus datos quedaron guardados.");
            renderAvisosWhatsApp();   // si cambió el teléfono, los avisos por WhatsApp quedan en pausa hasta que los active para el número nuevo
        }
        else toast(await YS.leerMensaje(res, "No se pudieron guardar los cambios."), "error");
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    } finally {
        boton.disabled = false;
    }
}

// ── AVISOS POR WHATSAPP (consentimiento) ───────────────────
// El texto, su versión y el estado los sirve la API (GET /consentimiento-whatsapp); lo que se lee acá es lo que queda registrado con la
// aceptación. Aceptar es una acción EXPLÍCITA: la casilla nunca viene tildada y no se guarda nada hasta apretar "Activar". Quién es sale
// del token en el servidor, no de esta pantalla. Todo lo que manda el servidor entra por textContent, nunca como HTML.
// La tarjeta nace OCULTA y solo se muestra si la API la ofrece: con la llave Mensajeria:AvisosPorWhatsApp apagada (por defecto) la API contesta
// motivo "avisos-apagados" y no se ve nada (ni el link para escribirle a la clínica), así no se promete un aviso que no va a llegar.
let _estadoWhatsApp = null;

async function renderAvisosWhatsApp() {
    const tarjeta = document.getElementById("avisosWhatsApp");
    if (!tarjeta) return;
    let e;
    try { e = await YS.api("/consentimiento-whatsapp"); }
    catch {
        if (document.getElementById("avisosWhatsApp") === tarjeta) {
            tarjeta.classList.remove("oculto");
            tarjeta.replaceChildren(YS.el("p", { class: "texto-suave" }, "No pudimos consultar tus avisos por WhatsApp. ",
                YS.el("button", { type: "button", class: "ui-btn ui-btn--sm", text: "Reintentar", on: { click: renderAvisosWhatsApp } })));
        }
        return;
    }
    if (document.getElementById("avisosWhatsApp") !== tarjeta) return;   // el usuario ya cambió de sección
    if (YS.whatsapp.apagado(e)) {                                         // avisos apagados: la tarjeta no existe para la persona
        _estadoWhatsApp = null;
        tarjeta.classList.add("oculto");
        tarjeta.replaceChildren();
        return;
    }
    _estadoWhatsApp = e;
    tarjeta.classList.remove("oculto");

    const activo = e.estado === "acepta";
    const pausado = e.estado === "telefono-cambiado" || e.estado === "texto-desactualizado";
    const fecha = f => (f ? YS.fecha.corta(f) : "");
    const [tono, etiqueta] = activo ? ["ok", "Activados"] : pausado ? ["warn", "Pausados"] : ["neutral", "Desactivados"];
    const explicacion = ({
        "acepta": `Te avisamos por WhatsApp de tus turnos, al celular que figura en tu perfil. Los activaste el ${fecha(e.aceptadoUtc)}.`,
        "telefono-cambiado": "Cambiaste tu teléfono: los avisos por WhatsApp quedaron en pausa hasta que los actives para el número nuevo.",
        "texto-desactualizado": "Actualizamos el texto de los avisos por WhatsApp: para seguir recibiéndolos, leelo y volvé a aceptarlo.",
        "revocado": `Los desactivaste el ${fecha(e.revocadoUtc)}. Seguís viendo tus avisos dentro de YoSoy.`,
        "sin-decision": "Si los activás, te avisamos de tus turnos por WhatsApp. Es opcional y no cambia tu atención."
    })[e.estado] ?? "";

    const partes = [
        YS.el("div", { class: "wa-cabecera" },
            YS.el("div", { class: "titulo-tarjeta", text: "Avisos por WhatsApp" }),
            YS.el("span", { class: `ui-chip ui-chip--${tono} ui-chip--sin-punto`, text: etiqueta }))
    ];
    if (explicacion) partes.push(YS.el("p", { class: "texto-suave", text: explicacion }));
    if (e.estado === "revocado")
        partes.push(YS.el("p", { class: "texto-suave texto-chico", text: "Si antes respondiste BAJA por WhatsApp, además de activarlos acá respondé ALTA al mismo número." }));

    // El texto: a la vista cuando hay que decidir; plegado si ya está activo.
    if (e.version && Array.isArray(e.texto)) {
        const texto = YS.el("div", { class: "wa-texto", role: "group", "aria-label": "Texto del consentimiento de los avisos por WhatsApp" });
        YS.whatsapp.dibujarTexto(texto, e);
        partes.push(activo
            ? YS.el("details", { class: "wa-detalle" }, YS.el("summary", { text: "Ver el texto que aceptaste" }), texto)
            : texto);
    }

    if (!e.disponible && !activo)
        partes.push(YS.el("div", { class: "ui-alerta ui-alerta--info", text: YS.whatsapp.motivo(e.motivo) ?? "Por ahora no podés activar los avisos por WhatsApp." }));

    if (e.disponible && !activo) {
        const casilla = YS.el("input", { type: "checkbox", id: "aceptoAvisosWhatsApp", autocomplete: "off" });
        const boton = YS.el("button", {
            type: "button", class: "ui-btn ui-btn--primary", id: "btnActivarWhatsApp", disabled: true,
            text: pausado ? "Activar para este número" : "Activar avisos", on: { click: activarAvisosWhatsApp }
        });
        casilla.addEventListener("change", () => { boton.disabled = !casilla.checked; });
        partes.push(YS.el("div", { class: "wa-acciones" },
            YS.el("label", { class: "ui-check", for: "aceptoAvisosWhatsApp" }, casilla,
                YS.el("span", { text: "Leí el texto y quiero recibir avisos de mis turnos por WhatsApp" })),
            boton));
        // La otra forma: escribirle primero a la clínica. Solo si la API tiene el número, y no a quien acaba de desactivar los avisos.
        const contacto = e.estado === "revocado" ? null : YS.whatsapp.contacto(e);
        if (contacto) partes.push(contacto);
    }
    if (activo || pausado)
        partes.push(YS.el("div", { class: "wa-acciones" },
            YS.el("button", { type: "button", class: "ui-btn", id: "btnDesactivarWhatsApp", text: "Dejar de recibir avisos por WhatsApp", on: { click: desactivarAvisosWhatsApp } })));

    tarjeta.replaceChildren(...partes);
}

// Después de pedir o confirmar un turno: si todavía no decidió nada sobre los avisos por WhatsApp, se le cuentan (YS.whatsapp.sugerirTrasUnTurno).
// No se espera ni puede romper el flujo del turno, que ya terminó. "Ver cómo activarlos" lleva al perfil: acá no se activa nada.
function sugerirAvisosWhatsApp() {
    YS.whatsapp.sugerirTrasUnTurno({ alActivar: () => mostrarSeccion("perfil") });
}

async function activarAvisosWhatsApp() {
    const casilla = document.getElementById("aceptoAvisosWhatsApp");
    const boton = document.getElementById("btnActivarWhatsApp");
    if (!casilla?.checked || !_estadoWhatsApp?.version) return;   // sin el tilde de la persona no se manda nada
    boton.disabled = true;
    try {
        const res = await YS.fetch("/consentimiento-whatsapp/aceptar", { method: "POST", json: { version: _estadoWhatsApp.version } });
        if (res.ok) toast("Listo: vas a recibir los avisos de tus turnos por WhatsApp.");
        else toast(await YS.leerMensaje(res, "No se pudieron activar los avisos por WhatsApp."), "error");
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    }
    await renderAvisosWhatsApp();   // también si falló: si el texto cambió mientras tanto, se lee de nuevo el vigente
}

async function desactivarAvisosWhatsApp() {
    if (!await confirmar("¿Dejar de recibir avisos de tus turnos por WhatsApp? Los vas a seguir viendo dentro de YoSoy.",
        { titulo: "Avisos por WhatsApp", textoConfirmar: "Dejar de recibirlos", textoCancelar: "Volver" })) return;
    try {
        const res = await YS.fetch("/consentimiento-whatsapp/revocar", { method: "POST" });
        if (res.ok) toast("Listo: dejaste de recibir avisos por WhatsApp.");
        else toast(await YS.leerMensaje(res, "No se pudieron desactivar los avisos por WhatsApp."), "error");
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    }
    await renderAvisosWhatsApp();
}

function renderSesion() {
    return `
        <div class="card sesion-entrada">
            <div class="titulo-tarjeta">Entrar a la sala de video</div>
            <p class="texto-suave mb-12">Ingresá el código de sala que te pasó tu profesional. También te llega en las notificaciones.</p>
            <form id="formSala" class="ui-campo" novalidate onsubmit="event.preventDefault(); entrarSesion();">
                <label for="codigoSala">Código de sala</label>
                <div class="sesion-entrada-fila">
                    <input type="text" id="codigoSala" class="ui-input" placeholder="Ingresá el código de tu sala" maxlength="30" autocomplete="off"
                           oninput="this.value = this.value.toUpperCase()">
                    <button type="submit" class="ui-btn ui-btn--primary">Entrar</button>
                </div>
            </form>
        </div>`;
}

function entrarSesion() {
    const codigo = document.getElementById('codigoSala').value.trim();
    if (!codigo) { YS.form.marcar('codigoSala', 'Ingresá el código de sala.'); document.getElementById('codigoSala').focus(); return; }
    window.location.href = 'sesion_video.html?sala=' + encodeURIComponent(codigo) + '&idPaciente=' + idPaciente;
}

async function renderAdmision(c) {
    // ¿Ya mandó el informe? (404 = todavía no)
    let inf;
    try { inf = await YS.api(`/pacientes/${idPaciente}/informe-admision`, { permitir404: true }); }
    catch (e) { c.innerHTML = YS.errorCarga({ texto: e.message, reintentar: "mostrarSeccion('admision')" }); return; }
    if (YS.shell.actual !== "admision") return;
    if (inf) {
        c.innerHTML = `
            <div class="card card--angosta">
                <div class="titulo-tarjeta">Tu informe de admisión ya fue enviado</div>
                <p class="mb-12">Estado actual: <span class="panel-estado-badge estado-${YS.esc(String(inf.estado).replace(/\s+/g, "-"))}">${YS.esc(inf.estado)}</span></p>
                <p class="texto-suave">Cuando el equipo de admisión revise tu informe, te vamos a contactar para coordinar la primera entrevista.</p>
            </div>`;
        return;
    }

    const especialidades = await YS.api("/especialidades").catch(() => []);
    if (YS.shell.actual !== "admision") return;
    const opts = especialidades
        .filter(e => e.estado === "Activo")
        .map(e => `<option value="${e.idEspecialidad}">${YS.esc(e.nombre)}</option>`)
        .join("");
    const texto = (id, etiqueta, filas, placeholder = "") => `
        <div class="ui-campo ui-campo--ancho"><label for="${id}">${etiqueta}</label>
            <textarea id="${id}" class="ui-textarea" rows="${filas}" maxlength="1000" placeholder="${placeholder}"></textarea></div>`;

    c.innerHTML = `
        <div class="card card--angosta form-admision">
            <p class="texto-suave mb-16">Completá este formulario para que el equipo de admisión pueda conocerte y asignarte el profesional adecuado. Los campos con * son obligatorios.</p>
            <form id="formAdmision" novalidate>
                <div class="ui-form-grid">
                    <div class="ui-campo ui-campo--ancho"><label for="admEspecialidad">Especialidad solicitada *</label>
                        <select id="admEspecialidad" class="ui-select"><option value="">Elegí una especialidad</option>${opts}</select></div>
                    ${texto("admMotivo", "Motivo de consulta *", 3, "Contanos brevemente por qué buscás atención")}
                    <div class="ui-campo"><label for="admTratAnterior">¿Hiciste algún tratamiento antes?</label>
                        <select id="admTratAnterior" class="ui-select"><option value="false">No</option><option value="true">Sí</option></select></div>
                    <div class="ui-campo oculto" id="campoTratDetalle"><label for="admTratDetalle">¿Cuál?</label>
                        <input type="text" id="admTratDetalle" class="ui-input" maxlength="300"></div>
                    <div class="ui-campo"><label for="admMedicacion">¿Tomás alguna medicación?</label>
                        <select id="admMedicacion" class="ui-select"><option value="false">No</option><option value="true">Sí</option></select></div>
                    <div class="ui-campo oculto" id="campoMedDetalle"><label for="admMedDetalle">¿Cuál?</label>
                        <input type="text" id="admMedDetalle" class="ui-input" maxlength="300"></div>
                </div>

                <div class="ui-form-seccion">Conociéndote mejor</div>
                <p class="texto-suave mb-12">Estas preguntas nos ayudan a darte una atención personalizada. Son opcionales.</p>
                <div class="ui-form-grid">
                    ${texto("admGustos", "Gustos e intereses", 3, "Actividades que disfrutás, hobbies, música, películas o libros, deportes, qué te ayuda a relajarte, algo que quieras aprender")}
                    ${texto("admPreferencias", "Preferencias durante la atención", 3, "Cómo preferís que se dirijan a vos, algo que te genere ansiedad en una consulta, apoyos que necesites")}
                    <div class="ui-campo"><label for="admTipoEntrevista">Tipo de entrevista preferida</label>
                        <select id="admTipoEntrevista" class="ui-select"><option value="Sin preferencia">Sin preferencia</option><option value="Individual">Individual</option><option value="Familiar">Familiar</option><option value="Grupal">Grupal</option></select></div>
                    ${texto("admRedApoyo", "Red de apoyo", 2, "¿Con quién convivís? ¿Contás con familiares o personas de confianza que te acompañen?")}
                    ${texto("admObjetivos", "¿Qué esperás lograr con el tratamiento?", 2)}
                </div>
                <div class="ui-form-acciones">
                    <button type="submit" class="ui-btn ui-btn--primary" id="btnEnviarAdmision">Enviar informe de admisión</button>
                </div>
            </form>
        </div>`;
    // "¿Cuál?" aparece solo si se contesta que sí
    const alternar = (select, campo) => document.getElementById(select).addEventListener("change", e =>
        document.getElementById(campo).classList.toggle("oculto", e.target.value !== "true"));
    alternar("admTratAnterior", "campoTratDetalle");
    alternar("admMedicacion", "campoMedDetalle");
    document.getElementById("formAdmision").addEventListener("submit", e => { e.preventDefault(); enviarAdmision(); });
}

async function enviarAdmision() {
    const valido = YS.form.validar([
        { id: "admEspecialidad", requerido: "Elegí la especialidad." },
        { id: "admMotivo", requerido: "Contanos el motivo de consulta." }
    ]);
    if (!valido) return;

    const valor = id => document.getElementById(id).value.trim() || null;
    const tratamiento = document.getElementById("admTratAnterior").value === "true";
    const medicacion  = document.getElementById("admMedicacion").value === "true";
    const body = {
        idPaciente:                 parseInt(idPaciente),
        idEspecialidad:             parseInt(document.getElementById("admEspecialidad").value),
        motivoConsulta:             valor("admMotivo"),
        tratamientoAnterior:        tratamiento,
        tratamientoAnteriorDetalle: tratamiento ? valor("admTratDetalle") : null,
        recibeMedicacion:           medicacion,
        medicacionDetalle:          medicacion ? valor("admMedDetalle") : null,
        gustosIntereses:            valor("admGustos"),
        preferenciasAtencion:       valor("admPreferencias"),
        tipoEntrevista:             document.getElementById("admTipoEntrevista").value,
        redApoyo:                   valor("admRedApoyo"),
        objetivosPaciente:          valor("admObjetivos"),
        estado:                     "Pendiente"
    };

    const boton = document.getElementById("btnEnviarAdmision");
    boton.disabled = true;
    try {
        const r = await YS.fetch("/informes-admision", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });
        if (r.ok) {
            toast("Informe enviado. El equipo de admisión te va a contactar.");
            mostrarSeccion("admision");
        } else toast(await YS.leerMensaje(r, "No se pudo enviar el informe."), "error");
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    } finally {
        boton.disabled = false;
    }
}

async function cargarNotificaciones() {
    let notifs = [];
    try { notifs = await (await YS.fetch("/pacientes/" + idPaciente + "/notificaciones")).json(); } catch(e) { return; }
    const noLeidas = notifs.filter(function(n) { return n.leida === false; });
    window._notifsFlotantes = noLeidas;
    if (noLeidas.length === 0) return;

    let cont = document.getElementById("notif-container");
    if (cont === null) {
        cont = document.createElement("div");
        cont.id = "notif-container";
        cont.className = "notif-flotantes";
        document.body.appendChild(cont);
    }
    cont.innerHTML = "";

    // Como máximo tres, primero los que tienen acción (entrar a la entrevista); el resto queda en la campana
    const MAX_FLOTANTES = 3;
    const conAccion = noLeidas.filter(n => n.link);
    const visibles = conAccion.concat(noLeidas.filter(n => !n.link)).slice(0, MAX_FLOTANTES);
    visibles.forEach(function(n) {
        const card = document.createElement("div");
        card.className = "notif-flotante";
        let botones = '<button class="ui-btn ui-btn--sm" onclick="marcarLeida(' + n.idNotificacion + ')">Cerrar</button>';
        if (n.link !== null && n.link !== "") {
            botones = '<button class="ui-btn ui-btn--sm ui-btn--primary" onclick="unirseEntrevista(null, ' + n.idNotificacion + ')">Unirse a la entrevista</button>' + botones;
        }
        card.innerHTML = '<div class="notif-titulo">' + YS.esc(n.titulo) + '</div>' +
            '<div class="notif-mensaje">' + YS.esc(n.mensaje) + '</div>' +
            '<div class="notif-botones">' + botones + '</div>';
        cont.appendChild(card);
    });
    const resto = noLeidas.length - visibles.length;
    if (resto > 0) {
        const mas = document.createElement("div");
        mas.className = "notif-flotante notif-flotante--mas";
        mas.innerHTML = '<div class="notif-mensaje">Tenés ' + resto + ' aviso' + (resto === 1 ? '' : 's') + ' más.</div>' +
            '<div class="notif-botones"><button class="ui-btn ui-btn--sm" onclick="YS.shell.campana.abrir()">Ver todos</button></div>';
        cont.appendChild(mas);
    }
}

async function marcarLeida(idNotif) {
    try { await YS.fetch("/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}
    YS.shell.campana.refrescar();
    cargarNotificaciones();
    const cont = document.getElementById("notif-container");
    if (cont !== null) cont.innerHTML = "";
    cargarNotificaciones();
}

// El código de la sala se toma de la notificación cargada (antes viajaba dentro del onclick)
async function unirseEntrevista(codigoSala, idNotif) {
    codigoSala ??= (window._notifsFlotantes || []).find(n => n.idNotificacion === idNotif)?.link;
    if (!codigoSala) return;
    try { await YS.fetch("/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}
    window.location.href = "sesion_video.html?sala=" + encodeURIComponent(codigoSala) + "&idPaciente=" + idPaciente;
}

cargarNotificaciones();

// Turno pendiente que propuso el profesional y el paciente todavía no confirmó.
function propuestoPorProfesional(t) {
    return t.estadoDescripcion === "Pendiente" && t.creadoPor === "profesional" && !t.confirmadoPaciente;
}

async function confirmarTurnoPaciente(idTurno) {
    if (!await confirmar("¿Confirmar este turno?", { titulo: "Confirmar turno", textoConfirmar: "Confirmar" })) return;
    try {
        const r = await YS.fetch("/turnos/" + idTurno + "/confirmar-paciente", { method: "PUT" });
        if (r.ok) {
            toast("Turno confirmado. El profesional va a generar la sala para ese día.");
            if (document.getElementById("calGrid")) {
                const res = await YS.fetch("/pacientes/" + idPaciente + "/turnos-detalle");
                turnosCalendario = await res.json();
                dibujarCalendario();
                document.getElementById("panelTurno").classList.remove("visible");
            } else mostrarSeccion(YS.shell.actual);
            sugerirAvisosWhatsApp();
        } else if (r.status === 409) {
            // RN-02: falta firmar el consentimiento de telemedicina.
            if (await confirmar("Antes de confirmar el turno tenés que firmar el consentimiento informado para atención por telemedicina. ¿Querés firmarlo ahora?",
                                { titulo: "Falta el consentimiento", textoConfirmar: "Firmar ahora", textoCancelar: "Más tarde" }))
                location.href = "consentimiento.html";
        } else toast(await YS.leerMensaje(r, "No se pudo confirmar el turno."), "error");
    } catch(e) { toast("No se pudo conectar con el servidor.", "error"); }
}

// La campanita y su casilla de notificaciones ahora son la campana de la barra superior
// (shared/shell.js): al abrirla marca las notificaciones como leídas, igual que antes.
// Si se leyeron desde ahí, se sacan los avisos flotantes.
document.addEventListener("ys:avisos-leidos", () => document.getElementById("notif-container")?.remove());

YS.shell.iniciar({ rol: "paciente", navegar: id => mostrarSeccion(id) });
