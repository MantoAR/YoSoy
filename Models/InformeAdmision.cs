public class InformeAdmision
{
    public int IdInforme { get; set; }
    public int IdPaciente { get; set; }
    public int IdEspecialidad { get; set; }
    public string MotivoConsulta { get; set; }
    public bool TratamientoAnterior { get; set; }
    public string TratamientoAnteriorDetalle { get; set; }
    public bool RecibeMedicacion { get; set; }
    public string MedicacionDetalle { get; set; }
    public string GustosIntereses { get; set; }
    public string PreferenciasAtencion { get; set; }
    public string TipoEntrevista { get; set; }
    public string RedApoyo { get; set; }
    public string ObjetivosPaciente { get; set; }
    public string Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    // «Sobre mí»: horario y profesionales preferidos (migración 31). Solo se guardan y se muestran; el cruce con las agendas es un trabajo siguiente.
    public HorarioPreferidoAdmision? HorarioPreferido { get; set; }
    public ProfesionalesPreferidosAdmision? ProfesionalesPreferidos { get; set; }
}
