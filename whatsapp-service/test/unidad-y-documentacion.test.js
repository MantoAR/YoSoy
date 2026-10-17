// La unit de systemd propuesta para Baileys (deploy/whatsapp-service-baileys.service) y la documentación que la acompaña. No se puede
// probar contra systemd (no corre acá), pero sí que el archivo diga lo que el servicio necesita y que los documentos digan lo mismo
// que el código: las mismas variables, las mismas rutas, el mismo nombre de archivo.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { leerConfig } = require("../config");

const RAIZ = path.join(__dirname, "..");
const RAIZ_REPO = path.resolve(RAIZ, "..");
const RUTA_UNIT = path.join(RAIZ, "deploy", "whatsapp-service-baileys.service");
const RUTA_UNIT_VIEJA = path.join(RAIZ, "deploy", "whatsapp-service.service");
const DOCS = path.join(RAIZ_REPO, "docs");
const SIN_DOCS = !fs.existsSync(path.join(DOCS, "DEPLOY.md")) && "no está la carpeta docs/ (por ejemplo, en el servidor)";

const SESION = "/opt/yosoy/whatsapp/sesion-baileys";

// Una unit de systemd: { Unit: { Descripcion: ["..."] }, Service: { ... } }. Una clave puede repetirse (por eso son listas).
function leerUnidad(texto) {
    const secciones = {};
    let actual = null;
    for (const cruda of texto.split("\n")) {
        const linea = cruda.trim();
        if (linea === "" || linea.startsWith("#")) continue;
        const seccion = linea.match(/^\[(\w+)\]$/);
        if (seccion) { actual = secciones[seccion[1]] = {}; continue; }
        const par = linea.match(/^([A-Za-z]+)=(.*)$/);
        assert.ok(par && actual, `no es una línea de unit: ${linea}`);
        (actual[par[1]] ??= []).push(par[2]);
    }
    return secciones;
}

const unidadTexto = fs.readFileSync(RUTA_UNIT, "utf8");
const unidad = leerUnidad(unidadTexto);
const servicio = unidad.Service;
const una = clave => { assert.equal(servicio[clave]?.length, 1, `${clave} tiene que estar una sola vez`); return servicio[clave][0]; };
const entorno = Object.fromEntries((servicio.Environment ?? []).map(par => { const i = par.indexOf("="); return [par.slice(0, i), par.slice(i + 1)]; }));

// ---------- La unit ----------

test("la unit tiene las tres secciones y usa finales de línea LF (con CRLF systemd lee mal los valores)", () => {
    assert.deepEqual(Object.keys(unidad), ["Unit", "Service", "Install"]);
    assert.equal(unidadTexto.includes("\r"), false);
    assert.ok(unidadTexto.endsWith("\n"));
});

test("corre como el usuario del servicio, en la carpeta de la aplicación, con node 22 de siempre", () => {
    assert.equal(una("Type"), "simple");
    assert.equal(una("User"), "yosoy");
    assert.equal(una("Group"), "yosoy");
    assert.equal(una("WorkingDirectory"), "/opt/yosoy/whatsapp/app");
    assert.equal(una("ExecStart"), "/usr/bin/node index.js");
    assert.ok(fs.existsSync(path.join(RAIZ, "index.js")), "el ExecStart apunta a un archivo que existe");
    assert.deepEqual(unidad.Install.WantedBy, ["multi-user.target"]);
});

test("el secreto se lee del archivo de siempre y NO está escrito en la unit", () => {
    assert.equal(una("EnvironmentFile"), "/opt/yosoy/config/whatsapp.env", "sin el guion de adelante: si el archivo falta, no tiene que arrancar");
    assert.deepEqual(Object.keys(entorno).sort(), ["NODE_ENV", "WHATSAPP_BACKEND", "WHATSAPP_BAILEYS_SESSION_PATH"]);
    assert.doesNotMatch(unidadTexto, /secreto\s*=|password|passwd|token|api[_-]?key/i, "ningún secreto ni nombre de secreto asignado en la unit");
});

