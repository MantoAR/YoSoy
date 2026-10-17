// Sesión de Baileys en disco: la carpeta donde la librería guarda las credenciales de la cuenta de WhatsApp. ES UNA CREDENCIAL:
// quien tenga esos archivos puede usar la cuenta como si fuera el teléfono. Por eso:
//   - la ruta sale de una variable de entorno y se exige FUERA del repositorio y de la carpeta de la aplicación (si no, un
//     "git add" la subiría y cada despliegue la pisaría);
//   - la carpeta es 700 y los archivos 600: solo del usuario que corre el servicio;
//   - tiene que ser DEDICADA: si tiene otros archivos, la ruta está mal elegida; no se arranca y nunca se borra nada ajeno. La
//     única subcarpeta que se acepta es "datos/", que es de este servicio: los consentimientos de los pacientes y los contadores
//     de envío. Sobrevive a "desvincular" y a un cierre de sesión (el número y sus pacientes son los mismos) y no se borra sola;
//   - al arrancar se comprueba que se puede escribir: si no, las credenciales no se guardarían y habría que volver a vincular;
//   - creds.json tiene respaldo: la librería lo escribe sin cuidado (useMultiFileAuthState "no es para producción") y, si lo
//     encuentra cortado, lo da por inexistente y arma una identidad nueva EN SILENCIO: habría que volver a escanear el QR.
// Nada de este archivo escribe el contenido de las credenciales en el log.
"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { leerValido, escribirAtomico } = require("../archivos");

// Lo que Baileys escribe con useMultiFileAuthState: creds.json y un archivo .json por clave ("pre-key-12.json", "session-...").
// Más los respaldos y temporales de este módulo. Cualquier otra cosa en la carpeta significa que NO es una carpeta dedicada.
const ARCHIVO_DE_SESION = /\.(json|bak|tmp)$/i;
const CREDS = "creds.json";
const RESPALDO = "creds.json.bak";
const PRUEBA = "escritura-prueba.tmp";
const CARPETA_DE_DATOS = "datos";
const MODO_CARPETA = 0o700;
const MODO_ARCHIVO = 0o600;
const POSIX = process.platform !== "win32";
const VARIABLE = "WHATSAPP_BAILEYS_SESSION_PATH";

class ErrorDeSesion extends Error {
    constructor(mensaje) {
        super(mensaje);
        this.name = "ErrorDeSesion";
    }
}

// ¿"ruta" es "carpeta" o está adentro de ella?
function estaDentro(carpeta, ruta) {
    const rel = path.relative(carpeta, ruta);
    return rel === "" || (rel !== ".." && !rel.startsWith(`..${path.sep}`) && !path.isAbsolute(rel));
}

// Como fs.realpath, pero sirve aunque la ruta todavía no exista: resuelve la parte que existe (con sus symlinks) y le pega el resto.
async function rutaReal(ruta) {
    const faltantes = [];
    let actual = path.resolve(ruta);
    for (;;) {
        try {
            const real = await fs.promises.realpath(actual);
            return path.join(real, ...faltantes.reverse());
        } catch (err) {
            if (err.code !== "ENOENT" && err.code !== "ENOTDIR") throw err;
            const padre = path.dirname(actual);
            if (padre === actual) return path.resolve(ruta);
            faltantes.push(path.basename(actual));
            actual = padre;
        }
    }
}

// La raíz del repositorio git que contiene a la ruta (el primer ancestro con un ".git"), o null.
async function repositorioQueContiene(ruta) {
    let actual = ruta;
    for (;;) {
        try { await fs.promises.lstat(path.join(actual, ".git")); return actual; } catch { /* este no es la raíz de un repositorio */ }
        const padre = path.dirname(actual);
        if (padre === actual) return null;
        actual = padre;
    }
}

async function validarUbicacion(dir, appDir) {
    const real = await rutaReal(dir);
    const repo = await repositorioQueContiene(real);
    if (repo) {
        throw new ErrorDeSesion(
            `La carpeta de la sesión de WhatsApp (${dir}) está dentro de un repositorio git (${repo}). Es una credencial de la cuenta: ` +
            `tiene que estar FUERA del repositorio. Revisar ${VARIABLE}.`);
    }
    if (appDir) {
        const app = await rutaReal(appDir);
        if (estaDentro(app, real)) {
            throw new ErrorDeSesion(
                `La carpeta de la sesión de WhatsApp (${dir}) está dentro de la carpeta de la aplicación (${app}): cada despliegue la pisaría. ` +
                `Tiene que estar FUERA, al lado y no adentro (por ejemplo /opt/yosoy/whatsapp/sesion-baileys). Revisar ${VARIABLE}.`);
        }
        if (estaDentro(real, app)) {
            throw new ErrorDeSesion(
                `La carpeta de la sesión de WhatsApp (${dir}) contiene a la carpeta de la aplicación (${app}). Tiene que ser una carpeta ` +
                `propia, solo para la sesión. Revisar ${VARIABLE}.`);
        }
    }
}

