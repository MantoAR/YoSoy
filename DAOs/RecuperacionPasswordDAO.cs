using Microsoft.Data.SqlClient;

// Recuperación de contraseña autoservicio: un token de un solo uso por pedido, guardado
// hasheado (nunca el valor crudo que se manda por mail — mismo criterio que
// Usuario.PasswordHash). Estado va pasando por 'Pendiente' (recién creado, sin usar) ->
// 'Usada' (ya se reseteó la contraseña con él) o 'Reemplazada' (se pidió uno nuevo antes
// de usar este). PasswordTemporal y FechaResolucion, de la aprobación manual que esto
// reemplazó, quedan sin usar — ver CAMBIOS_AGUS.md.
public class RecuperacionPasswordDAO
{
    // Único lugar que fija cuánto dura un token: Program.cs la usa para la expiración
    // real (CrearToken) y EmailService para el texto del mail — así no pueden volver a
    // quedar desalineados (pasó: el mail decía "1 hora" con un token de 30 minutos).
    public const int DuracionTokenMinutos = 30;

    // Un solo token vigente por usuario a la vez: el anterior (si había) queda
    // Reemplazada antes de crear el nuevo, para que un link viejo deje de servir solo.
    public void CrearToken(int idUsuario, string tokenHash, DateTime expiracionUtc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var invalidar = new SqlCommand(
            "UPDATE RecuperacionPassword SET Estado = 'Reemplazada' WHERE IdUsuario = @IdUsuario AND Estado = 'Pendiente'", con);
        invalidar.Parameters.AddWithValue("@IdUsuario", idUsuario);
        invalidar.ExecuteNonQuery();

        using var cmd = new SqlCommand(
            "INSERT INTO RecuperacionPassword (IdUsuario, Estado, Token, TokenExpiracion) VALUES (@IdUsuario, 'Pendiente', @Token, @Exp)", con);
        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
        cmd.Parameters.AddWithValue("@Token", tokenHash);
        cmd.Parameters.AddWithValue("@Exp", expiracionUtc);
        cmd.ExecuteNonQuery();
    }

    // Cuántos pedidos (de cualquier estado) hizo este usuario en la ventana — para el
    // límite de intentos por usuario de POST /recuperacion-password, independiente del
    // límite por IP (que ya pone el rate limiter de Program.cs).
    public int ContarRecientes(int idUsuario, TimeSpan ventana)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT COUNT(1) FROM RecuperacionPassword WHERE IdUsuario = @IdUsuario AND FechaSolicitud > @Desde", con);
        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
        cmd.Parameters.AddWithValue("@Desde", DateTime.Now - ventana);
        return (int)cmd.ExecuteScalar();
    }

    // Null si el hash no corresponde a ningún token Pendiente, o si ya venció — el
    // endpoint no distingue cuál de los dos fue, mismo mensaje genérico en los dos casos.
    public int? ObtenerIdUsuarioPorTokenVigente(string tokenHash)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario FROM RecuperacionPassword WHERE Token = @Token AND Estado = 'Pendiente' AND TokenExpiracion > @Ahora", con);
        cmd.Parameters.AddWithValue("@Token", tokenHash);
        cmd.Parameters.AddWithValue("@Ahora", DateTime.Now);
        var resultado = cmd.ExecuteScalar();
        return resultado == null ? null : (int)resultado;
    }

    public void MarcarUsado(string tokenHash)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE RecuperacionPassword SET Estado = 'Usada', FechaResolucion = GETDATE() WHERE Token = @Token", con);
        cmd.Parameters.AddWithValue("@Token", tokenHash);
        cmd.ExecuteNonQuery();
    }
}
