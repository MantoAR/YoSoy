// Configuración: lo que se lee del entorno, los valores por defecto y los errores que cortan el arranque.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const { leerConfig, leerConfigBaileys, leerConfigEnvio, leerPuerto, ErrorDeConfiguracion, validarApiBaseUrl } = require("../config");

const SECRETO = "secreto-de-prueba-que-no-se-puede-filtrar";
const base = (extra = {}) => ({ API_BASE_URL: "http://127.0.0.1:5000", Mensajeria__WhatsApp__Secreto: SECRETO, ...extra });
const ABSOLUTA = path.resolve(path.sep, "datos", "sesion-baileys");

function falla(env, patron) {
    assert.throws(() => leerConfig(env), err => {
        assert.ok(err instanceof ErrorDeConfiguracion, `tenía que ser ErrorDeConfiguracion y fue ${err?.constructor?.name}`);
        assert.match(err.message, patron);
        assert.ok(!err.message.includes(SECRETO), "el mensaje de error no puede incluir el secreto");
        return true;
    });
}

test("sin variables de Baileys todo sigue como hoy: backend webjs, puerto 3002 y carpeta de whatsapp-web.js", () => {
    const c = leerConfig(base());
    assert.equal(c.backend, "webjs");
    assert.equal(c.puerto, 3002);
    assert.equal(c.apiBaseUrl, "http://127.0.0.1:5000");
    assert.equal(c.secreto, SECRETO);
    assert.equal(c.webjs.sessionPath, "./.wwebjs_auth");
    assert.equal(c.webjs.webVersion, null);
    assert.equal(c.debug, false);
    assert.equal(c.baileys, null);
});

test("las variables de whatsapp-web.js de siempre se siguen leyendo", () => {
    const c = leerConfig(base({ WHATSAPP_SESSION_PATH: "/opt/yosoy/whatsapp/sesion", WHATSAPP_WEB_VERSION: "2.3000.1", WHATSAPP_DEBUG: "1", WHATSAPP_SERVICE_PORT: "3010" }));
    assert.equal(c.webjs.sessionPath, "/opt/yosoy/whatsapp/sesion");
    assert.equal(c.webjs.webVersion, "2.3000.1");
    assert.equal(c.debug, true);
    assert.equal(c.puerto, 3010);
});

test("faltan API_BASE_URL o el secreto: error claro, sin repetir el secreto", () => {
    falla({ Mensajeria__WhatsApp__Secreto: SECRETO }, /Falta API_BASE_URL/);
    falla({ API_BASE_URL: "http://127.0.0.1:5000" }, /Falta Mensajeria__WhatsApp__Secreto/);
    falla(base({ Mensajeria__WhatsApp__Secreto: "" }), /Falta Mensajeria__WhatsApp__Secreto/);
});

test("WHATSAPP_BACKEND solo acepta webjs o baileys (sin distinguir mayúsculas)", () => {
    assert.equal(leerConfig(base({ WHATSAPP_BACKEND: "WebJs" })).backend, "webjs");
    falla(base({ WHATSAPP_BACKEND: "oficial" }), /WHATSAPP_BACKEND tiene que ser "webjs" o "baileys"/);
});

test("el puerto tiene que ser un entero válido", () => {
    falla(base({ WHATSAPP_SERVICE_PORT: "tres mil" }), /WHATSAPP_SERVICE_PORT tiene que ser un número entero/);
    falla(base({ WHATSAPP_SERVICE_PORT: "70000" }), /WHATSAPP_SERVICE_PORT tiene que estar entre 1 y 65535/);
});

test("con baileys, la carpeta de la sesión es obligatoria: sin valor por defecto, para que nunca caiga dentro del repo", () => {
    falla(base({ WHATSAPP_BACKEND: "baileys" }), /Falta WHATSAPP_BAILEYS_SESSION_PATH.*FUERA del repositorio/s);
});

test("con baileys, la carpeta de la sesión tiene que ser una ruta absoluta", () => {
    falla(base({ WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: "./auth" }), /ruta absoluta/);
    falla(base({ WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: "sesion" }), /ruta absoluta/);
});

