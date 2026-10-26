using Xunit;

// La tabla AceptacionVideollamada (migración 28) con SQL Server de verdad (yosoy_test) y datos propios: se guarda quién, cuándo, la versión y el hash del texto; hay una sola aceptación
// vigente por sesión y paciente; revocar y volver a aceptar son filas distintas. Se OMITE si la base no tiene la migración 28.
[Trait("Categoria", "Integracion")]
public class AceptacionVideollamadaBaseRealTests
{
    [FactConBaseDeConsentimientos]
    public void Se_guarda_con_usuario_fecha_version_y_hash_y_solo_hay_una_vigente()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.Usuario();
        var dao = new AceptacionVideollamadaDAO();
        var texto = TextoAceptacionVideollamada.Actual();
        var ahora = new DateTime(2031, 3, 4, 10, 0, 5, DateTimeKind.Utc);

        Assert.Null(dao.Ultima("turno", 777001, paciente));
        var fila = dao.Insertar(usuario, paciente, "turno", 777001, texto.Version, texto.Hash, ahora);
        Assert.Equal((usuario, paciente, "turno", 777001, texto.Version), (fila.IdUsuario, fila.IdPaciente, fila.Origen, fila.IdOrigen, fila.VersionTexto));
        Assert.Equal(ahora, fila.DecididoUtc);
        Assert.True(fila.Vigente);
        Assert.Equal(64, ((string)DatosDePrueba.Escalar("SELECT HashTexto FROM AceptacionVideollamada WHERE IdAceptacion = @i", ("@i", fila.IdAceptacion))!).Trim().Length);

        // Un segundo pedido a la vez sobre la misma sesión no crea otra vigente: vale la que ganó.
        var otra = dao.Insertar(usuario, paciente, "turno", 777001, texto.Version, texto.Hash, ahora.AddSeconds(1));
        Assert.Equal(fila.IdAceptacion, otra.IdAceptacion);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM AceptacionVideollamada WHERE IdPaciente = @p", ("@p", paciente)));
    }

    [FactConBaseDeConsentimientos]
    public void Revocar_deja_constancia_y_se_puede_volver_a_aceptar()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.Usuario();
        var dao = new AceptacionVideollamadaDAO();
        var texto = TextoAceptacionVideollamada.Actual();

        var fila = dao.Insertar(usuario, paciente, "admision", 777002, texto.Version, texto.Hash, DateTime.UtcNow);
        Assert.True(dao.Revocar(fila.IdAceptacion, DateTime.UtcNow, usuario));
        Assert.False(dao.Revocar(fila.IdAceptacion, DateTime.UtcNow, usuario));   // ya estaba revocada
        var revocada = dao.Ultima("admision", 777002, paciente)!;
        Assert.False(revocada.Vigente);
        Assert.Equal(usuario, DatosDePrueba.Escalar("SELECT IdUsuarioRevoca FROM AceptacionVideollamada WHERE IdAceptacion = @i", ("@i", fila.IdAceptacion)));

        var nueva = dao.Insertar(usuario, paciente, "admision", 777002, texto.Version, texto.Hash, DateTime.UtcNow);
        Assert.NotEqual(fila.IdAceptacion, nueva.IdAceptacion);
        Assert.Equal(nueva.IdAceptacion, dao.Ultima("admision", 777002, paciente)!.IdAceptacion);
        Assert.Equal(2, DatosDePrueba.Contar("SELECT COUNT(*) FROM AceptacionVideollamada WHERE IdPaciente = @p", ("@p", paciente)));
    }

    [FactConBaseDeConsentimientos]
    public void La_base_rechaza_un_origen_que_no_es_turno_ni_admision()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var texto = TextoAceptacionVideollamada.Actual();
        Assert.ThrowsAny<Exception>(() => new AceptacionVideollamadaDAO().Insertar(d.Usuario(), paciente, "otro", 1, texto.Version, texto.Hash, DateTime.UtcNow));
    }
}