test("el backend es baileys y la sesión está en su carpeta propia, afuera de la aplicación", () => {
    assert.equal(entorno.WHATSAPP_BACKEND, "baileys");
    assert.equal(entorno.WHATSAPP_BAILEYS_SESSION_PATH, SESION);
    assert.equal(entorno.NODE_ENV, "production");
    const relativa = path.posix.relative(una("WorkingDirectory"), SESION);
    assert.ok(relativa.startsWith(".."), "la sesión no está adentro de la aplicación: un despliegue la pisaría");
    assert.ok(path.posix.relative(SESION, una("WorkingDirectory")).startsWith(".."), "ni la aplicación adentro de la sesión");
});

test("lo único que puede escribir es la carpeta de la sesión (ProtectSystem=strict)", () => {
    assert.equal(una("ProtectSystem"), "strict");
    assert.deepEqual(servicio.ReadWritePaths, [SESION], "ni la aplicación, ni la sesión de whatsapp-web.js, ni el caché");
    assert.equal(una("UMask"), "0077");
});

test("el endurecimiento: sin privilegios nuevos, sin home, sin dispositivos, sin capacidades y solo las familias de red que hacen falta", () => {
    for (const clave of ["NoNewPrivileges", "ProtectHome", "PrivateTmp", "PrivateDevices", "ProtectKernelTunables", "ProtectKernelModules",
        "ProtectControlGroups", "RestrictSUIDSGID", "LockPersonality", "RestrictRealtime"]) {
        assert.equal(una(clave), "true", clave);
    }
    assert.equal(una("SystemCallArchitectures"), "native");
    assert.deepEqual(una("RestrictAddressFamilies").split(" ").sort(), ["AF_INET", "AF_INET6", "AF_NETLINK", "AF_UNIX"]);
    assert.equal(servicio.CapabilityBoundingSet?.[0], "", "ninguna capacidad");
    assert.doesNotMatch(unidadTexto, /MemoryDenyWriteExecute\s*=\s*true/, "node (V8 con JIT) no arranca con MemoryDenyWriteExecute");
});

test("la memoria tiene tope, y es bastante menor que la de whatsapp-web.js con Chromium", () => {
    assert.equal(una("MemoryMax"), "512M");
    const vieja = leerUnidad(fs.readFileSync(RUTA_UNIT_VIEJA, "utf8")).Service;
    assert.equal(vieja.MemoryMax[0], "2G");
});

test("si el proceso cae en bucle, systemd se rinde (no hay que volver a golpear a WhatsApp cada pocos segundos)", () => {
    assert.equal(una("Restart"), "on-failure");
    assert.ok(Number(una("RestartSec")) >= 10);
    assert.equal(unidad.Unit.StartLimitIntervalSec[0], "300");
    assert.equal(unidad.Unit.StartLimitBurst[0], "5");
    assert.deepEqual(unidad.Unit.After, ["network-online.target"]);
    assert.deepEqual(unidad.Unit.Wants, ["network-online.target"]);
});

test("TimeoutStopSec le da al apagado ordenado al menos el doble del tope que él mismo se pone", () => {
    const fuente = fs.readFileSync(path.join(RAIZ, "apagado.js"), "utf8");
    const presupuestoSeg = Number(/presupuestoMs = (\d+)/.exec(fuente)[1]) / 1000;
    const stop = Number(una("TimeoutStopSec"));
    assert.ok(stop >= presupuestoSeg * 2, `${stop} s no alcanza para un apagado de hasta ${presupuestoSeg} s`);
});

