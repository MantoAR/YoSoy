// Encuentra, en el código fuente del servicio, cada llamada que escribe un mensaje de log (log.info(...), this._log.error(...),
// decir(...)) y separa lo que es código de lo que es texto. Lo usa la guardia de privacidad (test/privacidad-logs.test.js) para
// mirar QUÉ variables entran en un mensaje sin confundirse con las palabras de los textos ("el texto del paciente nunca se loguea").
"use strict";

const PATRON_DE_LLAMADA = /(?:\b(?:this\._log|_log|log|logger)\??\.(?:info|warn|error|debug)|\bdecir)\??\(/g;

// f[i] es la comilla de apertura de un texto entre comillas simples o dobles: devuelve el índice siguiente al cierre.
function saltarTexto(f, i) {
    const comilla = f[i];
    i++;
    while (i < f.length && f[i] !== comilla) {
        if (f[i] === "\\") i++;
        i++;
    }
    return i + 1;
}

// f[i] es la comilla de apertura (`) de una plantilla: devuelve el código de sus ${...} y el índice siguiente al cierre.
function leerPlantilla(f, i) {
    let codigo = "";
    i++;
    while (i < f.length && f[i] !== "`") {
        if (f[i] === "\\") { i += 2; continue; }
        if (f[i] === "$" && f[i + 1] === "{") {
            i += 2;
            let profundidad = 1;
            let expresion = "";
            while (i < f.length && profundidad > 0) {
                const c = f[i];
                if (c === "`") { const r = leerPlantilla(f, i); expresion += ` ${r.codigo} `; i = r.fin; continue; }
                if (c === '"' || c === "'") { i = saltarTexto(f, i); continue; }
                if (c === "{") profundidad++;
                if (c === "}") { profundidad--; if (profundidad === 0) { i++; break; } }
                expresion += c;
                i++;
            }
            codigo += ` ${expresion} `;
            continue;
        }
        i++;
    }
    return { codigo, fin: i + 1 };
}

// Lee los argumentos de la llamada cuyo "(" está en `apertura`. Devuelve el índice del cierre y el código (sin los textos).
function leerArgumentos(f, apertura) {
    let i = apertura + 1;
    let profundidad = 1;
    let codigo = "";
    while (i < f.length && profundidad > 0) {
        const c = f[i];
        if (c === "`") { const r = leerPlantilla(f, i); codigo += ` ${r.codigo} `; i = r.fin; continue; }
        if (c === '"' || c === "'") { i = saltarTexto(f, i); continue; }
        if (c === "/" && f[i + 1] === "/") { while (i < f.length && f[i] !== "\n") i++; continue; }
        if (c === "/" && f[i + 1] === "*") { const fin = f.indexOf("*/", i + 2); i = fin < 0 ? f.length : fin + 2; continue; }
        if (c === "(") profundidad++;
        if (c === ")") { profundidad--; if (profundidad === 0) break; }
        codigo += c;
        i++;
    }
    return { fin: i + 1, codigo };
}

// [{ linea, texto, codigo }] de todas las llamadas de log del fuente.
function extraerLlamadasDeLog(fuente) {
    const llamadas = [];
    const patron = new RegExp(PATRON_DE_LLAMADA.source, "g");
    let m;
    while ((m = patron.exec(fuente)) !== null) {
        const apertura = m.index + m[0].length - 1;
        const { fin, codigo } = leerArgumentos(fuente, apertura);
        llamadas.push({ linea: fuente.slice(0, m.index).split("\n").length, texto: fuente.slice(m.index, fin), codigo });
        patron.lastIndex = fin;
    }
    return llamadas;
}

// Nombres que, si entran en un mensaje de log, son sospechosos: un teléfono, el texto de un mensaje, un identificador de
// WhatsApp, el QR, las credenciales o un secreto. Se buscan como palabras sueltas (item.telefono, this._qr) y en camelCase (ultimoQr).
const PALABRAS_SENSIBLES = "telefono|texto|jid|qr|secreto|creds|clave|huella|token|password|contrasena|from|pushname|perfil";
const SENSIBLE_SUELTA = new RegExp(`(?<![a-zA-Z])(?:${PALABRAS_SENSIBLES})(?![a-zA-Z])`, "i");
const SENSIBLE_EN_CAMELCASE = new RegExp(`(?<=[a-z])(?:${PALABRAS_SENSIBLES.split("|").map(p => p[0].toUpperCase() + p.slice(1)).join("|")})(?![a-z])`);

// Nombres que contienen una palabra de la lista sin ser lo que la lista teme: el CONTADOR de ventanas de QR no es el QR.
const INOFENSIVOS = /\b(?:this\.)?_?(?:max)?[vV]entanasQr\b/g;

function mencionaAlgoSensible(codigo) {
    const sinInofensivos = codigo.replace(INOFENSIVOS, " ");
    return SENSIBLE_SUELTA.test(sinInofensivos) || SENSIBLE_EN_CAMELCASE.test(sinInofensivos);
}

module.exports = { extraerLlamadasDeLog, mencionaAlgoSensible };
