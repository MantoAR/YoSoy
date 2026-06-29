const fs = require('fs');
const path = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';

// Leer como buffer para ver bytes reales
let content = fs.readFileSync(path, 'utf8');

// Reemplazar saltos de linea literales dentro de strings JS
// El problema es un caracter newline real dentro de un string con comillas simples
// Buscar patron: .join('\n') donde \n es un newline real
let fixed = content.replace(/\.join\('\n'\)/g, ".join('\\n')");

// También corregir el BOM y caracteres especiales en el blob
fixed = fixed.replace(/new Blob\(\['[^']*' \+/g, function(match) {
    return match.replace(/[\uFEFF\u00EF\u00BB\u00BF]/g, '');
});

let count = (content.match(/\.join\('\n'\)/g) || []).length;
console.log('Casos encontrados y corregidos:', count);

fs.writeFileSync(path, fixed, 'utf8');

// Verificar sintaxis
try {
    require('child_process').execSync('node --check ' + path, {stdio: 'pipe'});
    console.log('Sintaxis: OK');
} catch(e) {
    console.log('ERROR:', e.stderr.toString().split('\n').slice(0,3).join('\n'));
}