using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

// POST /profesionales y PUT /profesionales/{id} devolvían 500 si la matrícula ya existía (UNIQUE) o la
// especialidad no existía (FK). RegistroProfesionalService los resuelve como 409 / 400 con mensajes
// neutrales. Estas pruebas no usan base de datos: el acceso a datos es un repositorio en memoria.
public class RegistroProfesionalServiceTests
{
    // ── Doble de prueba ──────────────────────────────────────────────────────────────────────

    private sealed class RepoEnMemoria : IProfesionalRegistroRepositorio
    {
        public HashSet<int> Especialidades { get; } = new() { 1, 2 };
        public List<Profesional> Profesionales { get; } = new();
        public int? NumeroSqlAlInsertar { get; set; }      // simula la carrera: el pre-chequeo pasó y la base igual rechaza
        public int? NumeroSqlAlModificar { get; set; }
        public int Insertados { get; private set; }
        public int Modificados { get; private set; }
        private int _siguienteId = 100;

        public bool ExisteEspecialidad(int idEspecialidad) => Especialidades.Contains(idEspecialidad);

        public bool ExisteMatricula(string matricula, int? excluirIdProfesional = null) =>
            Profesionales.Any(p => p.IdProfesional != excluirIdProfesional
                                   && string.Equals(p.Matricula, matricula, StringComparison.OrdinalIgnoreCase));

        public bool ExisteDni(string dniNormalizado, int? excluirIdProfesional = null) =>
            Profesionales.Any(p => p.IdProfesional != excluirIdProfesional && p.Dni != null
                                   && RegistroProfesionalService.NormalizarDni(p.Dni) == dniNormalizado);

        public Profesional? ObtenerPorId(int idProfesional) => Profesionales.FirstOrDefault(p => p.IdProfesional == idProfesional);

        public int Insertar(Profesional p)
        {
            if (NumeroSqlAlInsertar is { } n) throw SqlExceptionDePrueba.Crear(n);
            Insertados++;
            p.IdProfesional = _siguienteId++;
            Profesionales.Add(Copia(p));
            return p.IdProfesional;
        }

        public void Modificar(Profesional p)
        {
            if (NumeroSqlAlModificar is { } n) throw SqlExceptionDePrueba.Crear(n);
            Modificados++;
            Profesionales.RemoveAll(x => x.IdProfesional == p.IdProfesional);
            Profesionales.Add(Copia(p));
        }

        public Profesional Existente(string matricula, string? dni, int idEspecialidad = 1)
        {
            var p = new Profesional { Nombre = "Otra", Matricula = matricula, Dni = dni, IdEspecialidad = idEspecialidad, Estado = "Activo" };
            Insertar(p);
            return p;
        }

        private static Profesional Copia(Profesional p) => new()
        {
            IdProfesional = p.IdProfesional, Nombre = p.Nombre, Apellido = p.Apellido, Matricula = p.Matricula, Dni = p.Dni,
            IdEspecialidad = p.IdEspecialidad, Estado = p.Estado, Mail = p.Mail, Telefono = p.Telefono
        };
    }

    // SqlException no se puede construir con new: se arma por reflexión (campos privados), lo justo para que
    // Number devuelva el número de error. Si una versión nueva del paquete cambia los campos, falla la prueba
    // "El_doble_de_SqlException_..." de abajo y no las de negocio con un error confuso.
    private static class SqlExceptionDePrueba
    {
        public static SqlException Crear(int numero)
        {
            const BindingFlags campo = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var error = (SqlError)RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
            var campoNumero = typeof(SqlError).GetFields(campo)
                .First(f => f.FieldType == typeof(int) && f.Name.TrimStart('_').Equals("number", StringComparison.OrdinalIgnoreCase));
            campoNumero.SetValue(error, numero);

            var coleccion = (SqlErrorCollection)RuntimeHelpers.GetUninitializedObject(typeof(SqlErrorCollection));
            var campoLista = typeof(SqlErrorCollection).GetFields(campo)
                .First(f => typeof(System.Collections.IList).IsAssignableFrom(f.FieldType));
            var lista = (System.Collections.IList)Activator.CreateInstance(campoLista.FieldType)!;
            lista.Add(error);
            campoLista.SetValue(coleccion, lista);

            var ex = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
            var campoErrores = typeof(SqlException).GetFields(campo).First(f => f.FieldType == typeof(SqlErrorCollection));
            campoErrores.SetValue(ex, coleccion);
            return ex;
        }
    }

    [Theory]
    [InlineData(2627)]
    [InlineData(2601)]
    [InlineData(547)]
    public void El_doble_de_SqlException_devuelve_el_numero_de_error_pedido(int numero)
    {
        Assert.Equal(numero, SqlExceptionDePrueba.Crear(numero).Number);
    }

    private readonly RepoEnMemoria _repo = new();
    private RegistroProfesionalService Servicio => new(_repo);

