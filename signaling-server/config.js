// Configuración del servidor de señalización, leída de variables de entorno.
//
// Si falta algo o es inseguro, leerConfig tira un Error con un mensaje claro y el proceso no arranca.
// Acá no hay valores por defecto para lo que importa (el secreto), ni modos que "dejen pasar" a alguien.
'use strict';

const SECRETO_MIN = 32;

/** Valida SIGNALING_SECRETO y lo devuelve. Los mensajes nunca incluyen su valor. */
function leerSecreto(env = process.env) {
    const secreto = env.SIGNALING_SECRETO;
    if (!secreto) {
        throw new Error('Falta SIGNALING_SECRETO: es el secreto con el que la API firma los tokens de sala y no tiene '
            + 'valor por defecto. Generá uno con "openssl rand -hex 32".');
    }
    if (secreto.length < SECRETO_MIN) {
        throw new Error(`SIGNALING_SECRETO es demasiado corto (${secreto.length} caracteres; mínimo ${SECRETO_MIN}). `
            + 'Generá uno con "openssl rand -hex 32".');
    }
    // El secreto de la API no puede ser también el de la señalización: este proceso no debe poder falsificar un login.
    if (env.Jwt__Key && env.Jwt__Key === secreto) {
        throw new Error('SIGNALING_SECRETO tiene el mismo valor que Jwt__Key de la API: tienen que ser secretos distintos.');
    }
    return secreto;
}

function entero(env, nombre, porDefecto, min, max) {
    const crudo = env[nombre];
    if (crudo === undefined || crudo === '') return porDefecto;
    const n = /^\d+$/.test(String(crudo).trim()) ? Number(crudo) : NaN;
    if (!Number.isInteger(n) || n < min || n > max) {
        throw new Error(`${nombre} tiene que ser un entero entre ${min} y ${max} (llegó "${crudo}").`);
    }
    return n;
}

/** Lista de orígenes separados por coma. Vacía por defecto; "*" no se admite. */
function origenes(crudo) {
    if (!crudo || !crudo.trim()) return [];
    return crudo.split(',').map(s => s.trim()).filter(Boolean).map(origen => {
        if (origen === '*') {
            throw new Error('SIGNALING_ORIGENES_CORS no admite "*": listá los orígenes uno por uno.');
        }
        let url;
        try { url = new URL(origen); } catch { url = null; }
        if (!url || !/^https?:$/.test(url.protocol) || url.origin !== origen) {
            throw new Error(`SIGNALING_ORIGENES_CORS: "${origen}" no es un origen válido `
                + '(esquema, host y puerto, sin barra final; por ejemplo https://yosoytelemedicina.tech).');
        }
        return url.origin;
    });
}

function leerConfig(env = process.env) {
    return Object.freeze({
        secreto: leerSecreto(env),
        // Solo loopback por defecto: el único camino desde internet es Caddy, en la misma máquina.
        host: (env.HOST || '').trim() || '127.0.0.1',
        puerto: entero(env, 'PORT', 3001, 0, 65535),
        origenesCors: origenes(env.SIGNALING_ORIGENES_CORS),
        maxPayloadBytes: entero(env, 'SIGNALING_MAX_PAYLOAD_BYTES', 64 * 1024, 1024, 1024 * 1024),
        maxHandshakesPorMin: entero(env, 'SIGNALING_MAX_HANDSHAKES_POR_MIN', 60, 1, 100000),
        maxEventosPor10s: entero(env, 'SIGNALING_MAX_EVENTOS_POR_10S', 100, 1, 100000)
    });
}

module.exports = { leerConfig, leerSecreto, SECRETO_MIN };
