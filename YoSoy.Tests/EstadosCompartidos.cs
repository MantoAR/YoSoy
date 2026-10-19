using Microsoft.Data.SqlClient;

/// <summary>
/// El catálogo EstadoTurno es COMPARTIDO por todas las pruebas que usan la base, también las que corren en paralelo, y una base recién creada (como la de producción, o la
/// que arma Scripts\crear-base-de-pruebas.ps1) lo tiene VACÍO: las pruebas crean los estados que necesitan. Este registro cuenta cuántas pruebas de este proceso usan cada
/// estado, para que la fila que creó una no se la borre a otra que la encontró ahí y todavía la está usando.
///
///  · <see cref="Tomar"/>: el id del estado con esa descripción. Si alguna prueba de este proceso ya lo usa, se suma un uso. Si no, se mira la base: si ya lo tiene, se usa
///    (y NUNCA se borra: no lo creó esta suite); si no, se crea.
///  · <see cref="Soltar"/>: resta un uso. Cuando lo suelta la última prueba, se borra la fila que se creó acá. Si todavía la usa algo que no es de esta suite (un turno), la
///    clave foránea frena el borrado y queda, como en cualquier base real.
///
/// Antes cada prueba lo hacía por su cuenta: quien creaba el estado lo borraba al terminar, aunque otra prueba lo hubiera encontrado ahí y todavía lo estuviera usando. A esa
/// otra prueba le desaparecía el estado a mitad de camino: el pedido de turno daba 500 ("No existe el estado 'Pendiente' en EstadoTurno", era el caso de
/// CierreSeguridadTests.Turno_solicitar_ignora_el_idPaciente_del_cuerpo_y_usa_el_del_token) o la clave foránea frenaba el INSERT del turno. Solo pasaba contra una base con el
/// catálogo vacío y solo a veces: una corrida que dejaba una fila sin borrar escondía el problema en las siguientes.
/// </summary>
public static class EstadosCompartidos
{
    private sealed class Uso
    {
        public int Id;
        public int Usos;
        public bool LaCreoEstaSuite;
    }

    private static readonly object Candado = new();
    private static readonly Dictionary<string, Uso> EnUso = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>El id del estado con esa descripción (lo crea si la base no lo tiene). Hay que devolverlo con <see cref="Soltar"/> cuando la prueba termina.</summary>
    public static int Tomar(string descripcion)
    {
        lock (Candado)
        {
            if (EnUso.TryGetValue(descripcion, out var uso))
            {
                uso.Usos++;
                return uso.Id;
            }

            const string buscar = "SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = @d";
            var existente = DatosDePrueba.Escalar(buscar, ("@d", descripcion));
            var creada = false;
            if (existente is null)
            {
                try
                {
                    existente = DatosDePrueba.Escalar(
                        "INSERT INTO EstadoTurno (Descripcion, Estado) VALUES (@d, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        ("@d", descripcion));
                    creada = true;
                }
                catch (SqlException ex) when (ex.Number is 2601 or 2627)
                {
                    // Lo creó alguien que no pasa por este registro, en el mismo instante: no es de esta suite.
                    existente = DatosDePrueba.Escalar(buscar, ("@d", descripcion));
                }
            }

            var id = Convert.ToInt32(existente);
            EnUso[descripcion] = new Uso { Id = id, Usos = 1, LaCreoEstaSuite = creada };
            return id;
        }
    }

    /// <summary>
    /// Devuelve un uso. Si era el último y la fila la creó esta suite, la borra. Devuelve el error del borrado si falló (lo normal es la clave foránea, 547: la sigue usando un
    /// turno que no es de esta prueba y la fila queda), o null si no hubo nada que borrar o se borró bien.
    /// </summary>
    public static SqlException? Soltar(string descripcion)
    {
        lock (Candado)
        {
            if (!EnUso.TryGetValue(descripcion, out var uso) || --uso.Usos > 0) return null;
            EnUso.Remove(descripcion);
            if (!uso.LaCreoEstaSuite) return null;
            try
            {
                DatosDePrueba.Ejecutar("DELETE FROM EstadoTurno WHERE IdEstadoTurno = @i", ("@i", uso.Id));
                return null;
            }
            catch (SqlException ex)
            {
                return ex;
            }
        }
    }
}
