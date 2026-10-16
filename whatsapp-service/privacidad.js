// Privacidad en los logs de este proceso: el número de teléfono de un paciente no se escribe completo.
//
// Un "posible número" es una secuencia de 7 o más dígitos, con o sin "+" y con espacios, guiones, puntos o paréntesis
// sueltos entre dígitos ("1122334455", "+54 9 11 2233-4455", "5491122334455@c.us", "10234567890123@lid"). Se deja solo
// el final (los últimos 4 dígitos): alcanza para saber de quién se habla en una consulta puntual sin que el log sea una
// lista de teléfonos. Lo que tiene menos de 7 dígitos (puertos, ids como "cola-12") no se toca.
const POSIBLE_NUMERO = /\+?\d(?:[\s().-]?\d){6,}/g;

function ocultarNumeros(valor) {
    return String(valor ?? "").replace(POSIBLE_NUMERO, coincidencia => "***" + coincidencia.replace(/\D/g, "").slice(-4));
}

module.exports = { ocultarNumeros };
