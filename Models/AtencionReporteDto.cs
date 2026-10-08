// DTO del reporte "Atenciones por Profesional".
//
// A diferencia del endpoint GET /reportes/atenciones, que devuelve la fecha ya
// formateada como string ("dd/MM/yyyy" y "HH:mm"), acá FechaHora viaja como
// DateTime real. Eso permite calcular el período mínimo/máximo del reporte,
// emitir el atributo data-fecha en formato ISO (yyyy-MM-dd) y ordenar sin tener
// que parsear "dd/MM/yyyy" en el navegador.
public class AtencionReporteDto
{
    public string   Nombre            { get; set; } = "";
    public string   Dni               { get; set; } = "";
    public int?     Nhc               { get; set; }
    public DateTime FechaHora         { get; set; }
    public string   TipoCierre        { get; set; } = "";
    public string   NombreProfesional { get; set; } = "";
    public int      IdProfesional     { get; set; }
}
