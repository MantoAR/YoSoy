using Microsoft.Data.SqlClient;
using System.Data;

// Documentos clínicos (migración 35): plantillas versionadas, documentos emitidos (de solo agregar) y addenda.
// Las reglas viven en Services/Documentos y Endpoints/DocumentosEndpoints.cs; acá solo SQL.

public record PlantillaResumen(int IdPlantilla, string Clave, string Grupo, string Titulo, string? Descripcion, bool Activa, int Orden,
    int? IdVersionPublicada, int? NumeroPublicada, bool? Provisoria, DateTime? FechaPublicacion, int? IdBorrador);
public record VersionPlantilla(int IdVersion, int IdPlantilla, int Numero, string CamposJson, string CuerpoJson, string Estado, bool Provisoria,
    string? MotivoCambio, string? Autor, DateTime FechaCreacion, DateTime? FechaPublicacion, string? HashSha256);
public record PlantillaVigente(int IdPlantilla, string Clave, string Grupo, string Titulo, string? Descripcion, int IdVersion, int Numero,
    bool Provisoria, string CamposJson, string CuerpoJson);
public record DocumentoResumen(int IdDocumento, int IdPaciente, string Clave, string Titulo, string Grupo, int NumeroVersion, bool Provisoria,
    int IdProfesional, string Profesional, DateTime FechaEmision, string HashSha256, int Addenda);
public record DocumentoCompleto(DocumentoResumen Resumen, int IdVersion, string DatosJson, string ContenidoJson, string? HashPdfSha256,
    int? IdTurno, int? IdEventoCrisis);
public record AddendumDto(int IdAddendum, string Texto, int IdProfesional, string Profesional, DateTime Fecha, string HashSha256);
public record TurnoParaDocumento(int IdTurno, DateTime Fecha, TimeSpan Hora, string Estado);

public class DocumentosClinicosDAO
{
    private static SqlConnection Abrir() { var c = Conexion.ObtenerConexion(); c.Open(); return c; }

    private static SqlCommand Cmd(SqlConnection con, string sql, SqlTransaction? tx = null, params (string, object?)[] ps)
    {
        var cmd = new SqlCommand(sql, con, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    // ─────────────────────────────── Plantillas (Configurador) ───────────────────────────────
    public List<PlantillaResumen> Plantillas()
    {
        using var con = Abrir();
        using var cmd = Cmd(con, @"
            SELECT p.IdPlantilla, p.Clave, p.Grupo, p.Titulo, p.Descripcion, p.Activa, p.Orden,
                   v.IdVersion, v.Numero, v.Provisoria, v.FechaPublicacion,
                   (SELECT b.IdVersion FROM PlantillaDocumentoVersion b WHERE b.IdPlantilla = p.IdPlantilla AND b.Estado = N'Borrador') AS IdBorrador
            FROM PlantillaDocumento p
            LEFT JOIN PlantillaDocumentoVersion v ON v.IdPlantilla = p.IdPlantilla AND v.Estado = N'Publicada'
            ORDER BY p.Orden, p.Titulo;");
        var lista = new List<PlantillaResumen>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new PlantillaResumen((int)r["IdPlantilla"], (string)r["Clave"], (string)r["Grupo"], (string)r["Titulo"], r["Descripcion"] as string,
                (bool)r["Activa"], (int)r["Orden"], r["IdVersion"] as int?, r["Numero"] as int?, r["Provisoria"] as bool?,
                r["FechaPublicacion"] as DateTime?, r["IdBorrador"] as int?));
        return lista;
    }

    public PlantillaResumen? Plantilla(int idPlantilla) => Plantillas().FirstOrDefault(p => p.IdPlantilla == idPlantilla);

    private const string SelectVersion = @"
        SELECT v.IdVersion, v.IdPlantilla, v.Numero, v.CamposJson, v.CuerpoJson, v.Estado, v.Provisoria, v.MotivoCambio,
               u.Username AS Autor, v.FechaCreacion, v.FechaPublicacion, v.HashSha256
        FROM PlantillaDocumentoVersion v LEFT JOIN Usuario u ON u.IdUsuario = v.IdUsuarioAutor ";

    private static VersionPlantilla LeerVersion(SqlDataReader r) => new((int)r["IdVersion"], (int)r["IdPlantilla"], (int)r["Numero"],
        (string)r["CamposJson"], (string)r["CuerpoJson"], (string)r["Estado"], (bool)r["Provisoria"], r["MotivoCambio"] as string,
        r["Autor"] as string, (DateTime)r["FechaCreacion"], r["FechaPublicacion"] as DateTime?, (r["HashSha256"] as string)?.Trim());

    public List<VersionPlantilla> Versiones(int idPlantilla)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, SelectVersion + "WHERE v.IdPlantilla = @P ORDER BY v.Numero DESC;", null, ("@P", idPlantilla));
        var lista = new List<VersionPlantilla>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(LeerVersion(r));
        return lista;
    }

