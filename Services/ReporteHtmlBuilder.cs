using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

// Genera el reporte "Atenciones por Profesional" como un único archivo HTML
// autocontenido: CSS y JavaScript embebidos, sin ninguna dependencia de CDN.
// El archivo tiene que poder abrirse con doble click y sin conexión a internet.
//
// Criterio de seguridad aplicado en todo el archivo:
//
//  * Todo valor que viene de la base se escapa con WebUtility.HtmlEncode antes
//    de entrar al HTML, tanto en el contenido de las celdas como en los
//    atributos data-*. HtmlEncode escapa las comillas dobles como &quot;, así
//    que los atributos entrecomillados también quedan a salvo.
//
//  * La tabla se renderiza acá, del lado del servidor. El JS embebido nunca
//    arma filas ni las inyecta con innerHTML: solamente muestra, oculta y
//    reordena los <tr> que ya existen. De esa forma ningún dato de la base
//    vuelve a tratarse como string HTML dentro del navegador.
//
//  * Lo único que se inyecta adentro del <script> son metadatos serializados
//    con JsonSerializer, nunca concatenados a mano. El encoder por defecto de
//    System.Text.Json escapa '<', '>' y '&' como <, > y &, con
//    lo cual un "</script>" dentro de un dato no puede cerrar el bloque.
public static class ReporteHtmlBuilder
{
    // ── DIMENSIÓN DE APILADO DEL GRÁFICO ────────────────────────────────────
    // El gráfico apila cada barra (un profesional) por categoría de cierre.
    // La paleta es la que ya usa la app; los colores se asignan por POSICIÓN en
    // la lista ordenada de categorías, calculada una sola vez sobre el conjunto
    // COMPLETO de datos. Por eso el color de una categoría es estable: no
    // depende de qué filas queden visibles tras filtrar.
    private static readonly string[] PaletaCategorias =
    {
        "#6A5ACD", "#0d6efd", "#20c997", "#fd7e14", "#6610f2", "#0dcaf0", "#dc3545"
    };

    // Las atenciones sin tipo de cierre se agrupan bajo esta categoría, que
    // siempre lleva gris y siempre se dibuja última (arriba del apilado).
    private const  string CategoriaSinDato = "Sin especificar";
    private const  string ColorSinDato     = "#adb5bd";

    private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

    // Categoría de apilado de una atención. Cambiar esta única función alcanza
    // para apilar por otra dimensión (modalidad, especialidad, etc.).
    private static string Categoria(AtencionReporteDto d) =>
        string.IsNullOrWhiteSpace(d.TipoCierre) ? CategoriaSinDato : d.TipoCierre.Trim();

