using Microsoft.Data.SqlClient;

// Test de integración con la base local real para el link público de reserva (Fase 3a,
// Paso 3): genera un slug la primera vez que se activa, lo conserva al apagar/prender de
// nuevo, y ObtenerPorSlugActivo da null tanto para un slug inexistente como para uno
// existente con el link apagado — misma respuesta para los dos casos a propósito.
public class ProfesionalLinkPublicoIntegrationTests
{
    [Fact]
    public void ActivarLinkPublico_genera_slug_una_sola_vez_y_lo_conserva()
    {
        const int idProfesionalReal = 1;   // Santiago Manto, ya existente — se restaura al final
        var dao = new ProfesionalDAO();

        try
        {
            var activado1 = dao.ActivarLinkPublico(idProfesionalReal, true);
            Assert.False(string.IsNullOrEmpty(activado1.Slug));
            Assert.True(activado1.LinkPublicoActivo);
            Assert.Matches("^[a-z0-9-]+$", activado1.Slug!);

            var apagado = dao.ActivarLinkPublico(idProfesionalReal, false);
            Assert.Equal(activado1.Slug, apagado.Slug);     // apagar no borra el slug
            Assert.False(apagado.LinkPublicoActivo);

            var reactivado = dao.ActivarLinkPublico(idProfesionalReal, true);
            Assert.Equal(activado1.Slug, reactivado.Slug);  // reactivar no genera uno nuevo

            // Con el link apagado, la búsqueda pública no lo tiene que encontrar.
            dao.ActivarLinkPublico(idProfesionalReal, false);
            Assert.Null(dao.ObtenerPorSlugActivo(activado1.Slug!));

            // Reactivado, sí.
            dao.ActivarLinkPublico(idProfesionalReal, true);
            var porSlug = dao.ObtenerPorSlugActivo(activado1.Slug!);
            Assert.NotNull(porSlug);
            Assert.Equal(idProfesionalReal, porSlug!.IdProfesional);

            // Slug que nunca existió: también null, misma forma de respuesta.
            Assert.Null(dao.ObtenerPorSlugActivo("no-existe-esto-nunca-123456"));
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            var cmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; UPDATE Profesional SET Slug = NULL, LinkPublicoActivo = 0 WHERE IdProfesional = @Id", con);
            cmd.Parameters.AddWithValue("@Id", idProfesionalReal);
            cmd.ExecuteNonQuery();

            var cmdVerificar = new SqlCommand("SELECT Slug, LinkPublicoActivo FROM Profesional WHERE IdProfesional = @Id", con);
            cmdVerificar.Parameters.AddWithValue("@Id", idProfesionalReal);
            using var reader = cmdVerificar.ExecuteReader();
            reader.Read();
            Assert.True(reader["Slug"] == DBNull.Value);
            Assert.False((bool)reader["LinkPublicoActivo"]);
        }
    }
}
