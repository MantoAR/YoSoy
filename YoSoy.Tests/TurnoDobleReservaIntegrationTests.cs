using Microsoft.Data.SqlClient;

// Test de integración con la base local real: confirma que InsertarSiLibre (la inserción
// que ya usa TurnoReglasService.Solicitar/SolicitarUrgente) de verdad evita la doble reserva
// bajo concurrencia real — no alcanza con probarlo en secuencia contra un repositorio falso,
// hace falta el locking real de la base (Fase 3a, Paso 1).
public class TurnoDobleReservaIntegrationTests
{
    [Fact]
    public async Task Pedidos_simultaneos_del_mismo_horario_solo_dejan_un_turno()
    {
        const int idProfesionalReal = 1;   // Santiago Manto, ya existente — no se modifica
        const int idPacienteReal = 1;      // Juan García, ya existente — no se modifica
        var fecha = new DateTime(2030, 6, 17);     // bien en el futuro: no hay datos reales ahí
        var hora = new TimeSpan(10, 0, 0);

        var dao = new TurnoRepositorioDAO();
        var turno = new Turno
        {
            IdPaciente = idPacienteReal, IdProfesional = idProfesionalReal,
            IdEstadoTurno = 1, Fecha = fecha, Hora = hora, Modalidad = "Virtual", Motivo = "[TEST] doble reserva"
        };

        try
        {
            // 15 pedidos en paralelo, mismo paciente/profesional/horario: cada uno abre su
            // propia conexión (Task.Run + hilo propio), así se ejercita el locking real de
            // la base, no solo el orden en que los llama C#.
            var tareas = Enumerable.Range(0, 15)
                .Select(_ => Task.Run(() => dao.InsertarSiLibre(turno, "paciente")))
                .ToArray();
            var resultados = await Task.WhenAll(tareas);

            var exitosos = resultados.Where(id => id is not null).ToList();
            Assert.Single(exitosos);   // de los 15 pedidos simultáneos, solo uno se creó

            using var con = Conexion.ObtenerConexion();
            con.Open();
            var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM Turno WHERE IdPaciente = @Pac AND IdProfesional = @Prof AND Fecha = @Fecha AND Hora = @Hora", con);
            cmd.Parameters.AddWithValue("@Pac", idPacienteReal);
            cmd.Parameters.AddWithValue("@Prof", idProfesionalReal);
            cmd.Parameters.AddWithValue("@Fecha", fecha);
            cmd.Parameters.AddWithValue("@Hora", hora);
            Assert.Equal(1, (int)cmd.ExecuteScalar());   // y en la base queda exactamente una fila
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            var cmd = new SqlCommand(
                "DELETE FROM Turno WHERE IdPaciente = @Pac AND IdProfesional = @Prof AND Fecha = @Fecha AND Hora = @Hora", con);
            cmd.Parameters.AddWithValue("@Pac", idPacienteReal);
            cmd.Parameters.AddWithValue("@Prof", idProfesionalReal);
            cmd.Parameters.AddWithValue("@Fecha", fecha);
            cmd.Parameters.AddWithValue("@Hora", hora);
            cmd.ExecuteNonQuery();

            var cmdVerificar = new SqlCommand(
                "SELECT COUNT(*) FROM Turno WHERE IdPaciente = @Pac AND IdProfesional = @Prof AND Fecha = @Fecha", con);
            cmdVerificar.Parameters.AddWithValue("@Pac", idPacienteReal);
            cmdVerificar.Parameters.AddWithValue("@Prof", idProfesionalReal);
            cmdVerificar.Parameters.AddWithValue("@Fecha", fecha);
            Assert.Equal(0, (int)cmdVerificar.ExecuteScalar());
        }
    }
}