    /// <summary>
    /// Construye el HTML completo del reporte.
    /// </summary>
    /// <param name="datos">Misma colección que alimenta la exportación a CSV.</param>
    /// <param name="idProfesional">Filtro aplicado en el origen, o null si son todos.</param>
    public static string Construir(List<AtencionReporteDto> datos, int? idProfesional)
    {
        datos ??= new List<AtencionReporteDto>();

        // ── Métricas del encabezado y de las tarjetas KPI ───────────────────
        var total         = datos.Count;
        var profesionales = datos.Select(d => d.NombreProfesional)
                                 .Distinct()
                                 .OrderBy(n => n, System.StringComparer.CurrentCultureIgnoreCase)
                                 .ToList();

        var promedio = profesionales.Count > 0
            ? (double)total / profesionales.Count
            : 0d;

        var top = datos.GroupBy(d => d.NombreProfesional)
                       .OrderByDescending(g => g.Count())
                       .ThenBy(g => g.Key)
                       .FirstOrDefault();

        var periodo = total > 0
            ? datos.Min(d => d.FechaHora).ToString("dd/MM/yyyy", Cultura) + " – " +
              datos.Max(d => d.FechaHora).ToString("dd/MM/yyyy", Cultura)
            : "sin datos";

        // Nombre del profesional filtrado, si el reporte vino acotado a uno solo.
        var profesionalFiltrado = idProfesional.HasValue
            ? datos.FirstOrDefault(d => d.IdProfesional == idProfesional.Value)?.NombreProfesional
            : null;

        var generado = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm", Cultura);

        // ── Categorías de apilado, con color estable ────────────────────────
        // El orden se fija acá, sobre TODOS los datos del reporte: primero las
        // categorías reales por frecuencia descendente, y "Sin especificar"
        // forzada al final para que quede siempre como segmento superior.
        // Como el color sale de esta lista y no del bucle de dibujo, filtrar en
        // el navegador no puede cambiarle el color a una categoría.
        var categorias = datos.GroupBy(Categoria)
                              .Where(g => g.Key != CategoriaSinDato)
                              .OrderByDescending(g => g.Count())
                              .ThenBy(g => g.Key, System.StringComparer.CurrentCultureIgnoreCase)
                              .Select((g, i) => new
                              {
                                  nombre = g.Key,
                                  color  = PaletaCategorias[i % PaletaCategorias.Length]
                              })
                              .ToList();

        if (datos.Any(d => Categoria(d) == CategoriaSinDato))
            categorias.Add(new { nombre = CategoriaSinDato, color = ColorSinDato });

        // Metadatos que consume el JS embebido. Se serializan, no se concatenan.
        var meta = JsonSerializer.Serialize(new
        {
            categorias       = categorias,
            categoriaSinDato = CategoriaSinDato,
            etiquetaSinDatos = "No hay atenciones que coincidan con los filtros aplicados."
        });

        var sb = new StringBuilder(64 * 1024);

        // ── Cabecera del documento ──────────────────────────────────────────
        sb.Append("""
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>YoSoy — Atenciones por Profesional</title>
<style>
/* Paleta y geometría de la app, expuestas como custom properties para que sea
   fácil ajustarlas sin tocar el resto de la hoja. */
:root{
  --color-primario:#6A5ACD;
  --color-primario-hover:#9D6BCF;
  --color-secundario:#0d6efd;
  --color-titulo:#4a3a9e;
  --color-fondo:#f5f0ff;
  --color-superficie:#ffffff;
  --color-borde:#e0d4f5;
  --color-fila-hover:#f0eaff;
  --color-texto-tabla:#3d3d3d;
  --color-exito:#198754;
  --color-texto-suave:#888888;
  --color-linea-tabla:#f0edf7;
  --gradiente-marca:linear-gradient(to right,#0d6efd,#6A5ACD);
  --radio-boton:8px;
  --radio-control:10px;
  --radio-tarjeta:12px;
  --sombra-tarjeta:0 2px 8px rgba(0,0,0,.08);
  --sombra-panel:0 4px 15px rgba(0,0,0,.07);
  --tipografia:Arial, Helvetica, sans-serif;
}

*{box-sizing:border-box;margin:0;padding:0}

body{
  font-family:var(--tipografia);
  background:var(--color-fondo);
  color:var(--color-texto-tabla);
  padding:24px;
  line-height:1.45;
}

.contenedor{max-width:1280px;margin:0 auto}

/* ── ENCABEZADO ─────────────────────────────────────────── */
.encabezado{
  background:var(--gradiente-marca);
  color:#fff;
  border-radius:var(--radio-tarjeta);
  padding:24px 28px;
  box-shadow:var(--sombra-panel);
  margin-bottom:20px;
}
.marca{font-size:27px;font-weight:bold;letter-spacing:.5px}
.titulo-reporte{font-size:18px;margin-top:2px;opacity:.95}
.meta{
  margin-top:16px;
  display:flex;
  flex-wrap:wrap;
  gap:6px 28px;
  font-size:13px;
}
.meta div{opacity:.95}
.meta b{font-weight:bold}

/* ── TARJETAS KPI ───────────────────────────────────────── */
.kpis{
  display:grid;
  grid-template-columns:repeat(auto-fit,minmax(215px,1fr));
  gap:16px;
  margin-bottom:20px;
}
.kpi{
  background:var(--color-superficie);
  border-radius:var(--radio-tarjeta);
  padding:18px 20px;
  box-shadow:var(--sombra-tarjeta);
  border-left:5px solid var(--color-primario);
}
.kpi:nth-child(2){border-left-color:var(--color-secundario)}
.kpi:nth-child(3){border-left-color:var(--color-primario-hover)}
.kpi:nth-child(4){border-left-color:var(--color-exito)}
.kpi-etiqueta{
  font-size:11.5px;
  font-weight:bold;
  color:var(--color-primario);
  text-transform:uppercase;
  letter-spacing:.5px;
}
.kpi-valor{
  font-size:29px;
  font-weight:bold;
  color:var(--color-titulo);
  margin-top:6px;
  line-height:1.15;
}
.kpi-valor.texto{font-size:18px;word-break:break-word}

/* ── PANELES ────────────────────────────────────────────── */
.panel{
  background:var(--color-superficie);
  border-radius:var(--radio-tarjeta);
  padding:22px 24px;
  box-shadow:var(--sombra-tarjeta);
  margin-bottom:20px;
}
.panel h2{font-size:17px;color:var(--color-titulo);margin-bottom:16px}

/* ── CONTROLES ──────────────────────────────────────────── */
.controles{display:flex;flex-wrap:wrap;gap:12px;align-items:flex-end}
.campo label{
  display:block;
  font-size:12px;
  font-weight:bold;
  color:var(--color-primario);
  margin-bottom:4px;
}
.campo input,.campo select{
  height:40px;
  border:2px solid var(--color-borde);
  border-radius:var(--radio-control);
  padding:0 12px;
  font-size:14px;
  font-family:inherit;
  color:var(--color-texto-tabla);
  background:#fff;
  outline:none;
  transition:border-color .2s;
}
.campo input:focus,.campo select:focus{border-color:var(--color-primario)}
.campo input[type=search]{min-width:250px}
.campo select{min-width:220px}

.btn{
  height:40px;
  border:none;
  border-radius:var(--radio-boton);
  padding:0 20px;
  font-size:14px;
  font-weight:bold;
  font-family:inherit;
  color:#fff;
  cursor:pointer;
  transition:background .2s,filter .2s;
}
.btn-primario{background:var(--color-primario)}
.btn-primario:hover{background:var(--color-primario-hover)}
.btn-exito{background:var(--color-exito)}
.btn-exito:hover{filter:brightness(1.12)}
.btn-borde{
  background:#fff;
  color:var(--color-primario);
  border:2px solid var(--color-borde);
}
.btn-borde:hover{border-color:var(--color-primario)}

.contador{
  font-size:13px;
  color:var(--color-texto-suave);
  margin:14px 0 12px;
}

/* ── GRÁFICO ────────────────────────────────────────────── */
#grafico{width:100%;height:auto;display:block}
.barra{cursor:pointer;transition:opacity .15s}
.barra:hover{opacity:.78}
.etiqueta-barra{cursor:default}

/* Leyenda de categorías del apilado */
.leyenda{
  display:flex;
  flex-wrap:wrap;
  gap:9px 22px;
  margin-top:16px;
  padding-top:14px;
  border-top:1px solid var(--color-linea-tabla);
  font-size:13px;
}
.leyenda-item{display:inline-flex;align-items:center;gap:7px;color:var(--color-texto-tabla)}
.leyenda-color{
  width:13px;
  height:13px;
  border-radius:3px;
  flex-shrink:0;
  display:inline-block;
  -webkit-print-color-adjust:exact;
  print-color-adjust:exact;
}
#tooltip{
  position:absolute;
  display:none;
  background:#2b2b3d;
  color:#fff;
  padding:7px 11px;
  border-radius:var(--radio-boton);
  font-size:12.5px;
  pointer-events:none;
  z-index:50;
  box-shadow:0 4px 14px rgba(0,0,0,.25);
  white-space:nowrap;
}

/* ── TABLA ──────────────────────────────────────────────── */
.tabla-wrap{overflow-x:auto;border-radius:var(--radio-tarjeta)}
table{width:100%;border-collapse:collapse;font-size:14px}
thead{background:var(--gradiente-marca);color:#fff}
th{
  padding:12px 14px;
  text-align:left;
  font-weight:600;
  cursor:pointer;
  user-select:none;
  white-space:nowrap;
}
th:hover{filter:brightness(1.08)}
th .flecha{font-size:10px;margin-left:5px;opacity:.55}
th.ordenada .flecha{opacity:1}
td{
  padding:11px 14px;
  border-bottom:1px solid var(--color-linea-tabla);
  color:var(--color-texto-tabla);
  vertical-align:middle;
}
tbody tr:hover{background:var(--color-fila-hover)}

.vacio{
  padding:38px 20px;
  text-align:center;
  color:var(--color-texto-suave);
  font-size:15px;
}

.pie{
  text-align:center;
  font-size:12px;
  color:var(--color-texto-suave);
  padding:8px 0 4px;
}

/* ── IMPRESIÓN / PDF ────────────────────────────────────── */
@media print{
  @page{margin:12mm}
  body{background:#fff;padding:0;font-size:11.5pt}
  .contenedor{max-width:none}
  /* Fuera los controles interactivos: en papel no sirven de nada. */
  .no-imprimir{display:none !important}
  .panel,.kpi{box-shadow:none;border:1px solid var(--color-borde)}
  /* Encabezado, cabecera de tabla, barras del grafico y cuadraditos de la
     leyenda tienen que salir con color en papel, o el apilado no se entiende. */
  .encabezado,thead,#grafico,.leyenda-color{
    -webkit-print-color-adjust:exact;
    print-color-adjust:exact;
  }
  .leyenda{page-break-inside:avoid}
  .tabla-wrap{overflow:visible}
  /* La tabla se imprime tal como quedó filtrada en pantalla. */
  thead{display:table-header-group}
  tr{page-break-inside:avoid}
  .panel{page-break-inside:auto}
  td,th{padding:7px 9px}
}
</style>
</head>
<body>
<div class="contenedor">

""");

        // ── Encabezado con marca, período y datos de generación ─────────────
        sb.Append("<header class=\"encabezado\">\n")
          .Append("  <div class=\"marca\">YoSoy</div>\n")
          .Append("  <div class=\"titulo-reporte\">Reporte de Atenciones por Profesional</div>\n")
          .Append("  <div class=\"meta\">\n")
          .Append("    <div><b>Período de los datos:</b> ").Append(E(periodo)).Append("</div>\n")
          .Append("    <div><b>Generado:</b> ").Append(E(generado)).Append("</div>\n");

        if (!string.IsNullOrWhiteSpace(profesionalFiltrado))
            sb.Append("    <div><b>Profesional filtrado:</b> ").Append(E(profesionalFiltrado)).Append("</div>\n");
        else
            sb.Append("    <div><b>Alcance:</b> todos los profesionales</div>\n");

        sb.Append("  </div>\n</header>\n\n");

        // ── Tarjetas KPI. Se emiten con los valores del total y el JS las
        //    recalcula al vuelo cada vez que cambia un filtro. ──────────────
        sb.Append("<section class=\"kpis\">\n")
          .Append("  <div class=\"kpi\"><div class=\"kpi-etiqueta\">Total de atenciones</div>")
          .Append("<div class=\"kpi-valor\" id=\"kpiTotal\">").Append(total).Append("</div></div>\n")

          .Append("  <div class=\"kpi\"><div class=\"kpi-etiqueta\">Profesionales</div>")
          .Append("<div class=\"kpi-valor\" id=\"kpiProfesionales\">").Append(profesionales.Count).Append("</div></div>\n")

          .Append("  <div class=\"kpi\"><div class=\"kpi-etiqueta\">Promedio por profesional</div>")
          .Append("<div class=\"kpi-valor\" id=\"kpiPromedio\">")
          .Append(promedio.ToString("0.0", new CultureInfo("es-AR"))).Append("</div></div>\n")

          .Append("  <div class=\"kpi\"><div class=\"kpi-etiqueta\">Profesional con más atenciones</div>")
          .Append("<div class=\"kpi-valor texto\" id=\"kpiTop\">")
          .Append(top != null ? E(top.Key) + " (" + top.Count() + ")" : "—")
          .Append("</div></div>\n")
          .Append("</section>\n\n");

        if (total == 0)
        {
            // ── Estado vacío: el reporte se genera igual, con un mensaje ────
            sb.Append("""
<section class="panel">
  <div class="vacio">
    <p><b>No hay atenciones registradas para el filtro seleccionado.</b></p>
    <p style="margin-top:8px">El reporte se generó igualmente para dejar constancia de la consulta realizada.</p>
  </div>
</section>
<div class="no-imprimir" style="text-align:center;margin-bottom:20px">
  <button class="btn btn-primario" onclick="window.print()">Imprimir / Guardar como PDF</button>
</div>

""");
        }
        else
        {
            // ── Gráfico de barras en SVG puro ───────────────────────────────
            sb.Append("""
<section class="panel">
  <h2>Atenciones por profesional, apiladas por tipo de cierre</h2>
  <svg id="grafico" viewBox="0 0 860 360" role="img" aria-label="Gráfico de barras apiladas de atenciones por profesional y tipo de cierre"></svg>
  <div class="leyenda" id="leyenda"></div>
</section>

<section class="panel">
  <div class="controles no-imprimir">
    <div class="campo">
      <label for="buscador">Buscar</label>
      <input type="search" id="buscador" placeholder="Paciente, DNI, tipo de cierre...">
    </div>
    <div class="campo">
      <label for="filtroProfesional">Profesional</label>
      <select id="filtroProfesional">
        <option value="">Todos los profesionales</option>

""");

            // Opciones del desplegable. Se renderizan del lado del servidor y
            // escapadas, así el JS no tiene que manipular nombres como string.
            foreach (var p in profesionales)
                sb.Append("        <option value=\"").Append(E(p)).Append("\">").Append(E(p)).Append("</option>\n");

            sb.Append("""
      </select>
    </div>
    <div class="campo">
      <label for="fechaDesde">Desde</label>
      <input type="date" id="fechaDesde">
    </div>
    <div class="campo">
      <label for="fechaHasta">Hasta</label>
      <input type="date" id="fechaHasta">
    </div>
    <button class="btn btn-borde" id="btnLimpiar">Limpiar filtros</button>
    <button class="btn btn-primario" id="btnImprimir">Imprimir / Guardar como PDF</button>
  </div>

  <div class="contador" id="contador"></div>

  <div class="tabla-wrap">
    <table>
      <thead>
        <tr>
          <th data-col="0">Paciente<span class="flecha">&#9650;&#9660;</span></th>
          <th data-col="1">DNI<span class="flecha">&#9650;&#9660;</span></th>
          <th data-col="2">NHC<span class="flecha">&#9650;&#9660;</span></th>
          <th data-col="3">Fecha<span class="flecha">&#9650;&#9660;</span></th>
          <th data-col="4">Hora<span class="flecha">&#9650;&#9660;</span></th>
          <th data-col="5">Tipo de Cierre<span class="flecha">&#9650;&#9660;</span></th>
          <th data-col="6">Profesional<span class="flecha">&#9650;&#9660;</span></th>
        </tr>
      </thead>
      <tbody id="cuerpoTabla">

""");

            // ── Filas de la tabla ───────────────────────────────────────────
            // data-fecha va en ISO (yyyy-MM-dd) para que el filtro por rango y
            // el ordenamiento no dependan de parsear "dd/MM/yyyy".
            // data-busqueda concentra los campos buscables ya en minúsculas,
            // para que el buscador en vivo no tenga que recorrer celdas.
            foreach (var d in datos)
            {
                var fechaCorta = d.FechaHora.ToString("dd/MM/yyyy", Cultura);
                var horaCorta  = d.FechaHora.ToString("HH:mm", Cultura);
                var nhcTexto   = d.Nhc?.ToString(Cultura) ?? "";
                var cierre     = string.IsNullOrWhiteSpace(d.TipoCierre) ? "" : d.TipoCierre;

                var busqueda = string.Join(" ",
                    d.Nombre, d.Dni, nhcTexto, fechaCorta, horaCorta, cierre, d.NombreProfesional
                ).ToLowerInvariant();

                sb.Append("        <tr data-fecha=\"").Append(d.FechaHora.ToString("yyyy-MM-dd", Cultura))
                  .Append("\" data-prof=\"").Append(E(d.NombreProfesional))
                  .Append("\" data-cierre=\"").Append(E(Categoria(d)))
                  .Append("\" data-busqueda=\"").Append(E(busqueda)).Append("\">")
                  .Append("<td>").Append(E(d.Nombre)).Append("</td>")
                  .Append("<td>").Append(E(d.Dni)).Append("</td>")
                  .Append("<td>").Append(nhcTexto.Length > 0 ? E(nhcTexto) : "—").Append("</td>")
                  .Append("<td>").Append(fechaCorta).Append("</td>")
                  .Append("<td>").Append(horaCorta).Append("</td>")
                  .Append("<td>").Append(cierre.Length > 0 ? E(cierre) : "—").Append("</td>")
                  .Append("<td>").Append(E(d.NombreProfesional)).Append("</td>")
                  .Append("</tr>\n");
            }

            sb.Append("""
      </tbody>
    </table>
  </div>

  <div class="vacio" id="sinResultados" style="display:none">
    No hay atenciones que coincidan con los filtros aplicados.
  </div>
</section>

""");
        }

        sb.Append("<div class=\"pie\">YoSoy — Plataforma de Telemedicina en Salud Mental · Reporte generado el ")
          .Append(E(generado)).Append("</div>\n")
          .Append("</div>\n<div id=\"tooltip\"></div>\n\n");

        // ── JavaScript embebido ─────────────────────────────────────────────
        sb.Append("<script>\n")
          .Append("const META = ").Append(meta).Append(";\n")
          .Append("""

(function(){
  "use strict";

  var tbody = document.getElementById('cuerpoTabla');
  if (!tbody) return; // reporte sin datos: no hay nada que filtrar ni graficar

  var SVGNS    = 'http://www.w3.org/2000/svg';
  var filas    = Array.prototype.slice.call(tbody.querySelectorAll('tr[data-fecha]'));
  var buscador = document.getElementById('buscador');
  var selProf  = document.getElementById('filtroProfesional');
  var desde    = document.getElementById('fechaDesde');
  var hasta    = document.getElementById('fechaHasta');
  var contador = document.getElementById('contador');
  var sinRes   = document.getElementById('sinResultados');
  var tooltip  = document.getElementById('tooltip');
  var svg      = document.getElementById('grafico');
  var leyenda  = document.getElementById('leyenda');

  // ── DIMENSIÓN DE APILADO ────────────────────────────────────────────────
  // Constante única que gobierna el gráfico apilado. Para apilar por otra
  // dimensión en el futuro (modalidad, especialidad, obra social...) alcanza
  // con cambiar estas dos cosas y su equivalente en C#:
  //
  //   ATRIBUTO_CATEGORIA -> el atributo data-* que lleva cada <tr>
  //   CATEGORIAS         -> la lista ordenada de {nombre, color}
  //
  // El resto del código de dibujo, leyenda y tooltip no necesita tocarse.
  //
  // CATEGORIAS viene serializada desde C#, calculada sobre el conjunto
  // completo de datos. El color de cada categoría se busca SIEMPRE en esta
  // lista, nunca por el índice del bucle de dibujo: por eso una categoría
  // conserva su color aunque los filtros cambien qué barras se ven.
  var ATRIBUTO_CATEGORIA = 'data-cierre';
  var CATEGORIAS         = META.categorias;            // orden de apilado, de abajo hacia arriba
  var CATEGORIA_SIN_DATO = META.categoriaSinDato;      // siempre última, en gris

  // ── FILTRADO ────────────────────────────────────────────────────────────
  // Solo alterna la visibilidad de los <tr> que ya vinieron renderizados
  // desde el servidor. No se crea ni se reescribe ninguna fila.
  function aplicarFiltros(){
    var texto   = buscador.value.trim().toLowerCase();
    var prof    = selProf.value;
    var fDesde  = desde.value;   // yyyy-MM-dd o cadena vacía
    var fHasta  = hasta.value;
    var visibles = 0;
    var porProf  = {};   // nombre -> { total: n, cats: { categoria: n } }

    for (var i = 0; i < filas.length; i++){
      var tr = filas[i];
      var f  = tr.getAttribute('data-fecha');
      var p  = tr.getAttribute('data-prof');
      var ok = true;

      if (prof   && p !== prof)                                        ok = false;
      if (ok && fDesde && f < fDesde)                                  ok = false;
      if (ok && fHasta && f > fHasta)                                  ok = false;
      if (ok && texto && tr.getAttribute('data-busqueda').indexOf(texto) === -1) ok = false;

      tr.style.display = ok ? '' : 'none';
      if (ok){
        visibles++;
        var cat = tr.getAttribute(ATRIBUTO_CATEGORIA) || CATEGORIA_SIN_DATO;
        var e   = porProf[p] || (porProf[p] = { total: 0, cats: {} });
        e.total++;
        e.cats[cat] = (e.cats[cat] || 0) + 1;
      }
    }

    contador.textContent = 'Mostrando ' + visibles + ' de ' + filas.length + ' registros';
    if (sinRes) sinRes.style.display = visibles ? 'none' : 'block';
    actualizarKpis(visibles, porProf);
    dibujarGrafico(porProf);
  }

  // ── KPIs ────────────────────────────────────────────────────────────────
  // Se recalculan sobre lo que quedó visible, para que las tarjetas, el
  // gráfico y la tabla no puedan desincronizarse entre sí.
  function actualizarKpis(total, porProf){
    var nombres = Object.keys(porProf);
    texto('kpiTotal', total);
    texto('kpiProfesionales', nombres.length);

    var prom = nombres.length ? (total / nombres.length) : 0;
    texto('kpiPromedio', prom.toFixed(1).replace('.', ','));

    var topNombre = '', topValor = -1;
    for (var i = 0; i < nombres.length; i++){
      if (porProf[nombres[i]].total > topValor){
        topValor  = porProf[nombres[i]].total;
        topNombre = nombres[i];
      }
    }
    texto('kpiTop', nombres.length ? topNombre + ' (' + topValor + ')' : '—');
  }

  function texto(id, valor){
    var el = document.getElementById(id);
    if (el) el.textContent = valor;  // textContent, nunca innerHTML
  }

  // ── ORDENAMIENTO ────────────────────────────────────────────────────────
  // Deliberado: al ordenar se MUEVEN los nodos <tr> existentes con
  // appendChild sobre un fragmento, en vez de reconstruir el HTML de la
  // tabla. appendChild sobre un nodo que ya está en el DOM lo reubica, no lo
  // duplica. Así se mantiene la propiedad de que el JS jamás vuelve a tocar
  // los datos de la base como string HTML.
  var ordenCol = -1, ordenAsc = true;

  function ordenarPor(indice){
    if (ordenCol === indice) ordenAsc = !ordenAsc;
    else { ordenCol = indice; ordenAsc = true; }

    var numerica = (indice === 1 || indice === 2); // DNI y NHC
    var copia = filas.slice();

    copia.sort(function(a, b){
      var va = valorOrden(a, indice);
      var vb = valorOrden(b, indice);
      var r;
      if (numerica){
        r = (parseFloat(va.replace(/\D/g, '')) || 0) - (parseFloat(vb.replace(/\D/g, '')) || 0);
      } else {
        r = va.localeCompare(vb, 'es', { sensitivity: 'base', numeric: true });
      }
      return ordenAsc ? r : -r;
    });

    var frag = document.createDocumentFragment();
    for (var i = 0; i < copia.length; i++) frag.appendChild(copia[i]); // mueve, no clona
    tbody.appendChild(frag);

    marcarEncabezado(indice);
  }

  function valorOrden(tr, i){
    // La columna Fecha se ordena por el data-fecha en ISO, no por el texto
    // "dd/MM/yyyy" que ve el usuario. Se le agrega la hora para desempatar.
    if (i === 3){
      return tr.getAttribute('data-fecha') + ' ' + (tr.cells[4] ? tr.cells[4].textContent : '');
    }
    return tr.cells[i] ? tr.cells[i].textContent.trim() : '';
  }

  function marcarEncabezado(indice){
    var ths = document.querySelectorAll('th[data-col]');
    for (var i = 0; i < ths.length; i++){
      var esta = parseInt(ths[i].getAttribute('data-col'), 10) === indice;
      ths[i].classList.toggle('ordenada', esta);
      var f = ths[i].querySelector('.flecha');
      if (f) f.textContent = esta ? (ordenAsc ? '▲' : '▼') : '▲▼';
    }
  }

  // ── GRÁFICO DE BARRAS EN SVG PURO ───────────────────────────────────────
  // Sin librerías externas. Los nombres se insertan con textContent sobre
  // nodos creados con createElementNS, nunca como marcado.
  function crear(tag, attrs){
    var el = document.createElementNS(SVGNS, tag);
    for (var k in attrs) el.setAttribute(k, attrs[k]);
    return el;
  }

  function dibujarGrafico(porProf){
    if (!svg) return;
    while (svg.firstChild) svg.removeChild(svg.firstChild);

    // Una barra por profesional, de mayor a menor total de atenciones.
    var entradas = Object.keys(porProf).map(function(n){
      return { nombre: n, total: porProf[n].total, cats: porProf[n].cats };
    }).sort(function(a, b){ return b.total - a.total; });

    var W = 860, H = 360, ml = 52, mr = 16, mt = 20;

    if (!entradas.length){
      svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
      var vacio = crear('text', {
        x: W / 2, y: H / 2, 'text-anchor': 'middle', 'font-size': 15, fill: '#888888',
        'font-family': 'Arial, Helvetica, sans-serif'
      });
      vacio.textContent = META.etiquetaSinDatos;
      svg.appendChild(vacio);
      dibujarLeyenda({});
      return;
    }

    var n     = entradas.length;
    var rotar = n > 10;               // con muchas barras, las etiquetas se inclinan
    var mb    = rotar ? 96 : 52;      // el margen inferior acompaña al alto de las etiquetas
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);

    var ancho = W - ml - mr;
    var alto  = H - mt - mb;
    var max   = 0;
    for (var i = 0; i < entradas.length; i++) max = Math.max(max, entradas[i].total);

    var paso = Math.max(1, Math.ceil(max / 4));
    var tope = paso * 4;

    // Grilla horizontal y escala del eje Y (siempre en enteros).
    for (var t = 0; t <= 4; t++){
      var v = paso * t;
      var y = mt + alto - (v / tope) * alto;
      svg.appendChild(crear('line', {
        x1: ml, y1: y, x2: ml + ancho, y2: y, stroke: '#ece6f7', 'stroke-width': 1
      }));
      var et = crear('text', {
        x: ml - 10, y: y + 4, 'text-anchor': 'end', 'font-size': 12, fill: '#888888',
        'font-family': 'Arial, Helvetica, sans-serif'
      });
      et.textContent = v;
      svg.appendChild(et);
    }

    svg.appendChild(crear('line', {
      x1: ml, y1: mt + alto, x2: ml + ancho, y2: mt + alto,
      stroke: '#d9cfee', 'stroke-width': 1.5
    }));

    var carril     = ancho / n;
    var anchoBarra = Math.max(6, Math.min(64, carril * 0.62));
    var totalesCat = {};   // acumulado por categoría, para armar la leyenda

    entradas.forEach(function(e, idx){
      var x = ml + carril * idx + (carril - anchoBarra) / 2;
      var acumulado = 0;

      // Segmentos apilados. Se recorre CATEGORIAS en su orden fijo, así la
      // primera categoría queda abajo y "Sin especificar" arriba de todo.
      // El color sale de cat.color, nunca del índice del bucle: por eso es
      // estable frente a los filtros.
      for (var c = 0; c < CATEGORIAS.length; c++){
        var cat = CATEGORIAS[c];
        var v   = e.cats[cat.nombre] || 0;
        if (!v) continue;

        totalesCat[cat.nombre] = (totalesCat[cat.nombre] || 0) + v;

        var hSeg    = (v / tope) * alto;
        var yArriba = mt + alto - ((acumulado + v) / tope) * alto;
        var pct     = Math.round((v / e.total) * 100);

        var seg = crear('rect', {
          x: x, y: yArriba, width: anchoBarra, height: Math.max(hSeg, 1),
          fill: cat.color, rx: 2, ry: 2
        });
        seg.setAttribute('class', 'barra');

        // Se pasa el rect por parámetro: si se capturara la variable del
        // bucle, todos los segmentos terminarían apuntando al último.
        (function(rect, nombreProf, nombreCat, cant, porcentaje){
          var tip = nombreProf + ' — ' + nombreCat + ': ' + cant +
                    (cant === 1 ? ' atención' : ' atenciones') + ' (' + porcentaje + '%)';
          rect.addEventListener('mousemove', function(ev){ mostrarTooltip(ev, tip); });
          rect.addEventListener('mouseleave', ocultarTooltip);
        })(seg, e.nombre, cat.nombre, v, pct);

        svg.appendChild(seg);
        acumulado += v;
      }

      // Total del profesional, arriba de la barra completa.
      var yTope = mt + alto - (e.total / tope) * alto;
      var valor = crear('text', {
        x: x + anchoBarra / 2, y: yTope - 7, 'text-anchor': 'middle',
        'font-size': 12, 'font-weight': 'bold', fill: '#4a3a9e',
        'font-family': 'Arial, Helvetica, sans-serif'
      });
      valor.textContent = e.total;
      svg.appendChild(valor);

      var cx = x + anchoBarra / 2;
      var cy = mt + alto + 20;
      var attrs = {
        x: cx, y: cy, 'font-size': 11.5, fill: '#555555',
        'font-family': 'Arial, Helvetica, sans-serif'
      };
      if (rotar){
        attrs['text-anchor'] = 'end';
        attrs.transform = 'rotate(-32 ' + cx + ' ' + cy + ')';
      } else {
        attrs['text-anchor'] = 'middle';
      }
      var etiqueta = crear('text', attrs);
      etiqueta.setAttribute('class', 'etiqueta-barra');
      etiqueta.textContent = acortar(e.nombre, rotar ? 18 : 14);
      (function(nodo, nombreProf, tot){
        var tip = nombreProf + ' — ' + tot + (tot === 1 ? ' atención' : ' atenciones');
        nodo.addEventListener('mousemove', function(ev){ mostrarTooltip(ev, tip); });
        nodo.addEventListener('mouseleave', ocultarTooltip);
      })(etiqueta, e.nombre, e.total);
      svg.appendChild(etiqueta);
    });

    dibujarLeyenda(totalesCat);
  }

  // ── LEYENDA ─────────────────────────────────────────────────────────────
  // Recorre CATEGORIAS en el mismo orden del apilado. Una categoría que quedó
  // en cero tras filtrar simplemente no se dibuja.
  function dibujarLeyenda(totalesCat){
    if (!leyenda) return;
    while (leyenda.firstChild) leyenda.removeChild(leyenda.firstChild);

    for (var i = 0; i < CATEGORIAS.length; i++){
      var cat = CATEGORIAS[i];
      var t   = totalesCat[cat.nombre] || 0;
      if (!t) continue;

      var item = document.createElement('span');
      item.className = 'leyenda-item';

      var cuadro = document.createElement('span');
      cuadro.className = 'leyenda-color';
      cuadro.style.backgroundColor = cat.color;
      item.appendChild(cuadro);

      var txt = document.createElement('span');
      txt.textContent = cat.nombre + ' (' + t + ')';   // textContent, nunca innerHTML
      item.appendChild(txt);

      leyenda.appendChild(item);
    }
  }

  function acortar(txt, max){
    return txt.length > max ? txt.slice(0, max - 1) + '…' : txt;
  }

  // ── TOOLTIP ─────────────────────────────────────────────────────────────
  function mostrarTooltip(ev, texto){
    tooltip.textContent = texto;   // textContent: el nombre nunca se interpreta
    tooltip.style.display = 'block';
    tooltip.style.left = (ev.pageX + 14) + 'px';
    tooltip.style.top  = (ev.pageY - 36) + 'px';
  }

  function ocultarTooltip(){
    tooltip.style.display = 'none';
  }

  // ── EVENTOS ─────────────────────────────────────────────────────────────
  buscador.addEventListener('input', aplicarFiltros);
  selProf.addEventListener('change', aplicarFiltros);
  desde.addEventListener('change', aplicarFiltros);
  hasta.addEventListener('change', aplicarFiltros);

  document.getElementById('btnLimpiar').addEventListener('click', function(){
    buscador.value = '';
    selProf.value  = '';
    desde.value    = '';
    hasta.value    = '';
    aplicarFiltros();
  });

  document.getElementById('btnImprimir').addEventListener('click', function(){
    window.print();
  });

  var ths = document.querySelectorAll('th[data-col]');
  for (var k = 0; k < ths.length; k++){
    (function(th){
      th.addEventListener('click', function(){
        ordenarPor(parseInt(th.getAttribute('data-col'), 10));
      });
    })(ths[k]);
  }

  aplicarFiltros();
})();
</script>
</body>
</html>
""");

        return sb.ToString();
    }

    // Escape HTML de todo valor proveniente de la base. WebUtility.HtmlEncode
    // escapa < > & " y ', así que sirve tanto para el contenido de las celdas
    // como para los atributos entrecomillados.
    private static string E(string? valor) => WebUtility.HtmlEncode(valor ?? "");
}
