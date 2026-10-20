// La carpeta de la sesión de Baileys (baileys/sesion.js): es una credencial de la cuenta de WhatsApp. Se prueba que quede fuera del
// repositorio y de la aplicación, con permisos de dueño solo, que sea dedicada, que se pueda escribir y que creds.json tenga
// respaldo. Todo en carpetas temporales; la "librería" es un doble que escribe como la real (sin cuidado), sin red ni WhatsApp.
require("./helpers/sin-red");
const test = require("node:test");
const { mock } = test;
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { crearSesion, ErrorDeSesion, CREDS, RESPALDO } = require("../baileys/sesion");
const { conRutaDentroDeLaAplicacion, APLICACION } = require("./helpers/dentro-de-la-aplicacion");

const POSIX = process.platform !== "win32";
const SOLO_POSIX = !POSIX && "los permisos de POSIX no existen en Windows";
const ES_ROOT = typeof process.getuid === "function" && process.getuid() === 0;
const RAIZ_REPO = path.resolve(__dirname, "..", "..");

const modo = ruta => fs.statSync(ruta).mode & 0o777;
const leerUmask = () => { const actual = process.umask(0); process.umask(actual); return actual; };

// Un directorio temporal propio para cada prueba (fuera del repo), con su registro de log; restaura el umask del proceso.
function conRaiz(cuerpo) {
    return async () => {
        const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-sesion-"));
        const lineas = [];
        const log = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
        const umaskInicial = leerUmask();
        try { await cuerpo(raiz, { log, lineas, texto: () => lineas.join("\n") }); }
        finally {
            process.umask(umaskInicial);
            fs.rmSync(raiz, { recursive: true, force: true });
        }
    };
}

// Hace lo mismo que useMultiFileAuthState en lo que importa acá: si creds.json no es JSON válido arma una identidad nueva EN
// SILENCIO, y guarda con un writeFile común (sin cuidado). `alGuardar` reemplaza la escritura para simular fallas.
function libreriaFalsa({ alGuardar } = {}) {
    return {
        async useMultiFileAuthState(dir) {
            const archivo = path.join(dir, CREDS);
            let creds;
            try { creds = JSON.parse(await fs.promises.readFile(archivo, "utf8")); } catch { creds = { registered: false, identidadNueva: true }; }
            return {
                state: { creds },
                saveCreds: async () => {
                    if (alGuardar) await alGuardar(archivo);
                    else await fs.promises.writeFile(archivo, JSON.stringify(creds));
                }
            };
        }
    };
}

const V1 = JSON.stringify({ registered: true, version: 1, me: { id: "5491100000000:7@s.whatsapp.net" } });
const V2 = JSON.stringify({ registered: true, version: 2, me: { id: "5491100000000:7@s.whatsapp.net" } });

// ---------- Ubicación: fuera del repositorio y de la aplicación ----------

test("preparar rechaza una carpeta dentro de un repositorio git y no crea nada", conRaiz(async raiz => {
    fs.mkdirSync(path.join(raiz, "repo", ".git"), { recursive: true });
    const dir = path.join(raiz, "repo", "datos", "sesion");
    await assert.rejects(() => crearSesion({ dir }).preparar(), err => err instanceof ErrorDeSesion && /dentro de un repositorio git/.test(err.message));
    assert.equal(fs.existsSync(path.join(raiz, "repo", "datos")), false);
}));

test("un repositorio con .git como archivo (submódulo, worktree) también cuenta", conRaiz(async raiz => {
    fs.mkdirSync(path.join(raiz, "repo"));
    fs.writeFileSync(path.join(raiz, "repo", ".git"), "gitdir: ../otro/.git\n");
    await assert.rejects(() => crearSesion({ dir: path.join(raiz, "repo", "sesion") }).preparar(), /dentro de un repositorio git/);
}));

test("la carpeta de la sesión no puede ser la raíz de un repositorio", conRaiz(async raiz => {
    fs.mkdirSync(path.join(raiz, "repo", ".git"), { recursive: true });
    await assert.rejects(() => crearSesion({ dir: path.join(raiz, "repo") }).preparar(), /dentro de un repositorio git/);
}));

