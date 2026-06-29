const fs = require('fs');

// Corregir separador en dashboard del configurador
const confPath = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/js/dashboard.js';
let conf = fs.readFileSync(confPath, 'utf8');
conf = conf.replace(
    ".join(','); }).join(',');",
    ".join(';'); }).join(';');"
);
fs.writeFileSync(confPath, conf, 'utf8');
console.log('Configurador CSV OK');

// Corregir separador en dashboard del profesional
const profPath = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let prof = fs.readFileSync(profPath, 'utf8');
prof = prof.replace(
    ".join(','); }).join(',');",
    ".join(';'); }).join(';');"
);
fs.writeFileSync(profPath, prof, 'utf8');
console.log('Profesional CSV OK');

// Verificar sintaxis
const { execSync } = require('child_process');
try {
    execSync('node --check ' + confPath, {stdio: 'pipe'});
    console.log('Sintaxis configurador: OK');
} catch(e) {
    console.log('ERROR configurador:', e.stderr.toString().split('\n')[0]);
}
try {
    execSync('node --check ' + profPath, {stdio: 'pipe'});
    console.log('Sintaxis profesional: OK');
} catch(e) {
    console.log('ERROR profesional:', e.stderr.toString().split('\n')[0]);
}