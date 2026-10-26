// EncoladoGrabacionReglas.Encolar: al guardar una sesión clínica (POST /sesiones-clinicas), si venía de una
// videollamada que se estaba grabando, la vincula y la pasa a "pendiente". Sin base: contra FakeTranscripcionRepo.
public class EncoladoGrabacionReglasTests
{
    private const int IdSesion = 777;

    [Fact]
    public void Sin_referencia_a_grabacion_no_encola_nada()
    {
        var repo = new FakeTranscripcionRepo();

        var estado = EncoladoGrabacionReglas.Encolar(repo, null, IdSesion);

        Assert.Equal(EncoladoGrabacionReglas.SinGrabacion, estado);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("otro-origen", 1)]
    [InlineData("turno", 0)]
    [InlineData("turno", -1)]
    public void Una_referencia_invalida_no_encola_nada(string? origen, int id)
    {
        var repo = new FakeTranscripcionRepo();

        var estado = EncoladoGrabacionReglas.Encolar(repo, new GrabacionRefRequest { Origen = origen, Id = id }, IdSesion);

        Assert.Equal(EncoladoGrabacionReglas.SinGrabacion, estado);
    }

    [Fact]
    public void Sin_grabacion_activa_para_esa_sesion_de_video_no_encola_nada()
    {
        var repo = new FakeTranscripcionRepo();

        var estado = EncoladoGrabacionReglas.Encolar(repo, new GrabacionRefRequest { Origen = "turno", Id = 1 }, IdSesion);

        Assert.Equal(EncoladoGrabacionReglas.SinGrabacion, estado);
    }

    [Fact]
    public void Con_una_grabacion_activa_la_vincula_y_pasa_a_pendiente()
    {
        var repo = new FakeTranscripcionRepo();
        var fila = repo.Crear("turno", 1, idConsentimientoVideo: 55, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);

        var estado = EncoladoGrabacionReglas.Encolar(repo, new GrabacionRefRequest { Origen = "turno", Id = 1 }, IdSesion);

        Assert.Equal(TranscripcionEstado.Pendiente, estado);
        var actualizada = repo.Filas.Single(f => f.IdTranscripcion == fila.IdTranscripcion);
        Assert.Equal(TranscripcionEstado.Pendiente, actualizada.Estado);
        Assert.Equal(IdSesion, actualizada.IdSesion);
    }

    [Fact]
    public void Una_grabacion_ya_vinculada_antes_no_se_vuelve_a_encolar()
    {
        var repo = new FakeTranscripcionRepo();
        var fila = repo.Crear("turno", 1, idConsentimientoVideo: 55, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        repo.VincularSesion(fila.IdTranscripcion, idSesion: 1);   // ya se vinculó a otra sesión antes

        var estado = EncoladoGrabacionReglas.Encolar(repo, new GrabacionRefRequest { Origen = "turno", Id = 1 }, IdSesion);

        Assert.Equal(EncoladoGrabacionReglas.SinGrabacion, estado);
    }

    [Fact]
    public void Acepta_origen_admision_igual_que_turno()
    {
        var repo = new FakeTranscripcionRepo();
        repo.Crear("admision", 5, idConsentimientoVideo: 55, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);

        var estado = EncoladoGrabacionReglas.Encolar(repo, new GrabacionRefRequest { Origen = "ADMISION", Id = 5 }, IdSesion);

        Assert.Equal(TranscripcionEstado.Pendiente, estado);
    }
}