test("preparar rechaza una carpeta dentro de la aplicación (cada despliegue la pisaría) o que la contenga", conRaiz(async raiz => {
    const app = path.join(raiz, "app");
    fs.mkdirSync(app);
    await assert.rejects(() => crearSesion({ dir: path.join(app, "sesion"), appDir: app }).preparar(), /dentro de la carpeta de la aplicación/);
    await assert.rejects(() => crearSesion({ dir: path.join(app, "..sesion"), appDir: app }).preparar(), /dentro de la carpeta de la aplicación/);
    await assert.rejects(() => crearSesion({ dir: app, appDir: app }).preparar(), /dentro de la carpeta de la aplicación/);
    await assert.rejects(() => crearSesion({ dir: raiz, appDir: app }).preparar(), /contiene a la carpeta de la aplicación/);
    assert.equal(fs.existsSync(path.join(app, "sesion")), false);
}));

test("una carpeta vecina de la aplicación con nombre parecido NO cuenta como adentro", conRaiz(async raiz => {
    const app = path.join(raiz, "app");
    fs.mkdirSync(app);
    await crearSesion({ dir: path.join(raiz, "app-sesion"), appDir: app }).preparar();
    await crearSesion({ dir: path.join(raiz, "..app"), appDir: app }).preparar();
}));

test("un enlace simbólico hacia adentro de la aplicación no sirve para esquivar la regla",
    { skip: SOLO_POSIX }, conRaiz(async raiz => {
        const app = path.join(raiz, "app");
        fs.mkdirSync(app);
        fs.symlinkSync(app, path.join(raiz, "enlace"), "dir");
        await assert.rejects(() => crearSesion({ dir: path.join(raiz, "enlace", "sesion"), appDir: app }).preparar(), /dentro de la carpeta de la aplicación/);
    }));

test("la ubicación del propio repositorio de YoSoy no sirve como carpeta de sesión", () =>
    // Es lo que pasaría si WHATSAPP_BAILEYS_SESSION_PATH apuntara al repo.
    conRutaDentroDeLaAplicacion("sesion-prueba-que-no-tiene-que-existir", async adentro => {
        await assert.rejects(() => crearSesion({ dir: adentro, appDir: APLICACION }).preparar(), /dentro de/);
        assert.equal(fs.existsSync(adentro), false, "no se creó nada en el repositorio");
    }));

// ---------- Carpeta dedicada ----------

test("preparar rechaza una carpeta con archivos que no son de la sesión, y no cambia nada", conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, "app.js"), "console.log(1)");
    fs.mkdirSync(path.join(dir, "subcarpeta"));
    await assert.rejects(() => crearSesion({ dir }).preparar(), err => /no son de la sesión/.test(err.message) && /app\.js/.test(err.message) && /WHATSAPP_BAILEYS_SESSION_PATH/.test(err.message));
    assert.deepEqual(fs.readdirSync(dir).sort(), ["app.js", "subcarpeta"]);
}));

test("una subcarpeta con nombre de archivo de sesión sigue siendo ajena: ni se acepta ni se borra", conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(path.join(dir, "creds.json"), { recursive: true });
    await assert.rejects(() => crearSesion({ dir }).preparar(), /no son de la sesión/);
    await assert.rejects(() => crearSesion({ dir }).limpiar(), /no son de la sesión/);
    assert.ok(fs.statSync(path.join(dir, "creds.json")).isDirectory());
}));

test("preparar rechaza una ruta que es un archivo", conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    fs.writeFileSync(dir, "no soy una carpeta");
    await assert.rejects(() => crearSesion({ dir }).preparar(), /no es una carpeta/);
}));

test("preparar acepta una carpeta con solo archivos de la sesión, y se puede repetir", conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    fs.writeFileSync(path.join(dir, "pre-key-12.json"), "{}");
    fs.writeFileSync(path.join(dir, "session-5491122334455.0.json"), "{}");
    const sesion = crearSesion({ dir });
    await sesion.preparar();
    await sesion.preparar();
    assert.deepEqual(fs.readdirSync(dir).sort(), ["creds.json", "datos", "pre-key-12.json", "session-5491122334455.0.json"], "la prueba de escritura no deja rastros");
    assert.deepEqual(fs.readdirSync(path.join(dir, "datos")), []);
}));