    public VersionPlantilla? Version(int idVersion)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, SelectVersion + "WHERE v.IdVersion = @V;", null, ("@V", idVersion));
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerVersion(r) : null;
    }

    public VersionPlantilla? VersionEnEstado(int idPlantilla, string estado)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, SelectVersion + "WHERE v.IdPlantilla = @P AND v.Estado = @E;", null, ("@P", idPlantilla), ("@E", estado));
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerVersion(r) : null;
    }

    /// <summary>Crea o reemplaza el borrador de la plantilla (hay uno solo). Devuelve su id.</summary>
    public int GuardarBorrador(int idPlantilla, string camposJson, string cuerpoJson, bool provisoria, int idUsuario)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, @"
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @Id INT = (SELECT IdVersion FROM PlantillaDocumentoVersion WITH (UPDLOCK, HOLDLOCK) WHERE IdPlantilla = @P AND Estado = N'Borrador');
            IF @Id IS NULL
            BEGIN
                INSERT INTO PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, IdUsuarioAutor)
                VALUES (@P, (SELECT ISNULL(MAX(Numero), 0) + 1 FROM PlantillaDocumentoVersion WHERE IdPlantilla = @P), @C, @B, N'Borrador', @Prov, @U);
                SET @Id = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE PlantillaDocumentoVersion SET CamposJson = @C, CuerpoJson = @B, Provisoria = @Prov, IdUsuarioAutor = @U, FechaCreacion = SYSDATETIME()
                WHERE IdVersion = @Id;
            COMMIT;
            SELECT @Id;", null, ("@P", idPlantilla), ("@C", camposJson), ("@B", cuerpoJson), ("@Prov", provisoria), ("@U", idUsuario));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public bool DescartarBorrador(int idPlantilla)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, "DELETE FROM PlantillaDocumentoVersion WHERE IdPlantilla = @P AND Estado = N'Borrador';", null, ("@P", idPlantilla));
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Publica: la versión publicada actual pasa a Retirada y <paramref name="idVersion"/> (un borrador) pasa a Publicada, en una
    /// transacción. Si <paramref name="copiaDe"/> viene, en vez de publicar un borrador crea una versión nueva con el contenido de
    /// esa versión (restaurar). Devuelve el id de la versión publicada.
    /// </summary>
    public int Publicar(int idPlantilla, int? idBorrador, VersionPlantilla? copiaDe, string motivo, string hash, int idUsuario)
    {
        using var con = Abrir();
        using var tx = con.BeginTransaction();
        try
        {
            Cmd(con, "UPDATE PlantillaDocumentoVersion SET Estado = N'Retirada' WHERE IdPlantilla = @P AND Estado = N'Publicada';", tx, ("@P", idPlantilla)).ExecuteNonQuery();
            int id;
            if (copiaDe is not null)
            {
                id = Convert.ToInt32(Cmd(con, @"
                    INSERT INTO PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado, Provisoria, MotivoCambio, IdUsuarioAutor, FechaPublicacion, HashSha256)
                    OUTPUT INSERTED.IdVersion
                    VALUES (@P, (SELECT MAX(Numero) + 1 FROM PlantillaDocumentoVersion WHERE IdPlantilla = @P), @C, @B, N'Publicada', @Prov, @M, @U, SYSDATETIME(), @H);", tx,
                    ("@P", idPlantilla), ("@C", copiaDe.CamposJson), ("@B", copiaDe.CuerpoJson), ("@Prov", copiaDe.Provisoria), ("@M", motivo), ("@U", idUsuario), ("@H", hash)).ExecuteScalar());
            }
            else
            {
                id = idBorrador!.Value;
                var n = Cmd(con, @"UPDATE PlantillaDocumentoVersion SET Estado = N'Publicada', MotivoCambio = @M, IdUsuarioAutor = @U, FechaPublicacion = SYSDATETIME(), HashSha256 = @H
                    WHERE IdVersion = @V AND IdPlantilla = @P AND Estado = N'Borrador';", tx,
                    ("@V", id), ("@P", idPlantilla), ("@M", motivo), ("@U", idUsuario), ("@H", hash)).ExecuteNonQuery();
                if (n != 1) throw new InvalidOperationException("El borrador cambió mientras se publicaba.");
            }
            tx.Commit();
            return id;
        }
        catch { tx.Rollback(); throw; }
    }

    public bool CambiarActiva(int idPlantilla, bool activa)
    {
        using var con = Abrir();
        return Cmd(con, "UPDATE PlantillaDocumento SET Activa = @A WHERE IdPlantilla = @P;", null, ("@A", activa), ("@P", idPlantilla)).ExecuteNonQuery() > 0;
    }

    // ─────────────────────────────── Plantillas vigentes (Profesional) ───────────────────────────────
    private const string SelectVigente = @"
        SELECT p.IdPlantilla, p.Clave, p.Grupo, p.Titulo, p.Descripcion, v.IdVersion, v.Numero, v.Provisoria, v.CamposJson, v.CuerpoJson
        FROM PlantillaDocumento p JOIN PlantillaDocumentoVersion v ON v.IdPlantilla = p.IdPlantilla AND v.Estado = N'Publicada'
        WHERE p.Activa = 1 ";

    private static PlantillaVigente LeerVigente(SqlDataReader r) => new((int)r["IdPlantilla"], (string)r["Clave"], (string)r["Grupo"],
        (string)r["Titulo"], r["Descripcion"] as string, (int)r["IdVersion"], (int)r["Numero"], (bool)r["Provisoria"],
        (string)r["CamposJson"], (string)r["CuerpoJson"]);

    public List<PlantillaVigente> Vigentes()
    {
        using var con = Abrir();
        using var cmd = Cmd(con, SelectVigente + "ORDER BY p.Orden, p.Titulo;");
        var lista = new List<PlantillaVigente>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(LeerVigente(r));
        return lista;
    }

    public PlantillaVigente? Vigente(string clave)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, SelectVigente + "AND p.Clave = @K;", null, ("@K", clave));
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerVigente(r) : null;
    }

    // ─────────────────────────────── Datos del sistema para la precarga ───────────────────────────────
    /// <summary>
    /// Los datos de la lista permitida (RenderizadorDocumento.DatosPermitidos) para un paciente y un profesional.
    /// Las fechas van como AAAA-MM-DD. Lo que no existe queda en null.
    /// </summary>
    public Dictionary<string, string?> DatosSistema(int idPaciente, int idProfesional, int? idTurno, DateTime hoy)
    {
        var d = RenderizadorDocumento.DatosPermitidos.Keys.ToDictionary(k => k, _ => (string?)null);
        using var con = Abrir();

        using (var r = Cmd(con, "SELECT Nombre, Dni, FechaNacimiento FROM Paciente WHERE IdPaciente = @Id;", null, ("@Id", idPaciente)).ExecuteReader())
            if (r.Read())
            {
                d["paciente.nombre"] = r["Nombre"] as string;
                d["paciente.dni"] = r["Dni"] as string;
                if (r["FechaNacimiento"] is DateTime fn)
                {
                    var edad = hoy.Year - fn.Year - (hoy.Date < fn.Date.AddYears(hoy.Year - fn.Year) ? 1 : 0);
                    if (edad >= 0) d["paciente.edad"] = edad.ToString();
                }
            }

        using (var r = Cmd(con, @"SELECT TOP (2) t.Nombre, t.Dni, t.Vinculo FROM PacienteTutor pt JOIN Tutor t ON t.IdTutor = pt.IdTutor
                                  WHERE pt.IdPaciente = @Id ORDER BY t.IdTutor;", null, ("@Id", idPaciente)).ExecuteReader())
            for (var i = 1; r.Read(); i++)
            {
                d[$"tutor{i}.nombre"] = r["Nombre"] as string;
                d[$"tutor{i}.dni"] = r["Dni"] as string;
                d[$"tutor{i}.vinculo"] = r["Vinculo"] as string;
            }

        using (var r = Cmd(con, @"
            SELECT LTRIM(RTRIM(ISNULL(p.Nombre, '') + ' ' + ISNULL(p.Apellido, ''))) AS Nombre,
                   ISNULL(m.Numero, p.Matricula) AS Matricula, ISNULL(em.Nombre, ep.Nombre) AS Especialidad
            FROM Profesional p
            LEFT JOIN ProfesionalMatricula m ON m.IdProfesional = p.IdProfesional AND m.Principal = 1 AND m.Activa = 1
            LEFT JOIN Especialidad em ON em.IdEspecialidad = m.IdEspecialidad
            LEFT JOIN Especialidad ep ON ep.IdEspecialidad = p.IdEspecialidad
            WHERE p.IdProfesional = @Id;", null, ("@Id", idProfesional)).ExecuteReader())
            if (r.Read())
            {
                d["profesional.nombre"] = r["Nombre"] as string;
                d["profesional.matricula"] = r["Matricula"] as string;
                d["profesional.especialidad"] = r["Especialidad"] as string;
            }

        if (idTurno is int t)
            using (var r = Cmd(con, @"
                SELECT tu.Fecha, tu.Hora,
                       ISNULL((SELECT TOP 1 a.IntervaloMinutos FROM Agenda a WHERE a.IdProfesional = tu.IdProfesional AND a.IntervaloMinutos > 0
                               AND a.HoraInicio <= tu.Hora AND a.HoraFin > tu.Hora), 30) AS Intervalo
                FROM Turno tu WHERE tu.IdTurno = @T AND tu.IdPaciente = @P;", null, ("@T", t), ("@P", idPaciente)).ExecuteReader())
                if (r.Read())
                {
                    var hora = (TimeSpan)r["Hora"];
                    d["turno.fecha"] = ((DateTime)r["Fecha"]).ToString("yyyy-MM-dd");
                    d["turno.horaInicio"] = hora.ToString(@"hh\:mm");
                    d["turno.horaFin"] = hora.Add(TimeSpan.FromMinutes(Convert.ToInt32(r["Intervalo"]))).ToString(@"hh\:mm");
                }

        // Inicio del tratamiento: el primer turno Realizado con este profesional (o, si no hay, el primero confirmado que ya pasó).
        var inicio = Cmd(con, @"
            SELECT MIN(CAST(tu.Fecha AS DATE)) FROM Turno tu JOIN EstadoTurno et ON et.IdEstadoTurno = tu.IdEstadoTurno
            WHERE tu.IdPaciente = @P AND tu.IdProfesional = @Prof
              AND (et.Descripcion = N'Realizado' OR (et.Descripcion = N'Confirmado' AND CAST(tu.Fecha AS DATE) <= @Hoy));", null,
            ("@P", idPaciente), ("@Prof", idProfesional), ("@Hoy", hoy.Date)).ExecuteScalar();
        if (inicio is DateTime fi) d["tratamiento.fechaInicio"] = fi.ToString("yyyy-MM-dd");

        d["hoy"] = hoy.ToString("yyyy-MM-dd");
        d["organizacion.localidad"] = Cmd(con, "SELECT Valor FROM ConfiguracionOrganizacion WHERE Clave = N'Organizacion.Localidad';").ExecuteScalar() as string;
        d["organizacion.nombre"] = Cmd(con, "SELECT TOP 1 Nombre FROM Organizacion WHERE Estado = 'Activo' ORDER BY IdOrganizacion;").ExecuteScalar() as string;
        return d;
    }

    /// <summary>Turnos del paciente con este profesional que sirven para una constancia de asistencia (Realizado, o Confirmado que ya pasó).</summary>
    public List<TurnoParaDocumento> TurnosParaConstancia(int idPaciente, int idProfesional, DateTime ahora)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, @"
            SELECT TOP (30) tu.IdTurno, tu.Fecha, tu.Hora, et.Descripcion
            FROM Turno tu JOIN EstadoTurno et ON et.IdEstadoTurno = tu.IdEstadoTurno
            WHERE tu.IdPaciente = @P AND tu.IdProfesional = @Prof
              AND (et.Descripcion = N'Realizado' OR (et.Descripcion = N'Confirmado' AND CAST(tu.Fecha AS DATE) <= @Hoy))
            ORDER BY tu.Fecha DESC, tu.Hora DESC;", null, ("@P", idPaciente), ("@Prof", idProfesional), ("@Hoy", ahora.Date));
        var lista = new List<TurnoParaDocumento>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new TurnoParaDocumento((int)r["IdTurno"], (DateTime)r["Fecha"], (TimeSpan)r["Hora"], (string)r["Descripcion"]));
        return lista;
    }

    public bool TurnoEsDelPaciente(int idTurno, int idPaciente)
    {
        using var con = Abrir();
        return Convert.ToInt32(Cmd(con, "SELECT COUNT(*) FROM Turno WHERE IdTurno = @T AND IdPaciente = @P;", null, ("@T", idTurno), ("@P", idPaciente)).ExecuteScalar()) > 0;
    }

    public bool EventoEsDelPaciente(int idEvento, int idPaciente)
    {
        using var con = Abrir();
        return Convert.ToInt32(Cmd(con, "SELECT COUNT(*) FROM EventoCrisis WHERE IdEvento = @E AND IdPaciente = @P;", null, ("@E", idEvento), ("@P", idPaciente)).ExecuteScalar()) > 0;
    }

    /// <summary>¿Hay una autorización para compartir información emitida para el paciente? (requisito del informe escolar)</summary>
    public bool TieneAutorizacionInfo(int idPaciente)
    {
        using var con = Abrir();
        return Convert.ToInt32(Cmd(con, @"
            SELECT COUNT(*) FROM DocumentoClinico d
            JOIN PlantillaDocumentoVersion v ON v.IdVersion = d.IdVersion
            JOIN PlantillaDocumento p ON p.IdPlantilla = v.IdPlantilla
            WHERE d.IdPaciente = @P AND p.Clave = 'autorizacion_info';", null, ("@P", idPaciente)).ExecuteScalar()) > 0;
    }

    /// <summary>La historia clínica del paciente; si no tiene, la abre (un documento siempre va a una historia clínica).</summary>
    public int HistoriaDe(int idPaciente)
    {
        using var con = Abrir();
        return Convert.ToInt32(Cmd(con, @"
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @Id INT = (SELECT IdHistoria FROM HistoriaClinica WITH (UPDLOCK, HOLDLOCK) WHERE IdPaciente = @P);
            IF @Id IS NULL
            BEGIN
                INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) VALUES (@P, CAST(GETDATE() AS DATE));
                SET @Id = SCOPE_IDENTITY();
            END
            COMMIT;
            SELECT @Id;", null, ("@P", idPaciente)).ExecuteScalar());
    }

    // ─────────────────────────────── Documentos emitidos ───────────────────────────────
    public int Insertar(int idPaciente, int idHistoria, int idProfesional, int idVersion, int? idTurno, int? idEvento, string datosJson,
        string contenidoJson, byte[] pdf, string hash, string hashPdf, DateTime fechaEmision, int idUsuario)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, @"
            INSERT INTO DocumentoClinico (IdPaciente, IdHistoria, IdProfesional, IdVersion, IdTurno, IdEventoCrisis, DatosJson, ContenidoJson,
                                          Pdf, HashSha256, HashPdfSha256, FechaEmision, IdUsuarioEmisor)
            OUTPUT INSERTED.IdDocumento
            VALUES (@Pac, @H, @Prof, @V, @T, @E, @D, @C, @Pdf, @Hash, @HashPdf, @F, @U);", null,
            ("@Pac", idPaciente), ("@H", idHistoria), ("@Prof", idProfesional), ("@V", idVersion), ("@T", idTurno), ("@E", idEvento),
            ("@D", datosJson), ("@C", contenidoJson), ("@Hash", hash), ("@HashPdf", hashPdf), ("@U", idUsuario));
        cmd.Parameters.Add("@Pdf", SqlDbType.VarBinary, -1).Value = pdf;
        cmd.Parameters.Add("@F", SqlDbType.DateTime2).Value = fechaEmision;
        return (int)cmd.ExecuteScalar();
    }

    private const string SelectDocumento = @"
        SELECT d.IdDocumento, d.IdPaciente, p.Clave, p.Titulo, p.Grupo, v.Numero, v.Provisoria, d.IdProfesional,
               LTRIM(RTRIM(ISNULL(pr.Nombre, '') + ' ' + ISNULL(pr.Apellido, ''))) AS Profesional, d.FechaEmision, d.HashSha256,
               (SELECT COUNT(*) FROM DocumentoClinicoAddendum a WHERE a.IdDocumento = d.IdDocumento) AS Addenda,
               d.IdVersion, d.DatosJson, d.ContenidoJson, d.HashPdfSha256, d.IdTurno, d.IdEventoCrisis
        FROM DocumentoClinico d
        JOIN PlantillaDocumentoVersion v ON v.IdVersion = d.IdVersion
        JOIN PlantillaDocumento p ON p.IdPlantilla = v.IdPlantilla
        JOIN Profesional pr ON pr.IdProfesional = d.IdProfesional ";

    private static DocumentoResumen LeerResumen(SqlDataReader r) => new((int)r["IdDocumento"], (int)r["IdPaciente"], (string)r["Clave"],
        (string)r["Titulo"], (string)r["Grupo"], (int)r["Numero"], (bool)r["Provisoria"], (int)r["IdProfesional"], (string)r["Profesional"],
        (DateTime)r["FechaEmision"], ((string)r["HashSha256"]).Trim(), (int)r["Addenda"]);

    public List<DocumentoResumen> DelPaciente(int idPaciente)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, SelectDocumento + "WHERE d.IdPaciente = @P ORDER BY d.FechaEmision DESC, d.IdDocumento DESC;", null, ("@P", idPaciente));
        var lista = new List<DocumentoResumen>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(LeerResumen(r));
        return lista;
    }

    public DocumentoCompleto? Documento(int idDocumento)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, SelectDocumento + "WHERE d.IdDocumento = @Id;", null, ("@Id", idDocumento));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DocumentoCompleto(LeerResumen(r), (int)r["IdVersion"], (string)r["DatosJson"], (string)r["ContenidoJson"],
            (r["HashPdfSha256"] as string)?.Trim(), r["IdTurno"] as int?, r["IdEventoCrisis"] as int?);
    }

    public byte[]? Pdf(int idDocumento)
    {
        using var con = Abrir();
        return Cmd(con, "SELECT Pdf FROM DocumentoClinico WHERE IdDocumento = @Id;", null, ("@Id", idDocumento)).ExecuteScalar() as byte[];
    }

    public List<AddendumDto> Addenda(int idDocumento)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, @"
            SELECT a.IdAddendum, a.Texto, a.IdProfesional, LTRIM(RTRIM(ISNULL(pr.Nombre, '') + ' ' + ISNULL(pr.Apellido, ''))) AS Profesional, a.Fecha, a.HashSha256
            FROM DocumentoClinicoAddendum a JOIN Profesional pr ON pr.IdProfesional = a.IdProfesional
            WHERE a.IdDocumento = @Id ORDER BY a.Fecha, a.IdAddendum;", null, ("@Id", idDocumento));
        var lista = new List<AddendumDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new AddendumDto((int)r["IdAddendum"], (string)r["Texto"], (int)r["IdProfesional"], (string)r["Profesional"],
                (DateTime)r["Fecha"], ((string)r["HashSha256"]).Trim()));
        return lista;
    }

    public int InsertarAddendum(int idDocumento, string texto, int idProfesional, DateTime fecha, string hash)
    {
        using var con = Abrir();
        using var cmd = Cmd(con, @"INSERT INTO DocumentoClinicoAddendum (IdDocumento, Texto, IdProfesional, Fecha, HashSha256)
            OUTPUT INSERTED.IdAddendum VALUES (@D, @T, @P, @F, @H);", null, ("@D", idDocumento), ("@T", texto), ("@P", idProfesional), ("@H", hash));
        cmd.Parameters.Add("@F", SqlDbType.DateTime2).Value = fecha;
        return (int)cmd.ExecuteScalar();
    }

    // ─────────────────────────────── Firma del profesional ───────────────────────────────
    public byte[]? Firma(int idProfesional)
    {
        using var con = Abrir();
        return Cmd(con, "SELECT FirmaImagen FROM Profesional WHERE IdProfesional = @Id;", null, ("@Id", idProfesional)).ExecuteScalar() as byte[];
    }

    public void GuardarFirma(int idProfesional, byte[]? imagen)
    {
        using var con = Abrir();
        using var cmd = new SqlCommand("UPDATE Profesional SET FirmaImagen = @F WHERE IdProfesional = @Id;", con);
        cmd.Parameters.Add("@F", SqlDbType.VarBinary, -1).Value = (object?)imagen ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        cmd.ExecuteNonQuery();
    }
}
