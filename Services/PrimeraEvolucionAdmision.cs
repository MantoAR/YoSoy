using Microsoft.Data.SqlClient;

/// <summary>
/// La primera evolución de la historia clínica que deja el camino viejo de «derivar» (el que tiene entrevista), a nombre del admisionista.
/// Escribe Evolucion.TipoCierre solo si la columna existe (migración 33): en producción no existía y la consulta fallaba con 500 en PUT /informes-admision/{id}/derivar.
/// Sin la columna se escribe lo mismo sin ese dato y la derivación sigue.
/// </summary>
public static class PrimeraEvolucionAdmision
{
    public const string TipoCierreDerivacion = "Derivación";

    public static bool TieneTipoCierre(SqlConnection con, SqlTransaction? tx = null)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Evolucion', 'TipoCierre') IS NULL THEN 0 ELSE 1 END", con, tx);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    public static string Sql(bool conTipoCierre) => @"
        DECLARE @IdHistoria INT = (SELECT TOP 1 IdHistoria FROM HistoriaClinica WHERE IdPaciente = @IdPac);
        IF @IdHistoria IS NULL
        BEGIN
            INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) VALUES (@IdPac, GETDATE());
            SET @IdHistoria = SCOPE_IDENTITY();
        END
        DECLARE @IdAdm INT = (SELECT TOP 1 IdProfesional FROM Profesional WHERE Matricula = 'ADM-001');
        DECLARE @NombreProf NVARCHAR(200) = (SELECT TOP 1 Nombre + ' ' + ISNULL(Apellido,'') FROM Profesional WHERE IdProfesional = @IdProf);
        IF @IdAdm IS NOT NULL
            INSERT INTO Evolucion (IdHistoria, IdProfesional, Fecha, Diagnostico, Observaciones, PlanTratamiento" + (conTipoCierre ? ", TipoCierre" : "") + @")
            VALUES (@IdHistoria, @IdAdm, GETDATE(), 'Evaluacion de admision',
                    ISNULL(@Obs, 'Entrevista de admision realizada.'),
                    'Derivado a: ' + ISNULL(@NombreProf, '')" + (conTipoCierre ? ", @TipoCierre" : "") + ")";

    public static void Insertar(SqlConnection con, int idPaciente, int idProfesional, string? observaciones, SqlTransaction? tx = null)
    {
        var conTipo = TieneTipoCierre(con, tx);
        using var cmd = new SqlCommand(Sql(conTipo), con, tx);
        cmd.Parameters.AddWithValue("@IdPac", idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Obs", (object?)observaciones ?? DBNull.Value);
        if (conTipo) cmd.Parameters.AddWithValue("@TipoCierre", TipoCierreDerivacion);
        cmd.ExecuteNonQuery();
    }
}
