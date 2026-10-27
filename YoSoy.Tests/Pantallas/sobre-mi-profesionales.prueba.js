// Bloque A (prompt 08): la lista «Profesionales que preferís» de «Sobre mí» muestra solo los de la especialidad elegida. Corre con node (la lanza PantallasNodeTests.cs), con un DOM falso.
const fs = require("fs");
const path = require("path");
const vm = require("vm");
const raiz = process.argv[2];
let ok = 0, fallas = 0;
const verificar = (c, m) => { if (c) ok++; else { fallas++; console.log("FALLA: " + m); } };

const fuente = fs.readFileSync(path.join(raiz, "PANTALLAS", "Pantalla_Paciente", "js", "primeros_pasos.js"), "utf8");
const desde = fuente.indexOf("function filtrarProfesionalesPorEspecialidad");
const hasta = fuente.indexOf("function leerFormularioSobreMi");
verificar(desde > 0 && hasta > desde, "se encuentra filtrarProfesionalesPorEspecialidad");

const etiqueta = (esp, marcado = true) => { const c = { checked: marcado }; return { dataset: { especialidad: String(esp) }, hidden: false, c, querySelector: () => c }; };
function correr(etiquetas, idEspecialidad) {
    const nota = { textContent: "" };
    const ctx = vm.createContext({ document: { querySelectorAll: () => etiquetas, getElementById: id => id === "admProfNota" ? nota : null } });
    vm.runInContext(fuente.slice(desde, hasta) + "\nglobalThis.f = filtrarProfesionalesPorEspecialidad;", ctx);
    ctx.f(idEspecialidad);
    return nota.textContent;
}

let ls = [etiqueta(1), etiqueta(2), etiqueta(1)];
correr(ls, "1");
verificar(!ls[0].hidden && ls[1].hidden && !ls[2].hidden, "con la especialidad 1 solo se ven los de la 1");
verificar(ls[1].c.checked === false && ls[0].c.checked === true, "el que se oculta se desmarca y los visibles conservan su marca");
ls = [etiqueta(1), etiqueta(2)];
correr(ls, "");
verificar(ls.every(l => !l.hidden), "sin especialidad elegida se ven todos");
ls = [etiqueta(1), etiqueta(2)];
const nota = correr(ls, "9");
verificar(ls.every(l => l.hidden) && nota.includes("lo decide la recepción"), "si la especialidad no tiene profesionales asignables lo dice");
verificar(correr([], "1").includes("Solo se muestran"), "sin lista no falla");

console.log(`OK ${ok}, FALLAS ${fallas}`);
process.exit(fallas ? 1 : 0);
