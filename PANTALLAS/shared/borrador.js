/* ═══════════════════════════════════════════════════════════════
   YoSoy · borrador local y cifrado de la nota de una sesión (window.YS.borrador)

   Mientras el profesional escribe la nota de una sesión, lo escrito se guarda en ESTE equipo, cifrado, para que una recarga, un cierre de pestaña o un corte de luz no lo
   pierdan. Es un BORRADOR: no es una evolución, no es una firma, no se manda al servidor ni lo ve nadie. Se descarta al guardar la sesión en la historia clínica.

   Qué hace y qué no hace (dicho sin vueltas):
     · El texto se cifra con AES-GCM de 256 bits. La clave se genera una vez por usuario en este navegador con extractable:false y vive en IndexedDB: el navegador la usa
       pero el código de la página no puede leerla ni exportarla.
     · Cada borrador lleva como dato asociado «usuario + sesión»: aunque alguien copie el registro de IndexedDB a otro turno u otro usuario, no se descifra.
     · Protege contra leer el disco o el perfil del navegador desde afuera. NO protege contra código malicioso que corra DENTRO de esta misma página, ni contra otra persona
       con la sesión abierta en el equipo. Por eso, al cerrar sesión de forma explícita se ofrece borrar los borradores.
     · Se borra al guardar con éxito, con «Descartar», a las 72 horas (se purga al abrir cualquier sala) y al cerrar sesión (previa pregunta si hay uno).
       El vencimiento de la sesión por inactividad NO lo borra (justamente es lo que más notas hace perder).
     · Si el navegador no tiene IndexedDB o WebCrypto (modo privado, políticas de la organización), disponible() da false y la pantalla lo dice: no finge que guarda.
     · Se guarda: nota, tipo de cierre, respuesta de «¿se realizó?» y la clave del cierre. NO se guarda transcripción ni emociones.

   La parte de cifrado y de almacenamiento recibe sus dependencias (subtle, almacén, reloj), así que se prueba en Node con un almacén en memoria
   (YoSoy.Tests/BorradorCifradoTests.cs). Contrato completo: docs/CIERRE_SESION.md.
   ═══════════════════════════════════════════════════════════════ */
