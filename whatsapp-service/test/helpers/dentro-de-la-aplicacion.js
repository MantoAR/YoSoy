// Rutas DENTRO de la carpeta de la aplicación (y del repositorio) para las pruebas que verifican que una carpeta de sesión ahí se
// RECHAZA. Si por un error del código se creara la carpeta, la prueba la borra al terminar (solo si no existía antes), para no
// dejar basura dentro del repositorio ni borrar nada que fuera de otra persona.
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const APLICACION = path.resolve(__dirname, "..", "..");

async function conRutaDentroDeLaAplicacion(nombre, cuerpo) {
    const ruta = path.join(APLICACION, nombre);
    const existia = fs.existsSync(ruta);
    try { await cuerpo(ruta); }
    finally { if (!existia) fs.rmSync(ruta, { recursive: true, force: true }); }
}

module.exports = { conRutaDentroDeLaAplicacion, APLICACION };