// Separa lo que es de la sesión de lo que no. Un symlink o una subcarpeta (salvo "datos/") cuentan como ajenos: nunca se los sigue.
async function listar(dir) {
    const entradas = await fs.promises.readdir(dir, { withFileTypes: true });
    const esPropia = e => e.isFile() && ARCHIVO_DE_SESION.test(e.name);
    const esDatos = e => e.isDirectory() && e.name === CARPETA_DE_DATOS;
    return {
        propias: entradas.filter(esPropia).map(e => e.name),
        datos: entradas.some(esDatos),
        ajenas: entradas.filter(e => !esPropia(e) && !esDatos(e)).map(e => e.name)
    };
}

function errorPorAjenas(ajenas) {
    const ejemplos = ajenas.slice(0, 3).join(", ") + (ajenas.length > 3 ? ", ..." : "");
    return new ErrorDeSesion(
        `La carpeta de la sesión de WhatsApp tiene archivos que no son de la sesión (${ejemplos}): no se usa ni se borra nada. ` +
        `Tiene que ser una carpeta propia, solo para la sesión. Revisar ${VARIABLE}.`);
}

// Deja la carpeta en 700 y sus archivos en 600 (en POSIX; en Windows los permisos no funcionan así y no se toca nada).
async function endurecerPermisos(dir, log) {
    if (!POSIX) return;
    try {
        const info = await fs.promises.stat(dir);
        if ((info.mode & 0o777) !== MODO_CARPETA) {
            await fs.promises.chmod(dir, MODO_CARPETA);
            log?.info(`Permisos de la carpeta de la sesión de WhatsApp ajustados a 700 (eran ${(info.mode & 0o777).toString(8)}).`);
        }
        let corregidos = 0;
        for (const nombre of (await listar(dir)).propias) {
            const archivo = path.join(dir, nombre);
            if (((await fs.promises.stat(archivo)).mode & 0o777) !== MODO_ARCHIVO) {
                await fs.promises.chmod(archivo, MODO_ARCHIVO);
                corregidos++;
            }
        }
        const datos = path.join(dir, CARPETA_DE_DATOS);
        if ((await listar(dir)).datos) {
            if (((await fs.promises.stat(datos)).mode & 0o777) !== MODO_CARPETA) await fs.promises.chmod(datos, MODO_CARPETA);
            for (const nombre of (await listar(datos).catch(() => ({ propias: [] }))).propias) {
                const archivo = path.join(datos, nombre);
                if (((await fs.promises.stat(archivo)).mode & 0o777) !== MODO_ARCHIVO) {
                    await fs.promises.chmod(archivo, MODO_ARCHIVO);
                    corregidos++;
                }
            }
        }
        if (corregidos > 0) log?.info(`Permisos de ${corregidos} archivo(s) de la sesión de WhatsApp ajustados a 600.`);
    } catch (err) {
        throw new ErrorDeSesion(
            `No se pudieron ajustar los permisos de la carpeta de la sesión de WhatsApp (${err.code || err.message}). ` +
            "Tiene que ser del mismo usuario que corre el servicio.");
    }
}

// Si no se puede escribir, las credenciales no se guardarían (una vinculación exitosa se perdería al reiniciar).
async function probarEscritura(dir) {
    const archivo = path.join(dir, PRUEBA);
    try {
        await fs.promises.writeFile(archivo, "prueba", { mode: MODO_ARCHIVO });
        await fs.promises.unlink(archivo);
    } catch (err) {
        throw new ErrorDeSesion(
            `La carpeta de la sesión de WhatsApp no se puede escribir (${err.code || err.message}): el servicio no podría guardar las ` +
            "credenciales y habría que volver a vincular en cada reinicio. Revisar el dueño, los permisos y, con systemd, ReadWritePaths.");
    }
}

// ── Respaldo de creds.json ───────────────────────────────────────────────────────────

// Al abrir la sesión: si creds.json falta o está cortado y hay un respaldo válido, se vuelve a él. Sin esto, la librería arma
// una identidad nueva sin avisar y el número queda desvinculado.
async function recuperarCredenciales(dir, log) {
    const creds = path.join(dir, CREDS);
    if ((await leerValido(creds)) !== null) return false;
    const respaldo = await leerValido(path.join(dir, RESPALDO));
    if (respaldo === null) {
        if (fs.existsSync(creds)) log?.error("creds.json de la sesión de WhatsApp está dañado y no hay un respaldo válido: hará falta volver a vincular el número.");
        return false;
    }
    await escribirAtomico(creds, respaldo);
    log?.error("creds.json de la sesión de WhatsApp faltaba o estaba dañado: se restauró desde el respaldo.");
    return true;
}

