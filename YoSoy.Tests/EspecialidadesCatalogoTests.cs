// Configuración › Especialidades (rama AAA1, migración 34): validaciones del catálogo, sin base de datos.
using static EspecialidadesEndpoints;

public class EspecialidadesCatalogoTests
{
    private static string? Validar(string? nombre, string? tipo = null, string? disciplina = null) =>
        EspecialidadesEndpoints.Validar(Normalizar(new EspecialidadRequest(nombre, disciplina, tipo, false)));

    [Fact]
    public void Nombre_obligatorio_de_3_a_100_caracteres()
    {
        Assert.NotNull(Validar(null));
        Assert.NotNull(Validar("  "));
        Assert.NotNull(Validar("Ab"));
        Assert.NotNull(Validar(new string('a', 101)));
        Assert.Null(Validar("Psicología Clínica"));
    }

    [Fact]
    public void Normaliza_espacios_y_tipo_por_defecto()
    {
        var r = Normalizar(new EspecialidadRequest("  Psicología   Sanitaria ", "  ", null, true));
        Assert.Equal("Psicología Sanitaria", r.Nombre);
        Assert.Null(r.Disciplina);
        Assert.Equal("Especialidad", r.Tipo);
    }

    [Theory]
    [InlineData("Especialidad", true)]
    [InlineData("Servicio", true)]
    [InlineData("Otra cosa", false)]
    public void Solo_dos_tipos(string tipo, bool valido) =>
        Assert.Equal(valido, Validar("Admisión", tipo) is null);

    [Fact]
    public void Disciplina_hasta_60_caracteres() =>
        Assert.NotNull(Validar("Psicología Clínica", null, new string('x', 61)));
}
