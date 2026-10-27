using Microsoft.Data.SqlClient;

// Configuración › Especialidades (rama AAA1, migración 34): el Configurador administra el catálogo.
//
//   GET    /especialidades/catalogo                 Todas, con disciplina, tipo, estado y en cuántos registros se usan.
//   POST   /especialidades/catalogo                 { nombre, disciplina, tipo, requiereMatriculaEspecialista }
//   PUT    /especialidades/catalogo/{id}            Los mismos datos.
//   PUT    /especialidades/catalogo/{id}/estado     { activa }: dar de baja o reactivar.
//   DELETE /especialidades/catalogo/{id}            Solo si nunca se usó. Si se usó, se da de baja: así no se pierde con qué
//                                                   especialidad atendió un profesional (la historia clínica se conserva, Ley 26.529).
//
// Las activas de tipo "Especialidad" son las que se ofrecen al registrar un profesional (GET /usuarios/equipo/opciones), así que
// un cambio acá se ve ahí en el momento. Las de tipo "Servicio" (Admisión, Atención domiciliaria...) solo sirven para derivar.
// Todo cambio queda en la auditoría administrativa. Los GET /especialidades de siempre no cambian.
public static class EspecialidadesEndpoints
{
    public record EspecialidadRequest(string? Nombre, string? Disciplina, string? Tipo, bool RequiereMatriculaEspecialista);
    public record EstadoEspecialidadRequest(bool Activa);

    public static readonly string[] Tipos = { "Especialidad", "Servicio" };

    private static IResult Error(int status, string mensaje) => Results.Json(new { mensaje }, statusCode: status);

    /// <summary>Trim y tipo por defecto. Sin base de datos.</summary>
    public static EspecialidadRequest Normalizar(EspecialidadRequest r) => r with
    {
        Nombre = string.IsNullOrWhiteSpace(r.Nombre) ? null : string.Join(' ', r.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
        Disciplina = string.IsNullOrWhiteSpace(r.Disciplina) ? null : r.Disciplina.Trim(),
        Tipo = string.IsNullOrWhiteSpace(r.Tipo) ? "Especialidad" : r.Tipo.Trim()
    };

    /// <summary>Motivo del rechazo, o null si el pedido (ya normalizado) es válido. Sin base de datos.</summary>
    public static string? Validar(EspecialidadRequest r)
    {
        if (r.Nombre is null || r.Nombre.Length < 3) return "Escribí el nombre de la especialidad (3 caracteres o más).";
        if (r.Nombre.Length > 100) return "El nombre puede tener hasta 100 caracteres.";
        if (r.Disciplina is { Length: > 60 }) return "La disciplina puede tener hasta 60 caracteres.";
        if (!Tipos.Contains(r.Tipo)) return "El tipo tiene que ser Especialidad o Servicio.";
        return null;
    }

    private static void Auditar(Identidad yo, string accion, int id, string detalle) =>
        AuditoriaAgenda.Registrar(yo.IdUsuario, accion, "Especialidad", id, detalle, yo.Modulo ?? yo.Rol);

    public static IEndpointRouteBuilder MapEspecialidadesCatalogo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/especialidades/catalogo", () => Results.Ok(new EspecialidadCatalogoDAO().Listar()))
            .Solo(Quien.Configurador);

        app.MapPost("/especialidades/catalogo", (EspecialidadRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var r = Normalizar(req);
            if (Validar(r) is string motivo) return Error(400, motivo);
            var dao = new EspecialidadCatalogoDAO();
            if (dao.NombreUsado(r.Nombre!, null)) return Error(409, $"Ya existe la especialidad \"{r.Nombre}\".");
            var id = dao.Crear(r);
            Auditar(yo, "Especialidad agregada", id, $"{r.Nombre} · {r.Tipo}{(r.Disciplina is null ? "" : " · " + r.Disciplina)}");
            return Results.Created($"/especialidades/catalogo/{id}", dao.Obtener(id));
        }).Solo(Quien.Configurador);

        app.MapPut("/especialidades/catalogo/{id:int}", (int id, EspecialidadRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var r = Normalizar(req);
            if (Validar(r) is string motivo) return Error(400, motivo);
            var dao = new EspecialidadCatalogoDAO();
            var antes = dao.Obtener(id);
            if (antes is null) return Results.NotFound();
            if (dao.NombreUsado(r.Nombre!, id)) return Error(409, $"Ya existe la especialidad \"{r.Nombre}\".");
            dao.Modificar(id, r);
            var cambios = new List<string>();
            if (antes.Nombre != r.Nombre) cambios.Add($"nombre: {antes.Nombre} → {r.Nombre}");
            if (antes.Disciplina != r.Disciplina) cambios.Add($"disciplina: {antes.Disciplina ?? "—"} → {r.Disciplina ?? "—"}");
            if (antes.Tipo != r.Tipo) cambios.Add($"tipo: {antes.Tipo} → {r.Tipo}");
            if (antes.RequiereMatriculaEspecialista != r.RequiereMatriculaEspecialista)
                cambios.Add($"matrícula de especialista: {(r.RequiereMatriculaEspecialista ? "sí" : "no")}");
            if (cambios.Count > 0) Auditar(yo, "Especialidad modificada", id, $"{r.Nombre} · {string.Join(" · ", cambios)}");
            return Results.Ok(dao.Obtener(id));
        }).Solo(Quien.Configurador);

        app.MapPut("/especialidades/catalogo/{id:int}/estado", (int id, EstadoEspecialidadRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new EspecialidadCatalogoDAO();
            var e = dao.Obtener(id);
            if (e is null) return Results.NotFound();
            var estado = req.Activa ? "Activo" : "Inactivo";
            if (e.Estado == estado) return Results.Ok(e);
            dao.CambiarEstado(id, estado);
            Auditar(yo, req.Activa ? "Especialidad reactivada" : "Especialidad dada de baja", id,
                $"{e.Nombre}{(e.Usos > 0 ? $" · en uso en {e.Usos} registro(s)" : "")}");
            return Results.Ok(dao.Obtener(id));
        }).Solo(Quien.Configurador);

        app.MapDelete("/especialidades/catalogo/{id:int}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new EspecialidadCatalogoDAO();
            var e = dao.Obtener(id);
            if (e is null) return Results.NotFound();
            if (e.Usos > 0 || !dao.EliminarSiNoSeUsa(id))
                return Error(409, $"\"{e.Nombre}\" ya se usó en {Math.Max(e.Usos, 1)} registro(s): no se puede borrar. Dala de baja y deja de ofrecerse.");
            Auditar(yo, "Especialidad eliminada", id, e.Nombre);
            return Results.NoContent();
        }).Solo(Quien.Configurador);

