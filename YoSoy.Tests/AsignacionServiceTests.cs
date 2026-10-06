// Tests de la cascada de asignación (Fase 2) con FakeAsignacionRepositorio: sin base de
// datos. El test de integración con la base real está en AsignacionIntegrationTests.
public class AsignacionServiceTests
{
    private static CandidatoAsignacion Cand(int id, bool disponible, int carga, DateTime? ultima = null) =>
        new(id, disponible, carga, ultima);

    [Fact]
    public void Elige_al_disponible_con_menos_carga()
    {
        var candidatos = new[] { Cand(1, true, 3), Cand(2, true, 1), Cand(3, true, 5) };
        var r = AsignacionService.Elegir(candidatos, new Random(1));
        Assert.Equal(2, r.IdProfesional);
        Assert.Equal("AutomaticaPorCarga", r.Motivo);
    }

    [Fact]
    public void Ignora_a_los_no_disponibles_aunque_tengan_menos_carga()
    {
        var candidatos = new[] { Cand(1, false, 0), Cand(2, true, 4) };
        var r = AsignacionService.Elegir(candidatos, new Random(1));
        Assert.Equal(2, r.IdProfesional);
    }

    [Fact]
    public void Desempata_por_antiguedad_de_la_ultima_asignacion()
    {
        var candidatos = new[]
        {
            Cand(1, true, 2, new DateTime(2026, 9, 1)),   // la más reciente
            Cand(2, true, 2, new DateTime(2026, 1, 1)),   // hace más tiempo: le toca a este
            Cand(3, true, 2, new DateTime(2026, 6, 1)),
        };
        var r = AsignacionService.Elegir(candidatos, new Random(1));
        Assert.Equal(2, r.IdProfesional);
        Assert.Equal("AutomaticaPorCarga", r.Motivo);
    }

    [Fact]
    public void Nunca_asignado_cuenta_como_lo_mas_antiguo_para_el_desempate()
    {
        var candidatos = new[]
        {
            Cand(1, true, 0, new DateTime(2020, 1, 1)),
            Cand(2, true, 0, ultima: null),   // nunca recibió un caso: gana el desempate
        };
        var r = AsignacionService.Elegir(candidatos, new Random(1));
        Assert.Equal(2, r.IdProfesional);
    }

    [Fact]
    public void Respaldo_aleatorio_con_semilla_fija_es_reproducible()
    {
        // Misma carga y misma antigüedad (ambos nunca asignados): no hay desempate posible,
        // decide el generador aleatorio. Con la misma semilla, siempre el mismo resultado.
        var candidatos = new[] { Cand(1, true, 0), Cand(2, true, 0), Cand(3, true, 0) };
        var r1 = AsignacionService.Elegir(candidatos, new Random(42));
        var r2 = AsignacionService.Elegir(candidatos, new Random(42));
        Assert.Equal("AutomaticaAleatoria", r1.Motivo);
        Assert.Equal(r1.IdProfesional, r2.IdProfesional);
        Assert.Contains(r1.IdProfesional, candidatos.Select(c => (int?)c.IdProfesional));
    }

    [Fact]
    public void Grupo_sin_profesionales_queda_sin_asignar()
    {
        var r = AsignacionService.Elegir(Array.Empty<CandidatoAsignacion>(), new Random(1));
        Assert.Null(r.IdProfesional);
        Assert.Equal("SinDisponibles", r.Motivo);
    }

    [Fact]
    public void Nadie_disponible_queda_sin_asignar_aunque_haya_candidatos()
    {
        var candidatos = new[] { Cand(1, false, 0), Cand(2, false, 2) };
        var r = AsignacionService.Elegir(candidatos, new Random(1));
        Assert.Null(r.IdProfesional);
        Assert.Equal("SinDisponibles", r.Motivo);
    }

    [Fact]
    public void Un_profesional_en_dos_grupos_carga_cuenta_para_los_dos()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.AgregarMiembro(1, 50);
        repo.AgregarMiembro(2, 50);                 // profesional 50 está en los grupos 1 y 2
        repo.ConAgendaDisponible.Add(50);
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Asignado", IdProfesionalAsignado = 50, IdGrupoActual = 1, Origen = "Admision" });

