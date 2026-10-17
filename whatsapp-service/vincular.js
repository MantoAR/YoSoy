// Vinculación por QR desde la terminal: muestra el QR por pantalla, sin pasar por el log ni por HTTP, y termina apenas el número
// queda vinculado. Es la forma de respaldo para cuando no se puede usar el Configurador (por ejemplo, por SSH).
//
//   sudo systemctl stop whatsapp-service
//   cd /opt/yosoy/whatsapp/app
//   sudo -u yosoy env WHATSAPP_BAILEYS_SESSION_PATH=/opt/yosoy/whatsapp/sesion-baileys node vincular.js
//   sudo systemctl start whatsapp-service
//
// Qué hace, y qué NO:
//   - corre en una terminal: si la salida no es una terminal, se niega, para que el QR no termine en un archivo ni en el journal;
//   - con el servicio apagado: dos conexiones con la misma cuenta se pisan;
//   - no como root: la sesión quedaría de root y el servicio (otro usuario) no la podría leer;
//   - usa la misma carpeta y las mismas protecciones que el servicio (ServicioBaileys, sesion.preparar());
//   - no reenvía ningún mensaje a la API y no necesita el secreto compartido;
//   - el QR vive solo en memoria y en la pantalla: no se escribe en ningún archivo, en el log ni en la salida de errores.
//
// Códigos de salida: 0 vinculado (o ya lo estaba) · 1 no se pudo hacer · 2 el QR venció o se agotó el tiempo · 130 interrumpido.
"use strict";

const net = require("node:net");

const { leerConfigBaileys, leerPuerto, ErrorDeConfiguracion } = require("./config");
const { crearSesion } = require("./baileys/sesion");
const { ServicioBaileys, RELOJ_REAL } = require("./baileys/servicio");
const { ocultarNumeros, crearLogSeguro } = require("./privacidad");
const { secretosDelQr } = require("./logs");

const UNA_VENTANA_MS = 3 * 60 * 1000;     // una ventana de QR dura ~2,7 minutos

// ¿Hay algo escuchando en ese puerto de la propia máquina? (el servicio, si está corriendo)
function puertoOcupado(puerto, host = "127.0.0.1", esperaMs = 1000) {
    return new Promise(resolver => {
        const socket = net.connect({ port: puerto, host });
        const terminar = ocupado => { socket.destroy(); resolver(ocupado); };
        socket.setTimeout(esperaMs, () => terminar(false));
        socket.once("connect", () => terminar(true));
        socket.once("error", () => terminar(false));
    });
}

// El QR como texto para la terminal (bloques de medio alto, para que entre en pantalla).
function dibujarQr(qr) {
    const qrTerminal = require("qrcode-terminal");
    let dibujo = "";
    qrTerminal.generate(qr, { small: true }, texto => { dibujo = texto; });
    return dibujo;
}

