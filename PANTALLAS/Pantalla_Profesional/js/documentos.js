// Documentos clínicos en el panel del profesional (migración 35):
//   - pestaña "Documentos" de la historia clínica: lista, Ver, Descargar PDF, Agregar addendum y Nuevo documento;
//   - "Nuevo documento" en 5 pasos: tipo → datos (precargados) → vista previa → confirmación → emitido;
//   - sugerencias al cerrar una sesión con Alta o Derivación (las manda sesion_video.html);
//   - la firma para los PDF, en Mi perfil.
// Todo lo que viene del servidor pasa por YS.esc. El servidor vuelve a controlar quién puede emitir, ver y descargar.
const DOC_GRUPOS = ["Consentimientos", "Constancias", "Informes", "Clínica"];
const docFecha = f => f ? YS.fecha.corta(f) + " " + YS.fecha.hora(f) : "";

// ── Pestaña de la historia clínica ──────────────────────────
function htmlDocumentosHC(idPaciente, lista) {
    return `
        <div class="ui-entre mb-12">
            <p class="texto-suave mb-0">Consentimientos, constancias e informes emitidos. Un documento emitido no se modifica: las correcciones van como addendum.</p>
            <button class="ui-btn ui-btn--primary ui-btn--sm" type="button" onclick="abrirNuevoDocumento(${idPaciente})">${YS.icono('mas')}Nuevo documento</button>
        </div>
        ${!lista.length ? YS.vacio({ icono: 'documento', texto: 'Todavía no hay documentos emitidos para este paciente.' }) : `
        <div class="tabla-wrapper"><table>
            <thead><tr><th>Fecha</th><th>Documento</th><th>Versión</th><th>Profesional</th><th>Código</th><th></th></tr></thead>
            <tbody>${lista.map(d => `<tr>
                <td>${YS.esc(docFecha(d.fechaEmision))}</td>
                <td>${YS.esc(d.titulo)}${d.provisoria ? ' <span class="ui-chip ui-chip--warn ui-chip--sin-punto">Provisorio</span>' : ''}${d.addenda ? ` <span class="ui-chip ui-chip--info ui-chip--sin-punto">${d.addenda} addendum</span>` : ''}</td>
                <td>v${d.version}</td>
                <td>${YS.esc(d.profesional)}</td>
                <td><code>${YS.esc(d.codigo)}</code></td>
                <td class="celda-acciones">
                    <button class="ui-btn ui-btn--sm" type="button" onclick="verDocumento(${d.idDocumento})">Ver</button>
                    <button class="ui-btn ui-btn--sm" type="button" onclick="descargarDocumento(${d.idDocumento})">PDF</button>
                </td>
            </tr>`).join('')}</tbody>
        </table></div>`}`;
}

async function recargarDocumentosHC(idPaciente) {
    const panel = document.getElementById('hc-panel-documentos');
    if (!panel) return;
    const lista = await YS.api(`/pacientes/${idPaciente}/documentos`).catch(() => []);
    panel.innerHTML = htmlDocumentosHC(idPaciente, lista);
    const n = document.querySelector('#hc-tab-documentos .ui-contador');
    if (n) n.textContent = lista.length;
}

async function descargarDocumento(id) {
    try { await YS.documento.descargar(`/documentos/${id}/pdf`); }
    catch (e) { toast(e.message, 'error'); }
}

