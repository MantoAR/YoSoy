using Microsoft.Data.SqlClient;

// Test de integración con la base local real (yosoy): crea sus propias filas de prueba
// (dos grupos temporales, un vínculo a un profesional real ya existente, y un caso) y las
// borra todas en el finally. Usa al profesional 1 (Santiago Manto) sin tocarlo: solo lo
// vincula a los grupos nuevos y crea un Caso nuevo, nada existente se modifica.
public class AsignacionIntegrationTests
{
    [Fact]
    public void Candidatos_y_cascada_contra_la_base_real_no_dejan_filas_propias()
    {
        const int idProfesionalReal = 1;   // Santiago Manto: Activo, con Agenda Disponible=1 (ver CAMBIOS_AGUS.md)
        const int idPacienteReal = 1;

        var grupoDao = new GrupoDAO();
        var casoDao = new CasoDAO();
        var repo = new AsignacionRepositorioDAO();
        var servicio = new AsignacionService(repo, new Random(123));

        int idGrupoA = grupoDao.Insertar(new Grupo { Nombre = "[TEST] Grupo A", Activo = true });
        int idGrupoB = grupoDao.Insertar(new Grupo { Nombre = "[TEST] Grupo B", Activo = true });
        int idCasoConCarga = 0, idCasoNuevo = 0, idCasoDerivacion = 0;

        try
        {
            // El profesional real queda en los dos grupos (prueba "un profesional en dos grupos").
            grupoDao.AgregarMiembro(idGrupoA, idProfesionalReal);
            grupoDao.AgregarMiembro(idGrupoB, idProfesionalReal);

            // Un caso ya asignado por el grupo A: la carga tiene que verse también desde el grupo B.
            idCasoConCarga = casoDao.Insertar(new Caso { IdPaciente = idPacienteReal, Estado = "Asignado", IdGrupoActual = idGrupoA, IdProfesionalAsignado = idProfesionalReal, Origen = "Admision" });

            var candidatosB = repo.ObtenerCandidatos(idGrupoB);
            var candidato = Assert.Single(candidatosB);
            Assert.Equal(idProfesionalReal, candidato.IdProfesional);
            Assert.True(candidato.Disponible);
            Assert.Equal(1, candidato.CasosActivos);

            // Cascada real de punta a punta sobre el grupo B: único candidato, se sugiere y se confirma.
            idCasoNuevo = casoDao.Insertar(new Caso { IdPaciente = idPacienteReal, Estado = "Nuevo", Origen = "Admision" });
            var derivacion = servicio.SugerirDerivacion(idCasoNuevo, idGrupoB, "Regla", idCreadoPor: null);
            idCasoDerivacion = derivacion.IdCasoDerivacion;
            Assert.Equal(idProfesionalReal, derivacion.IdProfesionalSugerido);

            servicio.ConfirmarDerivacion(idCasoDerivacion, idUsuario: null);

            var casoConfirmado = casoDao.ObtenerPorId(idCasoNuevo)!;
            Assert.Equal("Asignado", casoConfirmado.Estado);
            Assert.Equal(idProfesionalReal, casoConfirmado.IdProfesionalAsignado);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            void Ejecutar(string sql, string param, int valor)
            {
                var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue(param, valor);
                cmd.ExecuteNonQuery();
            }
            if (idCasoDerivacion != 0) Ejecutar("DELETE FROM CasoDerivacion WHERE IdCasoDerivacion = @Id", "@Id", idCasoDerivacion);
            foreach (var idCaso in new[] { idCasoConCarga, idCasoNuevo }.Where(id => id != 0))
            {
                Ejecutar("DELETE FROM HistorialAsignacion WHERE IdCaso = @Id", "@Id", idCaso);
                Ejecutar("DELETE FROM CasoDerivacion WHERE IdCaso = @Id", "@Id", idCaso);
                Ejecutar("DELETE FROM Caso WHERE IdCaso = @Id", "@Id", idCaso);
            }
            Ejecutar("DELETE FROM ProfesionalGrupo WHERE IdGrupo = @Id", "@Id", idGrupoA);
            Ejecutar("DELETE FROM ProfesionalGrupo WHERE IdGrupo = @Id", "@Id", idGrupoB);
            Ejecutar("DELETE FROM Grupo WHERE IdGrupo = @Id", "@Id", idGrupoA);
            Ejecutar("DELETE FROM Grupo WHERE IdGrupo = @Id", "@Id", idGrupoB);

            // Verifica que no quedó nada propio: ni rastro de los dos IdGrupo de prueba.
            var cmdVerificar = new SqlCommand(
                "SELECT COUNT(*) FROM Grupo WHERE IdGrupo IN (@A, @B)", con);
            cmdVerificar.Parameters.AddWithValue("@A", idGrupoA);
            cmdVerificar.Parameters.AddWithValue("@B", idGrupoB);
            Assert.Equal(0, (int)cmdVerificar.ExecuteScalar());
        }
    }
}
