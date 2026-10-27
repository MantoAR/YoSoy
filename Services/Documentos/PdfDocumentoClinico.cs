using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// El PDF de un documento clínico (RN-D6): se genera UNA vez, al emitir, y se guarda. Cada descarga entrega esos mismos bytes.
// QuestPDF con la licencia Community (gratuita para organizaciones con facturación anual menor a USD 1.000.000, proyectos de
// código abierto y uso académico; ver docs/PRUEBA_DOCUMENTOS_CLINICOS.md). La licencia se declara en Program.cs.
//
// Contenido: membrete (organización y localidad; profesional con su matrícula principal), los bloques renderizados por
// RenderizadorDocumento, los espacios de firma (la del profesional con su imagen si la subió), y al pie: página, fecha de
// emisión y código de verificación (primeros 12 caracteres del hash del contenido). Si la versión es provisoria, un aviso.
public sealed record MembreteDocumento(string? Organizacion, string? Localidad, string? Profesional, string? Matricula, string? Especialidad);

public static class PdfDocumentoClinico
{
    public static byte[] Generar(DocumentoRenderizado doc, MembreteDocumento membrete, byte[]? firmaProfesional,
        DateTime fechaEmision, string codigoVerificacion)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10.5f).LineHeight(1.35f));

                page.Header().PaddingBottom(10).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(membrete.Organizacion ?? "YoSoy").SemiBold().FontSize(12);
                        if (!string.IsNullOrWhiteSpace(membrete.Localidad)) c.Item().Text(membrete.Localidad).FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text(membrete.Profesional ?? "").SemiBold();
                        var mat = string.Join(" · ", new[] { membrete.Especialidad, string.IsNullOrWhiteSpace(membrete.Matricula) ? null : "Mat. " + membrete.Matricula }
                            .Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (mat.Length > 0) c.Item().AlignRight().Text(mat).FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(6);
                    if (doc.Provisoria)
                        col.Item().Background(Colors.Amber.Lighten4).Border(0.5f).BorderColor(Colors.Amber.Darken2).Padding(6)
                            .Text("Texto provisorio: pendiente de validación legal.").FontSize(9).FontColor(Colors.Amber.Darken4);

                    var firmas = doc.Bloques.Where(b => b.Tipo == "firma").ToList();
                    foreach (var b in doc.Bloques.Where(b => b.Tipo != "firma"))
                    {
                        switch (b.Tipo)
                        {
                            case "titulo":
                                col.Item().PaddingTop(4).PaddingBottom(4).AlignCenter().Text(t => { t.DefaultTextStyle(s => s.FontSize(14).Bold()); Spans(t, b.Lineas[0]); });
                                break;
                            case "subtitulo":
                                col.Item().PaddingTop(6).Text(t => { t.DefaultTextStyle(s => s.FontSize(11.5f).SemiBold()); Spans(t, b.Lineas[0]); });
                                break;
                            case "lista":
                                col.Item().Column(lista =>
                                {
                                    lista.Spacing(2);
                                    foreach (var linea in b.Lineas)
                                        lista.Item().Row(r =>
                                        {
                                            r.ConstantItem(14).Text("•");
                                            r.RelativeItem().Text(t => Spans(t, linea));
                                        });
                                });
                                break;
                            default:
                                col.Item().Text(t => { t.Justify(); Spans(t, b.Lineas[0]); });
                                break;
                        }
                    }

                    if (firmas.Count > 0)
                    {
                        col.Item().PaddingTop(28).Column(zona =>
                        {
                            zona.Spacing(24);
                            foreach (var par in firmas.Chunk(2))
                                zona.Item().Row(row =>
                                {
                                    row.Spacing(30);
                                    foreach (var f in par)
                                        row.RelativeItem().Column(c =>
                                        {
                                            var leyenda = string.Concat(f.Lineas[0].Select(s => s.Texto));
                                            var esProfesional = leyenda.StartsWith("Profesional", StringComparison.OrdinalIgnoreCase);
                                            if (esProfesional && firmaProfesional is { Length: > 0 })
                                                c.Item().Height(45).AlignCenter().Image(firmaProfesional).FitArea();
                                            else
                                                c.Item().Height(45);
                                            c.Item().BorderTop(0.75f).BorderColor(Colors.Grey.Darken2).PaddingTop(3)
                                                .AlignCenter().Text(leyenda).FontSize(9);
                                        });
                                    if (par.Length == 1) row.RelativeItem();
                                });
                        });
                    }
                });

                page.Footer().BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text($"Emitido el {fechaEmision:dd/MM/yyyy HH:mm} · Código de verificación {codigoVerificacion}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                        t.Span("Página ");
                        t.CurrentPageNumber();
                        t.Span(" de ");
                        t.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Spans(TextDescriptor t, List<Segmento> segmentos)
    {
        foreach (var s in segmentos)
        {
            var span = t.Span(s.Texto);
            if (s.Negrita) span.Bold();
        }
    }
}