test("las variables WHATSAPP_ de la unit son las que lee config.js, y la configuración que arma la unit es válida", () => {
    const fuenteConfig = fs.readFileSync(path.join(RAIZ, "config.js"), "utf8");
    for (const nombre of Object.keys(entorno).filter(n => n.startsWith("WHATSAPP_"))) {
        assert.ok(fuenteConfig.includes(`"${nombre}"`), `config.js no lee ${nombre}`);
    }
    const config = leerConfig({ API_BASE_URL: "http://127.0.0.1:5000", Mensajeria__WhatsApp__Secreto: "secreto-de-prueba", ...entorno });
    assert.equal(config.backend, "baileys");
    assert.equal(config.baileys.sessionPath, path.resolve(SESION));
});

test("la unit de whatsapp-web.js de siempre sigue ahí (es el destino del rollback) y no se tocó el nombre del servicio", () => {
    const vieja = leerUnidad(fs.readFileSync(RUTA_UNIT_VIEJA, "utf8")).Service;
    assert.equal(vieja.WorkingDirectory[0], "/opt/yosoy/whatsapp/app");
    assert.equal(vieja.EnvironmentFile[0], "/opt/yosoy/config/whatsapp.env");
    assert.ok(vieja.Environment.some(e => e.startsWith("WHATSAPP_SESSION_PATH=")));
    assert.ok(!vieja.Environment.some(e => e.startsWith("WHATSAPP_BACKEND=")), "sin la variable, el backend es el de siempre");
});

test("git guarda las units de whatsapp-service con LF aunque estén en un Windows con autocrlf", { skip: !fs.existsSync(path.join(RAIZ_REPO, ".git")) && "no está el repositorio" }, () => {
    const r = spawnSync("git", ["-C", RAIZ_REPO, "check-attr", "eol", "--", "whatsapp-service/deploy/whatsapp-service-baileys.service", "whatsapp-service/deploy/whatsapp-service.service"], { encoding: "utf8" });
    if (r.error) return;                                    // sin git no hay nada que comprobar
    assert.equal(r.status, 0);
    const lineas = r.stdout.trim().split(/\r?\n/);
    assert.equal(lineas.length, 2);
    for (const linea of lineas) assert.match(linea, /eol: lf$/, linea);
});

// ---------- La documentación dice lo mismo que el código ----------

const leer = nombre => fs.readFileSync(path.join(DOCS, nombre), "utf8").replace(/\r\n/g, "\n");

test("el README documenta TODAS las variables WHATSAPP_ que lee config.js", () => {
    const fuente = fs.readFileSync(path.join(RAIZ, "config.js"), "utf8");
    const nombres = [...new Set(fuente.match(/\bWHATSAPP_[A-Z0-9_]+\b/g))];
    assert.ok(nombres.length >= 15, `se encontraron ${nombres.length} variables en config.js`);
    const readme = fs.readFileSync(path.join(RAIZ, "README.md"), "utf8");
    const sinDocumentar = nombres.filter(n => !readme.includes(n));
    assert.deepEqual(sinDocumentar, [], `variables que lee config.js y el README no menciona: ${sinDocumentar.join(", ")}`);
});

test("el README dice los valores por defecto de los topes tal como están en el código", () => {
    const fuente = fs.readFileSync(path.join(RAIZ, "config.js"), "utf8");
    const readme = fs.readFileSync(path.join(RAIZ, "README.md"), "utf8");
    for (const [variable, valor] of [["WHATSAPP_TOPE_DIARIO", 100], ["WHATSAPP_TOPE_POR_HORA", 40], ["WHATSAPP_TOPE_POR_PERSONA", 3], ["WHATSAPP_CONSENTIMIENTO_DIAS", 365], ["WHATSAPP_BAILEYS_VENTANAS_QR", 2]]) {
        assert.ok(new RegExp(`"${variable}", ${valor},`).test(fuente), `${variable} ya no vale ${valor} en config.js`);
        const fila = readme.split("\n").find(l => l.startsWith(`| \`${variable}\``) && l.includes("Por defecto") === false && /\| `?\d+/.test(l));
        assert.ok(fila && fila.includes(`\`${valor}`), `el README no dice ${valor} para ${variable}`);
    }
});

