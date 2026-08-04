const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');

if (c.includes('/informes-admision')) {
    console.log('Los endpoints ya existen, no se hace nada.');
    process.exit(0);
}

const endpoints = `
// ── Informe de Admision ─────────────────────────────────────
app.MapPost("/informes-admision", (InformeAdmision i) =>
{
    var id = new InformeAdmisionDAO().Insertar(i);
    i.IdInforme = id;
    return Results.Created($"/informes-admision/{id}", i);
});

app.MapGet("/informes-admision", (string? estado) =>
    new InformeAdmisionDAO().ObtenerTodos(estado));

app.MapGet("/informes-admision/{id}", (int id) =>
{
    var i = new InformeAdmisionDAO().ObtenerPorId(id);
    return i is null ? Results.NotFound() : Results.Ok(i);
});

app.MapGet("/pacientes/{id}/informe-admision", (int id) =>
{
    var i = new InformeAdmisionDAO().ObtenerPorPaciente(id);
    return i is null ? Results.NotFound() : Results.Ok(i);
});

app.MapPut("/informes-admision/{id}/estado", (int id, string estado) =>
{
    new InformeAdmisionDAO().CambiarEstado(id, estado);
    return Results.NoContent();
});

`;

c = c.replace('app.Run();', endpoints + 'app.Run();');
fs.writeFileSync(path, c, 'utf8');
console.log('Endpoints de admision agregados OK');
console.log('Verificacion:', fs.readFileSync(path, 'utf8').includes('/informes-admision') ? 'presentes' : 'ERROR');