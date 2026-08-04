const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');

const viejo = ", 'Derivacion')\", con);";
const nuevo = ", N'Derivación')\", con);";

if (c.includes(nuevo)) {
    console.log('Ya estaba corregido.');
} else if (c.includes(viejo)) {
    c = c.replace(viejo, nuevo);
    fs.writeFileSync(path, c, 'utf8');
    console.log('TipoCierre corregido a Derivación (con tilde) OK');
} else {
    console.log('ERROR: no se encontro el texto a reemplazar');
}