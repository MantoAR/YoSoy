// Entorno armado para probar ServicioBaileys: carpeta de sesión temporal (fuera del repo), fachada falsa, reloj falso, un log que
// captura las líneas y un reenviador que anota lo que se le mandaría a la API. Nada abre red.
"use strict";

const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { ServicioBaileys } = require("../../baileys/servicio");
const { crearSesion } = require("../../baileys/sesion");
const { fachadaFalsa } = require("./baileys-falso");
const { RelojFalso, esperarHasta } = require("./reloj-falso");

function crearEntorno({ registrada = false, opcionesFachada, opcionesServicio = {}, sesionPersonalizada } = {}) {
    const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-ws-"));
    const dirSesion = path.join(raiz, "sesion");
    if (registrada) {
        fs.mkdirSync(dirSesion, { recursive: true });
        fs.writeFileSync(path.join(dirSesion, "creds.json"), JSON.stringify({ registered: false, me: { id: "5491100000000:7@s.whatsapp.net" } }));
    }

    const falso = fachadaFalsa(opcionesFachada);
    const reloj = new RelojFalso();
    const lineas = [];
    const log = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
    const reenviados = [];
    const sesion = sesionPersonalizada ? sesionPersonalizada(dirSesion, log) : crearSesion({ dir: dirSesion, log });
    const servicio = new ServicioBaileys({
        fachada: falso.fachada,
        sesion,
        log,
        reenviar: (telefono, texto) => { reenviados.push({ telefono, texto }); },
        reloj,
        ...opcionesServicio
    });

    return {
        servicio, falso, reloj, lineas, reenviados, sesion, raiz, dirSesion,
        // Arranca y espera a que el servicio haya creado su primer socket.
        async iniciar() {
            servicio.iniciar();
            await esperarHasta(() => falso.sockets.length >= 1, { descripcion: "la creación del primer socket" });
            await reloj.avanzar(0);
            return falso.ultimo;
        },
        // Todo el log junto, para buscar en él cosas que NO tienen que aparecer.
        logCompleto: () => lineas.join("\n"),
        limpiar() { fs.rmSync(raiz, { recursive: true, force: true }); }
    };
}

module.exports = { crearEntorno };
