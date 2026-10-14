using System.Text.Json;

// El recifrado manual de lo que se guardó en claro antes del PR 6 (RecifrarEmociones.Procesar). Es lo único de este PR que
// MODIFICA datos clínicos ya guardados, así que se prueba con cuidado: simulación por defecto, nada que no vuelva a
// descifrarse igual, ninguna fila pisada si cambió mientras tanto, y repetirlo no hace nada.
public class RecifrarEmocionesTests
{
    private const string Json1 = "{\"Neutral\":50,\"Felicidad\":30,\"Tristeza\":20}";
    private const string Json2 = "{\"Neutral\":10,\"Enojo\":90}";

    // Las filas de SesionClinica con emociones, en memoria. Se comporta como la tabla: el reemplazo solo vale si la fila
    // todavía tiene el texto viejo.
    private sealed class RepoEnMemoria : IRecifradoRepositorio
    {
        public readonly SortedDictionary<int, string> Filas = new();
        public readonly HashSet<int> CambianAntesDeEscribir = new();   // simula que otro proceso toca la fila justo antes
        public int Escrituras { get; private set; }
        public int Lecturas { get; private set; }

        public List<(int Id, string Texto)> Lote(int despuesDeId, int lote)
        {
            Lecturas++;
            return Filas.Where(f => f.Key > despuesDeId).Take(lote).Select(f => (f.Key, f.Value)).ToList();
        }

        public bool Reemplazar(int id, string viejo, string nuevo)
        {
            if (CambianAntesDeEscribir.Contains(id)) Filas[id] = viejo + " (editada por otro)";
            if (!Filas.TryGetValue(id, out var actual) || actual != viejo) return false;
            Filas[id] = nuevo;
            Escrituras++;
            return true;
        }
    }

    // Un cifrado que cifra bien pero "descifra" distinto: la prueba de ida y vuelta tiene que frenar la escritura.
    private sealed class CifradoQueNoVuelve : IEmocionesCifrado
    {
        private readonly IEmocionesCifrado _real;
        public CifradoQueNoVuelve(IEmocionesCifrado real) => _real = real;
        public string? ProblemaDeConfiguracion() => _real.ProblemaDeConfiguracion();
        public string Cifrar(string texto, string datosAsociados) => _real.Cifrar(texto, datosAsociados);
        public ResultadoDescifrado Descifrar(string valor, string datosAsociados, out string? texto)
        {
            _real.Descifrar(valor, datosAsociados, out _);
            texto = "otra cosa";
            return ResultadoDescifrado.Ok;
        }
    }

    private static (EscenarioEmociones E, RepoEnMemoria Repo, string YaCifrado) Armar()
    {
        var e = new EscenarioEmociones();
        var repo = new RepoEnMemoria();
        var yaCifrado = e.Cifrado.Cifrar(Json2, EmocionesCifradoService.DatosAsociadosDeSesion(3));
        repo.Filas[1] = Json1;                  // en claro
        repo.Filas[2] = Json2;                  // en claro
        repo.Filas[3] = yaCifrado;              // ya cifrada
        repo.Filas[4] = "esto no es un json";   // en claro pero no es JSON
        return (e, repo, yaCifrado);
    }

    private static string Leer(IEmocionesCifrado c, string valor, int idSesion)
    {
        Assert.Equal(ResultadoDescifrado.Ok, c.Descifrar(valor, EmocionesCifradoService.DatosAsociadosDeSesion(idSesion), out var t));
        return t!;
    }

    [Fact]
    public void La_simulacion_cuenta_y_no_modifica_nada()
    {
        var (e, repo, yaCifrado) = Armar();
        var antes = repo.Filas.ToDictionary(f => f.Key, f => f.Value);

        var r = RecifrarEmociones.Procesar(aplicar: false, e.Cifrado, repo);

        Assert.Equal(new ResultadoRecifrado(Total: 4, YaCifradas: 1, EnClaro: 2, NoJson: 1, Recifradas: 0, CambiaronMientras: 0, NoVerificaron: 0), r);
        Assert.Equal(0, repo.Escrituras);
        Assert.Equal(antes, repo.Filas);
        Assert.Equal(yaCifrado, repo.Filas[3]);
    }

    [Fact]
    public void Aplicar_cifra_solo_lo_que_esta_en_claro_y_se_puede_leer_de_vuelta_igual()
    {
        var (e, repo, yaCifrado) = Armar();

        var r = RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo);

