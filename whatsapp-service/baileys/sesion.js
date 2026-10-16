// Sesión de Baileys en disco: la carpeta donde la librería guarda las credenciales de la cuenta de WhatsApp. ES UNA CREDENCIAL:
// quien tenga esos archivos puede usar la cuenta. Por eso (etapa 2) la ruta sale de una variable de entorno, queda fuera del
// repositorio y con permisos restrictivos.
"use strict";

const fs = require("node:fs");
const path = require("node:path");

// Lo que Baileys escribe con useMultiFileAuthState: creds.json y un archivo .json por clave ("pre-key-12.json", "session-...").
// Más los temporales/respaldos de este módulo. Cualquier otra cosa en la carpeta significa que NO es una carpeta dedicada.
const ARCHIVO_DE_SESION = /\.(json|bak|tmp)$/i;

function crearSesion({ dir, log }) {
    return {
        dir,

        // Abre (y crea si falta) la carpeta y devuelve { state, saveCreds } de la librería.
        async abrirAuth(fachada) {
            await fs.promises.mkdir(dir, { recursive: true, mode: 0o700 });
            return fachada.useMultiFileAuthState(dir);
        },

        // Borra las credenciales (sesión cerrada o "desvincular"). Nunca borra la carpeta en sí ni nada que no sea de la sesión:
        // si hay otros archivos, la ruta está mal elegida y se frena antes de borrar nada.
        async limpiar() {
            let entradas;
            try { entradas = await fs.promises.readdir(dir, { withFileTypes: true }); }
            catch (err) { if (err.code === "ENOENT") return 0; throw err; }
            const ajenas = entradas.filter(e => !e.isFile() || !ARCHIVO_DE_SESION.test(e.name));
            if (ajenas.length > 0) {
                throw new Error("La carpeta de la sesión tiene archivos que no son de la sesión de WhatsApp: no se borra nada. Revisar WHATSAPP_BAILEYS_SESSION_PATH.");
            }
            await Promise.all(entradas.map(e => fs.promises.unlink(path.join(dir, e.name))));
            if (log) log.info(`Sesión de WhatsApp borrada (${entradas.length} archivos).`);
            return entradas.length;
        }
    };
}

module.exports = { crearSesion, ARCHIVO_DE_SESION };
