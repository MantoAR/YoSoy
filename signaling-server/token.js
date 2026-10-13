// Tokens de sala: verificación (lo que hace el servidor) y emisión (solo para pruebas y la herramienta de
// desarrollo emitir-token.js; en producción los emite la API). El contrato está en README.md, "Token de sala".
'use strict';

const jwt = require('jsonwebtoken');

const ROLES = Object.freeze(['profesional', 'paciente']);
// Juegos de caracteres acotados a propósito: sala y sub terminan en los logs y en claves de un Map.
const RE_SALA = /^[A-Za-z0-9_.:-]{1,100}$/;
const RE_SUB = /^[A-Za-z0-9_.:-]{1,64}$/;
const MAX_TOKEN = 4096;

class ErrorToken extends Error {
    constructor(codigo, detalle) {
        super(detalle || codigo);
        this.name = 'ErrorToken';
        // 'token-ausente' | 'token-invalido' | 'token-vencido' | 'claims-invalidos'
        this.codigo = codigo;
    }
}

/**
 * Verifica un token de sala y devuelve { sala, rol, sub, exp }. Tira ErrorToken si no sirve.
 *  - Solo HS256: con "algorithms" fijado, jsonwebtoken rechaza alg:none y cualquier otro algoritmo.
 *  - La firma se comprueba antes que el vencimiento, así que "vencido" solo se le dice a quien tiene un token
 *    bien firmado.
 */
function verificarToken(token, secreto) {
    if (typeof token !== 'string' || token.length === 0 || token.length > MAX_TOKEN) {
        throw new ErrorToken('token-ausente');
    }
    let claims;
    try {
        claims = jwt.verify(token, secreto, { algorithms: ['HS256'] });
    } catch (e) {
        throw new ErrorToken(e && e.name === 'TokenExpiredError' ? 'token-vencido' : 'token-invalido', e && e.name);
    }
    if (!claims || typeof claims !== 'object') throw new ErrorToken('token-invalido', 'payload');
    // jsonwebtoken solo revisa "exp" si viene. Un token sin vencimiento valdría para siempre.
    if (typeof claims.exp !== 'number' || !Number.isFinite(claims.exp)) throw new ErrorToken('token-invalido', 'sin exp');

    const sub = typeof claims.sub === 'number' && Number.isSafeInteger(claims.sub) ? String(claims.sub) : claims.sub;
    if (!ROLES.includes(claims.rol)
        || typeof claims.sala !== 'string' || !RE_SALA.test(claims.sala)
        || typeof sub !== 'string' || !RE_SUB.test(sub)) {
        throw new ErrorToken('claims-invalidos');
    }
    return { sala: claims.sala, rol: claims.rol, sub, exp: claims.exp };
}

/** Emite un token válido para el contrato. Solo para pruebas y desarrollo. */
function emitirToken({ sala, rol, sub, expiraEnSeg = 3600 }, secreto) {
    return jwt.sign({ sala, rol }, secreto, { algorithm: 'HS256', subject: String(sub), expiresIn: expiraEnSeg });
}

module.exports = { verificarToken, emitirToken, ErrorToken, ROLES };