async function ejecutarVinculacion({
    env = process.env,
    stdout = process.stdout,
    stderr = process.stderr,
    esTTY = Boolean(process.stdout.isTTY),
    uid = typeof process.getuid === "function" ? process.getuid() : null,
    hayServicio = puertoOcupado,
    cargarBaileys = () => require("./baileys/cargar").cargarBaileys(),
    dibujar = dibujarQr,
    reloj = RELOJ_REAL,
    esperar = ms => new Promise(resolver => reloj.setTimeout(resolver, ms)),
    consultaMs = 500,
    senal = null
} = {}) {
    const decir = texto => stderr.write(`${texto}\n`);
    // Lo que escribe el servicio por la salida de errores pasa por el filtro de privacidad: teléfonos enmascarados y, si el QR llegara
    // a aparecer en un mensaje, tapado (el QR va SOLO por la pantalla, nunca por acá).
    let servicio = null;
    const log = crearLogSeguro(
        { info: m => decir(`[vincular] ${m}`), error: m => decir(`[vincular] ${m}`) },
        { secretos: () => secretosDelQr(servicio?.qrTexto()) });

    if (!esTTY) {
        decir("Este comando muestra un código QR: correrlo en una terminal, no redirigido a un archivo, a un pipe ni al journal.");
        return 1;
    }
    if (uid === 0) {
        decir("No correrlo como root: la sesión quedaría de root y el servicio no la podría leer. Usar el usuario del servicio, por ejemplo: " +
            "sudo -u yosoy env WHATSAPP_BAILEYS_SESSION_PATH=/opt/yosoy/whatsapp/sesion-baileys node vincular.js");
        return 1;
    }

    let config, puerto;
    try {
        config = leerConfigBaileys(env);
        puerto = leerPuerto(env);
    } catch (err) {
        if (!(err instanceof ErrorDeConfiguracion)) throw err;
        decir(err.message);
        return 1;
    }

    if (await hayServicio(puerto)) {
        decir(`El servicio parece estar corriendo (hay algo escuchando en 127.0.0.1:${puerto}). Dos conexiones con la misma cuenta se pisan: ` +
            "pararlo primero (sudo systemctl stop whatsapp-service) y volver a correr esto.");
        return 1;
    }

    const sesion = crearSesion({ dir: config.sessionPath, log, appDir: __dirname });
    try {
        await sesion.preparar();
    } catch (err) {
        decir(err.message);
        return 1;
    }

    const fachada = await cargarBaileys();
    servicio = new ServicioBaileys({
        fachada,
        sesion,
        log,
        reenviar: () => {},                      // acá no se atiende a nadie: solo se vincula
        reloj,
        versionWa: config.versionWa,
        buscarVersionWa: config.buscarVersionWa,
        maxVentanasQr: config.maxVentanasQr
    });

    const inicio = reloj.ahora();
    const topeMs = config.maxVentanasQr * UNA_VENTANA_MS + 60 * 1000;
    let ultimoQr = null;
    let mostroQr = false;
    try {
        servicio.iniciar();
        for (;;) {
            const salud = servicio.salud();
            if (salud.conectado) {
                const { numero } = await servicio.estado();
                const enmascarado = ocultarNumeros(numero ?? "desconocido");
                if (mostroQr) {
                    decir(`Listo: el número ${enmascarado} quedó vinculado y la sesión guardada en ${config.sessionPath}. ` +
                        "Ya se puede arrancar el servicio (sudo systemctl start whatsapp-service).");
                } else {
                    decir(`Esta sesión ya estaba vinculada (número ${enmascarado}): no hizo falta escanear nada.`);
                }
                return 0;
            }
            if (salud.requiereVincular && salud.motivo === "qr_vencido") {
                decir("El QR venció sin escanearse. Volver a correr el comando para pedir otro.");
                return 2;
            }
            if (salud.requiereVincular && salud.motivo !== "esperando_escaneo") {
                decir(`No se pudo vincular (${salud.motivo}). Ver los mensajes de arriba.`);
                return 1;
            }
            if (senal?.aborted) {
                decir("Interrumpido.");
                return 130;
            }
            if (reloj.ahora() - inicio > topeMs) {
                decir("Se agotó el tiempo sin que el QR se escaneara. Volver a correr el comando para pedir otro.");
                return 2;
            }

            const qr = servicio.qrTexto();
            if (qr && qr !== ultimoQr) {
                ultimoQr = qr;
                mostroQr = true;
                stdout.write("\x1b[H\x1b[J");        // el QR nuevo reemplaza al anterior en la pantalla
                stdout.write("Escanear este código con el teléfono del número de la clínica:\n" +
                    "WhatsApp → Ajustes → Dispositivos vinculados → Vincular un dispositivo.\n" +
                    "El código se renueva solo cada pocos segundos. Ctrl+C para cancelar.\n\n");
                stdout.write(`${dibujar(qr)}\n`);
            }
            await esperar(consultaMs);
        }
    } finally {
        await servicio.cerrar().catch(() => {});     // espera a que termine de guardar las credenciales
    }
}

module.exports = { ejecutarVinculacion, puertoOcupado, dibujarQr };

if (require.main === module) {
    const controlador = new AbortController();
    process.on("SIGINT", () => controlador.abort());
    process.on("SIGTERM", () => controlador.abort());
    ejecutarVinculacion({ senal: controlador.signal }).then(
        codigo => process.exit(codigo),
        err => { process.stderr.write(`Error: ${ocultarNumeros(err?.message || String(err))}\n`); process.exit(1); }
    );
}
