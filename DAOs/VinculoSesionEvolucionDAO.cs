using Microsoft.Data.SqlClient;

/// <summary>Una evolución de la historia clínica que se podría vincular a la sesión: solo lo mínimo para reconocerla (el profesional ve sus propias evoluciones).</summary>
public record EvolucionVinculable(int IdEvolucion, DateTime Fecha, string? Resumen);

public record SesionParaVincular(int IdSesion, int IdPaciente, int IdProfesional, DateTime FechaHora, int? IdEvolucion);

public class ResultadoVinculoEvolucion
{
    public string? Rechazo { get; init; }
    public bool YaEstaba { get; init; }
    public static ResultadoVinculoEvolucion Rechazar(string codigo) => new() { Rechazo = codigo };
}

/// <summary>
/// El vínculo entre una sesión clínica y una evolución de la historia clínica. Las dos cosas se ENLAZAN, no se unifican: la sesión conserva lo que se dijo en la sala y la evolución es el
/// registro clínico. El cierre de sesión no crea ninguna evolución; el vínculo lo hace el profesional a mano, y solo entre cosas del mismo paciente, el mismo profesional y el mismo día.
/// Va en SesionClinica.IdEvolucion (migración 26) a propósito: la tabla Evolucion no se toca (una evolución firmada es inmutable).
/// </summary>
public class VinculoSesionEvolucionDAO
{
    public const string SesionInexistente = "sesion-inexistente";
    public const string EvolucionInexistente = "evolucion-inexistente";
    public const string EvolucionNoCoincide = "evolucion-no-coincide";
    public const string SesionYaVinculada = "sesion-ya-vinculada";
    public const string EvolucionYaVinculada = "evolucion-ya-vinculada";

    public static string Mensaje(string codigo) => codigo switch
    {
        SesionInexistente => "No encontramos esa sesión entre las tuyas.",
        EvolucionInexistente => "No encontramos esa evolución.",
        EvolucionNoCoincide => "Esa evolución no es del mismo paciente, del mismo profesional y del mismo día que la sesión, así que no se pudo vincular.",
        SesionYaVinculada => "Esta sesión ya está vinculada a otra evolución.",
        EvolucionYaVinculada => "Esa evolución ya está vinculada a otra sesión.",
        _ => "No se pudo vincular la sesión con la evolución."
    };

    public SesionParaVincular? ObtenerSesion(int idSesion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        return LeerSesion(con, null, idSesion, bloquear: false);
    }

