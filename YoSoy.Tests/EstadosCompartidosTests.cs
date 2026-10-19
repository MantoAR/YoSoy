using System.Collections.Concurrent;

// El catálogo EstadoTurno es compartido por todas las pruebas que usan la base (y corren en paralelo), y una base recién creada lo tiene vacío. Estas pruebas verifican la regla
// de EstadosCompartidos / DatosDePrueba.EstadoTurno: el estado que creó una prueba no se borra mientras otra lo sigue usando. Antes, quien lo creaba lo borraba al terminar, y a la
// prueba que lo había encontrado ahí le desaparecía a mitad de camino: era el 500 intermitente de
// CierreSeguridadTests.Turno_solicitar_ignora_el_idPaciente_del_cuerpo_y_usa_el_del_token ("No existe el estado 'Pendiente' en EstadoTurno").
// Cada prueba usa una descripción PROPIA (no 'Pendiente'): no depende de lo que haya en el catálogo ni toca el de las demás.
[Trait("Categoria", "Integracion")]
public class EstadosCompartidosTests
{
    private static string Propia() => "XTEST-" + Guid.NewGuid().ToString("N")[..10];

    private static int Filas(string descripcion) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM EstadoTurno WHERE Descripcion = @d", ("@d", descripcion));

    [Fact]
    public void El_estado_que_creo_una_prueba_no_se_borra_mientras_otra_lo_sigue_usando()
    {
        var descripcion = Propia();
        var creadora = new DatosDePrueba();
        var buscadora = new DatosDePrueba();
        try
        {
            var id = creadora.EstadoTurno(descripcion);                  // la base no lo tenía: lo crea
            Assert.Equal(id, buscadora.EstadoTurno(descripcion));         // la otra prueba lo encuentra ahí
            creadora.Dispose();                                           // la que lo creó termina primero...
            Assert.Equal(1, Filas(descripcion));                          // ...y el estado sigue, porque la otra lo está usando
            buscadora.Dispose();                                          // la última en soltarlo lo borra
            Assert.Equal(0, Filas(descripcion));
        }
        finally
        {
            creadora.Dispose();
            buscadora.Dispose();
        }
    }

    [Fact]
    public void Un_estado_que_la_base_ya_tenia_no_se_borra_nunca()
    {
        var descripcion = Propia();
        var idPrevio = Convert.ToInt32(DatosDePrueba.Escalar(
            "INSERT INTO EstadoTurno (Descripcion, Estado) VALUES (@d, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@d", descripcion)));
        var prueba = new DatosDePrueba();
        try
        {
            Assert.Equal(idPrevio, prueba.EstadoTurno(descripcion));
            prueba.Dispose();
            Assert.Equal(1, Filas(descripcion));                          // no lo creó esta suite: sigue ahí
        }
        finally
        {
            prueba.Dispose();
            DatosDePrueba.Ejecutar("DELETE FROM EstadoTurno WHERE IdEstadoTurno = @i", ("@i", idPrevio));
        }
    }

    [Fact]
    public void Pedir_el_mismo_estado_dos_veces_en_la_misma_prueba_no_deja_la_fila_colgada()
    {
        var descripcion = Propia();
        var prueba = new DatosDePrueba();
        try
        {
            var primero = prueba.EstadoTurno(descripcion);
            Assert.Equal(primero, prueba.EstadoTurno(descripcion));
            prueba.Dispose();
            Assert.Equal(0, Filas(descripcion));                          // los dos usos se devolvieron: la fila se borró
        }
        finally
        {
            prueba.Dispose();
        }
    }

    [Fact]
    public async Task Con_muchas_pruebas_a_la_vez_el_estado_no_desaparece_mientras_alguna_lo_usa()
    {
        var descripcion = Propia();
        var faltas = new ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 40; i++)
            {
                var prueba = new DatosDePrueba();
                try
                {
                    var id = prueba.EstadoTurno(descripcion);
                    // Mientras esta "prueba" lo tiene tomado, el estado tiene que estar en la base.
                    if (DatosDePrueba.Contar("SELECT COUNT(*) FROM EstadoTurno WHERE IdEstadoTurno = @i", ("@i", id)) != 1)
                        faltas.Add($"el estado {id} no estaba en la base mientras se usaba");
                }
                finally
                {
                    prueba.Dispose();
                }
            }
        })));

        Assert.Empty(faltas);
        Assert.Equal(0, Filas(descripcion));                              // y la última en soltarlo lo borró
    }
}
