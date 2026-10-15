// Política de contraseñas y mensajes de conflicto del alta pública (PoliticaContrasena, MensajesConflictoRegistro).
// Sin base de datos. Que los endpoints las usen lo prueba RegistroHardeningIntegrationTests.
public class PoliticaContrasenaTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("12345")]
    public void Una_contrasena_vacia_o_de_menos_de_6_no_pasa(string? contrasena)
    {
        var error = PoliticaContrasena.Validar(contrasena);
        Assert.NotNull(error);
        Assert.Contains("6", error);
    }

    [Fact]
    public void El_mensaje_del_minimo_es_el_que_ya_mostraban_las_pantallas()
    {
        Assert.Equal("La contraseña debe tener al menos 6 caracteres.", PoliticaContrasena.Validar("abc"));
    }

    [Fact]
    public void De_6_a_100_caracteres_pasa()
    {
        Assert.Null(PoliticaContrasena.Validar("123456"));
        Assert.Null(PoliticaContrasena.Validar(new string('x', 100)));
    }

    [Fact]
    public void De_mas_de_100_no_pasa()
    {
        var error = PoliticaContrasena.Validar(new string('x', 101));
        Assert.NotNull(error);
        Assert.Contains("100", error);
    }
}

public class MensajesConflictoRegistroTests
{
    [Theory]
    [InlineData("mail")]
    [InlineData("dni")]
    [InlineData("conflicto")]     // el resguardo de la carrera entre dos altas (SqlException 2627/2601)
    [InlineData("otra-cosa")]
    [InlineData(null)]
    public void DNI_mail_y_cualquier_otro_conflicto_responden_lo_mismo(string? campo)
    {
        Assert.Equal(MensajesConflictoRegistro.DatosYaRegistrados, MensajesConflictoRegistro.Paciente(campo));
    }

    [Fact]
    public void El_mensaje_neutral_no_dice_cual_dato_coincidio()
    {
        var m = MensajesConflictoRegistro.DatosYaRegistrados;
        Assert.DoesNotContain("DNI", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mail", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documento", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correo", m, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void El_mensaje_neutral_orienta_a_quien_de_verdad_ya_tiene_cuenta()
    {
        var m = MensajesConflictoRegistro.DatosYaRegistrados;
        Assert.Contains("ingresá", m);
        Assert.Contains("recuperá tu contraseña", m);
    }

    [Fact]
    public void El_nombre_de_usuario_en_uso_sigue_siendo_especifico()
    {
        Assert.Equal("Ese nombre de usuario ya está en uso.", MensajesConflictoRegistro.Paciente("usuario"));
        Assert.NotEqual(MensajesConflictoRegistro.DatosYaRegistrados, MensajesConflictoRegistro.Paciente("usuario"));
    }
}