    private static Profesional Solicitud(string matricula = "MP-1000", string? dni = "20123456", int idEspecialidad = 1) => new()
    {
        Nombre = "Ana", Apellido = "Paz", Matricula = matricula, Dni = dni, IdEspecialidad = idEspecialidad,
        Estado = "Pendiente", Mail = "ana@example.com", Telefono = "1100000000"
    };

    // ── Alta ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Alta_valida_se_crea_y_devuelve_el_id()
    {
        var r = Servicio.Registrar(Solicitud());
        Assert.True(r.Ok);
        Assert.Equal(200, r.StatusCode);
        Assert.True(r.IdProfesional >= 100);
        Assert.Equal(1, _repo.Insertados);
    }

    [Fact]
    public void Alta_con_matricula_repetida_es_409_y_no_inserta()
    {
        _repo.Existente("MP-1000", "30111222");
        var r = Servicio.Registrar(Solicitud("MP-1000", "20123456"));

        Assert.False(r.Ok);
        Assert.Equal(EstadoRegistroProfesional.Duplicado, r.Estado);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal(RegistroProfesionalService.MensajeDuplicadoAlta, r.Mensaje);
        Assert.Equal(1, _repo.Insertados);                  // solo el existente
    }

    [Fact]
    public void La_matricula_se_compara_sin_distinguir_mayusculas_ni_espacios_de_los_costados()
    {
        _repo.Existente("MP-1000", null);
        var r = Servicio.Registrar(Solicitud("  mp-1000 ", "20123456"));
        Assert.Equal(409, r.StatusCode);
    }

    [Theory]
    [InlineData("20123456", "20123456")]
    [InlineData("20.123.456", "20123456")]      // con puntos
    [InlineData("20 123 456", "20123456")]      // con espacios
    [InlineData("20-123-456", "20123456")]      // con guiones
    [InlineData("20123456", "20.123.456")]      // y al revés
    public void Alta_con_dni_repetido_es_409_aunque_cambie_el_formato(string existente, string nuevo)
    {
        _repo.Existente("MP-0001", existente);
        var r = Servicio.Registrar(Solicitud("MP-2000", nuevo));

        Assert.Equal(EstadoRegistroProfesional.Duplicado, r.Estado);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal(1, _repo.Insertados);
    }

    [Fact]
    public void El_mensaje_es_el_mismo_para_matricula_y_para_dni_y_no_dice_cual_coincidio()
    {
        _repo.Existente("MP-1000", "30111222");

        var porMatricula = Servicio.Registrar(Solicitud("MP-1000", "99999999"));
        var porDni       = Servicio.Registrar(Solicitud("MP-9999", "30111222"));

        Assert.Equal(409, porMatricula.StatusCode);
        Assert.Equal(409, porDni.StatusCode);
        Assert.Equal(porMatricula.Mensaje, porDni.Mensaje);
        Assert.DoesNotContain("MP-1000", porMatricula.Mensaje);
        Assert.DoesNotContain("30111222", porDni.Mensaje);
        Assert.DoesNotContain("Otra", porMatricula.Mensaje);            // ni el nombre de quien ya estaba
    }

    [Fact]
    public void Alta_sin_dni_no_chequea_dni_y_no_choca_con_los_que_tampoco_tienen()
    {
        _repo.Existente("MP-1000", null);
        var r = Servicio.Registrar(Solicitud("MP-2000", dni: null));
        Assert.True(r.Ok);

        var vacio = Servicio.Registrar(Solicitud("MP-3000", dni: "   "));
        Assert.True(vacio.Ok);
    }

    [Fact]
    public void Alta_con_especialidad_inexistente_es_400_y_no_inserta()
    {
        var r = Servicio.Registrar(Solicitud(idEspecialidad: 999));

        Assert.Equal(EstadoRegistroProfesional.EspecialidadInexistente, r.Estado);
        Assert.Equal(400, r.StatusCode);
        Assert.Equal("La especialidad elegida no existe.", r.Mensaje);
        Assert.Equal(0, _repo.Insertados);
    }

    [Theory]
    [InlineData(null, "MP-1")]
    [InlineData("  ", "MP-1")]
    [InlineData("Ana", null)]
    [InlineData("Ana", "   ")]
    public void Alta_sin_nombre_o_sin_matricula_es_400(string? nombre, string? matricula)
    {
        var p = Solicitud();
        p.Nombre = nombre!;
        p.Matricula = matricula!;

        var r = Servicio.Registrar(p);

        Assert.Equal(EstadoRegistroProfesional.DatosInvalidos, r.Estado);
        Assert.Equal(400, r.StatusCode);
        Assert.Equal(0, _repo.Insertados);
    }

    [Fact]
    public void Alta_recorta_los_espacios_de_nombre_matricula_y_dni_antes_de_guardar()
    {
        var p = Solicitud("  MP-77  ", "  20123456 ");
        p.Nombre = "  Ana ";

        var r = Servicio.Registrar(p);

        Assert.True(r.Ok);
        var guardado = _repo.Profesionales.Single();
        Assert.Equal("MP-77", guardado.Matricula);
        Assert.Equal("20123456", guardado.Dni);
        Assert.Equal("Ana", guardado.Nombre);
    }