async function verDocumento(id) {
    const cuerpo = YS.el('div', { html: YS.cargando() });
    const m = YS.modal({
        titulo: 'Documento', cuerpo, tamano: 'grande',
        acciones: [
            { texto: 'Verificar integridad', alClick: () => verificarDocumento(id) },
            { texto: 'Descargar PDF', alClick: () => descargarDocumento(id) },
            { texto: 'Cerrar', tipo: 'primary', foco: true, alClick: c => c.cerrar() }
        ]
    });
    let d;
    try { d = await YS.api(`/documentos/${id}`); }
    catch (e) { cuerpo.innerHTML = YS.errorCarga({ texto: e.message }); return; }
    if (m.cerrado) return;
    m.elemento.querySelector('.ui-modal-titulo').textContent = d.titulo;
    cuerpo.innerHTML = `
        <p class="texto-suave mb-12">Emitido el ${YS.esc(docFecha(d.fechaEmision))} por ${YS.esc(d.profesional)} · versión ${d.version} · código <code>${YS.esc(d.codigo)}</code></p>
        ${YS.documento.html(d.documento)}
        <h4 class="mt-16">Addenda</h4>
        <div id="doc-addenda">${d.addenda.length ? d.addenda.map(a => `
            <div class="ui-alerta ui-alerta--info mb-8"><strong>${YS.esc(docFecha(a.fecha))} · ${YS.esc(a.profesional)}</strong><br>${YS.esc(a.texto).replace(/\n/g, '<br>')}</div>`).join('')
            : '<p class="texto-suave">Sin addenda.</p>'}</div>
        ${d.puedeAgregarAddendum ? `
            <div class="ui-campo mt-12"><label for="doc-addendum">Agregar addendum</label>
                <textarea id="doc-addendum" class="ui-input" rows="3" maxlength="4000" placeholder="Corrección o aclaración. Queda con fecha y hora, y no se puede borrar."></textarea></div>
            <button class="ui-btn ui-btn--primary ui-btn--sm" type="button" id="doc-addendum-btn">Agregar addendum</button>` : ''}`;
    document.getElementById('doc-addendum-btn')?.addEventListener('click', async () => {
        const txt = document.getElementById('doc-addendum').value.trim();
        if (!txt) { YS.form.marcar('doc-addendum', 'Escribí el addendum.'); return; }
        if (!await confirmar('El addendum queda guardado en la historia clínica con fecha y hora, y no se puede modificar ni borrar.', { titulo: 'Agregar addendum', textoConfirmar: 'Agregar' })) return;
        try {
            await YS.api(`/documentos/${id}/addenda`, { method: 'POST', json: { texto: txt } });
            toast('Addendum agregado.');
            m.cerrar();
            verDocumento(id);
        } catch (e) { toast(e.message, 'error'); }
    });
}

async function verificarDocumento(id) {
    try {
        const r = await YS.api(`/documentos/${id}/integridad`);
        YS.toast(r.integro ? `Íntegro: el documento y su PDF no cambiaron desde que se emitieron (código ${r.codigo}).`
            : 'Atención: el documento no coincide con su huella. Avisá al Profesional auditor.', r.integro ? 'ok' : 'error', 7000);
    } catch (e) { toast(e.message, 'error'); }
}

