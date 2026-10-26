using Microsoft.Data.SqlClient;
using Xunit;

// La misma decisión contra SQL Server (yosoy_test) con datos propios: el DAO lee las firmas, la edad y los tutores, y FirmarSiNoExiste respeta versión y firmante. Se OMITE si la
// base no tiene la migración 28.
public sealed class FactConBaseDeConsentimientosAttribute : FactAttribute
{
    public FactConBaseDeConsentimientosAttribute()
    {
        var motivo = BaseDeConsentimiento.NoDisponible();
        if (motivo is null && !Migracion28.Disponible()) motivo = "La base no tiene la migración migrations/add_consentimientos_circuito.sql.";
        if (motivo is not null) Skip = motivo;
    }
}

[Trait("Categoria", "Integracion")]
public class ConsentimientoTratamientoBaseRealTests
{
    private static readonly DateTime Hoy = new(2031, 3, 4);

    private static int Tutor(DatosDePrueba d, int idPaciente)
    {
        var t = Convert.ToInt32(DatosDePrueba.Escalar(
            "INSERT INTO Tutor (Nombre, Dni, Vinculo, Contacto) VALUES (@n, @d, N'Madre', N'x'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@n", "Tutor " + d.Marca), ("@d", d.Marca + "t")));
        d.AlLimpiar("Tutor", "IdTutor", t);
        DatosDePrueba.Ejecutar("INSERT INTO PacienteTutor (IdPaciente, IdTutor) VALUES (@p, @t)", ("@p", idPaciente), ("@t", t));
        return t;
    }

    private static void NacioEn(int idPaciente, DateTime fecha) =>
        DatosDePrueba.Ejecutar("UPDATE Paciente SET FechaNacimiento = @f WHERE IdPaciente = @p", ("@f", fecha), ("@p", idPaciente));

    [FactConBaseDeConsentimientos]
    public void Sin_firma_el_paciente_no_esta_vigente_y_con_firma_si()
    {
        using var d = new DatosDePrueba();
        var tipo = d.TipoConsentimiento();
        var desc = (string)DatosDePrueba.Escalar("SELECT Descripcion FROM TipoConsentimiento WHERE IdTipoConsentimiento = @t", ("@t", tipo))!;
        var paciente = d.Paciente();
        var dao = new ConsentimientoDAO();

        Assert.Equal(ConsentimientoTratamiento.SinFirma, dao.EstadoDeTratamiento(paciente, desc, "v1", Hoy).Motivo);
        Assert.Equal(1, dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy));
        Assert.True(dao.EstadoDeTratamiento(paciente, desc, "v1", Hoy).Vigente);
        // Firmar de nuevo la misma versión no duplica.
        Assert.Equal(0, dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Consentimiento WHERE IdPaciente = @p", ("@p", paciente)));
    }

    [FactConBaseDeConsentimientos]
    public void Cambiar_la_version_del_texto_pide_firmar_de_nuevo()
    {
        using var d = new DatosDePrueba();
        var tipo = d.TipoConsentimiento();
        var desc = (string)DatosDePrueba.Escalar("SELECT Descripcion FROM TipoConsentimiento WHERE IdTipoConsentimiento = @t", ("@t", tipo))!;
        var paciente = d.Paciente();
        var dao = new ConsentimientoDAO();
        dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy);

        var e = dao.EstadoDeTratamiento(paciente, desc, "v2", Hoy);
        Assert.False(e.Vigente);
        Assert.Equal(ConsentimientoTratamiento.VersionAnterior, e.Motivo);

        Assert.Equal(1, dao.FirmarSiNoExiste(paciente, desc, "v2", Hoy));
        Assert.True(dao.EstadoDeTratamiento(paciente, desc, "v2", Hoy).Vigente);
    }

    [FactConBaseDeConsentimientos]
    public void Una_persona_menor_necesita_la_firma_de_su_tutor()
    {
        using var d = new DatosDePrueba();
        var tipo = d.TipoConsentimiento();
        var desc = (string)DatosDePrueba.Escalar("SELECT Descripcion FROM TipoConsentimiento WHERE IdTipoConsentimiento = @t", ("@t", tipo))!;
        var paciente = d.Paciente();
        NacioEn(paciente, Hoy.AddYears(-12));
        var tutor = Tutor(d, paciente);
        var dao = new ConsentimientoDAO();

        // La firma propia del menor no alcanza.
        dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy);
        var e = dao.EstadoDeTratamiento(paciente, desc, "v1", Hoy);
        Assert.False(e.Vigente);
        Assert.True(e.EsMenor);
        Assert.Equal(ConsentimientoTratamiento.FaltaTutor, e.Motivo);
        Assert.Equal(new[] { tutor }, dao.TutoresDe(paciente).Select(t => t.IdTutor));

        // Firma el tutor: vigente, y queda dicho quién.
        Assert.Equal(1, dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy, tutor));
        e = dao.EstadoDeTratamiento(paciente, desc, "v1", Hoy);
        Assert.True(e.Vigente);
        Assert.Equal(tutor, e.IdTutorFirmante);
        // Repetirla no duplica.
        Assert.Equal(0, dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy, tutor));
    }

    [FactConBaseDeConsentimientos]
    public void Quien_cumple_18_ya_no_necesita_tutor()
    {
        using var d = new DatosDePrueba();
        var tipo = d.TipoConsentimiento();
        var desc = (string)DatosDePrueba.Escalar("SELECT Descripcion FROM TipoConsentimiento WHERE IdTipoConsentimiento = @t", ("@t", tipo))!;
        var paciente = d.Paciente();
        NacioEn(paciente, Hoy.AddYears(-20));
        var dao = new ConsentimientoDAO();
        dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy);
        var e = dao.EstadoDeTratamiento(paciente, desc, "v1", Hoy);
        Assert.True(e.Vigente);
        Assert.False(e.EsMenor);
    }

    [FactConBaseDeConsentimientos]
    public void RN02_y_la_pagina_de_firma_usan_la_misma_decision()
    {
        using var d = new DatosDePrueba();
        var tipo = d.TipoConsentimiento();
        var desc = (string)DatosDePrueba.Escalar("SELECT Descripcion FROM TipoConsentimiento WHERE IdTipoConsentimiento = @t", ("@t", tipo))!;
        var paciente = d.Paciente();
        var repo = new TurnoRepositorioDAO();
        var dao = new ConsentimientoDAO();

        dao.FirmarSiNoExiste(paciente, desc, "v1", Hoy);
        Assert.True(repo.TieneConsentimiento(paciente, desc, "v1"));
        // Con otra versión exigida ambos dicen que no.
        Assert.False(repo.TieneConsentimiento(paciente, desc, "v2"));
        Assert.False(dao.EstadoDeTratamiento(paciente, desc, "v2", DateTime.Today).Vigente);
    }
}