        return app;
    }
}

public record EspecialidadCatalogo(int IdEspecialidad, string Nombre, string? Disciplina, string Tipo,
    bool RequiereMatriculaEspecialista, string Estado, int Usos);

public class EspecialidadCatalogoDAO
{
    // En cuántos registros se usa: toda tabla con una clave foránea a Especialidad (se lee del esquema, así una tabla nueva
    // cuenta sola) más InformeAdmision.IdEspecialidad, que no tiene clave foránea.
    private const string SqlUsos = @"
        DECLARE @sql NVARCHAR(MAX) = N'SELECT 0';
        SELECT @sql = @sql + N' + (SELECT COUNT(*) FROM ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
                           + N' WHERE ' + QUOTENAME(c.name) + N' = @Id)'
        FROM sys.foreign_key_columns fkc
        JOIN sys.tables  t ON t.object_id = fkc.parent_object_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        WHERE fkc.referenced_object_id = OBJECT_ID(N'dbo.Especialidad');
        IF COL_LENGTH(N'dbo.InformeAdmision', N'IdEspecialidad') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc
                           WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.InformeAdmision') AND fkc.referenced_object_id = OBJECT_ID(N'dbo.Especialidad'))
            SET @sql = @sql + N' + (SELECT COUNT(*) FROM dbo.InformeAdmision WHERE IdEspecialidad = @Id)';
        EXEC sp_executesql @sql, N'@Id INT', @Id = @IdEsp;";

