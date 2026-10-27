// Bloque E (prompt 08): provincia con autocompletado (PANTALLAS/shared/provincias.js): el filtro, el nombre canónico, la validación y el combobox con teclado y ARIA, con un DOM falso.
// Corre con node (la lanza PantallasNodeTests.cs).
const path = require("path");
const raiz = process.argv[2];
let ok = 0, fallas = 0;
const verificar = (c, m) => { if (c) ok++; else { fallas++; console.log("FALLA: " + m); } };

globalThis.YS = {};
const P = require(path.join(raiz, "PANTALLAS", "shared", "provincias.js"));
verificar(YS.provincias === P, "se registra en YS.provincias");

// ── La lista ──
verificar(P.LISTA.length === 24, "23 provincias + la Ciudad Autónoma de Buenos Aires = 24 (hay " + P.LISTA.length + ")");
verificar(P.LISTA.includes("Ciudad Autónoma de Buenos Aires") && P.LISTA.includes("Tierra del Fuego Antártida e Islas del Atlántico Sur") && P.LISTA.includes("Tucumán"), "están los nombres largos y con tilde tal cual los pidió el dueño");
verificar(new Set(P.LISTA).size === 24, "sin repetidas");

// ── Filtro ──
const f = t => P.filtrar(t).join("|");
verificar(f("Cor") === "Córdoba|Corrientes", "«Cor» sugiere Córdoba y Corrientes (" + f("Cor") + ")");
verificar(f("cor") === "Córdoba|Corrientes" && f("COR") === "Córdoba|Corrientes", "sin distinguir mayúsculas");
verificar(f("cordoba") === "Córdoba" && f("CÓRDOBA") === "Córdoba" && f("córdoba") === "Córdoba", "sin distinguir tildes: «cordoba» encuentra Córdoba");
verificar(f("") === "" && f("   ") === "" && f(null) === "" && f(undefined) === "", "vacío no muestra lista");
verificar(f("xyz") === "" && f("zzzz") === "" && f("Madrid") === "", "algo inexistente no muestra nada");
verificar(f("Sa") === "Salta|San Juan|San Luis|Santa Cruz|Santa Fe|Santiago del Estero", "«Sa» → las que empiezan con Sa (" + f("Sa") + ")");
verificar(f("rio") === "Entre Ríos|La Rioja|Río Negro" && f("entre") === "Entre Ríos", "«rio» sin tilde encuentra Río Negro (y las que tienen esa palabra: Entre Ríos, La Rioja)");
verificar(f("aires").includes("Buenos Aires") && f("aires").includes("Ciudad Autónoma de Buenos Aires"), "con 3 letras o más también por una palabra («aires»)");
verificar(f("n") === "Neuquén", "una sola letra: solo las que EMPIEZAN con ella (no por palabras sueltas): " + f("n"));
verificar(f("  cor  ") === "Córdoba|Corrientes", "los espacios de más no importan");

// ── Nombre canónico y error ──
verificar(P.canonica("cordoba") === "Córdoba" && P.canonica(" CÓRDOBA ") === "Córdoba" && P.canonica("ciudad autonoma de buenos aires") === "Ciudad Autónoma de Buenos Aires", "canonica devuelve el nombre con su tilde");
verificar(P.canonica("Cor") === null && P.canonica("Cba") === null && P.canonica("") === null && P.canonica(null) === null, "una parte, una sigla o vacío no son una provincia");
verificar(P.error("xyz") === "Elegí una provincia de la lista." && P.MENSAJE_ERROR === "Elegí una provincia de la lista.", "algo que no es de la lista: «Elegí una provincia de la lista.»");
verificar(P.error("Córdoba") === null && P.error("cordoba") === null && P.error("") === null, "una provincia, o vacío (lo pide «requerido»), no es error");
verificar(P.error("Cba", "Cba") === null && P.error("Cba", "Cordoba capital") === "Elegí una provincia de la lista.", "el valor viejo ya guardado se acepta tal cual; otro texto inválido no");

