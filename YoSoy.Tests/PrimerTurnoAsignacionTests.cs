// Primer turno de una asignación nueva (Mis pacientes › Confirmar): no se elige, es el primer horario libre de la agenda
// del profesional, así los primeros turnos quedan uno detrás de otro y no quedan huecos.
// Reloj del escenario: jueves 24/09/2026 09:00. El profesional 10 atiende de lunes a viernes de 9 a 12, cada 30 minutos.
public class PrimerTurnoAsignacionTests
{
    private readonly Escenario _e = new();
    private static DateTime Dia(int d, int h, int m = 0) => new(2026, 9, d, h, m, 0);   // septiembre 2026

    [Fact]
    public void Es_el_primer_horario_libre_posterior_a_ahora()
        => Assert.Equal(Dia(24, 9, 30), _e.Reglas.PrimerHorarioLibre(1, 10));   // las 9:00 ya pasaron (es "ahora")

    [Fact]
    public void Los_primeros_turnos_quedan_uno_detras_de_otro()
    {
        var primero = _e.Reglas.PrimerHorarioLibre(1, 10)!.Value;
        Assert.True(_e.Reglas.Solicitar(1, 10, primero, null, "profesional").Regla.Ok);
        Assert.Equal(Dia(24, 10), _e.Reglas.PrimerHorarioLibre(2, 10));
    }

    [Fact]
    public void Un_hueco_que_se_libera_se_ocupa_primero()
    {
        _e.Db.AgregarTurno(2, 10, Dia(24, 10), BaseEnMemoria.Confirmado);
        _e.Db.AgregarTurno(2, 10, Dia(24, 9, 30), BaseEnMemoria.Cancelado);
        Assert.Equal(Dia(24, 9, 30), _e.Reglas.PrimerHorarioLibre(1, 10));
    }

    [Fact]
    public void Si_el_paciente_ya_tiene_turno_ese_dia_pasa_al_siguiente()
    {
        _e.Db.AgregarTurno(1, 10, Dia(24, 11), BaseEnMemoria.Pendiente);
        Assert.Equal(Dia(25, 9), _e.Reglas.PrimerHorarioLibre(1, 10));
    }

    [Fact]
    public void Las_franjas_de_admision_no_se_usan_para_el_primer_turno()
    {
        _e.Db.Agenda.Clear();
        _e.Db.Agenda.Add(new Agenda { IdProfesional = 10, Dia = "jueves", HoraInicio = new(14, 0, 0), HoraFin = new(16, 0, 0),
                                      Disponible = true, IntervaloMinutos = 30, EsAdmision = true });
        Assert.Null(_e.Reglas.PrimerHorarioLibre(1, 10));
    }

    [Fact]
    public void Sin_agenda_no_hay_primer_turno() => Assert.Null(_e.Reglas.PrimerHorarioLibre(1, 77));
}
