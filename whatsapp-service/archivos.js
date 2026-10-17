// Escritura de archivos del servicio que no se puede cortar a la mitad: se escribe en un temporal, se baja a disco y se renombra, así
// el destino queda entero o como estaba, nunca a medias. Lo usan la sesión (baileys/sesion.js) y los datos del servicio (almacen.js).
"use strict";

const fs = require("node:fs");

const MODO_ARCHIVO = 0o600;      // archivos solo del usuario que corre el servicio

// El texto del archivo si es un objeto JSON válido; null si no existe, está vacío o quedó cortado.
async function leerValido(archivo) {
    let texto;
    try { texto = await fs.promises.readFile(archivo, "utf8"); } catch { return null; }
    try {
        const valor = JSON.parse(texto);
        return valor && typeof valor === "object" && !Array.isArray(valor) ? texto : null;
    } catch {
        return null;
    }
}

async function escribirAtomico(destino, texto) {
    const temporal = `${destino}.tmp`;
    try {
        const f = await fs.promises.open(temporal, "w", MODO_ARCHIVO);
        try { await f.writeFile(texto, "utf8"); await f.sync(); } finally { await f.close(); }
        await fs.promises.rename(temporal, destino);
    } catch (err) {
        await fs.promises.unlink(temporal).catch(() => {});
        throw err;
    }
}

module.exports = { leerValido, escribirAtomico, MODO_ARCHIVO };
