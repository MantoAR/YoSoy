var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

// ── Especialidad ──────────────────────────────────────────
app.MapGet("/especialidades", () =>
    new EspecialidadDAO().ObtenerTodos());

app.MapGet("/especialidades/{id}", (int id) =>
{
    var e = new EspecialidadDAO().ObtenerPorId(id);
    return e is null ? Results.NotFound() : Results.Ok(e);
});

app.MapPost("/especialidades", (Especialidad e) =>
{
    e.Estado = "Activo";
    new EspecialidadDAO().Insertar(e);
    return Results.Created($"/especialidades", e);
});

app.MapPut("/especialidades/{id}", (int id, Especialidad e) =>
{
    e.IdEspecialidad = id;
    new EspecialidadDAO().Modificar(e);
    return Results.Ok(e);
});

app.MapDelete("/especialidades/{id}", (int id) =>
{
    new EspecialidadDAO().Baja(id);
    return Results.NoContent();
});

// ── Rol ───────────────────────────────────────────────────
app.MapGet("/roles", () =>
    new RolDAO().ObtenerTodos());

app.MapGet("/roles/{id}", (int id) =>
{
    var r = new RolDAO().ObtenerPorId(id);
    return r is null ? Results.NotFound() : Results.Ok(r);
});

app.MapPost("/roles", (Rol r) =>
{
    r.Estado = "Activo";
    new RolDAO().Insertar(r);
    return Results.Created($"/roles", r);
});

app.MapPut("/roles/{id}", (int id, Rol r) =>
{
    r.IdRol = id;
    new RolDAO().Modificar(r);
    return Results.Ok(r);
});

app.MapDelete("/roles/{id}", (int id) =>
{
    new RolDAO().Baja(id);
    return Results.NoContent();
});

// ── EstadoTurno ───────────────────────────────────────────
app.MapGet("/estadosturnos", () =>
    new EstadoTurnoDAO().ObtenerTodos());

app.MapGet("/estadosturnos/{id}", (int id) =>
{
    var et = new EstadoTurnoDAO().ObtenerPorId(id);
    return et is null ? Results.NotFound() : Results.Ok(et);
});

app.MapPost("/estadosturnos", (EstadoTurno et) =>
{
    et.Estado = "Activo";
    new EstadoTurnoDAO().Insertar(et);
    return Results.Created($"/estadosturnos", et);
});

app.MapPut("/estadosturnos/{id}", (int id, EstadoTurno et) =>
{
    et.IdEstadoTurno = id;
    new EstadoTurnoDAO().Modificar(et);
    return Results.Ok(et);
});

app.MapDelete("/estadosturnos/{id}", (int id) =>
{
    new EstadoTurnoDAO().Baja(id);
    return Results.NoContent();
});

// ── TipoConsentimiento ────────────────────────────────────
app.MapGet("/tiposconsentimiento", () =>
    new TipoConsentimientoDAO().ObtenerTodos());

app.MapGet("/tiposconsentimiento/{id}", (int id) =>
{
    var tc = new TipoConsentimientoDAO().ObtenerPorId(id);
    return tc is null ? Results.NotFound() : Results.Ok(tc);
});

app.MapPost("/tiposconsentimiento", (TipoConsentimiento tc) =>
{
    tc.Estado = "Activo";
    new TipoConsentimientoDAO().Insertar(tc);
    return Results.Created($"/tiposconsentimiento", tc);
});

app.MapPut("/tiposconsentimiento/{id}", (int id, TipoConsentimiento tc) =>
{
    tc.IdTipoConsentimiento = id;
    new TipoConsentimientoDAO().Modificar(tc);
    return Results.Ok(tc);
});

app.MapDelete("/tiposconsentimiento/{id}", (int id) =>
{
    new TipoConsentimientoDAO().Baja(id);
    return Results.NoContent();
});

app.Run();
