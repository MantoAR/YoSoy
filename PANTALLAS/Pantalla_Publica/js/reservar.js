// Página pública de reserva por link (Fase 3a, Paso 3): sin sesión, sin cookies propias,
// sin ids internos. Identifica al paciente reusando el login/registro ya existente (mismo
// localStorage débil de siempre, documentado en CAMBIOS_AGUS.md) y, si ya hay sesión, pide
// el turno directo contra /public/profesionales/{slug}/reservar (mismo TurnoReglasService
// del Paso 1 — RN-01/02/05/07 se aplican igual que en cualquier otro camino).

const slug = location.pathname.split('/').filter(Boolean).pop();
const params = new URLSearchParams(location.search);
const slotPreseleccionado = params.get('slot');

function sesionPaciente() {
    const idPaciente = localStorage.getItem('idPaciente');
    return localStorage.getItem('rol') === 'paciente' && idPaciente ? parseInt(idPaciente) : null;
}

function agrupar(lista) {
    const porDia = new Map();
    lista.forEach(iso => {
        const d = new Date(iso);
        const clave = YS.fecha.aISO(d);
        if (!porDia.has(clave)) porDia.set(clave, { fecha: d, horas: [] });
        porDia.get(clave).horas.push({ iso, texto: YS.fecha.hora(d) });
    });
    return porDia;
}

async function cargar() {
    const cont = document.getElementById('contenido');
    cont.innerHTML = YS.cargando('Buscando los horarios disponibles…');

    let r;
    try { r = await YS.fetch(`/public/profesionales/${encodeURIComponent(slug)}/disponibilidad?dias=14`, { sinRedireccion: true }); }
    catch { cont.innerHTML = YS.errorCarga({ texto: 'No se pudo conectar con el servidor.', reintentar: 'cargar()' }); return; }

    if (r.status === 404) {
        cont.innerHTML = YS.vacio({ icono: 'sin-conexion', titulo: 'Este link no está disponible', texto: 'Puede que ya no exista o que el profesional lo haya desactivado.' });
        return;
    }
    if (r.status === 429) {
        cont.innerHTML = YS.vacio({ icono: 'reloj', titulo: 'Demasiados pedidos', texto: await YS.leerMensaje(r, 'Probá de nuevo en un minuto.') });
        return;
    }
    if (!r.ok) {
        cont.innerHTML = YS.errorCarga({ texto: await YS.leerMensaje(r, 'No se pudo cargar la página.'), reintentar: 'cargar()' });
        return;
    }

    render(await r.json());
}

function render(data) {
    const cont = document.getElementById('contenido');
    const porDia = agrupar(data.horariosLibres || []);
    cont.innerHTML = `
        <div class="reservar-header">
            <h1>${YS.esc(data.nombre)}</h1>
            ${data.especialidad ? `<p class="texto-suave">${YS.esc(data.especialidad)}</p>` : ''}
            <p class="texto-suave">Las consultas son por videollamada.</p>
        </div>
        ${!porDia.size
            ? YS.vacio({ icono: 'reloj', titulo: 'No hay horarios libres por ahora', texto: 'Probá de nuevo más tarde.' })
            : `<p class="texto-suave mb-12">Elegí un horario. Si no tenés cuenta de paciente todavía, te vamos a pedir que inicies sesión o te registres antes de confirmar.</p>
               <div class="vista-previa-grilla">${[...porDia.values()].map(dia => `
                <div class="vista-previa-dia">
                    <div class="vista-previa-fecha">${YS.esc(YS.fecha.separador(dia.fecha))}</div>
                    <div class="vista-previa-horas">${dia.horas.map(h =>
                        `<button type="button" class="ui-btn ui-btn--sm" onclick="elegirHorario('${h.iso}')">${YS.esc(h.texto)}</button>`).join('')}</div>
                </div>`).join('')}</div>`}
        <div id="avisoReserva"></div>`;

    if (slotPreseleccionado && (data.horariosLibres || []).includes(slotPreseleccionado)) {
        elegirHorario(slotPreseleccionado);
    } else if (slotPreseleccionado) {
        YS.toast('Ese horario ya no está disponible. Elegí otro.', 'warn');
    }
}

function elegirHorario(iso) {
    const idPaciente = sesionPaciente();
    if (!idPaciente) {
        const destino = `/reservar/${slug}?slot=${encodeURIComponent(iso)}`;
        location.href = `/pantallas/index.html?return=${encodeURIComponent(destino)}`;
        return;
    }
    confirmarReserva(idPaciente, iso);
}

async function confirmarReserva(idPaciente, iso) {
    document.getElementById('avisoReserva').innerHTML = '';
    try {
        const r = await YS.fetch(`/public/profesionales/${encodeURIComponent(slug)}/reservar`, {
            method: 'POST', json: { idPaciente, momento: iso, motivo: null }, sinRedireccion: true
        });
        if (r.ok) {
            document.getElementById('contenido').innerHTML = YS.vacio({
                icono: 'check', titulo: '¡Listo!', texto: 'Tu turno quedó pendiente. El profesional te lo va a confirmar.'
            });
            return;
        }
        const data = await r.json().catch(() => ({}));
        if (data.codigo === 'RN-02' && data.urlConsentimiento) {
            document.getElementById('avisoReserva').innerHTML = `
                <div class="ui-alerta ui-alerta--info mt-12">
                    ${YS.esc(data.mensaje || 'Antes de pedir el turno tenés que firmar el consentimiento informado.')}
                    <a href="${YS.esc(data.urlConsentimiento)}">Firmar consentimiento</a>
                </div>`;
            return;
        }
        if (r.status === 409) { YS.toast(data.mensaje || 'Ese horario se acaba de ocupar.', 'warn'); cargar(); return; }
        YS.toast(data.mensaje || 'No se pudo pedir el turno.', 'error');
    } catch {
        YS.toast('No se pudo conectar con el servidor.', 'error');
    }
}

cargar();
