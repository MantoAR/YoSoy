using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class ConsentimientoDAO
{
    public int Insertar(Consentimiento c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado) VALUES (@IdPaciente, @IdTipoConsentimiento, @Version, @FechaFirma, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@IdPaciente",           c.IdPaciente);
        cmd.Parameters.AddWithValue("@IdTipoConsentimiento", c.IdTipoConsentimiento);
        cmd.Parameters.AddWithValue("@Version",              (object)c.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFirma",           c.FechaFirma);
        cmd.Parameters.AddWithValue("@Estado",               (object)c.Estado  ?? DBNull.Value);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Consentimiento c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Consentimiento SET IdPaciente = @IdPaciente, IdTipoConsentimiento = @IdTipoConsentimiento, Version = @Version, FechaFirma = @FechaFirma, Estado = @Estado WHERE IdConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente",           c.IdPaciente);
        cmd.Parameters.AddWithValue("@IdTipoConsentimiento", c.IdTipoConsentimiento);
        cmd.Parameters.AddWithValue("@Version",              (object)c.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFirma",           c.FechaFirma);
        cmd.Parameters.AddWithValue("@Estado",               (object)c.Estado  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",                   c.IdConsentimiento);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Consentimiento SET Estado = 'Inactivo' WHERE IdConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Consentimiento> ObtenerTodos()
    {
        var lista = new List<Consentimiento>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdConsentimiento, IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado FROM Consentimiento", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Consentimiento ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdConsentimiento, IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado FROM Consentimiento WHERE IdConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Consentimiento> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<Consentimiento>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdConsentimiento, IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado FROM Consentimiento WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    // RN-02: consentimiento vigente de un tipo (buscado por Descripcion). No hay
    // vencimiento en el modelo: vigente = existe y no fue dado de baja ('Inactivo').
    public Consentimiento? ObtenerVigente(int idPaciente, string descripcionTipo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 c.IdConsentimiento, c.IdPaciente, c.IdTipoConsentimiento, c.Version, c.FechaFirma, c.Estado
            FROM Consentimiento c
            JOIN TipoConsentimiento tc ON tc.IdTipoConsentimiento = c.IdTipoConsentimiento
            WHERE c.IdPaciente = @IdPac AND tc.Descripcion = @Tipo
              AND ISNULL(c.Estado, 'Activo') <> 'Inactivo'
            ORDER BY c.FechaFirma DESC", con);
        cmd.Parameters.AddWithValue("@IdPac", idPaciente);
        cmd.Parameters.AddWithValue("@Tipo",  descripcionTipo);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    /// <summary>
    /// Registra la firma solo si no hay una vigente del mismo tipo. Chequeo e inserción
    /// van en una transacción con UPDLOCK/HOLDLOCK para que un doble clic no deje dos filas.
    /// Devuelve 1 si firmó, 0 si ya estaba firmado y -1 si el tipo no existe en la base.
    /// </summary>
    public int FirmarSiNoExiste(int idPaciente, string descripcionTipo, string version, DateTime fechaHora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SET XACT_ABORT ON;
            DECLARE @IdTipo INT = (SELECT IdTipoConsentimiento FROM TipoConsentimiento WHERE Descripcion = @Tipo);
            DECLARE @Resultado INT = -1;
            IF @IdTipo IS NOT NULL
            BEGIN
                BEGIN TRAN;
                IF EXISTS (SELECT 1 FROM Consentimiento WITH (UPDLOCK, HOLDLOCK)
                           WHERE IdPaciente = @IdPac AND IdTipoConsentimiento = @IdTipo
                             AND ISNULL(Estado, 'Activo') <> 'Inactivo')
                    SET @Resultado = 0;
                ELSE
                BEGIN
                    INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado)
                    VALUES (@IdPac, @IdTipo, @Version, @Fecha, 'Activo');
                    SET @Resultado = 1;
                END
                COMMIT;
            END
            SELECT @Resultado;", con);
        cmd.Parameters.AddWithValue("@IdPac",   idPaciente);
        cmd.Parameters.AddWithValue("@Tipo",    descripcionTipo);
        cmd.Parameters.AddWithValue("@Version", version.Length > 20 ? version[..20] : version);
        cmd.Parameters.AddWithValue("@Fecha",   fechaHora);
        return (int)cmd.ExecuteScalar();
    }

    private Consentimiento Mapear(SqlDataReader r) => new Consentimiento
    {
        IdConsentimiento     = (int)r["IdConsentimiento"],
        IdPaciente           = (int)r["IdPaciente"],
        IdTipoConsentimiento = (int)r["IdTipoConsentimiento"],
        Version              = r["Version"] == DBNull.Value ? null : (string)r["Version"],
        FechaFirma           = (DateTime)r["FechaFirma"],
        Estado               = r["Estado"]  == DBNull.Value ? null : (string)r["Estado"]
    };
}
