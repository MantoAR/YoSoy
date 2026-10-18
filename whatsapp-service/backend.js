// Arma el backend de WhatsApp: Baileys, el único motor del servicio (whatsapp-web.js, Puppeteer y Chromium se sacaron por completo).
//
// La interfaz del backend es la que usa servidor.js:
//   iniciar()                 empieza a conectar
//   cerrar()                  apagado ordenado
//   estado()                  { conectado, numero, qrPendiente, qr, ... }
//   enviar(telefono, texto)   lo llama la cola de envíos
//   desvincular()             cierra la sesión y deja listo un QR nuevo
//   recuperar()               red de seguridad ante un error sin capturar del proceso
//   salud()                   { ok, estado, ... } para GET /salud
//   vincular()                arranca una vinculación por QR a pedido
// y además trae, para la cola, `opcionesDeCola` (consentimiento, topes, ritmo y estado de la conexión) y `datos.cerrar()` (lo que hay
// que esperar al apagar). Sin esas opciones la cola no se arma (ver crearColaParaBackend): no hay envíos sin protecciones.
"use strict";

const path = require("node:path");

async function crearBackend(config, log, deps = {}) {
    const { crearSesion } = require("./baileys/sesion");
    const { ServicioBaileys } = require("./baileys/servicio");
    const { crearReenviador } = require("./reenvio");
    const { crearAlmacenJson } = require("./almacen");
    const { RegistroDeConsentimiento } = require("./consentimiento");
    const { LimiteDeEnvios } = require("./limites");
    const { derivarClaveDeHuellas } = require("./privacidad");
    // `deps` existe para los tests: reemplaza lo que toca la librería real y la red.
    const cargar = deps.cargarBaileys ?? require("./baileys/cargar").cargarBaileys;

    // Primero la carpeta de la sesión (ubicación, permisos, que se pueda escribir): si está mal, se corta acá, antes de
    // cargar la librería y de abrir el puerto.
    const sesion = crearSesion({ dir: config.baileys.sessionPath, log, appDir: deps.appDir ?? __dirname });
    await sesion.preparar();
    const fachada = await cargar();

    // Consentimiento y topes de envío: sus datos viven en datos/, dentro de la carpeta de la sesión. Los teléfonos
    // se guardan como huella, con una clave derivada del secreto compartido.
    const envio = config.baileys.envio;
    const reloj = deps.reloj;
    const clave = derivarClaveDeHuellas(config.secreto);
    const consentimiento = new RegistroDeConsentimiento({
        almacen: crearAlmacenJson(path.join(sesion.dirDatos, "consentimientos.json"), { log, nombre: "los consentimientos" }),
        clave, diasDeVigencia: envio.diasDeConsentimiento, log, ...(reloj ? { reloj } : {})
    });
    await consentimiento.cargar();
    const limites = new LimiteDeEnvios({
        topeDiario: envio.topeDiario, topePorHora: envio.topePorHora, topePorDestinatario: envio.topePorPersona,
        zonaHoraria: envio.zonaHoraria, clave, log, ...(reloj ? { reloj } : {}),
        almacen: crearAlmacenJson(path.join(sesion.dirDatos, "envios.json"), { log, nombre: "los contadores de envío" })
    });
    await limites.cargar();

    // Un mensaje de texto de una persona es la constancia de que escribió primero (o su pedido de BAJA/ALTA): se anota ANTES de
    // reenviarlo a la API, así la respuesta del bot ya la encuentra.
    const reenviarALaApi = deps.reenviar ?? crearReenviador({ apiBaseUrl: config.apiBaseUrl, secreto: config.secreto, log });
    const reenviar = (telefono, texto) => {
        consentimiento.alMensajeEntrante(telefono, texto);
        return reenviarALaApi(telefono, texto);
    };

    const servicio = new ServicioBaileys({
        fachada,
        sesion,
        log,
        reenviar,
        versionWa: config.baileys.versionWa,
        buscarVersionWa: config.baileys.buscarVersionWa,
        maxVentanasQr: config.baileys.maxVentanasQr,
        simularEscritura: envio.simularEscritura,
        ...(reloj ? { reloj } : {})
    });
    // Lo que index.js le pasa a la cola de envíos, y lo que tiene que esperar al apagar.
    servicio.opcionesDeCola = {
        consentimiento,
        limites,
        estadoDeConexion: () => servicio.salud().estado,
        espaciado: { minMs: envio.pausaMinSeg * 1000, maxMs: envio.pausaMaxSeg * 1000 },
        pausaLarga: { cada: [8, 14], ms: [30 * 1000, 75 * 1000] },
        ...(reloj ? { reloj } : {})
    };
    servicio.datos = { cerrar: () => Promise.all([consentimiento.cerrar(), limites.cerrar()]) };
    return servicio;
}

module.exports = { crearBackend };
