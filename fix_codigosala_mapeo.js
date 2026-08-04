const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');

const ancla = 'idProfesionalDerivado   = reader["IdProfesionalDerivado"] == DBNull.Value ? (int?)null : (int)reader["IdProfesionalDerivado"],';
const linea = '\n            codigoSala              = reader["CodigoSala"] == DBNull.Value ? "" : reader["CodigoSala"].ToString(),';

if (c.includes('codigoSala              = reader')) {
    console.log('Ya estaba, no se hace nada.');
} else if (c.includes(ancla)) {
    c = c.replace(ancla, ancla + linea);
    fs.writeFileSync(path, c, 'utf8');
    console.log('Linea codigoSala agregada al mapeo OK');
} else {
    console.log('ERROR: no se encontro el ancla');
}