test("con baileys y la carpeta bien puesta, la configuración queda completa", () => {
    const c = leerConfig(base({ WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: ABSOLUTA }));
    assert.equal(c.backend, "baileys");
    assert.equal(c.baileys.sessionPath, ABSOLUTA);
    assert.equal(c.baileys.versionWa, null);
    assert.equal(c.baileys.buscarVersionWa, false);
    assert.equal(c.baileys.maxVentanasQr, 2);
});

test("la versión de WhatsApp Web se puede fijar a mano, y se valida", () => {
    const env = base({ WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: ABSOLUTA });
    assert.deepEqual(leerConfig({ ...env, WHATSAPP_BAILEYS_VERSION_WA: "2.3000.1037753511" }).baileys.versionWa, [2, 3000, 1037753511]);
    assert.equal(leerConfig({ ...env, WHATSAPP_BAILEYS_BUSCAR_VERSION: "1" }).baileys.buscarVersionWa, true);
    falla({ ...env, WHATSAPP_BAILEYS_VERSION_WA: "ultima" }, /WHATSAPP_BAILEYS_VERSION_WA tiene que tener la forma/);
});

test("las ventanas de QR que se abren solas son entre 1 y 5 (por defecto 2)", () => {
    const env = base({ WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: ABSOLUTA });
    assert.equal(leerConfig({ ...env, WHATSAPP_BAILEYS_VENTANAS_QR: "1" }).baileys.maxVentanasQr, 1);
    assert.equal(leerConfig({ ...env, WHATSAPP_BAILEYS_VENTANAS_QR: "5" }).baileys.maxVentanasQr, 5);
    falla({ ...env, WHATSAPP_BAILEYS_VENTANAS_QR: "0" }, /WHATSAPP_BAILEYS_VENTANAS_QR tiene que estar entre 1 y 5/);
    falla({ ...env, WHATSAPP_BAILEYS_VENTANAS_QR: "6" }, /WHATSAPP_BAILEYS_VENTANAS_QR tiene que estar entre 1 y 5/);
    falla({ ...env, WHATSAPP_BAILEYS_VENTANAS_QR: "muchas" }, /WHATSAPP_BAILEYS_VENTANAS_QR tiene que ser un número entero/);
});

test("las protecciones de envío tienen valores prudentes por defecto", () => {
    assert.deepEqual(leerConfigEnvio({}), {
        topeDiario: 100, topePorHora: 40, topePorPersona: 3, diasDeConsentimiento: 365,
        zonaHoraria: "America/Argentina/Buenos_Aires", pausaMinSeg: 4, pausaMaxSeg: 9, simularEscritura: true
    });
    const c = leerConfig(base({ WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: ABSOLUTA }));
    assert.deepEqual(c.baileys.envio, leerConfigEnvio({}), "con baileys vienen en la configuración");
    assert.equal(leerConfig(base()).baileys, null, "con webjs no hay nada de esto");
});

