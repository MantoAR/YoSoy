// Privacidad de este proceso:
//   1. LOGS: el número de teléfono de un paciente no se escribe completo (ocultarNumeros), y con Baileys nada que sea un secreto
//      (crearLogSeguro). Nunca se loguea el texto de un mensaje, el QR, las credenciales ni el secreto compartido.
//   2. CONSENTIMIENTO (con Baileys): reglas de qué cuenta como "esta persona quiere recibir mensajes por WhatsApp" y cómo se guarda
//      sin guardar el teléfono (huellaDeTelefono, interpretarComando). El registro que las usa está en consentimiento.js.
//
// ── 1. Logs ──
//
// Un "posible número" es una secuencia de 7 o más dígitos, con o sin "+" y con espacios, guiones, puntos o paréntesis
// sueltos entre dígitos ("1122334455", "+54 9 11 2233-4455", "5491122334455@c.us", "10234567890123@lid"). Se deja solo
// el final (los últimos 4 dígitos): alcanza para saber de quién se habla en una consulta puntual sin que el log sea una
// lista de teléfonos. Lo que tiene menos de 7 dígitos (puertos, ids como "cola-12") no se toca.
const crypto = require("node:crypto");
const { ultimosDiez } = require("./baileys/numeros");

const POSIBLE_NUMERO = /\+?\d(?:[\s().-]?\d){6,}/g;
// La versión de WhatsApp Web (2.3000.1037753511) parece un número por sus dígitos, pero no es de nadie y sirve para diagnosticar.
const VERSION_DE_WHATSAPP_WEB = /^\d{1,2}\.\d{4}\.\d{7,}$/;

function ocultarNumeros(valor) {
    return String(valor ?? "").replace(POSIBLE_NUMERO, coincidencia =>
        VERSION_DE_WHATSAPP_WEB.test(coincidencia) ? coincidencia : "***" + coincidencia.replace(/\D/g, "").slice(-4));
}

// Un log que filtra todo lo que se le escribe, por si algún mensaje trae lo que no debe (por ejemplo, el error de una librería que
// repite una dirección o una cabecera): primero tapa los secretos y después enmascara los teléfonos. En ESE orden, porque un secreto
// con dígitos y guiones quedaría a medio enmascarar y ya no se lo reconocería. `secretos` es una función porque hay secretos que
// cambian (el QR vigente); los que miden menos de 8 caracteres se ignoran (taparían cualquier cosa).
function crearLogSeguro(log, { secretos = () => [] } = {}) {
    const limpiar = mensaje => {
        let texto = String(mensaje ?? "");
        for (const secreto of secretos()) {
            if (typeof secreto === "string" && secreto.length >= 8) texto = texto.split(secreto).join("[secreto]");
        }
        return ocultarNumeros(texto);
    };
    return { info: mensaje => log.info(limpiar(mensaje)), error: mensaje => log.error(limpiar(mensaje)) };
}

// ── 2. Consentimiento ──
//
// Una persona acepta recibir mensajes cuando (a) le escribe ella primero al número de la clínica, o (b) la API afirma que lo aceptó
// (optIn:true en el pedido). Y deja de aceptar cuando escribe BAJA (o STOP...): eso pesa más que cualquier otra constancia, hasta
// que escriba ALTA. Acá solo están las reglas; el registro persistente es consentimiento.js.

// El mensaje ENTERO tiene que ser una de estas palabras (sin distinguir mayúsculas, tildes ni signos): "cancelar" NO está, porque en
// el bot es cancelar un turno, y "baja" metida en una frase ("el turno se dio de baja") tampoco cuenta.
const PALABRAS_DE_BAJA = new Set(["baja", "stop", "parar", "basta", "no molestar", "desuscribir", "desuscribirme", "dar de baja", "darme de baja",
    "no quiero recibir mensajes", "no quiero mas mensajes"]);
const PALABRAS_DE_ALTA = new Set(["alta", "start", "suscribir", "suscribirme", "reanudar", "quiero recibir mensajes"]);

function normalizarComando(texto) {
    return String(texto ?? "")
        .normalize("NFD").replace(/[\u0300-\u036f]/g, "")        // sin tildes
        .toLowerCase()
        .replace(/[^a-z0-9\s]/g, " ")                              // sin signos
        .replace(/\s+/g, " ")
        .trim();
}

// "baja" | "alta" | null
function interpretarComando(texto) {
    const limpio = normalizarComando(texto);
    if (PALABRAS_DE_BAJA.has(limpio)) return "baja";
    if (PALABRAS_DE_ALTA.has(limpio)) return "alta";
    return null;
}

// La clave con la que se calculan las huellas se deriva del secreto compartido del servicio: no está en ningún archivo de datos, así
// quien se lleve un archivo de datos solo no puede reconstruir los teléfonos probando números. (Si el secreto compartido se
// cambia, las huellas viejas dejan de coincidir: el registro "se olvida" y se vuelve al valor seguro de no enviar.)
function derivarClaveDeHuellas(secreto) {
    if (!secreto) throw new Error("Falta el secreto compartido para calcular las huellas de los teléfonos.");
    return crypto.createHmac("sha256", String(secreto)).update("yosoy-whatsapp-huellas-v1").digest();
}

// La huella de un teléfono (32 caracteres hexadecimales): HMAC-SHA256 de sus últimos 10 dígitos, igual que la API identifica a un
// paciente ("+54 9 11 5555-0001" y "11 5555-0001" dan la misma huella). null si no puede ser un teléfono (menos de 8 dígitos).
function huellaDeTelefono(telefono, clave) {
    const digitos = ultimosDiez(telefono);
    if (digitos.length < 8) return null;
    return crypto.createHmac("sha256", clave).update(digitos).digest("hex").slice(0, 32);
}

module.exports = { ocultarNumeros, crearLogSeguro, interpretarComando, derivarClaveDeHuellas, huellaDeTelefono };
