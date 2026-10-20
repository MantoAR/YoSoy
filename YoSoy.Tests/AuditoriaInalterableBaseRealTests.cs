using Microsoft.Data.SqlClient;

// Migración 19 (auditoría inalterable) contra una base real: el hash encadenado, el solo-agregar de LogAuditoria y de LogError,
// y que lo que ya hace la API (INSERT de siempre, sin columnas nuevas) sigue andando. Necesita las migraciones 18 y 19 aplicadas
// (la base de pruebas armada con docs/produccion/01-produccion.sql las trae).
[Trait("Categoria", "Integracion")]
public class AuditoriaInalterableBaseRealTests
{
    private static int Insertar(int idUsuario, string detalle)
    {
        // El mismo INSERT que hace LogAuditoriaDAO.Insertar: sin Categoria, Espacio ni hashes.
        new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = idUsuario, Accion = "Prueba de auditoría", Entidad = "Prueba", IdEntidad = 1, Detalle = detalle });
        return Convert.ToInt32(DatosDePrueba.Escalar("SELECT MAX(IdLog) FROM LogAuditoria WHERE Detalle = @d", ("@d", detalle)));
    }

    private static string Estado()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("EXEC sp_VerificarCadenaAuditoria", con);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return r.GetString(0);
    }

    [Fact]
    public void El_insert_de_siempre_sigue_andando_y_queda_encadenado_con_el_anterior()
    {
        using var d = new DatosDePrueba();
        var usuario = d.Usuario();
        var a = Insertar(usuario, d.Marca + "-A");
        var b = Insertar(usuario, d.Marca + "-B");

        // Entre A y B puede haber filas de otras pruebas: B apunta al Hash de la fila que tenga justo antes, y A tiene hash propio.
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE IdLog = @i AND Hash IS NOT NULL AND Categoria = 'Administrativa'", ("@i", a)));
        Assert.Equal(1, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM LogAuditoria x
            WHERE x.IdLog = @b AND x.HashAnterior = (SELECT TOP 1 Hash FROM LogAuditoria WHERE IdLog < @b AND Hash IS NOT NULL ORDER BY IdLog DESC)", ("@b", b)));
    }

    [Fact]
    public void El_hash_guardado_es_el_de_la_funcion_con_los_datos_de_la_fila()
    {
        using var d = new DatosDePrueba();
        var id = Insertar(d.Usuario(), d.Marca + "-H");
        Assert.Equal(1, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM LogAuditoria l
            WHERE l.IdLog = @i AND l.Hash = dbo.fn_HashAuditoria(l.HashAnterior, l.IdUsuario, l.Accion, l.Entidad, l.IdEntidad, l.Detalle, l.FechaHora, l.Categoria, l.Espacio)", ("@i", id)));
    }

    [Fact]
    public void No_se_puede_modificar_ni_borrar_la_auditoria()
    {
        using var d = new DatosDePrueba();
        var id = Insertar(d.Usuario(), d.Marca + "-M");

        var upd = Assert.Throws<SqlException>(() => DatosDePrueba.Ejecutar("UPDATE LogAuditoria SET Detalle = 'otra cosa' WHERE IdLog = @i", ("@i", id)));
        Assert.Equal(50018, upd.Number);
        var del = Assert.Throws<SqlException>(() => DatosDePrueba.Ejecutar("DELETE FROM LogAuditoria WHERE IdLog = @i", ("@i", id)));
        Assert.Equal(50018, del.Number);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE IdLog = @i AND Detalle LIKE @d", ("@i", id), ("@d", d.Marca + "-M")));
    }

    [Fact]
    public void Un_update_o_delete_que_no_toca_ninguna_fila_no_falla()
    {
        // El trigger solo reacciona si hay filas: las limpiezas de las pruebas que no encuentran nada no se rompen.
        DatosDePrueba.Ejecutar("DELETE FROM LogAuditoria WHERE IdLog = -1");
        DatosDePrueba.Ejecutar("UPDATE LogAuditoria SET Detalle = 'x' WHERE IdLog = -1");
    }

    [Fact]
    public void El_registro_de_errores_se_guarda_pero_no_se_modifica_ni_se_borra()
    {
        var codigo = "T" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant() + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        new DestinoErroresSql().Guardar(new ErrorRegistrado(codigo, DateTime.Now, "GET", "/prueba", 500, null, null, "Prueba", "mensaje", "detalle", "dev"));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogError WHERE Codigo = @c", ("@c", codigo)));

        Assert.Equal(50019, Assert.Throws<SqlException>(() => DatosDePrueba.Ejecutar("UPDATE LogError SET Mensaje = 'x' WHERE Codigo = @c", ("@c", codigo))).Number);
        Assert.Equal(50019, Assert.Throws<SqlException>(() => DatosDePrueba.Ejecutar("DELETE FROM LogError WHERE Codigo = @c", ("@c", codigo))).Number);
    }

    [Fact]
    public async Task Muchas_escrituras_a_la_vez_dejan_la_cadena_integra()
    {
        using var d = new DatosDePrueba();
        var usuario = d.Usuario();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => Insertar(usuario, $"{d.Marca}-P{i}"))));

        // Ningún Hash se repite como "anterior" de dos filas: nadie tomó el mismo antecesor.
        Assert.Equal(0, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM (SELECT HashAnterior FROM LogAuditoria WHERE HashAnterior IS NOT NULL
            GROUP BY HashAnterior HAVING COUNT(*) > 1) x"));
        Assert.Equal("Íntegra", Estado());
    }

    [Fact]
    public void La_verificacion_de_la_cadena_da_integra()
    {
        using var d = new DatosDePrueba();
        Insertar(d.Usuario(), d.Marca + "-V");
        Assert.Equal("Íntegra", Estado());
    }

    [Fact]
    public void Un_usuario_con_auditoria_no_se_puede_borrar_y_la_limpieza_de_pruebas_lo_deja_tal_cual()
    {
        var d = new DatosDePrueba();
        var usuario = d.Usuario();
        Insertar(usuario, d.Marca + "-U");

        d.Dispose();   // no tiene que lanzar ni imprimir fallos: retiene al usuario

        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Usuario WHERE IdUsuario = @u", ("@u", usuario)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE IdUsuario = @u", ("@u", usuario)));
    }
}
