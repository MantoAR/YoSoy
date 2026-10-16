// Test de integración con una base de datos: crea sus propias filas de prueba (el profesional, el paciente y su
// agenda, dos grupos temporales y los casos) y las borra todas. Antes usaba al profesional 1 (Santiago Manto) y
// al paciente 1 de la base de desarrollo y suponía que el profesional tenía una agenda disponible.
[Trait("Categoria", "Integracion")]
public class AsignacionIntegrationTests
{
    [Fact]
    public void Candidatos_y_cascada_contra_la_base_real_no_dejan_filas_propias()
    {
        var datos = new DatosDePrueba();
        var idProfesional = datos.Profesional(estado: "Activo");
        datos.Agenda(idProfesional, "Lunes");           // "disponible" = Activo + al menos una franja de Agenda con Disponible = 1
        var idPaciente = datos.Paciente();

        var grupoDao = new GrupoDAO();
        var casoDao = new CasoDAO();
        var repo = new AsignacionRepositorioDAO();
        var servicio = new AsignacionService(repo, new Random(123));

        int idGrupoA = grupoDao.Insertar(new Grupo { Nombre = $"[TEST] Grupo A {datos.Marca}", Activo = true });
        int idGrupoB = grupoDao.Insertar(new Grupo { Nombre = $"[TEST] Grupo B {datos.Marca}", Activo = true });
        // Lo que crean los grupos, los casos, el historial y las derivaciones se borra solo (en cascada).
        datos.AlLimpiar("Grupo", "IdGrupo", idGrupoB);
        datos.AlLimpiar("Grupo", "IdGrupo", idGrupoA);
        datos.AlLimpiar("Caso", "IdPaciente", idPaciente);
        datos.AlLimpiar("PacienteProfesional", "IdPaciente", idPaciente);

        try
        {
            // El profesional queda en los dos grupos (prueba "un profesional en dos grupos").
            grupoDao.AgregarMiembro(idGrupoA, idProfesional);
            grupoDao.AgregarMiembro(idGrupoB, idProfesional);

            // Un caso ya asignado por el grupo A: la carga tiene que verse también desde el grupo B.
            casoDao.Insertar(new Caso { IdPaciente = idPaciente, Estado = "Asignado", IdGrupoActual = idGrupoA, IdProfesionalAsignado = idProfesional, Origen = "Admision" });

            var candidatosB = repo.ObtenerCandidatos(idGrupoB);
            var candidato = Assert.Single(candidatosB);
            Assert.Equal(idProfesional, candidato.IdProfesional);
            Assert.True(candidato.Disponible);
            Assert.Equal(1, candidato.CasosActivos);

            // Cascada real de punta a punta sobre el grupo B: único candidato, se sugiere y se confirma.
            var idCasoNuevo = casoDao.Insertar(new Caso { IdPaciente = idPaciente, Estado = "Nuevo", Origen = "Admision" });
            var derivacion = servicio.SugerirDerivacion(idCasoNuevo, idGrupoB, "Regla", idCreadoPor: null);
            Assert.Equal(idProfesional, derivacion.IdProfesionalSugerido);

            servicio.ConfirmarDerivacion(derivacion.IdCasoDerivacion, idUsuario: null);

            var casoConfirmado = casoDao.ObtenerPorId(idCasoNuevo)!;
            Assert.Equal("Asignado", casoConfirmado.Estado);
            Assert.Equal(idProfesional, casoConfirmado.IdProfesionalAsignado);

            // PacienteProfesional (la tabla operativa de siempre) queda sincronizada.
            var pacienteProfesionalDao = new PacienteProfesionalDAO();
            Assert.Contains(pacienteProfesionalDao.ObtenerProfesionalesDePaciente(idPaciente), p => p.IdProfesional == idProfesional);
        }
        finally
        {
            datos.Dispose();
            // No queda nada propio: ni los grupos, ni sus casos, ni el vínculo del paciente.
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Grupo WHERE IdGrupo IN (@A, @B)", ("@A", idGrupoA), ("@B", idGrupoB)));
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Caso WHERE IdPaciente = @P", ("@P", idPaciente)));
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P", ("@P", idPaciente)));
        }
    }
}
