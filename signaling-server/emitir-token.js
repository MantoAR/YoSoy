// Herramienta de desarrollo: emite un token de sala para probar a mano (por ejemplo, para pegar en la consola
// del navegador). En producción los tokens los emite la API; esto necesita el mismo SIGNALING_SECRETO.
//
//   npm run token -- --sala SALA-1-ABC --rol paciente --sub 3 [--minutos 60]
'use strict';

const { parseArgs } = require('util');
const { leerSecreto } = require('./config');
const { emitirToken, verificarToken, ROLES } = require('./token');

try {
    const { values } = parseArgs({
        options: {
            sala: { type: 'string' },
            rol: { type: 'string' },
            sub: { type: 'string', default: '1' },
            minutos: { type: 'string', default: '60' }
        }
    });
    if (!values.sala || !ROLES.includes(values.rol)) {
        throw new Error(`Uso: npm run token -- --sala <código> --rol <${ROLES.join('|')}> [--sub <id>] [--minutos <n>]`);
    }
    const minutos = Number(values.minutos);
    if (!Number.isFinite(minutos) || minutos <= 0) throw new Error('--minutos tiene que ser un número mayor que 0.');

    const secreto = leerSecreto();
    const token = emitirToken({ sala: values.sala, rol: values.rol, sub: values.sub, expiraEnSeg: Math.round(minutos * 60) }, secreto);
    try {
        verificarToken(token, secreto);
    } catch (e) {
        throw new Error(`Con esos valores el servidor rechazaría el token (${e.codigo}): revisá --sala y --sub.`);
    }
    console.log(token);
} catch (e) {
    console.error(e.message);
    process.exit(1);
}