(function (global) {
    "use strict";

    // TODO-VALIDAR: cuánto tiempo puede quedar una nota en borrador en el equipo del profesional (retención) lo tienen que validar los psicólogos y legal.
    // 72 horas es un valor conservador provisorio; sobre todo en equipos compartidos del consultorio.
    const VIGENCIA_MS = 72 * 60 * 60 * 1000;
    const VERSION = 1;
    const TEXTO = new TextEncoder();
    const LEER = new TextDecoder();

    const aad = (idUsuario, clave) => TEXTO.encode(`ys-borrador|v${VERSION}|${idUsuario}|${clave}`);
    const idDelRegistro = (idUsuario, clave) => `${idUsuario}|${clave}`;
    const aBase64 = bytes => { let s = ""; bytes.forEach(b => { s += String.fromCharCode(b); }); return btoa(s); };
    const deBase64 = texto => Uint8Array.from(atob(texto), c => c.charCodeAt(0));

    /**
     * @param {{ subtle: SubtleCrypto, aleatorio: (n:number)=>Uint8Array, almacen: Almacen, ahora?: ()=>number }} dependencias
     * Almacen: { leer(tabla, clave), escribir(tabla, clave, valor), borrar(tabla, clave), claves(tabla) }, todo con promesas.
     */
    function crearBorrador({ subtle, aleatorio, almacen, ahora = () => Date.now() }) {
        const CLAVES = "claves";
        const BORRADORES = "borradores";

        async function claveDelUsuario(idUsuario, crear) {
            let k = await almacen.leer(CLAVES, `usuario:${idUsuario}`);
            if (!k && crear) {
                k = await subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
                await almacen.escribir(CLAVES, `usuario:${idUsuario}`, k);
            }
            return k || null;
        }

        return {
            /** ¿Se puede guardar un borrador cifrado en este equipo? Lo prueba de verdad (escribe, lee y borra un registro). */
            async disponible() {
                try {
                    if (!subtle || !almacen) return false;
                    await almacen.escribir(CLAVES, "sonda", "ok");
                    const ok = (await almacen.leer(CLAVES, "sonda")) === "ok";
                    await almacen.borrar(CLAVES, "sonda");
                    return ok;
                } catch { return false; }
            },

            /** Guarda (reemplaza) el borrador de esa sesión. `datos` es un objeto simple. Devuelve el momento en que quedó guardado (ms). */
            async guardar(idUsuario, clave, datos) {
                const k = await claveDelUsuario(idUsuario, true);
                const iv = aleatorio(12);
                const cifrado = await subtle.encrypt({ name: "AES-GCM", iv, additionalData: aad(idUsuario, clave) }, k, TEXTO.encode(JSON.stringify(datos)));
                const guardadoEn = ahora();
                await almacen.escribir(BORRADORES, idDelRegistro(idUsuario, clave), {
                    v: VERSION, idUsuario: String(idUsuario), clave, guardadoEn, iv: aBase64(iv), datos: aBase64(new Uint8Array(cifrado))
                });
                return guardadoEn;
            },

            /** El borrador de esa sesión: { datos, guardadoEn }, o null si no hay, venció o no se puede descifrar (otro usuario, otro turno, registro alterado). */
            async leer(idUsuario, clave) {
                const r = await almacen.leer(BORRADORES, idDelRegistro(idUsuario, clave));
                if (!r || r.v !== VERSION) return null;
                if (ahora() - r.guardadoEn > VIGENCIA_MS) { await almacen.borrar(BORRADORES, idDelRegistro(idUsuario, clave)); return null; }
                const k = await claveDelUsuario(idUsuario, false);
                if (!k) return null;
                try {
                    const plano = await subtle.decrypt({ name: "AES-GCM", iv: deBase64(r.iv), additionalData: aad(idUsuario, clave) }, k, deBase64(r.datos));
                    return { datos: JSON.parse(LEER.decode(plano)), guardadoEn: r.guardadoEn };
                } catch { return null; }
            },

            async borrar(idUsuario, clave) { await almacen.borrar(BORRADORES, idDelRegistro(idUsuario, clave)); },

            /** Borra los borradores de más de 72 horas (de cualquier usuario: son texto cifrado). Devuelve cuántos. */
            async purgarVencidos() {
                let n = 0;
                for (const id of await almacen.claves(BORRADORES)) {
                    const r = await almacen.leer(BORRADORES, id);
                    if (!r || ahora() - r.guardadoEn > VIGENCIA_MS) { await almacen.borrar(BORRADORES, id); n++; }
                }
                return n;
            },

            /** Cuántos borradores tiene ese usuario en este equipo. */
            async cantidadDeUsuario(idUsuario) {
                return (await almacen.claves(BORRADORES)).filter(id => id.startsWith(`${idUsuario}|`)).length;
            },

            /** Borra todos los borradores de ese usuario Y su clave de cifrado (lo que quede no se podría descifrar de todos modos). */
            async borrarDeUsuario(idUsuario) {
                for (const id of await almacen.claves(BORRADORES)) if (id.startsWith(`${idUsuario}|`)) await almacen.borrar(BORRADORES, id);
                await almacen.borrar(CLAVES, `usuario:${idUsuario}`);
            }
        };
    }

    /** El almacén real: IndexedDB. Falla (rechaza) si el navegador no la ofrece o la bloquea. */
    function almacenIndexedDB(indexedDB) {
        let abierta = null;
        const abrir = () => abierta || (abierta = new Promise((ok, mal) => {
            if (!indexedDB) { mal(new Error("sin IndexedDB")); return; }
            const pedido = indexedDB.open("ys-borrador", 1);
            pedido.onupgradeneeded = () => { pedido.result.createObjectStore("borradores"); pedido.result.createObjectStore("claves"); };
            pedido.onsuccess = () => ok(pedido.result);
            pedido.onerror = () => mal(pedido.error);
            pedido.onblocked = () => mal(new Error("IndexedDB bloqueada"));
        }));
        const pedir = async (tabla, modo, f) => {
            const bd = await abrir();
            return new Promise((ok, mal) => {
                const tx = bd.transaction(tabla, modo);
                const r = f(tx.objectStore(tabla));
                tx.oncomplete = () => ok(r && r.result);
                tx.onerror = tx.onabort = () => mal(tx.error || new Error("transacción cancelada"));
            });
        };
        return {
            leer: (tabla, clave) => pedir(tabla, "readonly", s => s.get(clave)),
            escribir: (tabla, clave, valor) => pedir(tabla, "readwrite", s => s.put(valor, clave)),
            borrar: (tabla, clave) => pedir(tabla, "readwrite", s => s.delete(clave)),
            claves: tabla => pedir(tabla, "readonly", s => s.getAllKeys())
        };
    }

    const api = { crearBorrador, almacenIndexedDB, VIGENCIA_MS };
    if (typeof module !== "undefined" && module.exports) module.exports = api;   // las pruebas de Node
    if (!global || !global.document) return;

    const YS = global.YS = global.YS || {};
    const crypto_ = global.crypto;
    try {
        YS.borrador = crearBorrador({
            subtle: crypto_ && crypto_.subtle,
            aleatorio: n => crypto_.getRandomValues(new Uint8Array(n)),
            almacen: almacenIndexedDB(global.indexedDB)
        });
    } catch { YS.borrador = null; }

    /**
     * Al cerrar sesión de forma explícita: si hay borradores de este usuario, se pregunta antes de borrarlos. Devuelve true si se puede seguir con el cierre de sesión.
     * (YS.sesion.cerrar lo llama; sin este archivo cargado, no hace nada.)
     */
    YS.borradorAlCerrarSesion = async function (idUsuario) {
        try {
            if (!YS.borrador || !idUsuario) return true;
            const n = await YS.borrador.cantidadDeUsuario(idUsuario);
            if (!n) return true;
            const mensaje = n === 1 ? "Hay un borrador de nota sin guardar en este equipo. Si cerrás sesión se descarta." : `Hay ${n} borradores de notas sin guardar en este equipo. Si cerrás sesión se descartan.`;
            const sigue = YS.confirmar
                ? await YS.confirmar({ titulo: "Borradores sin guardar", mensaje, textoConfirmar: "Descartar y cerrar sesión", textoCancelar: "Volver", peligro: true })
                : global.confirm(mensaje);
            if (!sigue) return false;
            await YS.borrador.borrarDeUsuario(idUsuario);
            return true;
        } catch { return true; }   // un fallo del borrador no impide cerrar sesión
    };
})(typeof window !== "undefined" ? window : undefined);