    private static int Usos(SqlConnection con, int id, SqlTransaction? tx = null)
    {
        using var cmd = new SqlCommand(SqlUsos, con, tx);
        cmd.Parameters.AddWithValue("@IdEsp", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<EspecialidadCatalogo> Listar()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var filas = new List<(int Id, string Nombre, string? Disciplina, string Tipo, bool Req, string Estado)>();
        using (var cmd = new SqlCommand(@"
            SELECT IdEspecialidad, Nombre, Disciplina, Tipo, RequiereMatriculaEspecialista, Estado
            FROM Especialidad ORDER BY CASE WHEN Estado = 'Activo' THEN 0 ELSE 1 END, Tipo, Disciplina, Nombre", con))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                filas.Add((rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetString(3), rd.GetBoolean(4), rd.GetString(5)));
        return filas.Select(f => new EspecialidadCatalogo(f.Id, f.Nombre, f.Disciplina, f.Tipo, f.Req, f.Estado, Usos(con, f.Id))).ToList();
    }

    public EspecialidadCatalogo? Obtener(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT IdEspecialidad, Nombre, Disciplina, Tipo, RequiereMatriculaEspecialista, Estado
            FROM Especialidad WHERE IdEspecialidad = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        (int, string, string?, string, bool, string)? f = null;
        using (var rd = cmd.ExecuteReader())
            if (rd.Read()) f = (rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetString(3), rd.GetBoolean(4), rd.GetString(5));
        if (f is null) return null;
        var v = f.Value;
        return new EspecialidadCatalogo(v.Item1, v.Item2, v.Item3, v.Item4, v.Item5, v.Item6, Usos(con, id));
    }

    /// <summary>¿Hay otra especialidad con ese nombre? Sin distinguir tildes ni mayúsculas ("Psicologia" = "Psicología").</summary>
    public bool NombreUsado(string nombre, int? exceptoId)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT 1 FROM Especialidad
            WHERE Nombre COLLATE Latin1_General_CI_AI = @N COLLATE Latin1_General_CI_AI
              AND (@Excepto IS NULL OR IdEspecialidad <> @Excepto)", con);
        cmd.Parameters.AddWithValue("@N", nombre);
        cmd.Parameters.AddWithValue("@Excepto", (object?)exceptoId ?? DBNull.Value);
        return cmd.ExecuteScalar() is not null;
    }

    public int Crear(EspecialidadesEndpoints.EspecialidadRequest r)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            INSERT INTO Especialidad (Nombre, Estado, Disciplina, Tipo, RequiereMatriculaEspecialista)
            OUTPUT INSERTED.IdEspecialidad
            VALUES (@N, 'Activo', @D, @T, @R);", con);
        cmd.Parameters.AddWithValue("@N", r.Nombre!);
        cmd.Parameters.AddWithValue("@D", (object?)r.Disciplina ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@T", r.Tipo!);
        cmd.Parameters.AddWithValue("@R", r.RequiereMatriculaEspecialista);
        return (int)cmd.ExecuteScalar()!;
    }

    public void Modificar(int id, EspecialidadesEndpoints.EspecialidadRequest r)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            UPDATE Especialidad SET Nombre = @N, Disciplina = @D, Tipo = @T, RequiereMatriculaEspecialista = @R
            WHERE IdEspecialidad = @Id;", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@N", r.Nombre!);
        cmd.Parameters.AddWithValue("@D", (object?)r.Disciplina ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@T", r.Tipo!);
        cmd.Parameters.AddWithValue("@R", r.RequiereMatriculaEspecialista);
        cmd.ExecuteNonQuery();
    }

    public void CambiarEstado(int id, string estado)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("UPDATE Especialidad SET Estado = @E WHERE IdEspecialidad = @Id;", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@E", estado);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Borra solo si nadie la usa, dentro de una transacción (vuelve a contar adentro: no hay carrera con un alta).</summary>
    public bool EliminarSiNoSeUsa(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction(System.Data.IsolationLevel.Serializable);
        if (Usos(con, id, tx) > 0) { tx.Rollback(); return false; }
        using var cmd = new SqlCommand("DELETE FROM Especialidad WHERE IdEspecialidad = @Id;", con, tx);
        cmd.Parameters.AddWithValue("@Id", id);
        var borradas = cmd.ExecuteNonQuery();
        tx.Commit();
        return borradas == 1;
    }
}