    // ── La carrera: el chequeo previo pasó y la base igual rechaza ───────────────────────────

    [Theory]
    [InlineData(2627)]
    [InlineData(2601)]
    public void Si_la_base_rechaza_por_unico_en_el_alta_es_409(int numero)
    {
        _repo.NumeroSqlAlInsertar = numero;
        var r = Servicio.Registrar(Solicitud());

        Assert.Equal(EstadoRegistroProfesional.Duplicado, r.Estado);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal(RegistroProfesionalService.MensajeDuplicadoAlta, r.Mensaje);
    }

    [Fact]
    public void Si_la_base_rechaza_por_clave_foranea_en_el_alta_es_400_de_especialidad()
    {
        _repo.NumeroSqlAlInsertar = 547;
        var r = Servicio.Registrar(Solicitud());

        Assert.Equal(EstadoRegistroProfesional.EspecialidadInexistente, r.Estado);
        Assert.Equal(400, r.StatusCode);
        Assert.Equal("La especialidad elegida no existe.", r.Mensaje);
    }

    [Fact]
    public void Un_error_de_sql_que_no_es_de_negocio_no_se_traga_y_sigue_siendo_500()
    {
        _repo.NumeroSqlAlInsertar = 1205;      // deadlock: no es duplicado ni clave foránea
        var ex = Assert.Throws<SqlException>(() => Servicio.Registrar(Solicitud()));
        Assert.Equal(1205, ex.Number);
    }

    // ── Edición ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Edicion_que_no_cambia_matricula_ni_dni_se_guarda()
    {
        var yo = _repo.Existente("MP-1000", "20123456");
        var cambios = Solicitud("MP-1000", "20123456");
        cambios.Telefono = "1155555555";

        var r = Servicio.Modificar(yo.IdProfesional, cambios);

        Assert.True(r.Ok);
        Assert.Equal(1, _repo.Modificados);
        Assert.Equal(yo.IdProfesional, cambios.IdProfesional);          // el id sale de la ruta, no del cuerpo
    }

    [Fact]
    public void Edicion_a_la_matricula_de_otro_profesional_es_409_y_no_modifica()
    {
        var yo = _repo.Existente("MP-1000", "20123456");
        _repo.Existente("MP-2000", "30111222");

        var r = Servicio.Modificar(yo.IdProfesional, Solicitud("MP-2000", "20123456"));

        Assert.Equal(EstadoRegistroProfesional.Duplicado, r.Estado);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal(RegistroProfesionalService.MensajeDuplicadoEdicion, r.Mensaje);
        Assert.Equal(0, _repo.Modificados);
    }

    [Fact]
    public void Edicion_al_dni_de_otro_profesional_es_409()
    {
        var yo = _repo.Existente("MP-1000", "20123456");
        _repo.Existente("MP-2000", "30111222");

        var r = Servicio.Modificar(yo.IdProfesional, Solicitud("MP-1000", "30.111.222"));

        Assert.Equal(409, r.StatusCode);
        Assert.Equal(0, _repo.Modificados);
    }

    [Fact]
    public void Una_ficha_con_dni_repetido_de_antes_puede_guardar_su_perfil_sin_tocar_el_dni()
    {
        // La base admite dos profesionales con el mismo DNI (Dni no tiene UNIQUE). Si no cambia el DNI, no se
        // le puede bloquear el guardado del perfil por algo que ya estaba así.
        var yo = _repo.Existente("MP-1000", "20123456");
        _repo.Existente("MP-2000", "20123456");

        var cambios = Solicitud("MP-1000", "20123456");
        cambios.Telefono = "1166666666";
        var r = Servicio.Modificar(yo.IdProfesional, cambios);

        Assert.True(r.Ok);
        Assert.Equal(1, _repo.Modificados);
    }

    [Fact]
    public void Edicion_con_especialidad_inexistente_es_400()
    {
        var yo = _repo.Existente("MP-1000", "20123456");
        var r = Servicio.Modificar(yo.IdProfesional, Solicitud("MP-1000", "20123456", idEspecialidad: 999));

        Assert.Equal(400, r.StatusCode);
        Assert.Equal("La especialidad elegida no existe.", r.Mensaje);
        Assert.Equal(0, _repo.Modificados);
    }

    [Theory]
    [InlineData(2627, 409)]
    [InlineData(2601, 409)]
    [InlineData(547, 400)]
    public void Si_la_base_rechaza_la_edicion_se_traduce_igual(int numero, int esperado)
    {
        var yo = _repo.Existente("MP-1000", "20123456");
        _repo.NumeroSqlAlModificar = numero;

        var r = Servicio.Modificar(yo.IdProfesional, Solicitud("MP-1000", "20123456"));

        Assert.Equal(esperado, r.StatusCode);
    }
}
