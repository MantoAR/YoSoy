using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class InformeAdmisionDAO
{
    public int Insertar(InformeAdmision i)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"INSERT INTO InformeAdmision
                (IdPaciente, IdEspecialidad, MotivoConsulta, TratamientoAnterior, TratamientoAnteriorDetalle,
                 RecibeMedicacion, MedicacionDetalle, GustosIntereses, PreferenciasAtencion, TipoEntrevista,
                 RedApoyo, ObjetivosPaciente, Estado)
              VALUES
                (@IdPaciente, @IdEspecialidad, @MotivoConsulta, @TratamientoAnterior, @TratamientoAnteriorDetalle,
                 @RecibeMedicacion, @MedicacionDetalle, @GustosIntereses, @PreferenciasAtencion, @TipoEntrevista,
                 @RedApoyo, @ObjetivosPaciente, @Estado);
              SELECT SCOPE_IDENTITY();", con);
        cmd.Parameters.AddWithValue("@IdPaciente", i.IdPaciente);
        cmd.Parameters.AddWithValue("@IdEspecialidad", i.IdEspecialidad);
        cmd.Parameters.AddWithValue("@MotivoConsulta", (object)i.MotivoConsulta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TratamientoAnterior", i.TratamientoAnterior);
        cmd.Parameters.AddWithValue("@TratamientoAnteriorDetalle", (object)i.TratamientoAnteriorDetalle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RecibeMedicacion", i.RecibeMedicacion);
        cmd.Parameters.AddWithValue("@MedicacionDetalle", (object)i.MedicacionDetalle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@GustosIntereses", (object)i.GustosIntereses ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PreferenciasAtencion", (object)i.PreferenciasAtencion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TipoEntrevista", (object)i.TipoEntrevista ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RedApoyo", (object)i.RedApoyo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ObjetivosPaciente", (object)i.ObjetivosPaciente ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado", (object)i.Estado ?? "Pendiente");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<InformeAdmision> ObtenerTodos(string estado = null)
    {
        var lista = new List<InformeAdmision>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var sql = @"SELECT IdInforme, IdPaciente, IdEspecialidad, MotivoConsulta, TratamientoAnterior,
                           TratamientoAnteriorDetalle, RecibeMedicacion, MedicacionDetalle, GustosIntereses,
                           PreferenciasAtencion, TipoEntrevista, RedApoyo, ObjetivosPaciente, Estado, FechaCreacion
                    FROM InformeAdmision" +
                  (estado != null ? " WHERE Estado = @Estado" : "") +
                  " ORDER BY FechaCreacion DESC";
        var cmd = new SqlCommand(sql, con);
        if (estado != null) cmd.Parameters.AddWithValue("@Estado", estado);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    public InformeAdmision ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT IdInforme, IdPaciente, IdEspecialidad, MotivoConsulta, TratamientoAnterior,
                     TratamientoAnteriorDetalle, RecibeMedicacion, MedicacionDetalle, GustosIntereses,
                     PreferenciasAtencion, TipoEntrevista, RedApoyo, ObjetivosPaciente, Estado, FechaCreacion
              FROM InformeAdmision WHERE IdInforme = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public InformeAdmision ObtenerPorPaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT TOP 1 IdInforme, IdPaciente, IdEspecialidad, MotivoConsulta, TratamientoAnterior,
                     TratamientoAnteriorDetalle, RecibeMedicacion, MedicacionDetalle, GustosIntereses,
                     PreferenciasAtencion, TipoEntrevista, RedApoyo, ObjetivosPaciente, Estado, FechaCreacion
              FROM InformeAdmision WHERE IdPaciente = @Id ORDER BY FechaCreacion DESC", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public void CambiarEstado(int idInforme, string estado)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE InformeAdmision SET Estado = @Estado WHERE IdInforme = @Id", con);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@Id", idInforme);
        cmd.ExecuteNonQuery();
    }

    private InformeAdmision Mapear(SqlDataReader r) => new InformeAdmision
    {
        IdInforme = (int)r["IdInforme"],
        IdPaciente = (int)r["IdPaciente"],
        IdEspecialidad = (int)r["IdEspecialidad"],
        MotivoConsulta = r["MotivoConsulta"] == DBNull.Value ? null : (string)r["MotivoConsulta"],
        TratamientoAnterior = r["TratamientoAnterior"] != DBNull.Value && (bool)r["TratamientoAnterior"],
        TratamientoAnteriorDetalle = r["TratamientoAnteriorDetalle"] == DBNull.Value ? null : (string)r["TratamientoAnteriorDetalle"],
        RecibeMedicacion = r["RecibeMedicacion"] != DBNull.Value && (bool)r["RecibeMedicacion"],
        MedicacionDetalle = r["MedicacionDetalle"] == DBNull.Value ? null : (string)r["MedicacionDetalle"],
        GustosIntereses = r["GustosIntereses"] == DBNull.Value ? null : (string)r["GustosIntereses"],
        PreferenciasAtencion = r["PreferenciasAtencion"] == DBNull.Value ? null : (string)r["PreferenciasAtencion"],
        TipoEntrevista = r["TipoEntrevista"] == DBNull.Value ? null : (string)r["TipoEntrevista"],
        RedApoyo = r["RedApoyo"] == DBNull.Value ? null : (string)r["RedApoyo"],
        ObjetivosPaciente = r["ObjetivosPaciente"] == DBNull.Value ? null : (string)r["ObjetivosPaciente"],
        Estado = r["Estado"] == DBNull.Value ? "Pendiente" : (string)r["Estado"],
        FechaCreacion = (DateTime)r["FechaCreacion"]
    };
}