    /// <summary>Las evoluciones del mismo paciente, el mismo profesional y el mismo día que la sesión que todavía no están vinculadas a ninguna sesión.</summary>
    public List<EvolucionVinculable> Vinculables(SesionParaVincular s)
    {
        var lista = new List<EvolucionVinculable>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT e.IdEvolucion, e.Fecha, LEFT(e.Observaciones, 80) AS Resumen
            FROM Evolucion e JOIN HistoriaClinica h ON h.IdHistoria = e.IdHistoria
            WHERE h.IdPaciente = @P AND e.IdProfesional = @M AND e.Fecha = @Dia
              AND NOT EXISTS (SELECT 1 FROM SesionClinica x WHERE x.IdEvolucion = e.IdEvolucion)
            ORDER BY e.IdEvolucion", con);
        cmd.Parameters.AddWithValue("@P", s.IdPaciente);
        cmd.Parameters.AddWithValue("@M", s.IdProfesional);
        cmd.Parameters.AddWithValue("@Dia", s.FechaHora.Date);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new EvolucionVinculable((int)r["IdEvolucion"], (DateTime)r["Fecha"], r["Resumen"] as string));
        return lista;
    }

    public ResultadoVinculoEvolucion Vincular(int idSesion, int idEvolucion, int idProfesional, AutorDelCierre autor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        try
        {
            using var tx = con.BeginTransaction();

            var s = LeerSesion(con, tx, idSesion, bloquear: true);
            // "No es tuya" y "no existe" dan lo mismo: no se confirma que una sesión ajena exista.
            if (s is null || s.IdProfesional != idProfesional) return ResultadoVinculoEvolucion.Rechazar(SesionInexistente);

            int idHistoria, evProfesional; DateTime evFecha;
            using (var cmd = new SqlCommand("SELECT IdHistoria, IdProfesional, Fecha FROM Evolucion WHERE IdEvolucion = @E", con, tx))
            {
                cmd.Parameters.AddWithValue("@E", idEvolucion);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return ResultadoVinculoEvolucion.Rechazar(EvolucionInexistente);
                idHistoria = (int)r["IdHistoria"]; evProfesional = (int)r["IdProfesional"]; evFecha = (DateTime)r["Fecha"];
            }
            int idPacienteDeLaHistoria;
            using (var cmd = new SqlCommand("SELECT IdPaciente FROM HistoriaClinica WHERE IdHistoria = @H", con, tx))
            {
                cmd.Parameters.AddWithValue("@H", idHistoria);
                idPacienteDeLaHistoria = (int)cmd.ExecuteScalar()!;
            }
            if (idPacienteDeLaHistoria != s.IdPaciente || evProfesional != s.IdProfesional || evFecha.Date != s.FechaHora.Date)
                return ResultadoVinculoEvolucion.Rechazar(EvolucionNoCoincide);

            // Repetir el mismo vínculo no cambia nada ni se audita de nuevo.
            if (s.IdEvolucion == idEvolucion) return new ResultadoVinculoEvolucion { YaEstaba = true };
            if (s.IdEvolucion is not null) return ResultadoVinculoEvolucion.Rechazar(SesionYaVinculada);

            using (var upd = new SqlCommand("UPDATE SesionClinica SET IdEvolucion = @E WHERE IdSesion = @S AND IdEvolucion IS NULL", con, tx))
            {
                upd.Parameters.AddWithValue("@E", idEvolucion);
                upd.Parameters.AddWithValue("@S", idSesion);
                if (upd.ExecuteNonQuery() != 1) return ResultadoVinculoEvolucion.Rechazar(SesionYaVinculada);
            }

            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = autor.IdUsuario, Accion = "Sesión vinculada a una evolución", Entidad = "SesionClinica", IdEntidad = idSesion,
                Detalle = CierreSesionReglas.Detalle($"SesionClinica #{idSesion} <-> Evolucion #{idEvolucion}, paciente #{s.IdPaciente}" + (autor.Calidad is null ? "" : $", como {autor.Calidad}")),
                Categoria = CategoriaAuditoria.Clinica, Espacio = autor.Espacio
            }, con, tx);

            tx.Commit();
            return new ResultadoVinculoEvolucion();
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            return ResultadoVinculoEvolucion.Rechazar(EvolucionYaVinculada);   // el índice único: esa evolución ya es de otra sesión
        }
    }

    private static SesionParaVincular? LeerSesion(SqlConnection con, SqlTransaction? tx, int idSesion, bool bloquear)
    {
        using var cmd = new SqlCommand(
            "SELECT IdSesion, IdPaciente, IdProfesional, FechaHora, IdEvolucion FROM SesionClinica " + (bloquear ? "WITH (UPDLOCK, ROWLOCK) " : "") + "WHERE IdSesion = @S", con, tx);
        cmd.Parameters.AddWithValue("@S", idSesion);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new SesionParaVincular((int)r["IdSesion"], (int)r["IdPaciente"], (int)r["IdProfesional"], (DateTime)r["FechaHora"], r["IdEvolucion"] as int?)
            : null;
    }

    /// <summary>Completa SesionClinica.IdEvolucion de las sesiones ya leídas (sin la migración 26 no hace nada). Una sola consulta por paciente o profesional.</summary>
    public static void Completar(List<SesionClinica> sesiones, string columnaFiltro, int valor)
    {
        if (sesiones.Count == 0 || !new CierreSesionDAO().Migracion26Disponible()) return;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand($"SELECT IdSesion, IdEvolucion FROM SesionClinica WHERE {columnaFiltro} = @V AND IdEvolucion IS NOT NULL", con);   // columnaFiltro: constante del llamador
        cmd.Parameters.AddWithValue("@V", valor);
        using var r = cmd.ExecuteReader();
        var vinculos = new Dictionary<int, int>();
        while (r.Read()) vinculos[(int)r["IdSesion"]] = (int)r["IdEvolucion"];
        foreach (var s in sesiones) if (vinculos.TryGetValue(s.IdSesion, out var e)) s.IdEvolucion = e;
    }
}