// ── Nuevo documento ─────────────────────────────────────────
// opciones: { clave, idTurno, idEventoCrisis, alEmitir }
async function abrirNuevoDocumento(idPaciente, opciones = {}) {
    const estado = { idPaciente, clave: opciones.clave || null, idTurno: opciones.idTurno || null, idEventoCrisis: opciones.idEventoCrisis || null, datos: {} };
    const cuerpo = YS.el('div', { html: YS.cargando() });
    const m = YS.modal({ titulo: 'Nuevo documento', cuerpo, tamano: 'grande' });
    const pie = YS.el('div', { class: 'ui-modal-pie' });
    m.elemento.appendChild(pie);
    const botones = lista => {
        pie.replaceChildren(...lista.map(b => YS.el('button', {
            class: 'ui-btn' + (b.tipo ? ' ui-btn--' + b.tipo : ''), type: 'button', text: b.texto, on: { click: b.alClick }
        })));
    };

    let plantillas = [];
    try { plantillas = await YS.api('/documentos/plantillas'); }
    catch (e) { cuerpo.innerHTML = YS.errorCarga({ texto: e.message }); return; }

    // Paso 1: tipo
    function paso1() {
        m.elemento.querySelector('.ui-modal-titulo').textContent = 'Nuevo documento · Elegí el tipo';
        cuerpo.innerHTML = `<div class="doc-tipos">${DOC_GRUPOS.map(g => {
            const del = plantillas.filter(p => p.grupo === g);
            return del.length ? `<div><h4>${YS.esc(g)}</h4>${del.map(p => `
                <button class="doc-tipo" type="button" data-clave="${YS.esc(p.clave)}"><strong>${YS.esc(p.titulo)}</strong>
                ${p.provisoria ? ' <span class="ui-chip ui-chip--warn ui-chip--sin-punto">Provisorio</span>' : ''}
                <span class="secundario">${YS.esc(p.descripcion || '')}</span></button>`).join('')}</div>` : '';
        }).join('')}</div>`;
        cuerpo.querySelectorAll('.doc-tipo').forEach(b => b.addEventListener('click', () => { estado.clave = b.dataset.clave; estado.datos = {}; paso2(); }));
        botones([{ texto: 'Cancelar', alClick: () => m.cerrar() }]);
    }

    // Paso 2: datos
    async function paso2(cambioDeTurno = false) {
        cuerpo.innerHTML = YS.cargando();
        let pre;
        try {
            pre = await YS.api(`/pacientes/${idPaciente}/documentos/precarga?clave=${encodeURIComponent(estado.clave)}` + (estado.idTurno ? `&idTurno=${estado.idTurno}` : ''));
        } catch (e) { cuerpo.innerHTML = YS.errorCarga({ texto: e.message }); botones([{ texto: 'Volver', alClick: paso1 }]); return; }
        estado.plantilla = pre.plantilla;
        // Lo que ya cargó el profesional se respeta; al cambiar de turno, solo se reemplazan la fecha y las horas del turno.
        if (cambioDeTurno)
            pre.plantilla.campos.filter(c => c.precarga?.startsWith('turno.')).forEach(c => { estado.datos[c.clave] = pre.valores[c.clave] ?? ''; });
        else estado.datos = { ...pre.valores, ...estado.datos };
        m.elemento.querySelector('.ui-modal-titulo').textContent = pre.plantilla.titulo;
        const precargados = new Set(pre.precargados || []);

        if (pre.requiereAutorizacion) {
            cuerpo.innerHTML = `<div class="ui-alerta ui-alerta--warn">Para emitir un informe para la escuela hace falta una <strong>autorización para compartir información</strong> emitida para este paciente, firmada por el paciente o su responsable.</div>`;
            botones([{ texto: 'Volver', alClick: paso1 },
                     { texto: 'Emitir la autorización primero', tipo: 'primary', alClick: () => { estado.clave = 'autorizacion_info'; estado.datos = {}; paso2(); } }]);
            return;
        }

        const campo = c => {
            const v = estado.datos[c.clave] ?? '';
            const id = 'docf-' + c.clave;
            const marca = precargados.has(c.clave) ? '<span class="ui-chip ui-chip--info ui-chip--sin-punto doc-precargado">Precargado</span>' : '';
            const etiqueta = `<label for="${id}">${YS.esc(c.etiqueta)}${c.requerido ? ' *' : ''}${marca}</label>`;
            const ayuda = c.ayuda ? `<span class="texto-chico ui-muted">${YS.esc(c.ayuda)}</span>` : '';
            switch (c.tipo) {
                case 'textoLargo': return `<div class="ui-campo ui-campo--ancho">${etiqueta}<textarea id="${id}" class="ui-input" rows="3" maxlength="4000">${YS.esc(v)}</textarea>${ayuda}</div>`;
                case 'seleccion': return `<div class="ui-campo">${etiqueta}<select id="${id}" class="ui-select"><option value="">Elegí…</option>${(c.opciones || []).map(o =>
                    `<option${o === v ? ' selected' : ''}>${YS.esc(o)}</option>`).join('')}</select>${ayuda}</div>`;
                case 'check': return `<div class="ui-campo"><label class="ui-check"><input type="checkbox" id="${id}"${v === 'true' ? ' checked' : ''}> ${YS.esc(c.etiqueta)}</label>${ayuda}</div>`;
                default: {
                    const tipo = { numero: 'number', fecha: 'date', hora: 'time' }[c.tipo] || 'text';
                    return `<div class="ui-campo">${etiqueta}<input id="${id}" type="${tipo}" class="ui-input" maxlength="300" value="${YS.esc(v)}">${ayuda}</div>`;
                }
            }
        };
        cuerpo.innerHTML = `
            ${pre.plantilla.provisoria ? '<div class="ui-alerta ui-alerta--warn mb-12">El texto de este documento es provisorio: todavía no lo validó un abogado.</div>' : ''}
            ${pre.turnos ? `<div class="ui-campo mb-12"><label for="doc-turno">Turno</label>
                <select id="doc-turno" class="ui-select"><option value="">Elegí el turno…</option>${pre.turnos.map(t =>
                    `<option value="${t.idTurno}"${t.idTurno === estado.idTurno ? ' selected' : ''}>${YS.esc(YS.fecha.corta(t.fecha))} ${YS.esc(t.hora)} hs · ${YS.esc(t.estado)}</option>`).join('')}</select>
                ${!pre.turnos.length ? '<span class="texto-chico ui-muted">No hay turnos realizados o confirmados con este paciente: completá la fecha y la hora a mano.</span>' : ''}</div>` : ''}
            <form id="doc-form" class="ui-form-grid" novalidate>${pre.plantilla.campos.map(campo).join('')}</form>`;
        document.getElementById('doc-turno')?.addEventListener('change', e => {
            leerFormulario();
            estado.idTurno = parseInt(e.target.value) || null;
            paso2(true);
        });
        botones([{ texto: 'Volver', alClick: () => { leerFormulario(); paso1(); } },
                 { texto: 'Vista previa', tipo: 'primary', alClick: () => { leerFormulario(); paso3(); } }]);
    }

    function leerFormulario() {
        (estado.plantilla?.campos || []).forEach(c => {
            const el = document.getElementById('docf-' + c.clave);
            if (!el) return;
            estado.datos[c.clave] = c.tipo === 'check' ? (el.checked ? 'true' : 'false') : el.value;
        });
    }

    // Paso 3: vista previa (la arma el servidor, con el mismo motor que la emisión)
    async function paso3() {
        const faltan = (estado.plantilla.campos || []).filter(c => c.requerido && c.tipo !== 'check' && !String(estado.datos[c.clave] ?? '').trim());
        if (faltan.length) {
            faltan.forEach(c => YS.form.marcar('docf-' + c.clave, 'Completá este dato.'));
            toast('Completá los datos marcados.', 'warn');
            return;
        }
        cuerpo.innerHTML = YS.cargando('Armando la vista previa…');
        let r;
        try { r = await YS.api(`/pacientes/${idPaciente}/documentos/vista-previa`, { method: 'POST', json: { clave: estado.clave, datos: estado.datos, idTurno: estado.idTurno } }); }
        catch (e) { cuerpo.innerHTML = YS.errorCarga({ texto: e.message }); botones([{ texto: 'Volver', alClick: () => paso2() }]); return; }
        const errores = Object.entries(r.errores || {});
        cuerpo.innerHTML = `
            ${errores.length ? `<div class="ui-alerta ui-alerta--danger mb-12">Revisá: ${errores.map(([k, v]) => YS.esc((estado.plantilla.campos.find(c => c.clave === k)?.etiqueta || k) + ': ' + v)).join(' · ')}</div>` : ''}
            ${YS.documento.html(r.documento)}`;
        botones([{ texto: 'Volver a los datos', alClick: () => paso2() },
                 ...(errores.length ? [] : [{ texto: 'Emitir', tipo: 'primary', alClick: paso4 }])]);
    }

    // Paso 4: confirmación y emisión
    async function paso4() {
        const ok = await confirmar('Una vez emitido, el documento se guarda en la historia clínica y no se puede modificar. Las correcciones se hacen con un addendum.',
            { titulo: 'Emitir documento', textoConfirmar: 'Emitir' });
        if (!ok) return;
        botones([]);
        cuerpo.innerHTML = YS.cargando('Emitiendo y generando el PDF…');
        try {
            const r = await YS.api(`/pacientes/${idPaciente}/documentos`, { method: 'POST', json: {
                clave: estado.clave, datos: estado.datos, idTurno: estado.idTurno, idEventoCrisis: estado.idEventoCrisis } });
            cuerpo.innerHTML = `<div class="ui-alerta ui-alerta--ok">
                <strong>${YS.esc(r.titulo)}</strong> emitido y guardado en la historia clínica.<br>Código de verificación: <code>${YS.esc(r.codigo)}</code></div>`;
            botones([{ texto: 'Descargar PDF', tipo: 'primary', alClick: () => descargarDocumento(r.idDocumento) },
                     { texto: 'Cerrar', alClick: () => m.cerrar() }]);
            recargarDocumentosHC(idPaciente);
            opciones.alEmitir?.(r);
        } catch (e) {
            const detalle = e.datos?.detalle;
            cuerpo.innerHTML = `<div class="ui-alerta ui-alerta--danger">${YS.esc(e.message)}${detalle && typeof detalle === 'object'
                ? '<br>' + Object.entries(detalle).map(([k, v]) => YS.esc(k + ': ' + v)).join('<br>') : ''}</div>`;
            botones([{ texto: 'Volver a los datos', alClick: () => paso2() }]);
        }
    }

    if (estado.clave && plantillas.some(p => p.clave === estado.clave)) paso2(); else paso1();
}

