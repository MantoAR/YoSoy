using System.Text.Json;

// El borrador local cifrado de la nota de una sesión (PANTALLAS/shared/borrador.js). La parte de cifrado y de almacenamiento se ejecuta de verdad en node con el WebCrypto real y
// un almacén en memoria (la capa de IndexedDB es una envoltura fina y se ensaya en el navegador, ver el guion manual de docs/CIERRE_SESION.md). Si no hay node, las pruebas
// quedan OMITIDAS (no pasan en falso).
public class BorradorCifradoTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Fuente => File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "shared", "borrador.js"));

    private const string Banco = """
        const { crearBorrador, VIGENCIA_MS } = require(process.env.BORRADOR);
        const mapas = { borradores: new Map(), claves: new Map() };
        let ahora = 1_000_000;
        const almacen = {
            leer: async (t, k) => mapas[t].get(k),
            escribir: async (t, k, v) => { mapas[t].set(k, v); },
            borrar: async (t, k) => { mapas[t].delete(k); },
            claves: async t => [...mapas[t].keys()],
        };
        const b = crearBorrador({ subtle: crypto.subtle, aleatorio: n => crypto.getRandomValues(new Uint8Array(n)), almacen, ahora: () => ahora });
        const dormir = ms => { ahora += ms; };
        """;

    private static JsonElement Correr(string escenario)
    {
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(
            new[] { "-e", "(async () => {" + Banco + "\n" + escenario + "\n})().catch(e => { console.error(e); process.exit(1); });" },
            new Dictionary<string, string> { ["BORRADOR"] = Path.Combine(Raiz, "PANTALLAS", "shared", "borrador.js") });
        Assert.True(codigo == 0, $"node terminó con {codigo}: {error}");
        return JsonDocument.Parse(salida.Trim()).RootElement;
    }

    private sealed class SkippableNodeAttribute : FactAttribute
    {
        public SkippableNodeAttribute()
        {
            var motivo = NodeDelServicioDeWhatsApp.NoDisponible();
            if (motivo is not null) Skip = motivo;
        }
    }

    [SkippableNode]
    public void Guarda_cifrado_y_lo_recupera_igual_sin_dejar_el_texto_a_la_vista()
    {
        var r = Correr("""
            const datos = { notas: 'Paciente refiere mejoría del sueño. Contexto laboral estable.', tipoCierre: 'Seguimiento', decision: 'si', claveCierre: 'abc' };
            const t = await b.guardar(7, 'cierre:7:turno:450', datos);
            const leido = await b.leer(7, 'cierre:7:turno:450');
            const registro = mapas.borradores.get('7|cierre:7:turno:450');
            const clave = mapas.claves.get('usuario:7');
            console.log(JSON.stringify({
                igual: JSON.stringify(leido.datos) === JSON.stringify(datos),
                guardadoEn: leido.guardadoEn === t,
                textoVisible: JSON.stringify(registro).includes('mejoría') || JSON.stringify(registro).includes('sueño'),
                extraible: clave.extractable,
                algoritmo: clave.algorithm.name + clave.algorithm.length,
                ivDistintos: await (async () => { await b.guardar(7, 'cierre:7:turno:450', datos); return mapas.borradores.get('7|cierre:7:turno:450').iv !== registro.iv; })(),
            }));
            """);

        Assert.True(r.GetProperty("igual").GetBoolean());
        Assert.True(r.GetProperty("guardadoEn").GetBoolean());
        Assert.False(r.GetProperty("textoVisible").GetBoolean());          // lo guardado es texto cifrado
        Assert.False(r.GetProperty("extraible").GetBoolean());             // la clave no se puede exportar
        Assert.Equal("AES-GCM256", r.GetProperty("algoritmo").GetString());
        Assert.True(r.GetProperty("ivDistintos").GetBoolean());            // un IV nuevo en cada escritura
    }

    [SkippableNode]
    public void Un_borrador_no_se_descifra_en_otro_turno_ni_con_otro_usuario_ni_si_se_altera()
    {
        var r = Correr("""
            await b.guardar(7, 'cierre:7:turno:450', { notas: 'secreto' });
            const reg = mapas.borradores.get('7|cierre:7:turno:450');
            const r = {};
            // Copiar el registro cifrado a OTRO turno del mismo usuario.
            mapas.borradores.set('7|cierre:7:turno:451', { ...reg, clave: 'cierre:7:turno:451' });
            r.otroTurno = await b.leer(7, 'cierre:7:turno:451');
            // Copiarlo a OTRO usuario (que tiene su propia clave).
            await b.guardar(8, 'cierre:8:turno:450', { notas: 'otra persona' });
            mapas.borradores.set('8|cierre:7:turno:450', { ...reg, idUsuario: '8' });
            r.otroUsuario = await b.leer(8, 'cierre:7:turno:450');
            // Alterar un byte del texto cifrado.
            const bytes = Buffer.from(reg.datos, 'base64'); bytes[0] ^= 1;
            mapas.borradores.set('7|cierre:7:turno:450', { ...reg, datos: bytes.toString('base64') });
            r.alterado = await b.leer(7, 'cierre:7:turno:450');
            // Sin la clave del usuario (se borró) no hay nada que descifrar.
            await b.guardar(9, 'cierre:9:x', { notas: 'x' });
            mapas.claves.delete('usuario:9');
            r.sinClave = await b.leer(9, 'cierre:9:x');
            console.log(JSON.stringify({ ...r, propio: (await b.leer(8, 'cierre:8:turno:450')).datos.notas }));
            """);

        Assert.Equal(JsonValueKind.Null, r.GetProperty("otroTurno").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("otroUsuario").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("alterado").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("sinClave").ValueKind);
        Assert.Equal("otra persona", r.GetProperty("propio").GetString());     // lo propio de cada usuario sigue andando
    }

    [SkippableNode]
    public void Vence_a_las_72_horas_se_purga_y_borrar_de_usuario_saca_borradores_y_clave()
    {
        var r = Correr("""
            const r = {};
            await b.guardar(7, 'a', { notas: 'uno' });
            dormir(VIGENCIA_MS - 1000);
            r.aunVigente = (await b.leer(7, 'a')) !== null;
            await b.guardar(7, 'b', { notas: 'dos' });          // este es nuevo
            dormir(2000);                                        // 'a' ya pasó las 72 h; 'b' no
            r.venció = (await b.leer(7, 'a')) === null;
            r.noVenció = (await b.leer(7, 'b')) !== null;
            await b.guardar(7, 'c', { notas: 'tres' });
            dormir(VIGENCIA_MS + 1);                             // todos vencidos
            r.purgados = await b.purgarVencidos();
            r.quedan = await b.cantidadDeUsuario(7);
            await b.guardar(7, 'd', { notas: 'cuatro' });
            await b.guardar(8, 'e', { notas: 'cinco' });
            await b.borrarDeUsuario(7);
            r.deSieteTrasBorrar = await b.cantidadDeUsuario(7);
            r.deOchoIntacto = await b.cantidadDeUsuario(8);
            r.claveDeSiete = mapas.claves.has('usuario:7');
            console.log(JSON.stringify(r));
            """);

        Assert.True(r.GetProperty("aunVigente").GetBoolean());
        Assert.True(r.GetProperty("venció").GetBoolean());
        Assert.True(r.GetProperty("noVenció").GetBoolean());
        Assert.True(r.GetProperty("purgados").GetInt32() >= 1);
        Assert.Equal(0, r.GetProperty("quedan").GetInt32());
        Assert.Equal(0, r.GetProperty("deSieteTrasBorrar").GetInt32());
        Assert.Equal(1, r.GetProperty("deOchoIntacto").GetInt32());            // el de otro usuario no se toca
        Assert.False(r.GetProperty("claveDeSiete").GetBoolean());
    }

    [SkippableNode]
    public void Si_el_almacenamiento_falla_disponible_da_false_y_no_finge_guardar()
    {
        var r = Correr("""
            const roto = crearBorrador({ subtle: crypto.subtle, aleatorio: n => crypto.getRandomValues(new Uint8Array(n)),
                almacen: { leer: async () => { throw new Error('IndexedDB bloqueada'); }, escribir: async () => { throw new Error('IndexedDB bloqueada'); }, borrar: async () => {}, claves: async () => [] } });
            const sinCrypto = crearBorrador({ subtle: undefined, aleatorio: () => new Uint8Array(12), almacen });
            let guardarFalla = false;
            try { await roto.guardar(7, 'a', { notas: 'x' }); } catch { guardarFalla = true; }
            console.log(JSON.stringify({ bien: await b.disponible(), roto: await roto.disponible(), sinCrypto: await sinCrypto.disponible(), guardarFalla }));
            """);

        Assert.True(r.GetProperty("bien").GetBoolean());
        Assert.False(r.GetProperty("roto").GetBoolean());
        Assert.False(r.GetProperty("sinCrypto").GetBoolean());
        Assert.True(r.GetProperty("guardarFalla").GetBoolean());      // guardar rechaza: la pantalla lo muestra, no lo calla
    }

    // ───────────── Lectura del archivo ─────────────

    [Fact]
    public void El_borrador_no_guarda_transcripcion_ni_emociones_ni_sale_del_equipo()
    {
        var fuente = Fuente;
        Assert.DoesNotContain("fetch(", fuente);
        Assert.DoesNotContain("XMLHttpRequest", fuente);
        Assert.DoesNotContain("sendBeacon", fuente);
        Assert.DoesNotContain("YS.api", fuente);
        Assert.DoesNotContain("localStorage", fuente);                      // el borrador no va en texto plano a localStorage (que además borra YS.sesion.limpiar)
        Assert.Contains("extractable", fuente);
        Assert.Matches(@"generateKey\(\{ name: ""AES-GCM"", length: 256 \}, false", fuente);
        Assert.DoesNotContain("exportKey", fuente);
    }
}
