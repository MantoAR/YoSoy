// Las reglas del cierre de sesión que no necesitan base: qué se acepta como tipo de cierre y como clave, cuándo se puede pasar un turno a Realizado y qué se le dice al
// profesional cuando algo se rechaza. Lo que toca la base (transacción, idempotencia, auditoría) está en CierreSesionBaseRealTests.
public class CierreSesionReglasTests
{
    [Theory]
    [InlineData("Seguimiento", "Seguimiento")]
    [InlineData("seguimiento", "Seguimiento")]
    [InlineData("  ALTA ", "Alta")]
    [InlineData("Derivación", "Derivación")]
    [InlineData("derivacion", "Derivación")]
    [InlineData("DERIVACIÓN", "Derivación")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void El_tipo_de_cierre_se_acepta_sin_importar_mayúsculas_ni_tilde_y_se_guarda_siempre_igual(string? crudo, string? esperado)
    {
        Assert.True(CierreSesionReglas.TryNormalizarTipo(crudo, out var tipo));
        Assert.Equal(esperado, tipo);
    }

    [Theory]
    [InlineData("Cualquier cosa")]
    [InlineData("Ausente")]
    [InlineData("Abandono")]
    [InlineData("Alta; DROP TABLE")]
    public void Un_tipo_de_cierre_que_no_es_ninguno_de_los_tres_se_rechaza(string crudo) =>
        Assert.False(CierreSesionReglas.TryNormalizarTipo(crudo, out _));

    [Fact]
    public void La_clave_tiene_que_ser_un_GUID_no_vacío()
    {
        var g = Guid.NewGuid();
        Assert.True(CierreSesionReglas.TryClave(g.ToString(), out var leida));
        Assert.Equal(g, leida);
        Assert.True(CierreSesionReglas.TryClave($" {g} ", out _));
        Assert.False(CierreSesionReglas.TryClave(Guid.Empty.ToString(), out _));
        Assert.False(CierreSesionReglas.TryClave("abc", out _));
        Assert.False(CierreSesionReglas.TryClave(null, out _));
        Assert.False(CierreSesionReglas.TryClave("", out _));
    }

    [Theory]
    [InlineData(null, null, false)]            // el pedido de siempre
    [InlineData("", null, false)]
    [InlineData(null, false, true)]            // aunque sea "false", el campo trae un cierre: se valida
    [InlineData(null, true, true)]
    [InlineData("3f9c0a7e-0000-4000-8000-000000000001", null, true)]
    public void Solo_los_pedidos_con_algún_campo_del_cierre_siguen_el_camino_nuevo(string? clave, bool? confirma, bool esperado) =>
        Assert.Equal(esperado, CierreSesionReglas.EsPedidoDeCierre(clave, confirma));

    [Theory]
    [InlineData("Confirmado", true, false)]
    [InlineData("confirmado", true, false)]
    [InlineData("Pendiente", false, false)]
    [InlineData("Cancelado", false, false)]
    [InlineData("Rechazado", false, false)]
    [InlineData("Realizado", false, true)]
    [InlineData(null, false, false)]
    [InlineData("Ausente", false, false)]       // ese estado no existe en el sistema y esta feature no lo agrega
    public void Solo_un_turno_Confirmado_puede_pasar_a_Realizado_y_Realizado_es_el_final(string? estado, bool puedePasar, bool yaEstaba)
    {
        Assert.Equal(puedePasar, CierreSesionReglas.PuedePasarARealizado(estado));
        Assert.Equal(yaEstaba, CierreSesionReglas.YaEstaRealizado(estado));
    }

    [Theory]
    [InlineData("Seguimiento", "seguimiento")]
    [InlineData("Alta", "alta")]
    [InlineData("Derivación", "derivacion")]
    [InlineData(null, "seguimiento")]
    public void El_siguiente_paso_sale_del_tipo_de_cierre(string? tipo, string esperado) =>
        Assert.Equal(esperado, CierreSesionReglas.SiguientePaso(tipo));

    [Fact]
    public void Cada_rechazo_tiene_un_mensaje_en_castellano_claro_que_dice_que_no_se_guardó_nada_o_qué_hacer()
    {
        var codigos = new[]
        {
            CierreSesionReglas.TurnoInexistente, CierreSesionReglas.TurnoNoCoincide, CierreSesionReglas.TurnoConSesion, CierreSesionReglas.EstadoNoPermitido,
            CierreSesionReglas.TurnoFuturo, CierreSesionReglas.CatalogoIncompleto, CierreSesionReglas.TipoCierreInvalido, CierreSesionReglas.ClaveInvalida,
            CierreSesionReglas.ClaveRequerida, CierreSesionReglas.RealizacionSinTurno, CierreSesionReglas.FaltaMigracion26, CierreSesionReglas.SoloElProfesionalConfirma
        };
        foreach (var codigo in codigos)
        {
            var m = CierreSesionReglas.Mensaje(codigo, "Pendiente");
            Assert.False(string.IsNullOrWhiteSpace(m), codigo);
            Assert.DoesNotContain("409", m);
            Assert.DoesNotContain("SQL", m, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", m, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("Pendiente", CierreSesionReglas.Mensaje(CierreSesionReglas.EstadoNoPermitido, "Pendiente"));
        Assert.Contains("No se realizó", CierreSesionReglas.Mensaje(CierreSesionReglas.EstadoNoPermitido, "Cancelado"));
    }

    [Fact]
    public void El_detalle_de_auditoría_respeta_el_tope_de_la_columna_aun_con_ids_enormes()
    {
        var largo = CierreSesionReglas.Detalle(new string('9', 900));
        Assert.Equal(500, largo.Length);
        Assert.Equal("corto", CierreSesionReglas.Detalle("corto"));
    }
}
