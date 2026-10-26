using Microsoft.Extensions.Logging.Abstractions;

// VistaSesionesClinicas.Armar: el estado de la transcripción se gatea exactamente igual que las emociones (el
// Configurador no ve ninguna de las dos). Sin base: ITranscripcionSesionRepositorio es el único doble que hace falta
// para esto, porque HistorialEmociones viene vacío en todos los casos (la lectura de emociones ya tiene su propia
// cobertura en otros tests).
public class VistaSesionesClinicasTests
{
    private static SesionClinica Sesion(int idSesion = 42) => new()
    {
        IdSesion = idSesion, IdPaciente = 301, IdProfesional = 201, FechaHora = new DateTime(2031, 3, 4, 10, 0, 0)
    };

    private static VistaSesionesClinicas Vista(FakeTranscripcionRepo repo) =>
        new(new SinCifrado(), repo, NullLogger<VistaSesionesClinicas>.Instance);

    // No hace falta un fake de verdad: Leer() nunca llama a Descifrar cuando HistorialEmociones está vacío.
    private sealed class SinCifrado : IEmocionesCifrado
    {
        public string? ProblemaDeConfiguracion() => null;
        public string Cifrar(string textoPlano, string datosAsociados) => throw new NotSupportedException();
        public ResultadoDescifrado Descifrar(string valorGuardado, string datosAsociados, out string? textoPlano)
        {
            textoPlano = null;
            throw new NotSupportedException();
        }
    }

    [Fact]
    public void Para_el_Configurador_el_estado_de_transcripcion_no_se_informa_aunque_exista()
    {
        var repo = new FakeTranscripcionRepo();
        var fila = repo.Crear("turno", 1, idConsentimientoVideo: 1, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        repo.VincularSesion(fila.IdTranscripcion, idSesion: 42);
        repo.CambiarEstado(fila.IdTranscripcion, TranscripcionEstado.Lista, duracionAudioSeg: 120, duracionProcesoSeg: 30, modelo: "small");

        var vista = Vista(repo).Armar(Sesion(), puedeVerEmociones: false);

        Assert.Null(vista.EstadoTranscripcion);
    }

    [Fact]
    public void Para_quien_puede_ver_emociones_el_estado_de_transcripcion_se_informa()
    {
        var repo = new FakeTranscripcionRepo();
        var fila = repo.Crear("turno", 1, idConsentimientoVideo: 1, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        repo.VincularSesion(fila.IdTranscripcion, idSesion: 42);
        repo.CambiarEstado(fila.IdTranscripcion, TranscripcionEstado.Lista, duracionAudioSeg: 120, duracionProcesoSeg: 30, modelo: "small");

        var vista = Vista(repo).Armar(Sesion(), puedeVerEmociones: true);

        Assert.Equal(TranscripcionEstado.Lista, vista.EstadoTranscripcion);
    }

    [Fact]
    public void Sin_ninguna_transcripcion_vinculada_el_estado_es_null_aunque_se_pueda_ver_emociones()
    {
        var repo = new FakeTranscripcionRepo();

        var vista = Vista(repo).Armar(Sesion(), puedeVerEmociones: true);

        Assert.Null(vista.EstadoTranscripcion);
    }

    [Fact]
    public void Armar_una_lista_aplica_el_mismo_gateo_a_cada_sesion()
    {
        var repo = new FakeTranscripcionRepo();
        var fila1 = repo.Crear("turno", 1, idConsentimientoVideo: 1, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        repo.VincularSesion(fila1.IdTranscripcion, idSesion: 1);
        var fila2 = repo.Crear("turno", 2, idConsentimientoVideo: 2, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        repo.VincularSesion(fila2.IdTranscripcion, idSesion: 2);

        var vistas = Vista(repo).Armar(new[] { Sesion(1), Sesion(2) }, puedeVerEmociones: true);

        Assert.All(vistas, v => Assert.Equal(TranscripcionEstado.Pendiente, v.EstadoTranscripcion));
    }
}