        Assert.Equal(new ResultadoRecifrado(4, 1, 2, 1, Recifradas: 2, CambiaronMientras: 0, NoVerificaron: 0), r);
        Assert.StartsWith("ys1.", repo.Filas[1]);
        Assert.StartsWith("ys1.", repo.Filas[2]);
        Assert.DoesNotContain("Neutral", repo.Filas[1]);                 // ya no queda nada legible
        Assert.Equal(Json1, Leer(e.Cifrado, repo.Filas[1], 1));          // y vuelve igual, byte a byte
        Assert.Equal(Json2, Leer(e.Cifrado, repo.Filas[2], 2));
        Assert.Equal(yaCifrado, repo.Filas[3]);                           // lo ya cifrado no se toca
        Assert.Equal("esto no es un json", repo.Filas[4]);                // lo que no es JSON, tampoco
        Assert.Equal(4, repo.Filas.Count);                                // nunca se borra una fila
    }

    [Fact]
    public void Cada_valor_queda_atado_a_su_sesion()
    {
        var (e, repo, _) = Armar();
        RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo);

        // El de la sesión 1 copiado a la 2 no se descifra: el dato asociado es la sesión.
        Assert.NotEqual(ResultadoDescifrado.Ok,
            e.Cifrado.Descifrar(repo.Filas[1], EmocionesCifradoService.DatosAsociadosDeSesion(2), out _));
    }

    [Fact]
    public void Repetirla_no_vuelve_a_cifrar_ni_a_escribir_nada()
    {
        var (e, repo, _) = Armar();
        RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo);
        var despues = repo.Filas.ToDictionary(f => f.Key, f => f.Value);
        var escrituras = repo.Escrituras;

        var r = RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo);

        Assert.Equal(new ResultadoRecifrado(4, YaCifradas: 3, EnClaro: 0, NoJson: 1, Recifradas: 0, CambiaronMientras: 0, NoVerificaron: 0), r);
        Assert.Equal(escrituras, repo.Escrituras);
        Assert.Equal(despues, repo.Filas);
    }

    [Fact]
    public void Una_fila_que_cambio_mientras_tanto_no_se_pisa_y_se_cuenta()
    {
        var (e, repo, _) = Armar();
        repo.CambianAntesDeEscribir.Add(1);

        var r = RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo);

        Assert.Equal(1, r.CambiaronMientras);
        Assert.Equal(1, r.Recifradas);                                    // la otra sí
        Assert.Equal(Json1 + " (editada por otro)", repo.Filas[1]);       // la editada por otro quedó como la dejó el otro
        Assert.StartsWith("ys1.", repo.Filas[2]);
    }

    [Fact]
    public void Si_la_prueba_de_ida_y_vuelta_no_cierra_no_se_escribe_nada()
    {
        var (e, repo, _) = Armar();
        var antes = repo.Filas.ToDictionary(f => f.Key, f => f.Value);

        var r = RecifrarEmociones.Procesar(aplicar: true, new CifradoQueNoVuelve(e.Cifrado), repo);

        Assert.Equal(2, r.NoVerificaron);
        Assert.Equal(0, r.Recifradas);
        Assert.Equal(0, repo.Escrituras);
        Assert.Equal(antes, repo.Filas);
    }

    [Fact]
    public void Sin_clave_no_se_puede_aplicar_y_no_se_toca_nada_pero_la_simulacion_cuenta()
    {
        var e = new EscenarioEmociones(conClave: false);
        var repo = new RepoEnMemoria();
        repo.Filas[1] = Json1;
        repo.Filas[2] = "esto no es un json";

        Assert.Throws<InvalidOperationException>(() => RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo));
        Assert.Equal(0, repo.Escrituras);
        Assert.Equal(Json1, repo.Filas[1]);

        var simulacion = RecifrarEmociones.Procesar(aplicar: false, e.Cifrado, repo);
        Assert.Equal(1, simulacion.EnClaro);
        Assert.Equal(1, simulacion.NoJson);
    }

    [Fact]
    public void Recorre_todo_por_lotes_sin_saltearse_ni_repetir_filas()
    {
        var e = new EscenarioEmociones();
        var repo = new RepoEnMemoria();
        for (var id = 10; id < 10 + 53; id++) repo.Filas[id * 3] = JsonSerializer.Serialize(new { Neutral = id });   // ids salteados

        var r = RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo, lote: 7);

        Assert.Equal(53, r.Total);
        Assert.Equal(53, r.Recifradas);
        Assert.True(repo.Lecturas >= 8);                                   // 53 filas en lotes de 7
        Assert.All(repo.Filas, f => Assert.StartsWith("ys1.", f.Value));
        foreach (var (id, valor) in repo.Filas)
            Assert.Equal(JsonSerializer.Serialize(new { Neutral = id / 3 }), Leer(e.Cifrado, valor, id));
    }

    [Fact]
    public void Una_base_sin_emociones_guardadas_no_hace_nada()
    {
        var e = new EscenarioEmociones();

        var r = RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, new RepoEnMemoria());

        Assert.Equal(new ResultadoRecifrado(0, 0, 0, 0, 0, 0, 0), r);
    }

    [Fact]
    public void Un_json_que_no_es_un_objeto_se_deja_como_esta()
    {
        var e = new EscenarioEmociones();
        var repo = new RepoEnMemoria();
        repo.Filas[1] = "[1,2,3]";
        repo.Filas[2] = "\"texto\"";
        repo.Filas[3] = "123";

        var r = RecifrarEmociones.Procesar(aplicar: true, e.Cifrado, repo);

        Assert.Equal(3, r.NoJson);
        Assert.Equal(0, repo.Escrituras);
    }
}
