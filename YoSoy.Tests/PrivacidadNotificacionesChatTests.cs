using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

public class PrivacidadNotificacionesChatTests
{
    private sealed class InternasEspia : INotificacionInternaDAO
    {
        public readonly List<NotificacionInterna> Guardadas = new();
        public int Insertar(NotificacionInterna n) { Guardadas.Add(n); return Guardadas.Count; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Urgencia_y_derivacion_conservan_el_destino_sin_identidad_ni_telefono(bool vinculado)
    {
        var e = new Escenario();
        var internas = new InternasEspia();
        using var servicios = new ServiceCollection().BuildServiceProvider();
        var notificador = new NotificadorTurnos(new FakeDatos(e.Db), e.Recordatorios, internas,
            e.Conversaciones, e.Mensajes, new CanalEspia(), e.WhatsApp, new FakeTurnoRepositorio(e.Db),
            servicios, new ConfigurationBuilder().Build(), e.Opciones, e.Reloj,
            NullLogger<NotificadorTurnos>.Instance);
        var c = new Conversacion { IdConversacion = 42, IdPaciente = vinculado ? 1 : null, Telefono = Escenario.TelJuan };

        notificador.AvisarUrgencia(c, "Necesito ayuda", seRespondioAlPaciente: false);
        notificador.AvisarDerivacion(c);

        Assert.Equal(2, internas.Guardadas.Count);
        Assert.Equal(new[] { "Urgencia", "DerivacionHumano" }, internas.Guardadas.Select(n => n.Tipo));
        foreach (var n in internas.Guardadas)
        {
            Assert.Equal(42, n.IdConversacion);
            Assert.Equal("Administrador", n.Destinatario);
            Assert.Contains("Conversación #42", n.Mensaje);
            Assert.Contains(vinculado ? "paciente vinculado" : "no identificado", n.Mensaje);
            Assert.DoesNotContain(Escenario.TelJuan, n.Titulo + n.Mensaje);
            Assert.DoesNotContain(Telefono.Normalizar(Escenario.TelJuan), n.Titulo + n.Mensaje);
            Assert.DoesNotContain("Juan", n.Titulo + n.Mensaje);
            Assert.DoesNotContain("García", n.Titulo + n.Mensaje);
        }
        Assert.Contains("Necesito ayuda", internas.Guardadas[0].Mensaje);
    }
}