// ── Sugerencias al cerrar una sesión (las manda sesion_video.html por postMessage) ──
const DOC_SUGERENCIA = { 'Alta': { clave: 'const_alta', texto: 'Emitir constancia de alta' },
                         'Derivación': { clave: 'derivacion', texto: 'Emitir nota de derivación' } };
window.addEventListener('message', e => {
    if (e.origin !== location.origin || e.data?.tipo !== 'ys:sesion-guardada') return;
    const s = DOC_SUGERENCIA[e.data.tipoCierre];
    const idPaciente = parseInt(e.data.idPaciente);
    if (!s || !idPaciente) return;
    YS.toast(`Cerraste la sesión con ${e.data.tipoCierre}. ¿Querés emitir el documento?`, 'info',
        { duracion: 15000, accion: { texto: s.texto, alClick: () => abrirNuevoDocumento(idPaciente, { clave: s.clave }) } });
});

// ── Firma para los documentos (Mi perfil) ───────────────────
async function cargarFirmaPerfil() {
    const cont = document.getElementById('firma-perfil');
    if (!cont) return;
    let url = null;
    try {
        const r = await YS.fetch('/profesionales/yo/firma', { sinRedireccion: true });
        if (r.ok) url = URL.createObjectURL(await r.blob());
    } catch { }
    cont.innerHTML = `
        <p class="texto-suave mb-12">Opcional. Se imprime sobre tu espacio de firma en los PDF de los documentos que emitas. Subí una foto o un escaneo en PNG o JPG, con fondo claro: el fondo se aclara solo.</p>
        ${url ? `<div class="mb-12"><img src="${url}" alt="Tu firma" style="max-height:80px;max-width:280px;border:1px dashed var(--border);padding:6px;background:#fff"></div>` : '<p class="texto-suave mb-12">Todavía no subiste tu firma.</p>'}
        <div class="ui-flex">
            <label class="ui-btn" for="firma-archivo">${url ? 'Reemplazar firma' : 'Subir firma'}</label>
            <input type="file" id="firma-archivo" accept="image/png,image/jpeg" class="oculto">
            ${url ? '<button class="ui-btn ui-btn--danger" type="button" id="firma-quitar">Quitar firma</button>' : ''}
        </div>`;
    document.getElementById('firma-archivo').addEventListener('change', e => subirFirma(e.target.files[0]));
    document.getElementById('firma-quitar')?.addEventListener('click', async () => {
        if (!await confirmar('¿Quitar tu firma? Los documentos ya emitidos no cambian.', { titulo: 'Quitar firma', textoConfirmar: 'Quitar', peligro: true })) return;
        try { await YS.fetch('/profesionales/yo/firma', { method: 'DELETE' }); toast('Firma quitada.', 'warn'); cargarFirmaPerfil(); }
        catch (err) { toast(err.message, 'error'); }
    });
}

