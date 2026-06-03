using Microsoft.Data.SqlClient;
using System.Collections.Generic;
public class HorarioAgendaDAO
{
    public List<HorarioAgenda> ObtenerPorAgenda(int id)
    {
        var lista = new List<HorarioAgenda>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdHorario,IdAgenda,Codigo,Descripcion,HoraInicio,HoraFin,FechaInicio,FechaFin,Lunes,Martes,Miercoles,Jueves,Viernes,Sabado,Domingo,IntervaloMinutos,MaxCitas,TrabajeFestivos,Estado FROM HorarioAgenda WHERE IdAgenda=@Id AND Estado='Activo'", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) lista.Add(Mapear(reader));
        return lista;
    }
    public void Insertar(HorarioAgenda h)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("INSERT INTO HorarioAgenda (IdAgenda,Codigo,Descripcion,HoraInicio,HoraFin,FechaInicio,FechaFin,Lunes,Martes,Miercoles,Jueves,Viernes,Sabado,Domingo,IntervaloMinutos,MaxCitas,TrabajeFestivos,Estado) VALUES (@IdA,@Cod,@Desc,@HI,@HF,@FI,@FF,@Lun,@Mar,@Mie,@Jue,@Vie,@Sab,@Dom,@Int,@Max,@Fest,'Activo')", con);
        cmd.Parameters.AddWithValue("@IdA", h.IdAgenda);
        cmd.Parameters.AddWithValue("@Cod", (object)h.Codigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Desc", (object)h.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HI", h.HoraInicio);
        cmd.Parameters.AddWithValue("@HF", h.HoraFin);
        cmd.Parameters.AddWithValue("@FI", h.FechaInicio);
        cmd.Parameters.AddWithValue("@FF", (object)h.FechaFin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Lun", h.Lunes);
        cmd.Parameters.AddWithValue("@Mar", h.Martes);
        cmd.Parameters.AddWithValue("@Mie", h.Miercoles);
        cmd.Parameters.AddWithValue("@Jue", h.Jueves);
        cmd.Parameters.AddWithValue("@Vie", h.Viernes);
        cmd.Parameters.AddWithValue("@Sab", h.Sabado);
        cmd.Parameters.AddWithValue("@Dom", h.Domingo);
        cmd.Parameters.AddWithValue("@Int", h.IntervaloMinutos);
        cmd.Parameters.AddWithValue("@Max", h.MaxCitas);
        cmd.Parameters.AddWithValue("@Fest", h.TrabajeFestivos);
        cmd.ExecuteNonQuery();
    }
    public void Modificar(HorarioAgenda h)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE HorarioAgenda SET Codigo=@Cod,Descripcion=@Desc,HoraInicio=@HI,HoraFin=@HF,FechaInicio=@FI,FechaFin=@FF,Lunes=@Lun,Martes=@Mar,Miercoles=@Mie,Jueves=@Jue,Viernes=@Vie,Sabado=@Sab,Domingo=@Dom,IntervaloMinutos=@Int,MaxCitas=@Max,TrabajeFestivos=@Fest,Estado=@Est WHERE IdHorario=@Id", con);
        cmd.Parameters.AddWithValue("@Cod", (object)h.Codigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Desc", (object)h.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HI", h.HoraInicio);
        cmd.Parameters.AddWithValue("@HF", h.HoraFin);
        cmd.Parameters.AddWithValue("@FI", h.FechaInicio);
        cmd.Parameters.AddWithValue("@FF", (object)h.FechaFin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Lun", h.Lunes);
        cmd.Parameters.AddWithValue("@Mar", h.Martes);
        cmd.Parameters.AddWithValue("@Mie", h.Miercoles);
        cmd.Parameters.AddWithValue("@Jue", h.Jueves);
        cmd.Parameters.AddWithValue("@Vie", h.Viernes);
        cmd.Parameters.AddWithValue("@Sab", h.Sabado);
        cmd.Parameters.AddWithValue("@Dom", h.Domingo);
        cmd.Parameters.AddWithValue("@Int", h.IntervaloMinutos);
        cmd.Parameters.AddWithValue("@Max", h.MaxCitas);
        cmd.Parameters.AddWithValue("@Fest", h.TrabajeFestivos);
        cmd.Parameters.AddWithValue("@Est", h.Estado);
        cmd.Parameters.AddWithValue("@Id", h.IdHorario);
        cmd.ExecuteNonQuery();
    }
    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        new SqlCommand("UPDATE HorarioAgenda SET Estado='Inactivo' WHERE IdHorario=@Id", con) { Parameters = { new("@Id", id) } }.ExecuteNonQuery();
    }
    private HorarioAgenda Mapear(SqlDataReader r) => new HorarioAgenda
    {
        IdHorario=(int)r["IdHorario"], IdAgenda=(int)r["IdAgenda"],
        Codigo=r["Codigo"]==DBNull.Value?null:(string)r["Codigo"],
        Descripcion=r["Descripcion"]==DBNull.Value?null:(string)r["Descripcion"],
        HoraInicio=(TimeSpan)r["HoraInicio"], HoraFin=(TimeSpan)r["HoraFin"],
        FechaInicio=(DateTime)r["FechaInicio"],
        FechaFin=r["FechaFin"]==DBNull.Value?null:(DateTime?)r["FechaFin"],
        Lunes=(bool)r["Lunes"], Martes=(bool)r["Martes"], Miercoles=(bool)r["Miercoles"],
        Jueves=(bool)r["Jueves"], Viernes=(bool)r["Viernes"], Sabado=(bool)r["Sabado"],
        Domingo=(bool)r["Domingo"], IntervaloMinutos=(int)r["IntervaloMinutos"],
        MaxCitas=(int)r["MaxCitas"], TrabajeFestivos=(bool)r["TrabajeFestivos"],
        Estado=(string)r["Estado"]
    };
}