// ---------- La carpeta de datos del servicio (datos/) ----------

test("preparar crea datos/ (para los consentimientos y los contadores) y la sesión sabe dónde está", conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    const sesion = crearSesion({ dir });
    await sesion.preparar();
    assert.equal(sesion.dirDatos, path.join(dir, "datos"));
    assert.ok(fs.statSync(sesion.dirDatos).isDirectory());
}));

test("datos/ sobrevive a limpiar(): cerrar la sesión no borra los consentimientos de los pacientes", conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    const sesion = crearSesion({ dir });
    await sesion.preparar();
    fs.writeFileSync(path.join(dir, CREDS), V1);
    fs.writeFileSync(path.join(sesion.dirDatos, "consentimientos.json"), "{}");

    assert.equal(await sesion.limpiar(), 1, "solo se borró creds.json");

    assert.deepEqual(fs.readdirSync(dir), ["datos"]);
    assert.deepEqual(fs.readdirSync(sesion.dirDatos), ["consentimientos.json"]);
}));

test("preparar rechaza datos/ con archivos que no son del servicio, y datos como archivo, enlace o anidado", conRaiz(async raiz => {
    const adentro = path.join(raiz, "uno");
    fs.mkdirSync(path.join(adentro, "datos"), { recursive: true });
    fs.writeFileSync(path.join(adentro, "datos", "notas.txt"), "ajeno");
    await assert.rejects(() => crearSesion({ dir: adentro }).preparar(), /no son de la sesión.*notas\.txt/);

    const anidado = path.join(raiz, "dos");
    fs.mkdirSync(path.join(anidado, "datos", "datos"), { recursive: true });
    await assert.rejects(() => crearSesion({ dir: anidado }).preparar(), /no son de la sesión.*datos/);

    const comoArchivo = path.join(raiz, "tres");
    fs.mkdirSync(comoArchivo);
    fs.writeFileSync(path.join(comoArchivo, "datos"), "soy un archivo");
    await assert.rejects(() => crearSesion({ dir: comoArchivo }).preparar(), /no son de la sesión.*datos/);
}));

test("una carpeta que no es de la sesión se rechaza ANTES de tocarle los permisos o de crearle nada", conRaiz(async raiz => {
    const dir = path.join(raiz, "ajena");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, "importante.conf"), "otra cosa");
    const permisos = mock.method(fs.promises, "chmod", async () => {});
    try {
        await assert.rejects(() => crearSesion({ dir }).preparar(), /no son de la sesión/);
        assert.equal(permisos.mock.callCount(), 0, "no se hizo ningún chmod");
    } finally {
        permisos.mock.restore();
    }
    assert.deepEqual(fs.readdirSync(dir), ["importante.conf"], "ni se creó datos/");
}));

// ---------- Permisos ----------

test("preparar crea la carpeta en 700 y lo que escriba la librería después queda en 600", { skip: SOLO_POSIX }, conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "nivel", "sesion");
    const sesion = crearSesion({ dir, log });
    process.umask(0o022);                              // como un usuario común: sin preparar(), la librería escribiría 644
    await sesion.preparar();
    assert.equal(modo(dir), 0o700);
    assert.equal(modo(path.join(dir, "datos")), 0o700);   // datos/ (consentimientos y contadores) la crea preparar() y es del servicio: mismos permisos

    const { saveCreds } = await sesion.abrirAuth(libreriaFalsa());
    await saveCreds();
    await saveCreds();                                 // la segunda deja el respaldo
    assert.equal(modo(path.join(dir, CREDS)), 0o600);
    assert.equal(modo(path.join(dir, RESPALDO)), 0o600);
    assert.deepEqual(fs.readdirSync(dir).sort(), [CREDS, RESPALDO, "datos"].sort(), "ni temporales sueltos (datos/ la crea preparar() a propósito)");
}));

