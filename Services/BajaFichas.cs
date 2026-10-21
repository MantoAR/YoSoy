using Microsoft.Data.SqlClient;

// Dar de baja (o de alta) a un paciente o a un profesional baja (o reactiva) TAMBIÉN a su usuario, en la misma transacción y con auditoría
// administrativa. Antes la baja tocaba solo la ficha y el usuario seguía pudiendo ingresar (el ingreso mira Usuario.Estado, no la ficha).
//
//   Baja:  ficha -> 'Inactivo' y cada usuario vinculado que esté 'Activo' -> 'Inactivo' (el valor que el ingreso ya rechaza).
//   Alta:  ficha -> 'Activo'   y cada usuario vinculado que esté 'Inactivo' -> 'Activo' (simétrica).
//
// Un usuario 'Pendiente' (invitación sin confirmar) no se toca en ninguno de los dos sentidos. Reactivar no distingue por qué estaba inactivo
// el usuario: si alguien lo había dado de baja a mano con DELETE /usuarios/{id}, el alta de la ficha también lo reactiva.
// No cierra las sesiones ya abiertas (no hay revocación de tokens): un token emitido vale hasta que vence, 8 horas.
public enum TipoFicha { Paciente, Profesional }

public record ResultadoCambioFicha(bool Existe, IReadOnlyList<(int IdUsuario, string Username)> Usuarios);

public class BajaFichasDAO
{
    public ResultadoCambioFicha Baja(TipoFicha tipo, int idFicha, int? idAutor, string? espacio) => Cambiar(tipo, idFicha, baja: true, idAutor, espacio);
    public ResultadoCambioFicha Alta(TipoFicha tipo, int idFicha, int? idAutor, string? espacio) => Cambiar(tipo, idFicha, baja: false, idAutor, espacio);

    private static ResultadoCambioFicha Cambiar(TipoFicha tipo, int idFicha, bool baja, int? idAutor, string? espacio)
    {
        var (tabla, columna, vinculo, nombre) = tipo == TipoFicha.Paciente
            ? ("Paciente", "IdPaciente", "UsuarioPaciente", "Paciente")
            : ("Profesional", "IdProfesional", "UsuarioProfesional", "Profesional");
        var (estadoFicha, deUsuario, aUsuario) = baja ? ("Inactivo", "Activo", "Inactivo") : ("Activo", "Inactivo", "Activo");

        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        int filas;
        using (var cmd = new SqlCommand($"UPDATE {tabla} SET Estado = @Estado WHERE {columna} = @Id", con, tx))
        {
            cmd.Parameters.AddWithValue("@Estado", estadoFicha);
            cmd.Parameters.AddWithValue("@Id", idFicha);
            filas = cmd.ExecuteNonQuery();
        }
        // Dar de alta algo que no existe es un 404 (como antes). La baja de un id inexistente sigue respondiendo 204 y dejando su registro.
        if (filas == 0 && !baja) { tx.Rollback(); return new ResultadoCambioFicha(false, Array.Empty<(int, string)>()); }

        var usuarios = new List<(int, string)>();
        using (var cmd = new SqlCommand($@"
            UPDATE Usuario SET Estado = @Nuevo
            OUTPUT inserted.IdUsuario, inserted.Username
            WHERE Estado = @Actual AND IdUsuario IN (SELECT IdUsuario FROM {vinculo} WHERE {columna} = @Id)", con, tx))
        {
            cmd.Parameters.AddWithValue("@Nuevo", aUsuario);
            cmd.Parameters.AddWithValue("@Actual", deUsuario);
            cmd.Parameters.AddWithValue("@Id", idFicha);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) usuarios.Add((rd.GetInt32(0), rd.GetString(1)));
        }

        AuditoriaAdmin.Insertar(con, tx, idAutor, (baja ? "Baja " : "Alta ") + nombre, nombre, idFicha,
            usuarios.Count == 0 ? null : $"{(baja ? "Se dio de baja" : "Se reactivó")} el usuario vinculado.", espacio);
        foreach (var (idUsuario, username) in usuarios)
            AuditoriaAdmin.Insertar(con, tx, idAutor, baja ? "Baja Usuario" : "Reactivación Usuario", "Usuario", idUsuario,
                $"{username}: por la {(baja ? "baja" : "alta")} de su ficha de {nombre.ToLowerInvariant()} #{idFicha}.", espacio);
        tx.Commit();
        return new ResultadoCambioFicha(filas > 0, usuarios);
    }
}
