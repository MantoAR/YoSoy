using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class LogAuditoriaDAO
{
    public void Insertar(LogAuditoria l)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        Insertar(l, con, null);
    }

    // Dentro de la transacción de quien llama: si el registro no se puede escribir, falla ella entera (a diferencia de la auditoría de lectura,
    // que no debe tumbar un pedido). Lo usa el cierre de sesión: no se cambia el estado de un turno sin dejar quién lo cambió.
    public void Insertar(LogAuditoria l, SqlConnection con, SqlTransaction? tx)
    {
        var cmd = new SqlCommand(@"
            INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
            VALUES (@IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @Categoria, @Espacio)", con, tx);
        cmd.Parameters.AddWithValue("@IdUsuario", (object?)l.IdUsuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Accion",    l.Accion);
        cmd.Parameters.AddWithValue("@Entidad",   l.Entidad);
        cmd.Parameters.AddWithValue("@IdEntidad", (object?)l.IdEntidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Detalle",   (object?)l.Detalle   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Categoria", l.Categoria);
        cmd.Parameters.AddWithValue("@Espacio",   (object?)l.Espacio   ?? DBNull.Value);
        // La base encadena el registro con el anterior (trigger TR_LogAuditoria_Encadenar) y
        // no permite modificarlo ni borrarlo después (TR_LogAuditoria_SoloAgregar).
        cmd.ExecuteNonQuery();
    }

    // Roles y permisos: el Configurador ve la parte administrativa y el auditor la clínica. Además de
    // la columna Categoria, cuentan como clínicos los registros de entidades que identifican
    // la atención de un paciente (asignaciones, derivaciones, admisión, reportes, turnos):
    // así los registros anteriores a esta etapa, que se guardaron como administrativos y ya no
    // se pueden corregir (la auditoría es de solo agregar), quedan del lado correcto.
    public const string EntidadesClinicas =
        "'HistoriaClinica','Evolucion','EventoCrisis','SesionClinica','Conversacion','Consentimiento','InteraccionIA'," +
        "'InformeAdmision','Derivacion','PacienteProfesional','Caso','Reporte','Turno'";

    public List<LogAuditoria> ObtenerPaginado(int pagina, int cantidad, string categoria)
    {
        var lista = new List<LogAuditoria>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var esClinica = $"(l.Categoria = 'Clinica' OR l.Entidad IN ({EntidadesClinicas}))";
        var filtro = categoria == CategoriaAuditoria.Clinica ? esClinica : $"NOT {esClinica}";
        var cmd = new SqlCommand($@"
            SELECT l.IdLog, l.IdUsuario, l.Accion, l.Entidad, l.IdEntidad, l.Detalle, l.FechaHora, l.Categoria, l.Espacio,
                   u.Username AS NombreUsuario
            FROM LogAuditoria l
            LEFT JOIN Usuario u ON u.IdUsuario = l.IdUsuario
            WHERE {filtro}
            ORDER BY l.FechaHora DESC, l.IdLog DESC
            OFFSET @Offset ROWS FETCH NEXT @Cantidad ROWS ONLY", con);
        cmd.Parameters.AddWithValue("@Offset",   (pagina - 1) * cantidad);
        cmd.Parameters.AddWithValue("@Cantidad", cantidad);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var l = Mapear(reader);
            l.NombreUsuario = reader["NombreUsuario"] == DBNull.Value ? null : (string)reader["NombreUsuario"];
            lista.Add(l);
        }
        return lista;
    }

    private LogAuditoria Mapear(SqlDataReader r) => new LogAuditoria
    {
        IdLog     = (int)r["IdLog"],
        IdUsuario = r["IdUsuario"] == DBNull.Value ? null : (int?)r["IdUsuario"],
        Accion    = (string)r["Accion"],
        Entidad   = (string)r["Entidad"],
        IdEntidad = r["IdEntidad"] == DBNull.Value ? null : (int?)r["IdEntidad"],
        Detalle   = r["Detalle"]   == DBNull.Value ? null : (string)r["Detalle"],
        FechaHora = (DateTime)r["FechaHora"],
        Categoria = (string)r["Categoria"],
        Espacio   = r["Espacio"]   == DBNull.Value ? null : (string)r["Espacio"]
    };
}
