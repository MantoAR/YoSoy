using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[Trait("Categoria", "Integracion")]
public class VentanaDeConversacionBaseRealTests
{
    [Fact]
    public async Task La_DI_comparte_el_reloj_fijo_y_el_historial_entre_resumen_y_envio()
    {
        using var datos = new DatosDePrueba();
        var telefono = "di24-" + Guid.NewGuid().ToString("N")[..12];
        datos.AlLimpiar("Conversacion", "Telefono", telefono);
        var c = new ConversacionDAO().ObtenerOCrear(telefono, "Prueba");
        var reloj = new RelojFijo(new DateTime(2031, 3, 4, 10, 0, 0));
        var historial = new MensajesChatEnMemoria();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mensajeria:PalabrasRiesgo:0"] = "urgencia"
        }).Build();
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        // Las mismas extensiones y el mismo orden que Program.cs.
        servicios.AddRelojDeLaClinica(config);
        servicios.RemoveAll<TimeProvider>();
        servicios.AddSingleton<TimeProvider>(reloj);
        servicios.AddMensajeria(config);
        servicios.RemoveAll<IMensajeChatDAO>();
        servicios.AddSingleton<IMensajeChatDAO>(historial);
        servicios.RemoveAll<IMessageChannel>();
        servicios.AddSingleton<IMessageChannel>(new CanalEspia());
        using var proveedor = servicios.BuildServiceProvider();
        var conversaciones = proveedor.GetRequiredService<IConversacionDAO>();
        var mensajes = proveedor.GetRequiredService<IServicioMensajes>();
        var entrante = mensajes.RegistrarEntrante(c, "Hola");
        Assert.Equal(reloj.Ahora, entrante.FechaHora);
        var abierta = conversaciones.ListarResumen(null).Single(x => x.IdConversacion == c.IdConversacion).Ventana!;
        Assert.True(abierta.Abierta);
        Assert.Equal(86400, abierta.SegundosRestantes);
        Assert.Equal("Enviado", (await mensajes.EnviarAsync(c, "Respuesta", "Humano")).EstadoEnvio);
        reloj.Avanzar(TimeSpan.FromDays(1));
        var cerrada = conversaciones.ListarResumen(null).Single(x => x.IdConversacion == c.IdConversacion).Ventana!;
        Assert.False(cerrada.Abierta);
        Assert.Equal(ServicioMensajes.CodigoVentanaCerrada,
            (await mensajes.EnviarAsync(c, "Respuesta tardía", "Humano")).CodigoDeError);
        Assert.Equal(2, historial.Todos.Count);
    }

    [Fact]
    public void Ultimo_entrante_ignora_bajas_salientes_y_otras_conversaciones()
    {
        var conversaciones = new ConversacionDAO();
        var mensajes = new MensajeChatDAO();
        var telefono = "v24-" + Guid.NewGuid().ToString("N")[..12];
        var conversacion = conversaciones.ObtenerOCrear(telefono, "WhatsApp");
        var otra = conversaciones.ObtenerOCrear(telefono + "2", "WhatsApp");
        var fecha = new DateTime(2031, 3, 4, 10, 0, 0);
        void Guardar(int id, string texto, DateTime hora, string direccion = "Entrante") =>
            mensajes.Insertar(new MensajeChat
            {
                IdConversacion = id, Direccion = direccion,
                Origen = direccion == "Entrante" ? "Paciente" : "Humano",
                Texto = texto, FechaHora = hora, EstadoEnvio = "Enviado"
            });
        try
        {
            Assert.Null(mensajes.UltimoEntrante(conversacion.IdConversacion));
            foreach (var baja in new[] { "BAJA", " ¡Bája! ", "STOP", "No quiero más mensajes." })
                Guardar(conversacion.IdConversacion, baja, fecha.AddHours(25));
            for (var i = 0; i < 41; i++)
                Guardar(conversacion.IdConversacion, "BAJA", fecha.AddHours(25).AddSeconds(i));
            Assert.Null(mensajes.UltimoEntrante(conversacion.IdConversacion));

            Guardar(conversacion.IdConversacion, "Hola", fecha.AddHours(-1));
            Guardar(conversacion.IdConversacion, "Necesito un turno", fecha);
            Guardar(conversacion.IdConversacion, "Respuesta", fecha.AddHours(26), "Saliente");
            Guardar(otra.IdConversacion, "Otra persona", fecha.AddHours(27));
            Assert.Equal(fecha, mensajes.UltimoEntrante(conversacion.IdConversacion));
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand(@"
                DELETE FROM MensajeChat WHERE IdConversacion IN (@Id, @Otra);
                DELETE FROM Conversacion WHERE IdConversacion IN (@Id, @Otra);", con);
            cmd.Parameters.AddWithValue("@Id", conversacion.IdConversacion);
            cmd.Parameters.AddWithValue("@Otra", otra.IdConversacion);
            cmd.ExecuteNonQuery();
        }
    }
}
