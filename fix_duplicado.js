const fs = require('fs');
const path = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let lines = fs.readFileSync(path, 'utf8').split('\n');

// Encontrar las dos funciones reportes
let primeraLinea = -1;
let segundaLinea = -1;
for (let i = 0; i < lines.length; i++) {
    if (lines[i].includes('async function reportes()')) {
        if (primeraLinea === -1) primeraLinea = i;
        else segundaLinea = i;
    }
}

console.log('Primera funcion reportes en linea:', primeraLinea + 1);
console.log('Segunda funcion reportes en linea:', segundaLinea + 1);

// Encontrar el fin de la primera funcion (buscar la siguiente funcion async o function)
let finPrimera = -1;
for (let i = primeraLinea + 1; i < segundaLinea; i++) {
    if (lines[i].match(/^async function |^function /)) {
        finPrimera = i;
        break;
    }
}

if (finPrimera === -1) finPrimera = segundaLinea;

console.log('La primera funcion va de linea', primeraLinea + 1, 'a', finPrimera);

// Eliminar las lineas de la primera funcion
lines.splice(primeraLinea, finPrimera - primeraLinea);

fs.writeFileSync(path, lines.join('\n'), 'utf8');
console.log('Funcion duplicada eliminada OK');

// Verificar
const resultado = fs.readFileSync(path, 'utf8');
const count = (resultado.match(/async function reportes\(\)/g) || []).length;
console.log('Funciones reportes restantes:', count);