const fs = require('fs');

// ── 1. Agregar CreadoPor y ConfirmadoPaciente al TurnoDetalleDAO ──
const daoPath = 'D:/YoSoy/DAOs/TurnoDetalleDAO.cs';
let dao = fs.readFileSync(daoPath, 'utf8');
let cambios = 0;

// Agregar columnas al SELECT_BASE
const selViejo = "t.Fecha, t.Hora, t.Modalidad, t.Motivo,\r\n               p.Mail AS MailProfesional, p.Telefono AS TelefonoProfesional";
const selViejoLF = "t.Fecha, t.Hora, t.Modalidad, t.Motivo,\n               p.Mail AS MailProfesional, p.Telefono AS TelefonoProfesional";
const selNuevo = "t.Fecha, t.Hora, t.Modalidad, t.Motivo,\n               ISNULL(t.ConfirmadoPaciente, 0) AS ConfirmadoPaciente, t.CodigoSala,\n               p.Mail AS MailProfesional, p.Telefono AS TelefonoProfesional";

if (dao.includes('ConfirmadoPaciente')) {
    console.log('1. DAO ya tenia las columnas');
} else if (dao.includes(selViejo)) {
    dao = dao.replace(selViejo, selNuevo);
    cambios++;
    console.log('1. Columnas agregadas al SELECT (CRLF) OK');
} else if (dao.includes(selViejoLF)) {
    dao = dao.replace(selViejoLF, selNuevo);
    cambios++;
    console.log('1. Columnas agregadas al SELECT (LF) OK');
} else {
    console.log('1. ERROR: no se encontro el SELECT_BASE');
}

// Agregar al Mapear
const mapViejo = "TelefonoProfesional= r[\"TelefonoProfesional\"]==DBNull.Value ? null : (string)r[\"TelefonoProfesional\"]\r\n    };";
const mapViejoLF = "TelefonoProfesional= r[\"TelefonoProfesional\"]==DBNull.Value ? null : (string)r[\"TelefonoProfesional\"]\n    };";
const mapNuevo = "TelefonoProfesional= r[\"TelefonoProfesional\"]==DBNull.Value ? null : (string)r[\"TelefonoProfesional\"],\n        ConfirmadoPaciente = r[\"ConfirmadoPaciente\"] != DBNull.Value && Convert.ToBoolean(r[\"ConfirmadoPaciente\"]),\n        CodigoSala         = r[\"CodigoSala\"]         == DBNull.Value ? null : (string)r[\"CodigoSala\"]\n    };";

if (dao.includes('ConfirmadoPaciente =')) {
    console.log('2. Mapear ya tenia las columnas');
} else if (dao.includes(mapViejo)) {
    dao = dao.replace(mapViejo, mapNuevo);
    cambios++;
    console.log('2. Mapear actualizado (CRLF) OK');
} else if (dao.includes(mapViejoLF)) {
    dao = dao.replace(mapViejoLF, mapNuevo);
    cambios++;
    console.log('2. Mapear actualizado (LF) OK');
} else {
    console.log('2. ERROR: no se encontro el final del Mapear');
}

fs.writeFileSync(daoPath, dao, 'utf8');

// ── 2. Agregar propiedades al modelo TurnoDetalle ──
const modPath = 'D:/YoSoy/Models/TurnoDetalle.cs';
let mod = fs.readFileSync(modPath, 'utf8');

if (mod.includes('ConfirmadoPaciente')) {
    console.log('3. Modelo ya tenia las propiedades');
} else {
    mod = mod.replace(
        "public string? TelefonoProfesional { get; set; }",
        "public string? TelefonoProfesional { get; set; }\r\n    public bool ConfirmadoPaciente { get; set; }\r\n    public string? CodigoSala { get; set; }"
    );
    fs.writeFileSync(modPath, mod, 'utf8');
    cambios++;
    console.log('3. Propiedades agregadas al modelo OK');
}

console.log('\nCambios: ' + cambios);