// Envuelve el saveCreds de la librería: antes guarda el creds.json actual (si es válido) como respaldo y, si después de la
// escritura el archivo quedó inservible (disco lleno, corte), vuelve a ese respaldo. El error original, si lo hubo, se conserva.
// El respaldo es una red de seguridad: si no se puede escribir, se avisa y se guardan igual las credenciales nuevas.
async function guardarConRespaldo(dir, guardar, log) {
    const creds = path.join(dir, CREDS);
    const respaldo = path.join(dir, RESPALDO);

    const anterior = await leerValido(creds);
    if (anterior !== null) {
        try { await escribirAtomico(respaldo, anterior); }
        catch (err) { log?.error(`No se pudo actualizar el respaldo de las credenciales de WhatsApp (${err.code || err.message}); se guardan igual.`); }
    }

    let falla = null;
    try { await guardar(); } catch (err) { falla = err; }

    if ((await leerValido(creds)) === null) {
        const previo = await leerValido(respaldo);
        if (previo !== null) {
            try {
                await escribirAtomico(creds, previo);
                log?.error("creds.json de la sesión de WhatsApp quedó dañado al guardar: se restauró la versión anterior.");
            } catch (err) {
                log?.error(`creds.json de la sesión de WhatsApp quedó dañado al guardar y no se pudo restaurar el respaldo (${err.code || err.message}).`);
            }
        }
    }
    if (falla) throw falla;
}

// ── La sesión ────────────────────────────────────────────────────────────────────────

// appDir (opcional): la carpeta de la aplicación, para verificar que la sesión quede fuera de ella.
function crearSesion({ dir, log, appDir }) {
    return {
        dir,
        dirDatos: path.join(dir, CARPETA_DE_DATOS),

        // Al arrancar, antes de abrir el puerto y de cargar la librería: valida la ubicación, crea la carpeta (700), corrige
        // permisos, exige que sea dedicada y que se pueda escribir. Cualquier problema corta el arranque con un mensaje claro.
        async preparar() {
            await validarUbicacion(dir, appDir);
            // Todo lo que cree este proceso (incluidos los archivos que escribe la librería, con sus permisos por omisión)
            // queda solo para su usuario.
            if (POSIX) process.umask(0o077);
            const info = await fs.promises.stat(dir).catch(err => { if (err.code === "ENOENT") return null; throw err; });
            if (info && !info.isDirectory()) throw new ErrorDeSesion(`${VARIABLE} apunta a algo que no es una carpeta (${dir}).`);
            try { await fs.promises.mkdir(dir, { recursive: true, mode: MODO_CARPETA }); }
            catch (err) { throw new ErrorDeSesion(`No se pudo crear la carpeta de la sesión de WhatsApp (${dir}): ${err.code || err.message}.`); }
            // Primero se mira que sea una carpeta PROPIA y recién después se le tocan los permisos o se le crea algo: en una carpeta
            // mal elegida (como /opt), un chmod 700 le rompería el acceso a otros.
            const { ajenas } = await listar(dir);
            if (ajenas.length > 0) throw errorPorAjenas(ajenas);
            const rutaDatos = path.join(dir, CARPETA_DE_DATOS);
            try { await fs.promises.mkdir(rutaDatos, { recursive: true, mode: MODO_CARPETA }); }
            catch (err) { throw new ErrorDeSesion(`No se pudo crear la carpeta de datos del servicio (${rutaDatos}): ${err.code || err.message}.`); }
            const interior = await listar(rutaDatos);
            const ajenasDeDatos = [...interior.ajenas, ...(interior.datos ? [CARPETA_DE_DATOS] : [])];
            if (ajenasDeDatos.length > 0) throw errorPorAjenas(ajenasDeDatos);
            await endurecerPermisos(dir, log);
            await probarEscritura(dir);
            await probarEscritura(rutaDatos);
            log?.info(`Carpeta de la sesión de WhatsApp lista: ${dir}${POSIX ? " (carpeta 700, archivos 600)" : ""}.`);
        },

        // Abre (y crea si falta) la carpeta y devuelve { state, saveCreds } de la librería, con creds.json respaldado.
        async abrirAuth(fachada) {
            await fs.promises.mkdir(dir, { recursive: true, mode: MODO_CARPETA });
            await recuperarCredenciales(dir, log);
            const { state, saveCreds } = await fachada.useMultiFileAuthState(dir);
            return { state, saveCreds: () => guardarConRespaldo(dir, saveCreds, log) };
        },

        // Borra las credenciales (sesión cerrada o "desvincular"), con su respaldo. Nunca borra la carpeta en sí ni nada que no sea
        // de la sesión: si hay otros archivos, la ruta está mal elegida y se frena antes de borrar nada.
        async limpiar() {
            let propias, ajenas;
            try { ({ propias, ajenas } = await listar(dir)); }
            catch (err) { if (err.code === "ENOENT") return 0; throw err; }
            if (ajenas.length > 0) throw errorPorAjenas(ajenas);
            await Promise.all(propias.map(nombre => fs.promises.unlink(path.join(dir, nombre)).catch(err => { if (err.code !== "ENOENT") throw err; })));
            log?.info(`Sesión de WhatsApp borrada (${propias.length} archivos).`);
            return propias.length;
        }
    };
}

module.exports = { crearSesion, ErrorDeSesion, ARCHIVO_DE_SESION, CREDS, RESPALDO, CARPETA_DE_DATOS };