// ── Combobox con un DOM falso: ARIA, teclado, mouse ──
class Elemento {
    constructor(tag) { this.tagName = tag; this.attrs = {}; this.children = []; this.listeners = {}; this.hidden = false; this.value = ""; this.id = ""; this.className = ""; this.textContent = ""; this.parentElement = null; this.classes = new Set(); }
    setAttribute(n, v) { this.attrs[n] = String(v); }
    getAttribute(n) { return this.attrs[n]; }
    removeAttribute(n) { delete this.attrs[n]; }
    addEventListener(n, f) { (this.listeners[n] ||= []).push(f); }
    removeEventListener(n, f) { this.listeners[n] = (this.listeners[n] || []).filter(x => x !== f); }
    dispatchEvent(e) { (this.listeners[e.type] || []).forEach(f => f(e)); return true; }
    get classList() { const s = this.classes; return { add: c => s.add(c), remove: c => s.delete(c), toggle: (c, on) => on ? s.add(c) : s.delete(c) }; }
    appendChild(c) { c.parentElement = this; this.children.push(c); return c; }
    replaceChildren(...cs) { this.children = []; cs.forEach(c => this.appendChild(c)); }
    insertAdjacentElement(_pos, el) { this.siguiente = el; return el; }
    remove() {}
}
globalThis.Event = class { constructor(type) { this.type = type; } };
const doc = { createElement: t => new Elemento(t) };
function nuevo() {
    const padre = new Elemento("div"); const input = new Elemento("input"); input.id = "provincia"; input.parentElement = padre;
    const c = P.combo(input, { documento: doc });
    const lista = input.siguiente;
    const escribir = t => { input.value = t; input.dispatchEvent(new Event("input")); };
    const tecla = (key) => { let evitada = false; input.dispatchEvent({ type: "keydown", key, preventDefault: () => { evitada = true; } }); return evitada; };
    return { input, lista, escribir, tecla, padre, c };
}

let t = nuevo();
verificar(t.input.getAttribute("role") === "combobox" && t.input.getAttribute("aria-autocomplete") === "list" && t.input.getAttribute("aria-expanded") === "false" && t.input.getAttribute("aria-controls") === t.lista.id, "ARIA del campo: combobox, autocomplete=list, expanded=false, controls");
verificar(t.lista.getAttribute("role") === "listbox" && t.lista.hidden === true, "la lista (listbox) empieza oculta: no se abre sola ni con un clic");
t.escribir("");
verificar(t.lista.hidden === true, "sin escribir no hay lista");
t.escribir("Cor");
verificar(t.lista.hidden === false && t.input.getAttribute("aria-expanded") === "true", "al escribir aparece la lista");
verificar(t.lista.children.length === 2 && t.lista.children.map(li => li.textContent).join("|") === "Córdoba|Corrientes" && t.lista.children.every(li => li.getAttribute("role") === "option"), "dos opciones con role=option");
t.tecla("ArrowDown");
verificar(t.lista.children[0].getAttribute("aria-selected") === "true" && t.input.getAttribute("aria-activedescendant") === t.lista.children[0].id, "flecha abajo marca la primera (aria-activedescendant)");
t.tecla("ArrowDown"); t.tecla("ArrowDown");
verificar(t.lista.children[0].getAttribute("aria-selected") === "true", "da la vuelta al llegar al final");
t.tecla("ArrowUp");
verificar(t.lista.children[1].getAttribute("aria-selected") === "true", "flecha arriba retrocede");
const evitoEnter = t.tecla("Enter");
verificar(t.input.value === "Corrientes" && t.lista.hidden === true && evitoEnter, "Enter elige la activa (nombre canónico) y cierra; no envía el formulario");
t.escribir("Cor"); t.tecla("Escape");
verificar(t.lista.hidden === true && t.input.value === "Cor", "Escape cierra sin elegir");
t.escribir("xyz");
verificar(t.lista.hidden === true && t.lista.children.length === 0, "algo inexistente: no se muestra nada");
t.escribir("cordoba"); t.tecla("Enter");
verificar(t.input.value === "Córdoba", "con una sola coincidencia, Enter la elige aunque no se haya movido con las flechas");
t.escribir("cordoba"); t.input.dispatchEvent(new Event("blur"));
verificar(t.input.value === "Córdoba" && t.lista.hidden === true, "al salir del campo, «cordoba» queda como «Córdoba»");
t.escribir("Cba"); t.input.dispatchEvent(new Event("blur"));
verificar(t.input.value === "Cba", "lo que no es una provincia no se cambia (lo marca la validación)");
t.escribir("Cor"); t.lista.children[0].dispatchEvent({ type: "mousedown", preventDefault() {} });
verificar(t.input.value === "Córdoba", "tocar o hacer clic en una sugerencia la elige (celular)");
let cambios = 0; t.input.addEventListener("change", () => cambios++);
t.escribir("Cor"); t.lista.children[1].dispatchEvent({ type: "touchstart", preventDefault() {} });
verificar(t.input.value === "Corrientes" && cambios === 1, "touchstart elige y avisa el cambio");

console.log(`OK ${ok}, FALLAS ${fallas}`);
process.exit(fallas ? 1 : 0);
