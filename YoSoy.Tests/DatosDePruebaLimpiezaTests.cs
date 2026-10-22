[Trait("Categoria", "Integracion")]
public class DatosDePruebaLimpiezaTests
{
    private static int UsuarioConPermisos(DatosDePrueba datos)
    {
        var id = datos.Usuario();
        DatosDePrueba.Ejecutar(@"
            INSERT INTO UsuarioRol (IdUsuario, IdRol)
                SELECT @u, IdRol FROM Permiso WHERE Clave IN ('configurador', 'crm');
            INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso)
                SELECT @u, IdRol, IdPermiso FROM Permiso WHERE Clave IN ('configurador', 'crm');", ("@u", id));
        return id;
    }

    [Fact]
    public void Limpiar_un_usuario_respeta_los_permisos_de_otro_con_los_mismos_roles()
    {
        using var ajenos = new DatosDePrueba();
        var otro = UsuarioConPermisos(ajenos);
        using (var propios = new DatosDePrueba())
            UsuarioConPermisos(propios);

        Assert.Equal(2, DatosDePrueba.Contar("SELECT COUNT(*) FROM UsuarioPermiso WHERE IdUsuario = @u", ("@u", otro)));
        Assert.Equal(2, DatosDePrueba.Contar("SELECT COUNT(*) FROM UsuarioRol WHERE IdUsuario = @u", ("@u", otro)));
    }
}