test("preparar deja datos/ en 700 y sus archivos en 600", { skip: SOLO_POSIX }, conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(path.join(dir, "datos"), { recursive: true });
    fs.chmodSync(path.join(dir, "datos"), 0o755);
    fs.writeFileSync(path.join(dir, "datos", "consentimientos.json"), "{}");
    fs.chmodSync(path.join(dir, "datos", "consentimientos.json"), 0o644);

    await crearSesion({ dir }).preparar();

    assert.equal(modo(path.join(dir, "datos")), 0o700);
    assert.equal(modo(path.join(dir, "datos", "consentimientos.json")), 0o600);
}));

test("preparar corrige una carpeta y archivos que tenían permisos de más", { skip: SOLO_POSIX }, conRaiz(async (raiz, { log, texto }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.chmodSync(dir, 0o755);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    fs.chmodSync(path.join(dir, CREDS), 0o644);
    fs.writeFileSync(path.join(dir, "pre-key-1.json"), "{}");
    fs.chmodSync(path.join(dir, "pre-key-1.json"), 0o666);

    await crearSesion({ dir, log }).preparar();

    assert.equal(modo(dir), 0o700);
    assert.equal(modo(path.join(dir, CREDS)), 0o600);
    assert.equal(modo(path.join(dir, "pre-key-1.json")), 0o600);
    assert.match(texto(), /ajustados a 700 \(eran 755\)/);
    assert.match(texto(), /2 archivo\(s\) de la sesión de WhatsApp ajustados a 600/);
}));

test("preparar avisa si no puede crear la carpeta", { skip: SOLO_POSIX || (ES_ROOT && "root puede escribir en cualquier lado") }, conRaiz(async raiz => {
    const solo = path.join(raiz, "solo-lectura");
    fs.mkdirSync(solo);
    fs.chmodSync(solo, 0o500);
    try {
        await assert.rejects(() => crearSesion({ dir: path.join(solo, "sesion") }).preparar(), /No se pudo crear la carpeta de la sesión.*EACCES/);
    } finally {
        fs.chmodSync(solo, 0o700);
    }
}));

test("preparar avisa si la carpeta no se puede escribir: las credenciales no se guardarían", conRaiz(async raiz => {
    const escritura = mock.method(fs.promises, "writeFile", async () => { throw Object.assign(new Error("sistema de archivos de solo lectura"), { code: "EROFS" }); });
    try {
        await assert.rejects(() => crearSesion({ dir: path.join(raiz, "sesion") }).preparar(), /no se puede escribir \(EROFS\).*ReadWritePaths/);
    } finally {
        escritura.mock.restore();
    }
}));

// ---------- Respaldo de creds.json ----------

for (const [descripcion, contenido] of [["vacío", ""], ["cortado", V1.slice(0, 18)], ["un arreglo", "[]"], ["null", "null"], ["un número", "42"], ["un texto", "\"hola\""]]) {
    test(`abrirAuth: creds.json ${descripcion} y hay respaldo válido: se vuelve al respaldo (la librería armaría una identidad nueva)`, conRaiz(async (raiz, { log, texto }) => {
        const dir = path.join(raiz, "sesion");
        fs.mkdirSync(dir);
        fs.writeFileSync(path.join(dir, RESPALDO), V1);
        fs.writeFileSync(path.join(dir, CREDS), contenido);

        const { state } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa());

        assert.equal(state.creds.registered, true, "la librería ya lee las credenciales buenas");
        assert.equal(state.creds.identidadNueva, undefined);
        assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V1);
        assert.match(texto(), /ERROR creds\.json .* se restauró desde el respaldo/);
    }));
}

test("abrirAuth: creds.json falta y hay respaldo válido: se restaura", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, RESPALDO), V1);
    const { state } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa());
    assert.equal(state.creds.registered, true);
    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V1);
}));

test("abrirAuth: creds.json dañado y sin respaldo: avisa que hay que volver a vincular y no toca el archivo", conRaiz(async (raiz, { log, texto }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), "{");
    const { state } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa());
    assert.equal(state.creds.identidadNueva, true);
    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), "{");
    assert.match(texto(), /está dañado y no hay un respaldo válido: hará falta volver a vincular/);
}));