test("DEPLOY.md describe el despliegue con el mismo nombre de unit, la misma carpeta de sesión y el rollback", { skip: SIN_DOCS }, () => {
    const deploy = leer("DEPLOY.md");
    for (const frase of ["Despliegue de whatsapp-service con Baileys", "whatsapp-service-baileys.service", SESION, "PUPPETEER_SKIP_DOWNLOAD=1",
        "npm ci --omit=dev", "systemd-analyze verify", "Mensajeria__CanalActivo=Baileys", "app-anterior-$FECHA", "## 10. Rollback", "WHATSAPP_TOPE_DIARIO=30"]) {
        assert.ok(deploy.includes(frase), `DEPLOY.md no menciona: ${frase}`);
    }
    assert.ok(deploy.includes("sed -i 's/\\r$//'"), "DEPLOY.md saca los CR por si la unit llegó con finales de línea de Windows");
    const seccion = deploy.slice(deploy.indexOf("# Despliegue de whatsapp-service con Baileys"));
    assert.ok(!/BEGIN (RSA|OPENSSH)|password\s*=|Secreto=[A-Za-z0-9]{8,}/i.test(seccion), "ningún secreto escrito en la sección nueva de DEPLOY.md");
    // Todas las veces que se nombra una carpeta de sesión nueva, es la misma que pone la unit.
    assert.deepEqual([...new Set(seccion.match(/\/opt\/yosoy\/whatsapp\/sesion-[a-z]+/g))], [SESION]);
    // El comando que instala las dependencias en el servidor no baja Chromium (alcanza con que el texto lo mencione en otro lado).
    assert.ok(seccion.includes("PUPPETEER_SKIP_DOWNLOAD=1 \\\n  npm ci --omit=dev"), "el npm ci del VPS lleva PUPPETEER_SKIP_DOWNLOAD=1");
    // Los topes iniciales del paso 5 son bajos (más que los de por defecto).
    const topeInicial = Number(/WHATSAPP_TOPE_DIARIO=(\d+)/.exec(seccion)[1]);
    assert.ok(topeInicial < 100, `el tope inicial (${topeInicial}) tiene que ser menor que el de por defecto`);
});

test("SERVIDOR.md cuenta la misma unit y la misma carpeta, y aclara que todavía no está desplegado", { skip: SIN_DOCS }, () => {
    const servidor = leer("SERVIDOR.md");
    for (const frase of ["WhatsApp con Baileys (propuesto, todavía no desplegado)", "whatsapp-service-baileys.service", SESION, "/salud"]) {
        assert.ok(servidor.includes(frase), `SERVIDOR.md no menciona: ${frase}`);
    }
    const filaDeMemoria = servidor.split("\n").find(l => l.startsWith("| Memoria |"));
    assert.ok(filaDeMemoria?.includes(`MemoryMax=${una("MemoryMax")}`), "la tabla de SERVIDOR.md dice la memoria que pone la unit");
    assert.deepEqual([...new Set(servidor.match(/\/opt\/yosoy\/whatsapp\/sesion-[a-z]+/g))], [SESION], "y la misma carpeta de sesión en todos lados");
});

test("PROGRESO_BAILEYS.md tiene lo que pidió el encargo: pasos manuales, qué falta y la tabla de etapas completa", { skip: SIN_DOCS }, () => {
    const progreso = leer("PROGRESO_BAILEYS.md");
    for (const titulo of ["## Pasos manuales para Agustín", "## Qué falta / pendiente de decisión", "## Estado de las etapas"]) {
        assert.ok(progreso.includes(titulo), `falta "${titulo}"`);
    }
    for (let etapa = 1; etapa <= 9; etapa++) assert.match(progreso, new RegExp(`^\\| ${etapa} \\|`, "m"), `falta la etapa ${etapa} en la tabla`);
});
