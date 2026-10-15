// Test de integración con una base de datos para el link público de reserva (Fase 3a,
// Paso 3): genera un slug la primera vez que se activa, lo conserva al apagar/prender de
// nuevo, y ObtenerPorSlugActivo da null tanto para un slug inexistente como para uno
// existente con el link apagado — misma respuesta para los dos casos a propósito.
// Usa un profesional PROPIO de la prueba (DatosDePrueba): antes dependía del profesional 1 de la
// base de desarrollo, le activaba el link y se lo restauraba al final.
[Trait("Categoria", "Integracion")]
public class ProfesionalLinkPublicoIntegrationTests
{
    [Fact]
    public void ActivarLinkPublico_genera_slug_una_sola_vez_y_lo_conserva()
    {
        using var datos = new DatosDePrueba();
        var idProfesional = datos.Profesional(estado: "Activo");   // ObtenerPorSlugActivo exige Estado = 'Activo'
        var dao = new ProfesionalDAO();

        var activado1 = dao.ActivarLinkPublico(idProfesional, true);
        Assert.False(string.IsNullOrEmpty(activado1.Slug));
        Assert.True(activado1.LinkPublicoActivo);
        Assert.Matches("^[a-z0-9-]+$", activado1.Slug!);

        var apagado = dao.ActivarLinkPublico(idProfesional, false);
        Assert.Equal(activado1.Slug, apagado.Slug);     // apagar no borra el slug
        Assert.False(apagado.LinkPublicoActivo);

        var reactivado = dao.ActivarLinkPublico(idProfesional, true);
        Assert.Equal(activado1.Slug, reactivado.Slug);  // reactivar no genera uno nuevo

        // Con el link apagado, la búsqueda pública no lo tiene que encontrar.
        dao.ActivarLinkPublico(idProfesional, false);
        Assert.Null(dao.ObtenerPorSlugActivo(activado1.Slug!));

        // Reactivado, sí.
        dao.ActivarLinkPublico(idProfesional, true);
        var porSlug = dao.ObtenerPorSlugActivo(activado1.Slug!);
        Assert.NotNull(porSlug);
        Assert.Equal(idProfesional, porSlug!.IdProfesional);

        // Slug que nunca existió: también null, misma forma de respuesta.
        Assert.Null(dao.ObtenerPorSlugActivo("no-existe-esto-nunca-123456"));
    }

    [Fact]
    public void Un_profesional_que_no_esta_activo_no_aparece_por_su_slug_aunque_el_link_este_prendido()
    {
        using var datos = new DatosDePrueba();
        var idProfesional = datos.Profesional(estado: "Activo");
        var dao = new ProfesionalDAO();
        var slug = dao.ActivarLinkPublico(idProfesional, true).Slug!;
        Assert.NotNull(dao.ObtenerPorSlugActivo(slug));

        DatosDePrueba.Ejecutar("UPDATE Profesional SET Estado = N'Inactivo' WHERE IdProfesional = @p", ("@p", idProfesional));

        Assert.Null(dao.ObtenerPorSlugActivo(slug));   // misma respuesta que un slug que no existe
    }
}