test("abrirAuth: con creds.json sano NO se restaura un respaldo más viejo", conRaiz(async (raiz, { log, lineas }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, RESPALDO), V1);
    fs.writeFileSync(path.join(dir, CREDS), V2);
    const { state } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa());
    assert.equal(state.creds.version, 2);
    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V2);
    assert.deepEqual(lineas, []);
}));

test("abrirAuth: con creds.json sano no toca nada ni avisa de nada", conRaiz(async (raiz, { log, lineas }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    const { state } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa());
    assert.equal(state.creds.version, 1);
    assert.deepEqual(fs.readdirSync(dir), [CREDS]);
    assert.deepEqual(lineas, []);
}));

test("abrirAuth en una carpeta nueva: la crea y no inventa credenciales", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    const { state } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa());
    assert.equal(state.creds.identidadNueva, true);
    assert.deepEqual(fs.readdirSync(dir), []);
}));

test("saveCreds: antes de escribir deja la versión anterior como respaldo", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    const { saveCreds } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa({ alGuardar: archivo => fs.promises.writeFile(archivo, V2) }));

    await saveCreds();

    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V2);
    assert.equal(fs.readFileSync(path.join(dir, RESPALDO), "utf8"), V1);
}));

test("saveCreds: si no se puede escribir el respaldo, se avisa y las credenciales nuevas se guardan igual, sin temporales sueltos", conRaiz(async (raiz, { log, texto }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    const { saveCreds } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa({ alGuardar: archivo => fs.promises.writeFile(archivo, V2) }));
    const renombrado = mock.method(fs.promises, "rename", async () => { throw Object.assign(new Error("cruce de dispositivos"), { code: "EXDEV" }); });
    try {
        await saveCreds();
    } finally {
        renombrado.mock.restore();
    }
    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V2);
    assert.match(texto(), /No se pudo actualizar el respaldo .*EXDEV.*se guardan igual/);
    assert.deepEqual(fs.readdirSync(dir), [CREDS], "el temporal del respaldo fallido no quedó");
}));

test("saveCreds: la primera vez (no hay nada anterior) no hay respaldo", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    const { saveCreds } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa({ alGuardar: archivo => fs.promises.writeFile(archivo, V1) }));
    await saveCreds();
    assert.deepEqual(fs.readdirSync(dir), [CREDS]);
}));

test("saveCreds: si la escritura corta el archivo y falla (disco lleno), se vuelve a la versión anterior y el error sube", conRaiz(async (raiz, { log, texto }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    const { saveCreds } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa({
        alGuardar: async archivo => { await fs.promises.writeFile(archivo, V2.slice(0, 12)); throw Object.assign(new Error("no space left on device"), { code: "ENOSPC" }); }
    }));

    await assert.rejects(() => saveCreds(), /no space left/);

    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V1);
    assert.match(texto(), /quedó dañado al guardar: se restauró la versión anterior/);
}));

test("saveCreds: si la escritura deja el archivo cortado sin avisar, también se restaura", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    const { saveCreds } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa({ alGuardar: archivo => fs.promises.writeFile(archivo, "{") }));
    await saveCreds();
    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V1);
}));

test("saveCreds: un creds.json cortado nunca pisa un respaldo bueno", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    const { saveCreds } = await crearSesion({ dir, log }).abrirAuth(libreriaFalsa({ alGuardar: archivo => fs.promises.writeFile(archivo, V2) }));
    // Con la sesión ya abierta, un corte dejó creds.json cortado; el respaldo anterior es bueno.
    fs.writeFileSync(path.join(dir, CREDS), "{");
    fs.writeFileSync(path.join(dir, RESPALDO), V1);

    await saveCreds();

    assert.equal(fs.readFileSync(path.join(dir, RESPALDO), "utf8"), V1, "el respaldo bueno sigue igual");
    assert.equal(fs.readFileSync(path.join(dir, CREDS), "utf8"), V2);
}));

// ---------- Borrado ----------

test("limpiar borra las credenciales, el respaldo y los temporales, y deja la carpeta", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    for (const nombre of [CREDS, RESPALDO, `${CREDS}.tmp`, "pre-key-3.json", "app-state-sync-key-AAAA.json"]) fs.writeFileSync(path.join(dir, nombre), "{}");

    assert.equal(await crearSesion({ dir, log }).limpiar(), 5);

    assert.deepEqual(fs.readdirSync(dir), []);
    assert.ok(fs.existsSync(dir), "la carpeta en sí no se borra");
}));