        var candidatosGrupo2 = repo.ObtenerCandidatos(2);
        Assert.Single(candidatosGrupo2);
        Assert.Equal(1, candidatosGrupo2[0].CasosActivos);   // el caso vino del grupo 1, pero la carga es por profesional
    }

    [Fact]
    public void SugerirDerivacion_deja_pendiente_de_confirmar_y_el_caso_en_Derivado()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.AgregarMiembro(1, 50);
        repo.ConAgendaDisponible.Add(50);
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Nuevo", Origen = "Admision" });
        var servicio = new AsignacionService(repo, new Random(1));

        var d = servicio.SugerirDerivacion(1, idGrupo: 1, origen: "Regla", idCreadoPor: null);

        Assert.Equal("Pendiente de confirmar", d.Estado);
        Assert.Equal(50, d.IdProfesionalSugerido);
        Assert.Equal("AutomaticaPorCarga", d.Motivo);
        Assert.Equal("Derivado", repo.Casos[0].Estado);
        Assert.Null(repo.Casos[0].IdProfesionalAsignado);    // sugerido, todavía no confirmado
        Assert.Empty(repo.Historial);                        // una sugerencia no es una asignación
    }

    [Fact]
    public void SugerirDerivacion_sin_candidatos_igual_deja_rastro_en_el_historial()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Nuevo", Origen = "Admision" });
        var servicio = new AsignacionService(repo, new Random(1));

        var d = servicio.SugerirDerivacion(1, idGrupo: 9, origen: "Regla", idCreadoPor: null);

        Assert.Null(d.IdProfesionalSugerido);
        Assert.Single(repo.Historial);
        Assert.Equal("SinDisponibles", repo.Historial[0].Motivo);
        Assert.Null(repo.Historial[0].IdProfesional);
    }

    [Fact]
    public void ConfirmarDerivacion_asigna_el_caso_y_conserva_el_motivo_original_en_el_historial()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.AgregarMiembro(1, 50);
        repo.ConAgendaDisponible.Add(50);
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Nuevo", Origen = "Admision" });
        var servicio = new AsignacionService(repo, new Random(1));
        var d = servicio.SugerirDerivacion(1, 1, "Regla", null);

        servicio.ConfirmarDerivacion(d.IdCasoDerivacion, idUsuario: 7);

        Assert.Equal("Asignado", repo.Casos[0].Estado);
        Assert.Equal(50, repo.Casos[0].IdProfesionalAsignado);
        Assert.Equal("Confirmada", repo.Derivaciones[0].Estado);
        var h = Assert.Single(repo.Historial);
        Assert.Equal(50, h.IdProfesional);
        Assert.Equal("AutomaticaPorCarga", h.Motivo);   // el motivo de la cascada, no "Manual"
        Assert.Equal(7, h.IdRealizadoPor);
    }

    [Fact]
    public void CorregirDerivacion_cambia_el_profesional_y_queda_Manual_en_el_historial()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.AgregarMiembro(1, 50);
        repo.AgregarMiembro(1, 60);
        repo.ConAgendaDisponible.Add(50);
        repo.ConAgendaDisponible.Add(60);
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Nuevo", Origen = "Admision" });
        repo.Casos.Add(new Caso { IdCaso = 2, IdPaciente = 2, Estado = "Asignado", IdProfesionalAsignado = 60, IdGrupoActual = 1, Origen = "Admision" });
        var servicio = new AsignacionService(repo, new Random(1));
        var d = servicio.SugerirDerivacion(1, 1, "Regla", null);   // 60 ya tiene carga: sugiere 50, sin ambigüedad
        Assert.Equal(50, d.IdProfesionalSugerido);

        servicio.CorregirDerivacion(d.IdCasoDerivacion, idUsuario: 7, nuevoProfesional: 60);

        Assert.Equal("Corregida", repo.Derivaciones[0].Estado);
        Assert.Equal(60, repo.Casos[0].IdProfesionalAsignado);
        var h = Assert.Single(repo.Historial);
        Assert.Equal(60, h.IdProfesional);
        Assert.Equal("Manual", h.Motivo);
    }

    [Fact]
    public void RechazarDerivacion_deja_el_caso_derivado_pero_sin_profesional()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.AgregarMiembro(1, 50);
        repo.ConAgendaDisponible.Add(50);
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Nuevo", Origen = "Admision" });
        var servicio = new AsignacionService(repo, new Random(1));
        var d = servicio.SugerirDerivacion(1, 1, "Regla", null);

        servicio.RechazarDerivacion(d.IdCasoDerivacion, idUsuario: 7);

        Assert.Equal("Rechazada", repo.Derivaciones[0].Estado);
        Assert.Null(repo.Casos[0].IdProfesionalAsignado);
        Assert.Empty(repo.Historial);
    }

    [Fact]
    public void ReasignarManual_no_pasa_por_la_cascada_y_queda_Manual()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Asignado", IdGrupoActual = 1, IdProfesionalAsignado = 50, Origen = "Admision" });
        var servicio = new AsignacionService(repo, new Random(1));

        servicio.ReasignarManual(1, idProfesional: 99, idUsuario: 7);

        Assert.Equal(99, repo.Casos[0].IdProfesionalAsignado);
        Assert.Equal("Asignado", repo.Casos[0].Estado);
        var derivacion = Assert.Single(repo.Derivaciones);
        Assert.Equal("Manual", derivacion.Origen);
        Assert.Equal("Confirmada", derivacion.Estado);
        Assert.NotNull(derivacion.FechaResolucion);
        var h = Assert.Single(repo.Historial);
        Assert.Equal(99, h.IdProfesional);
        Assert.Equal("Manual", h.Motivo);
    }

    [Fact]
    public void ReasignarManual_sin_grupo_todavia_tira_error_claro()
    {
        var repo = new FakeAsignacionRepositorio();
        repo.Casos.Add(new Caso { IdCaso = 1, IdPaciente = 1, Estado = "Nuevo", Origen = "Admision" });
        var servicio = new AsignacionService(repo, new Random(1));

        Assert.Throws<InvalidOperationException>(() => servicio.ReasignarManual(1, 99, 7));
    }
}
