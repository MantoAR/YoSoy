// Test de integración con una base de datos: confirma que InsertarSiLibre (la inserción
// que ya usa TurnoReglasService.Solicitar/SolicitarUrgente) de verdad evita la doble reserva
// bajo concurrencia real — no alcanza con probarlo en secuencia contra un repositorio falso,
// hace falta el locking real de la base (Fase 3a, Paso 1).
// El paciente, el profesional y el estado "Pendiente" son PROPIOS de la prueba (DatosDePrueba):
// antes dependía del paciente 1, el profesional 1 y de que el estado con id 1 existiera.
[Trait("Categoria", "Integracion")]
public class TurnoDobleReservaIntegrationTests
{
    [Fact]
    public async Task Pedidos_simultaneos_del_mismo_horario_solo_dejan_un_turno()
    {
        using var datos = new DatosDePrueba();
        var idProfesional = datos.Profesional();
        var idPaciente = datos.Paciente();
        var idEstadoPendiente = datos.EstadoTurno("Pendiente");
        datos.AlLimpiar("Turno", "IdPaciente", idPaciente);        // lo que crea InsertarSiLibre

        var fecha = new DateTime(2030, 6, 17);     // bien en el futuro
        var hora = new TimeSpan(10, 0, 0);

        var dao = new TurnoRepositorioDAO();
        var turno = new Turno
        {
            IdPaciente = idPaciente, IdProfesional = idProfesional,
            IdEstadoTurno = idEstadoPendiente, Fecha = fecha, Hora = hora, Modalidad = "Virtual", Motivo = "[TEST] doble reserva"
        };

        // 15 pedidos en paralelo, mismo paciente/profesional/horario: cada uno abre su
        // propia conexión (Task.Run + hilo propio), así se ejercita el locking real de
        // la base, no solo el orden en que los llama C#.
        var tareas = Enumerable.Range(0, 15)
            .Select(_ => Task.Run(() => dao.InsertarSiLibre(turno, "paciente")))
            .ToArray();
        var resultados = await Task.WhenAll(tareas);

        var exitosos = resultados.Where(id => id is not null).ToList();
        Assert.Single(exitosos);   // de los 15 pedidos simultáneos, solo uno se creó

        // y en la base queda exactamente una fila
        Assert.Equal(1, DatosDePrueba.Contar(
            "SELECT COUNT(*) FROM Turno WHERE IdPaciente = @Pac AND IdProfesional = @Prof AND Fecha = @Fecha AND Hora = @Hora",
            ("@Pac", idPaciente), ("@Prof", idProfesional), ("@Fecha", fecha), ("@Hora", hora)));
    }
}