test("limpiar no borra nada si hay archivos que no son de la sesión", conRaiz(async (raiz, { log }) => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    fs.writeFileSync(path.join(dir, CREDS), V1);
    fs.writeFileSync(path.join(dir, "notas.txt"), "mías");
    await assert.rejects(() => crearSesion({ dir, log }).limpiar(), /no son de la sesión/);
    assert.deepEqual(fs.readdirSync(dir).sort(), [CREDS, "notas.txt"]);
}));

test("limpiar de una carpeta que no existe no es un error", conRaiz(async raiz => {
    assert.equal(await crearSesion({ dir: path.join(raiz, "no-existe") }).limpiar(), 0);
}));

test("limpiar no sigue enlaces simbólicos: un enlace cuenta como ajeno y su destino queda intacto", { skip: SOLO_POSIX }, conRaiz(async raiz => {
    const dir = path.join(raiz, "sesion");
    fs.mkdirSync(dir);
    const secreto = path.join(raiz, "otra-cosa.json");
    fs.writeFileSync(secreto, "{}");
    fs.symlinkSync(secreto, path.join(dir, "pre-key-1.json"));
    await assert.rejects(() => crearSesion({ dir }).limpiar(), /no son de la sesión/);
    assert.ok(fs.existsSync(secreto));
}));

// ---------- El repositorio no se queda con credenciales ----------

const hayGit = (() => { const r = spawnSync("git", ["--version"]); return !r.error && r.status === 0; })();
const SOLO_CON_REPO = (!hayGit || !fs.existsSync(path.join(RAIZ_REPO, ".git"))) && "hace falta git y el repositorio";

test("el .gitignore frena los archivos de sesión de WhatsApp aunque alguien apunte la carpeta adentro del repo", { skip: SOLO_CON_REPO }, () => {
    const ignorada = ruta => spawnSync("git", ["-C", RAIZ_REPO, "check-ignore", "-q", "--no-index", ruta]).status === 0;
    for (const ruta of [
        "whatsapp-service/sesion-baileys/creds.json",
        "whatsapp-service/auth_info_baileys/creds.json",
        "cualquier/lado/creds.json",
        "cualquier/lado/creds.json.bak",
        "cualquier/lado/pre-key-12.json",
        "cualquier/lado/session-5491122334455.0.json",
        "cualquier/lado/sender-key-123--456.json",
        "cualquier/lado/sender-key-memory-123.json",
        "cualquier/lado/app-state-sync-key-AAAAAGQY.json",
        "cualquier/lado/app-state-sync-version-regular.json",
        "cualquier/lado/lid-mapping-1234.json",
        "cualquier/lado/device-list-1234.json",
        "cualquier/lado/tctoken-1234.json"
    ]) {
        assert.equal(ignorada(ruta), true, `${ruta} tendría que estar ignorada`);
    }
    // Control: lo que NO es de la sesión sigue versionándose.
    for (const ruta of ["whatsapp-service/package.json", "whatsapp-service/baileys/sesion.js", "docs/DEPLOY.md", "whatsapp-service/test/sesion.test.js"]) {
        assert.equal(ignorada(ruta), false, `${ruta} no tendría que estar ignorada`);
    }
});

test("ningún archivo con nombre de sesión de WhatsApp está versionado", { skip: SOLO_CON_REPO }, () => {
    const r = spawnSync("git", ["-C", RAIZ_REPO, "ls-files"], { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
    assert.equal(r.status, 0);
    const sospechosos = r.stdout.split(/\r?\n/).filter(ruta => /(^|\/)(creds\.json(\.bak)?|pre-key-[^/]*\.json|session-[^/]*\.json|sender-key[^/]*\.json|app-state-sync-[^/]*\.json|lid-mapping-[^/]*\.json|device-list-[^/]*\.json|tctoken-[^/]*\.json)$/.test(ruta));
    assert.deepEqual(sospechosos, []);
});