// Aclara el fondo (lo casi blanco pasa a transparente), achica a 600 px de ancho y la manda como PNG.
async function subirFirma(archivo) {
    if (!archivo) return;
    if (!/^image\/(png|jpeg)$/.test(archivo.type)) { toast('La firma tiene que ser PNG o JPG.', 'warn'); return; }
    try {
        const img = await new Promise((ok, mal) => { const i = new Image(); i.onload = () => ok(i); i.onerror = mal; i.src = URL.createObjectURL(archivo); });
        const escala = Math.min(1, 600 / img.width);
        const c = document.createElement('canvas');
        c.width = Math.max(1, Math.round(img.width * escala));
        c.height = Math.max(1, Math.round(img.height * escala));
        const ctx = c.getContext('2d');
        ctx.drawImage(img, 0, 0, c.width, c.height);
        const px = ctx.getImageData(0, 0, c.width, c.height);
        for (let i = 0; i < px.data.length; i += 4) {
            const lum = 0.299 * px.data[i] + 0.587 * px.data[i + 1] + 0.114 * px.data[i + 2];
            if (lum > 190) px.data[i + 3] = 0;                                         // fondo: transparente
            else { const k = lum < 120 ? 0 : (lum - 120) / 70; px.data[i] = px.data[i + 1] = px.data[i + 2] = Math.round(30 * k); }   // trazo: oscuro
        }
        ctx.putImageData(px, 0, 0);
        const dataUrl = c.toDataURL('image/png');
        if (dataUrl.length > 400000) { toast('La imagen es muy grande. Probá con una foto más chica o más recortada.', 'warn'); return; }
        await YS.api('/profesionales/yo/firma', { method: 'PUT', json: { imagenBase64: dataUrl } });
        toast('Firma guardada.');
        cargarFirmaPerfil();
    } catch (e) { toast(e?.message || 'No se pudo leer la imagen.', 'error'); }
}
