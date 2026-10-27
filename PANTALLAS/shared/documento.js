// Documentos clínicos (migración 35): cómo se VE un documento renderizado por el servidor (RenderizadorDocumento), en el
// panel del profesional y en el editor de plantillas del Configurador. El servidor manda bloques con segmentos de texto
// plano y una marca de negrita: acá todo pasa por YS.esc, nunca se inserta HTML que venga de la base.
(function () {
    const YS = window.YS;
    if (!YS) { console.error("documento.js necesita shared/ui.js"); return; }

    const CSS = `
.doc-papel { background: #fff; color: #1f2328; max-width: 720px; margin: 0 auto; padding: 36px 44px; border: 1px solid var(--border, #ddd);
  border-radius: 6px; box-shadow: 0 2px 10px rgba(0,0,0,.06); font-size: 14px; line-height: 1.55; }
.doc-papel h3.doc-titulo { text-align: center; font-size: 18px; margin: 4px 0 14px; }
.doc-papel h4.doc-subtitulo { font-size: 15px; margin: 14px 0 4px; }
.doc-papel p { margin: 0 0 8px; text-align: justify; }
.doc-papel ul { margin: 0 0 8px 18px; padding: 0; }
.doc-provisorio { background: #fff4d6; border: 1px solid #e0b44a; color: #7a5600; padding: 6px 10px; border-radius: 4px; font-size: 12.5px; margin-bottom: 12px; }
.doc-firmas { display: grid; grid-template-columns: 1fr 1fr; gap: 28px 32px; margin-top: 36px; }
.doc-firma { border-top: 1px solid #555; padding-top: 4px; text-align: center; font-size: 12px; color: #444; min-height: 20px; margin-top: 40px; }
.doc-tipos { display: grid; gap: 14px; }
.doc-tipos h4 { margin: 0 0 6px; }
.doc-tipo { display: block; width: 100%; text-align: left; padding: 10px 12px; border: 1px solid var(--border, #ddd); border-radius: 8px;
  background: var(--surface, #fff); cursor: pointer; margin-bottom: 6px; }
.doc-tipo:hover, .doc-tipo:focus-visible { border-color: var(--accent, #6a5acd); }
.doc-tipo .secundario { display: block; font-size: 12.5px; color: var(--text-3, #666); margin-top: 2px; }
.doc-precargado { font-size: 11px; margin-left: 6px; }
@media (max-width: 640px) { .doc-papel { padding: 20px 16px; } .doc-firmas { grid-template-columns: 1fr; } }`;
    if (!document.getElementById("ys-documento-css")) {
        const s = document.createElement("style");
        s.id = "ys-documento-css";
        s.textContent = CSS;
        document.head.appendChild(s);
    }

    const linea = segs => (segs || []).map(s => s.negrita ? `<strong>${YS.esc(s.texto)}</strong>` : YS.esc(s.texto)).join("");

    function html(doc) {
        if (!doc) return "";
        const bloques = doc.bloques || [];
        const cuerpo = bloques.filter(b => b.tipo !== "firma").map(b => {
            switch (b.tipo) {
                case "titulo": return `<h3 class="doc-titulo">${linea(b.lineas[0])}</h3>`;
                case "subtitulo": return `<h4 class="doc-subtitulo">${linea(b.lineas[0])}</h4>`;
                case "lista": return `<ul>${b.lineas.map(l => `<li>${linea(l)}</li>`).join("")}</ul>`;
                default: return `<p>${linea(b.lineas[0])}</p>`;
            }
        }).join("");
        const firmas = bloques.filter(b => b.tipo === "firma");
        return `<div class="doc-papel">
            ${doc.provisoria ? `<div class="doc-provisorio">Texto provisorio: pendiente de validación legal.</div>` : ""}
            ${cuerpo}
            ${firmas.length ? `<div class="doc-firmas">${firmas.map(f => `<div class="doc-firma">${linea(f.lineas[0])}</div>`).join("")}</div>` : ""}
        </div>`;
    }

    // Descarga un archivo protegido (el token va en el encabezado, así que no sirve un <a href> común).
    async function descargar(ruta, nombre) {
        const r = await YS.fetch(ruta);
        if (!r.ok) throw new Error(await YS.leerMensaje(r, "No se pudo descargar."));
        const blob = await r.blob();
        const cd = r.headers.get("Content-Disposition") || "";
        const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd);
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = nombre || (m ? decodeURIComponent(m[1]) : "documento.pdf");
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(a.href), 30000);
    }

    YS.documento = { html, descargar };
})();
