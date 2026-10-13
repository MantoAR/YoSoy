/* ═══════════════════════════════════════════════════════════════
   YoSoy · evolución por enfoque (YS.evolucion)

   Dibuja el formulario de evolución según la plantilla del enfoque (TCC, psicoanálisis,
   sistémica, infanto-juvenil o general) y la muestra ya firmada en la historia clínica.
   Las plantillas vienen del servidor (GET /plantillas-evolucion): una sola fuente, la
   misma que valida al firmar. Requiere shared/ui.js.
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS;
    let _plantillas = null, _pedido = null;

    const ETIQUETA_MODO = {
        Manual: "Manual",
        Dictado: "Dictado",
        Transcripcion: "Transcripción de la sesión",
        DictadoTranscripcion: "Dictado + transcripción"
    };

    YS.evolucion = {
        etiquetaModo: m => ETIQUETA_MODO[m] || m,

        /** Plantillas, escalas, modos y tipos de cierre (cacheado). */
        async cargar() {
            // Se guarda el pedido (no solo el resultado) para que dos llamadas seguidas hagan un solo GET.
            _pedido = _pedido || YS.api("/plantillas-evolucion").catch(e => { _pedido = null; throw e; });
            _plantillas = await _pedido;
            return _plantillas;
        },

        plantilla(enfoque) {
            return _plantillas?.plantillas.find(p => p.enfoque === enfoque) || null;
        },

        /**
         * Formulario editable. opciones: { enfoque, modo, transcripcionDisponible, dictadoDisponible,
         * valores: { contenido, diagnostico, planTratamiento, tipoCierre } }.
         * Devuelve { leer(), enfoque(), modo(), insertarEnCampoActivo(texto), insertarTranscripcion(texto) }.
         */
        formulario(contenedor, opciones = {}) {
            const datos = _plantillas;
            let enfoque = YS.evolucion.plantilla(opciones.enfoque) ? opciones.enfoque : "General";
            let modo = opciones.modo || "Manual";
            let campoActivo = null;
            let valores = opciones.valores || {};
            let tipoCierre = valores.tipoCierre || null;

            contenedor.replaceChildren();
            const cabecera = YS.el("div", { class: "evo-cabecera" });
            const selEnfoque = YS.el("select", { class: "ui-select", id: "evo-enfoque", "aria-label": "Enfoque" },
                datos.plantillas.map(p => YS.el("option", { value: p.enfoque, text: p.nombre })));
            selEnfoque.value = enfoque;
            const selModo = YS.el("select", { class: "ui-select", id: "evo-modo", "aria-label": "Cómo registrar la evolución" },
                datos.modos.map(m => {
                    const usaTransc = m === "Transcripcion" || m === "DictadoTranscripcion";
                    const usaDictado = m === "Dictado" || m === "DictadoTranscripcion";
                    const deshabilitado = (usaTransc && !opciones.transcripcionDisponible) || (usaDictado && !opciones.dictadoDisponible);
                    return YS.el("option", { value: m, text: ETIQUETA_MODO[m] + (deshabilitado ? " (no disponible)" : ""), disabled: deshabilitado });
                }));
            selModo.value = modo;
            if (selModo.selectedOptions[0]?.disabled) { modo = "Manual"; selModo.value = modo; }
            cabecera.append(
                YS.el("div", { class: "ui-campo" }, YS.el("label", { for: "evo-enfoque", text: "Enfoque" }), selEnfoque),
                YS.el("div", { class: "ui-campo" }, YS.el("label", { for: "evo-modo", text: "Registro" }), selModo));
            const descripcion = YS.el("p", { class: "evo-descripcion" });
            const cuerpo = YS.el("div", { class: "evo-campos" });
            const comunes = YS.el("div", { class: "evo-comunes" });
            contenedor.append(cabecera, descripcion, cuerpo, comunes);

            const recordarFoco = el => el.addEventListener("focus", () => { campoActivo = el; });

            function dibujarCampos() {
                const p = YS.evolucion.plantilla(enfoque);
                descripcion.textContent = p.descripcion +
                    (!p.sugiereTranscripcion ? " Este enfoque no sugiere transcripción: usala solo si la acordaste con el paciente." : "");
                cuerpo.replaceChildren();
                const previos = valores.contenido || {};
                for (const c of p.campos) {
                    const id = "evo-" + c.clave;
                    const etiqueta = YS.el("label", { for: id, text: c.etiqueta + (c.requerido ? " *" : "") });
                    let control;
                    if (c.tipo === "consultantes") {
                        cuerpo.append(campoConsultantes(c, previos.consultantes || []));
                        continue;
                    } else if (c.tipo === "seleccion") {
                        control = YS.el("select", { class: "ui-select", id, dataset: { clave: c.clave } },
                            YS.el("option", { value: "", text: "Elegí una opción" }),
                            (c.opciones || []).map(o => YS.el("option", { value: o, text: o })));
                        control.value = previos.campos?.[c.clave] || "";
                        if (c.clave === "riesgo") control.addEventListener("change", () => alertaRiesgoInfantil(control.value === "Sí"));
                    } else if (c.tipo === "texto") {
                        control = YS.el("input", { class: "ui-input", id, dataset: { clave: c.clave }, value: previos.campos?.[c.clave] || "" });
                        recordarFoco(control);
                    } else {
                        control = YS.el("textarea", { class: "ui-textarea evo-textarea", id, rows: 3, dataset: { clave: c.clave } });
                        control.value = previos.campos?.[c.clave] || "";
                        recordarFoco(control);
                    }
                    const campo = YS.el("div", { class: "ui-campo" }, etiqueta, control);
                    if (c.ayuda) campo.append(YS.el("div", { class: "ui-campo-ayuda", text: c.ayuda }));
                    cuerpo.append(campo);
                }
                cuerpo.append(YS.el("div", { id: "evo-alerta-riesgo", class: "ui-alerta ui-alerta--danger oculto", role: "alert" }));
                for (const clave of p.escalas) cuerpo.append(campoEscala(datos.escalas.find(e => e.clave === clave), previos.escalas?.[clave]));
                if (previos.campos?.riesgo === "Sí") alertaRiesgoInfantil(true);
            }

            function alertaRiesgoInfantil(si) {
                const a = cuerpo.querySelector("#evo-alerta-riesgo");
                a.textContent = "Indicadores de riesgo: activá el protocolo de crisis y evaluá la obligación de reportar a los organismos de protección de niñas, niños y adolescentes.";
                a.classList.toggle("oculto", !si);
            }

            function campoConsultantes(c, lista) {
                const tabla = YS.el("div", { class: "evo-consultantes", dataset: { clave: c.clave } });
                const filas = YS.el("div", { class: "evo-consultantes-filas" });
                const agregar = (v = {}) => {
                    const fila = YS.el("div", { class: "evo-consultante" },
                        YS.el("input", { class: "ui-input", placeholder: "Nombre", "aria-label": "Nombre del consultante", value: v.nombre || "", dataset: { k: "nombre" } }),
                        YS.el("input", { class: "ui-input", placeholder: "Vínculo (madre, pareja…)", "aria-label": "Vínculo", value: v.vinculo || "", dataset: { k: "vinculo" } }),
                        YS.el("label", { class: "evo-presente" },
                            YS.el("input", { type: "checkbox", checked: v.presente !== false, dataset: { k: "presente" } }), " Presente"),
                        YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", "aria-label": "Quitar", text: "✕", on: { click: () => fila.remove() } }));
                    filas.append(fila);
                };
                (lista.length ? lista : [{}]).forEach(agregar);
                tabla.append(
                    YS.el("div", { class: "evo-consultantes-titulo", text: c.etiqueta + (c.requerido ? " *" : "") }),
                    c.ayuda ? YS.el("div", { class: "ui-campo-ayuda", text: c.ayuda }) : "",
                    filas,
                    YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", text: "+ Agregar consultante", on: { click: () => agregar() } }));
                return tabla;
            }

            function campoEscala(def, previas) {
                const caja = YS.el("details", { class: "evo-escala", dataset: { escala: def.clave } });
                if (previas?.length) caja.open = true;
                const aplicar = YS.el("input", { type: "checkbox", checked: !!previas?.length, "aria-label": "Aplicar " + def.nombre });
                const resultado = YS.el("div", { class: "evo-escala-resultado", "aria-live": "polite" });
                const items = YS.el("ol", { class: "evo-escala-items" });
                def.items.forEach((texto, i) => {
                    const nombre = `esc-${def.clave}-${i}`;
                    items.append(YS.el("li", {},
                        YS.el("div", { class: "evo-escala-item", text: texto }),
                        YS.el("div", { class: "evo-escala-opciones", role: "radiogroup", "aria-label": texto },
                            def.respuestas.map((r, v) => YS.el("label", {},
                                YS.el("input", { type: "radio", name: nombre, value: v, checked: previas?.[i] === v,
                                                 on: { change: calcular } }), " " + r + " (" + v + ")")))));
                });
                function respuestas() {
                    return def.items.map((_, i) => {
                        const r = caja.querySelector(`input[name="esc-${def.clave}-${i}"]:checked`);
                        return r ? parseInt(r.value) : null;
                    });
                }
                function calcular() {
                    const rs = respuestas();
                    const faltan = rs.filter(v => v === null).length;
                    if (faltan) { resultado.textContent = `Faltan ${faltan} ítems.`; resultado.className = "evo-escala-resultado"; return; }
                    const total = rs.reduce((a, b) => a + b, 0);
                    const sev = def.bandas.find(b => total <= b.hasta).severidad;
                    const alerta = def.clave === "PHQ9" && rs[8] > 0;
                    resultado.textContent = `${total} punto${total === 1 ? "" : "s"} · ${sev}` + (alerta ? " · Ítem 9 positivo: evaluá el riesgo ahora y, si corresponde, activá el protocolo de crisis." : "");
                    resultado.className = "evo-escala-resultado" + (alerta ? " evo-escala-resultado--alerta" : "");
                }
                caja.append(
                    YS.el("summary", {}, aplicar, " " + def.nombre),
                    YS.el("p", { class: "evo-escala-consigna", text: def.consigna }), items, resultado);
                caja._leer = () => (aplicar.checked ? respuestas() : null);
                aplicar.addEventListener("change", () => { if (aplicar.checked) caja.open = true; });
                calcular();
                return caja;
            }

            function dibujarComunes() {
                comunes.replaceChildren();
                const diag = YS.el("input", { class: "ui-input", id: "evo-diagnostico", maxlength: 500, value: valores.diagnostico || "" });
                const plan = YS.el("textarea", { class: "ui-textarea evo-textarea", id: "evo-plan", rows: 2 });
                plan.value = valores.planTratamiento || "";
                [diag, plan].forEach(recordarFoco);
                const botones = YS.el("div", { class: "tipo-cierre", role: "radiogroup", "aria-label": "Tipo de cierre" },
                    datos.tiposCierre.map(t => YS.el("button", {
                        type: "button", class: "tipo-btn" + (t === tipoCierre ? " activo" : ""), "aria-pressed": t === tipoCierre, text: t,
                        on: { click: e => {
                            tipoCierre = t;
                            botones.querySelectorAll(".tipo-btn").forEach(b => { b.classList.toggle("activo", b === e.currentTarget); b.setAttribute("aria-pressed", b === e.currentTarget); });
                        } }
                    })));
                comunes.append(
                    YS.el("div", { class: "ui-campo" }, YS.el("label", { for: "evo-diagnostico", text: "Diagnóstico (opcional)" }), diag),
                    YS.el("div", { class: "ui-campo" }, YS.el("label", { for: "evo-plan", text: "Plan para la próxima sesión" }), plan),
                    YS.el("div", { class: "etiqueta-cierre", text: "Tipo de cierre *" }), botones);
            }

            function leer() {
                const p = YS.evolucion.plantilla(enfoque);
                const campos = {};
                cuerpo.querySelectorAll("[data-clave]").forEach(el => {
                    if (el.classList.contains("evo-consultantes")) return;
                    campos[el.dataset.clave] = el.value;
                });
                const consultantes = [...cuerpo.querySelectorAll(".evo-consultante")].map(f => ({
                    nombre: f.querySelector('[data-k="nombre"]').value,
                    vinculo: f.querySelector('[data-k="vinculo"]').value,
                    presente: f.querySelector('[data-k="presente"]').checked
                }));
                const escalas = {};
                cuerpo.querySelectorAll(".evo-escala").forEach(caja => {
                    const r = caja._leer();
                    if (r) escalas[caja.dataset.escala] = r.map(v => (v === null ? -1 : v));
                });
                return {
                    enfoque: p.enfoque, modoRegistro: modo,
                    contenido: { campos, consultantes, escalas },
                    diagnostico: comunes.querySelector("#evo-diagnostico").value,
                    planTratamiento: comunes.querySelector("#evo-plan").value,
                    tipoCierre
                };
            }

            selEnfoque.addEventListener("change", () => {
                // Conserva lo escrito en los campos que se llaman igual (por ejemplo "notas").
                const actual = leer();
                valores = { ...valores, contenido: actual.contenido };
                enfoque = selEnfoque.value;
                dibujarCampos();
            });
            selModo.addEventListener("change", () => { modo = selModo.value; contenedor.dispatchEvent(new CustomEvent("evo:modo", { detail: modo })); });

            dibujarCampos();
            dibujarComunes();

            return {
                leer,
                enfoque: () => enfoque,
                modo: () => modo,
                /** Dictado: agrega el texto en el último campo de texto que tuvo el foco. */
                insertarEnCampoActivo(texto) {
                    const destino = campoActivo && document.body.contains(campoActivo)
                        ? campoActivo : cuerpo.querySelector("textarea, input.ui-input");
                    if (!destino) return;
                    destino.value = (destino.value ? destino.value.trimEnd() + " " : "") + texto;
                    destino.dispatchEvent(new Event("input"));
                },
                /** Transcripción: la vuelca en el primer campo largo, marcada como borrador a revisar. */
                insertarTranscripcion(texto) {
                    const destino = cuerpo.querySelector("textarea");
                    if (!destino || !texto) return false;
                    destino.value = (destino.value ? destino.value.trimEnd() + "\n\n" : "") +
                        "[Borrador desde la transcripción: revisar, corregir y resumir antes de firmar]\n" + texto;
                    destino.focus();
                    return true;
                }
            };
        },

        /** Vista de solo lectura de una evolución (historia clínica). Devuelve un elemento. */
        vista(e, addenda = []) {
            let contenido = null;
            try { contenido = e.contenidoJson ? JSON.parse(e.contenidoJson) : null; } catch { contenido = null; }
            const p = contenido ? YS.evolucion.plantilla(e.enfoque) : null;
            const esAdmision = (e.diagnostico || "").toLowerCase().includes("admis");
            const bloque = (etiqueta, texto) => texto ? YS.el("div", { class: "evolucion-bloque" },
                YS.el("div", { class: "evolucion-bloque-label", text: etiqueta }),
                YS.el("div", { class: "evolucion-bloque-texto", text: texto })) : "";

            const chips = YS.el("span", { class: "evo-chips" },
                e.estado === "Borrador" ? YS.el("span", { class: "ui-chip ui-chip--warn", text: "Borrador sin firmar" })
                    : e.fechaFirma ? YS.el("span", { class: "ui-chip ui-chip--ok", text: "Firmada " + YS.fecha.corta(e.fechaFirma) }) : "",
                p ? YS.el("span", { class: "ui-chip ui-chip--marca ui-chip--sin-punto", text: p.nombre }) : "",
                e.modoRegistro && e.modoRegistro !== "Manual" ? YS.el("span", { class: "ui-chip ui-chip--neutral ui-chip--sin-punto", text: YS.evolucion.etiquetaModo(e.modoRegistro) }) : "",
                e.tipoCierre ? YS.el("span", { class: "ui-chip ui-chip--info ui-chip--sin-punto", text: e.tipoCierre }) : "");

            const item = YS.el("div", { class: "evolucion-item" + (esAdmision ? " evolucion-item--admision" : "") },
                esAdmision ? YS.el("div", { class: "evolucion-etiqueta-admision", text: "Inicio del proceso - Admisión" }) : "",
                YS.el("div", { class: "evolucion-fecha" },
                    new Date(e.fecha).toLocaleDateString("es-AR", { weekday: "long", day: "numeric", month: "long", year: "numeric" }), " ", chips),
                bloque("Diagnóstico", e.diagnostico));

            if (p && contenido) {
                for (const c of p.campos) {
                    if (c.tipo === "consultantes") {
                        const lista = (contenido.consultantes || []).map(x =>
                            `${x.nombre}${x.vinculo ? " (" + x.vinculo + ")" : ""}${x.presente === false ? " · ausente" : ""}`).join("\n");
                        item.append(bloque(c.etiqueta, lista));
                    } else item.append(bloque(c.etiqueta, contenido.campos?.[c.clave]));
                }
                for (const r of contenido.resultados || []) {
                    const def = _plantillas.escalas.find(x => x.clave === r.escala);
                    item.append(bloque(def ? def.nombre : r.escala,
                        `${r.total} punto${r.total === 1 ? "" : "s"} · ${r.severidad}` + (r.alertaRiesgo ? " · Ítem 9 positivo (evaluar riesgo)" : "")));
                }
            } else {
                item.append(bloque("Observaciones", e.observaciones));
            }
            item.append(bloque(esAdmision ? "Derivación" : "Plan", e.planTratamiento));

            const mias = addenda.filter(a => a.idEvolucion === e.idEvolucion);
            if (mias.length) item.append(YS.el("div", { class: "evo-addenda" },
                YS.el("div", { class: "evolucion-bloque-label", text: "Addenda" }),
                mias.map(a => YS.el("div", { class: "evo-addendum" },
                    YS.el("div", { class: "evo-addendum-meta", text: `${YS.fecha.corta(a.fecha)} · ${a.autor}` }),
                    YS.el("div", { class: "evolucion-bloque-texto", text: a.texto })))));
            return item;
        }
    };
})();