test("los topes, la vigencia, la zona horaria y las pausas se configuran y se validan", () => {
    const e = leerConfigEnvio({
        WHATSAPP_TOPE_DIARIO: "30", WHATSAPP_TOPE_POR_HORA: "10", WHATSAPP_TOPE_POR_PERSONA: "1", WHATSAPP_CONSENTIMIENTO_DIAS: "90",
        WHATSAPP_ZONA_HORARIA: "America/Argentina/Cordoba", WHATSAPP_PAUSA_MIN_S: "6", WHATSAPP_PAUSA_MAX_S: "6", WHATSAPP_SIMULAR_ESCRITURA: "0"
    });
    assert.deepEqual(e, { topeDiario: 30, topePorHora: 10, topePorPersona: 1, diasDeConsentimiento: 90, zonaHoraria: "America/Argentina/Cordoba", pausaMinSeg: 6, pausaMaxSeg: 6, simularEscritura: false });

    const envio = extra => base({ WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: ABSOLUTA, ...extra });
    falla(envio({ WHATSAPP_TOPE_DIARIO: "0" }), /WHATSAPP_TOPE_DIARIO tiene que estar entre 1 y 1000/);
    falla(envio({ WHATSAPP_TOPE_DIARIO: "1001" }), /WHATSAPP_TOPE_DIARIO tiene que estar entre 1 y 1000/);
    falla(envio({ WHATSAPP_TOPE_POR_HORA: "mucho" }), /WHATSAPP_TOPE_POR_HORA tiene que ser un número entero/);
    falla(envio({ WHATSAPP_TOPE_POR_PERSONA: "21" }), /WHATSAPP_TOPE_POR_PERSONA tiene que estar entre 1 y 20/);
    falla(envio({ WHATSAPP_CONSENTIMIENTO_DIAS: "0" }), /WHATSAPP_CONSENTIMIENTO_DIAS tiene que estar entre 1 y 3650/);
    falla(envio({ WHATSAPP_ZONA_HORARIA: "Marte/Olimpo" }), /WHATSAPP_ZONA_HORARIA no es una zona horaria válida/);
    falla(envio({ WHATSAPP_PAUSA_MIN_S: "10", WHATSAPP_PAUSA_MAX_S: "5" }), /WHATSAPP_PAUSA_MAX_S \(5\) no puede ser menor que WHATSAPP_PAUSA_MIN_S \(10\)/);
    assert.equal(leerConfigEnvio({ WHATSAPP_SIMULAR_ESCRITURA: "1" }).simularEscritura, true);
    assert.equal(leerConfigEnvio({ WHATSAPP_SIMULAR_ESCRITURA: "" }).simularEscritura, true, "solo '0' lo apaga");
});

test("el comando de vinculación lee solo lo de Baileys: no pide la API ni el secreto", () => {
    const c = leerConfigBaileys({ WHATSAPP_BAILEYS_SESSION_PATH: ABSOLUTA, WHATSAPP_BAILEYS_VENTANAS_QR: "3" });
    assert.deepEqual(c, { sessionPath: ABSOLUTA, versionWa: null, buscarVersionWa: false, maxVentanasQr: 3 });
    assert.throws(() => leerConfigBaileys({}), /Falta WHATSAPP_BAILEYS_SESSION_PATH/);
    assert.throws(() => leerConfigBaileys({ WHATSAPP_BAILEYS_SESSION_PATH: "relativa" }), /ruta absoluta/);
    assert.equal(leerPuerto({}), 3002);
    assert.equal(leerPuerto({ WHATSAPP_SERVICE_PORT: "4567" }), 4567);
    assert.throws(() => leerPuerto({ WHATSAPP_SERVICE_PORT: "70000" }), /WHATSAPP_SERVICE_PORT tiene que estar entre 1 y 65535/);
});

test("con baileys, API_BASE_URL no puede mandar el secreto en claro a otra máquina", () => {
    const env = { WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: ABSOLUTA };
    assert.equal(leerConfig(base({ ...env, API_BASE_URL: "http://localhost:5000/" })).apiBaseUrl, "http://localhost:5000");
    assert.equal(leerConfig(base({ ...env, API_BASE_URL: "https://api.ejemplo.test" })).apiBaseUrl, "https://api.ejemplo.test");
    falla(base({ ...env, API_BASE_URL: "http://api.ejemplo.test:5000" }), /otra máquina sin HTTPS/);
    falla(base({ ...env, API_BASE_URL: "no es una url" }), /no es una URL válida/);
    falla(base({ ...env, API_BASE_URL: "ftp://127.0.0.1" }), /http:\/\/ o https:\/\//);
});

test("con webjs, API_BASE_URL no se toca (comportamiento de siempre)", () => {
    assert.equal(leerConfig(base({ API_BASE_URL: "http://servidor-interno:5000/" })).apiBaseUrl, "http://servidor-interno:5000/");
});

test("validarApiBaseUrl reconoce el loopback en sus formas", () => {
    for (const ok of ["http://127.0.0.1:5000", "http://localhost", "http://[::1]:5000", "http://127.1.2.3"]) assert.doesNotThrow(() => validarApiBaseUrl(ok), ok);
    assert.throws(() => validarApiBaseUrl("http://0.0.0.0:5000"), ErrorDeConfiguracion);
});
