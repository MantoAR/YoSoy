// Un archivo JSON de datos del servicio (consentimientos, contadores de envío) que se lee al arrancar y se guarda de forma atómica.
// Los guardados se serializan y se juntan: si llegan varios mientras uno escribe, se escribe una sola vez lo último, así un envío
// no espera a un disco lento y nunca hay dos escrituras pisándose.
//
// Si el archivo está dañado no se arranca "como si nada" en silencio: se avisa, se lo aparta (nombre.corrupto.bak) y se empieza vacío.
"use strict";

const fs = require("node:fs");
const { leerValido, escribirAtomico } = require("./archivos");

function crearAlmacenJson(ruta, { log, nombre = "datos" } = {}) {
    let ultimoTexto = null;       // lo último que hay que escribir (null = nada pendiente)
    let escribiendo = null;       // la promesa de la escritura en curso

    async function vaciar() {
        try {
            while (ultimoTexto !== null) {
                const texto = ultimoTexto;
                ultimoTexto = null;
                try { await escribirAtomico(ruta, texto); }
                catch (err) { log?.error(`No se pudo guardar ${nombre} (${err.code || err.message}).`); }
            }
        } finally {
            escribiendo = null;
        }
    }

    return {
        ruta,

        // El objeto guardado, o null si no hay (o si estaba dañado: en ese caso se aparta y se avisa).
        async leer() {
            const texto = await leerValido(ruta);
            if (texto !== null) return JSON.parse(texto);
            if (fs.existsSync(ruta)) {
                log?.error(`El archivo de ${nombre} estaba dañado: se lo aparta (${ruta}.corrupto.bak) y se empieza vacío.`);
                await fs.promises.rename(ruta, `${ruta}.corrupto.bak`).catch(() => {});
            }
            return null;
        },

        // Programa el guardado de este valor (se toma una copia en este momento). Devuelve la promesa de la escritura.
        guardar(valor) {
            ultimoTexto = JSON.stringify(valor);
            if (!escribiendo) escribiendo = vaciar();
            return escribiendo;
        },

        // Espera a que termine lo que esté escribiéndose (apagado ordenado).
        async cerrar() {
            while (escribiendo) await escribiendo;
        }
    };
}

module.exports = { crearAlmacenJson };
