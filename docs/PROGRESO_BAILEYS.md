# Progreso: canal de WhatsApp con Baileys

Punto de retoma de la tarea larga "canal de WhatsApp con Baileys". Si se corta una sesión, **este archivo manda**: dice qué
está hecho, qué decidí y por qué, qué falta y qué pasos manuales le tocan a Agustín.

- **Rama:** `feat/whatsapp-baileys`, creada desde `origin/main` (b3c94af, PR #41). Sin merge a `main`.
- **Decisión de producto (Agustín):** Baileys (WhatsApp Web no oficial) en vez de la API oficial: más simple y menos RAM.
  Riesgo asumido: WhatsApp puede bloquear el número, por eso va un **número dedicado de la clínica**, nunca uno personal.
- **Límites duros:** nada de `.env`/secretos; sin deploy, sin `sudo`, sin tocar el VPS ni la base `yosoy`; **ningún test ni
  comando abre una conexión real a WhatsApp ni genera o escanea un QR**; sin merge a `main`; sin `--force`.
- **Última actualización:** 2026-10-16, tras la etapa 9 (dependencia).

## Estado de las etapas

La numeración es la del pedido. El orden de ejecución fue: primero los commits previos, después la **9** (la dependencia
condiciona cómo se escribe el resto) y recién ahí la 1 a la 8.

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| previo | Frase de `DESARROLLO_LOCAL.md` (base de pruebas), pedida aparte | hecho | `6e846be` |
| previo | Línea suelta ` main` al final de `DEPLOY.md` (resto de un conflicto de merge) | hecho | `c384e1c` |
| — | Este archivo | hecho | `892ae1e` |
| 9 | Dependencia: paquete, versión exacta, `npm audit`, red de seguridad de los tests | **hecho** | (este commit) |
| 1 | Canal Baileys en `whatsapp-service` + `Mensajeria__CanalActivo=Baileys` en la API | pendiente | — |
| 2 | Sesión persistente (ruta configurable, fuera del repo, permisos, `.gitignore`) | pendiente | — |
| 3 | Vinculación por QR (comando y endpoint local) | pendiente | — |
| 4 | Reconexión con backoff y manejo de `loggedOut` | pendiente | — |
| 5 | Anti-bloqueo (ritmo, pausas, tope diario, consentimiento) | pendiente | — |
| 6 | Privacidad en logs | pendiente | — |
| 7 | Salud y apagado ordenado | pendiente | — |
| 8 | Unidad systemd, `DEPLOY.md`, `SERVIDOR.md` | pendiente | — |

## Cómo retomar

1. `git switch feat/whatsapp-baileys` y mirar `git log --oneline origin/main..HEAD`: un commit por etapa.
2. Tests del servicio: `cd whatsapp-service && npm test` (Node 22 o más nuevo; no necesita red ni WhatsApp).
3. Si se tocó la API: `Scripts\correr-tests.ps1` contra `yosoy_test` (**nunca** contra `yosoy`).
4. Seguir por la primera etapa en `pendiente` de la tabla de arriba.

## Hallazgos de la lectura inicial (antes de tocar nada)

- **El servicio ya existe y usa `whatsapp-web.js`** (Puppeteer + Chromium, `MemoryMax=2G`). Baileys se agrega como un
  **segundo backend** detrás de la misma interfaz HTTP; el actual queda como está.
- **La interfaz que usa la API hoy** es HTTP en loopback: `POST /enviar` (`{telefono, texto}` → `{exito, idExterno, error}`),
  `GET /estado` y `POST /desvincular`, los tres con `X-Shared-Secret`; el servicio le reenvía los mensajes entrantes a
  `POST {API_BASE_URL}/mensajeria/whatsapp/entrante`. `CanalWhatsAppWebJs` y `WhatsAppAdminService` (API) hablan eso y no
  saben qué librería hay detrás.
- **`Mensajeria:CanalActivo` hoy acepta `Simulado` y `WhatsAppWebJs`**; cualquier otro valor frena el arranque. El pedido dice
  "Simulado y Baileys": se **agrega** `Baileys` y se **conserva** `WhatsAppWebJs`, porque el servidor puede tener esa
  variable cargada y sacarla rompería el arranque de la API en el próximo deploy.
- **`privacidad.js` no tenía ninguna regla de consentimiento**: solo `ocultarNumeros` (enmascarar teléfonos en logs). El pedido
  habla de "consentimiento/opt-in según privacidad.js"; la política de consentimiento se **escribe** ahí (etapa 5).
- **Hoy la API le manda WhatsApp a cualquier paciente con teléfono cargado** (`NotificadorTurnos.EnviarChat`): avisos de
  turno y recordatorios, sin ningún chequeo de consentimiento propio del canal. Es justo lo que la etapa 5 acota.
- **`YoSoy_Handoff_Migracion.md` no está** en el repo ni en `docs/` (ni en `Documents`, `Downloads`, `Desktop` ni bajo
  `D:\Claude`). El diseño sale del pedido y del código existente.
- **`DEPLOY.md` terminaba con una línea suelta ` main`** (resto del conflicto de merge del PR #34): se sacó en un commit mínimo.
- **El nodo local es Node 24 y el VPS usa Node 22**: el código no puede usar nada que exista solo en 24. Baileys exige Node >= 20.

## Dependencia (etapa 9): lo que encontré en npm

Consultado el 2026-10-16 con `npm view` (solo lectura) y con instalaciones de prueba en carpetas aparte (fuera del repo, con
`--package-lock-only --ignore-scripts`).

| | `@whiskeysockets/baileys@6.7.24` | `@whiskeysockets/baileys@7.0.0-rc14` |
|---|---|---|
| Estado | **última estable** (tag `legacy`) | candidata (RC), tag `latest` |
| Publicada | 2026-07-29 | 2026-07-29 |
| `engines` | Node >= 20 | Node >= 20 |
| Módulo | **ESM puro** (`"type": "module"`) | ESM puro |
| Paquetes en el árbol | 115 | 95 |
| Dependencias por git | **sí: `libsignal`** (`git+ssh://`, sin integridad en el lock) | no |
| Paquete nuevo | — | `whatsapp-rust-bridge@0.5.4` (WASM, un solo mantenedor) |
| `npm audit` | 0 vulnerabilidades | 0 vulnerabilidades |
| Advisory crítico GHSA-qvv5-jq5g-4cgg | parcheado (desde 6.7.22) | parcheado (desde rc12) |

- **Nombre del paquete:** `@whiskeysockets/baileys` es el que indica el README del proyecto. El nombre sin scope `baileys`
  publica lo mismo (mismos mantenedores: `purpshell`, `auties00`, `shelltear`), ambos desde GitHub Actions con atestación de
  procedencia SLSA. Se usa el que figura en el README. Ojo con los paquetes de nombre parecido (hay forks y copias que no son
  del proyecto, y esta librería maneja las credenciales de la cuenta): el nombre exacto importa, y por eso la versión va fija.
- **Elegí `6.7.24`** porque el pedido dice "última versión estable" y la 7.x todavía es RC. Pero está en **modo mantenimiento**:
  los mantenedores recomiendan pasar a la 7.x.
- **El problema de `libsignal` por git:** el lock lo deja como `git+ssh://git@github.com/...#<commit>` sin hash de integridad,
  y el VPS no tiene clave SSH contra GitHub: `npm ci` fallaría (es el mismo problema que ya documentó el README del servicio
  con `whatsapp-web.js`). Comparé archivo por archivo el commit de git con `libsignal@6.0.0` publicado en npm: `index.js`,
  `index.d.ts`, `package.json` y todo `src/` son **idénticos**. Se resuelve con `"overrides": {"libsignal": "6.0.0"}` y queda
  todo instalado desde el registro con integridad.
- **Scripts de instalación:** `preinstall` de Baileys es `node ./engine-requirements.js` (solo chequea Node >= 20); `protobufjs`
  tiene su `postinstall` conocido. `prepare` (que compila) no corre al instalar desde el registro.
- **Para pasar a la 7.x** alcanza con cambiar la versión exacta y sacar el `override`; la capa de integración (`baileys/cargar.js`)
  usa solo un subconjunto chico de la librería y una prueba de contrato lo vigila.

### Qué quedó hecho en la etapa 9

- `whatsapp-service/package.json`: `"@whiskeysockets/baileys": "6.7.24"` (**exacta, sin `^`**) y
  `"overrides": {"libsignal": "6.0.0"}`. Instalado con `npm install --save-exact --ignore-scripts`.
- `package-lock.json`: solo **altas** (99 paquetes), ningún paquete existente cambió de versión, origen ni hash; `libsignal` y
  Baileys salen de `registry.npmjs.org` y **todo paquete con origen trae `integrity`** (sin dependencias por git).
- **Instalación como en el VPS reproducida** en una carpeta aparte: `npm ci --omit=dev` desde el lock → 405 paquetes, 147 MB,
  sin errores (con `--ignore-scripts` y `PUPPETEER_SKIP_DOWNLOAD=1`, para no bajar Chromium).
- **`npm audit` del servicio: 13 altas, exactamente las mismas de antes** (9 de la cadena de descarga de Chromium de
  `puppeteer`/`whatsapp-web.js` y 4 de `patch-package`); **ninguna viene de Baileys ni de sus dependencias**. Si algún día se
  saca `whatsapp-web.js`, quedan solo las 4 de `patch-package`.
- **Costo de memoria medido:** importar Baileys suma ~48 MB de RSS (31 → 79 MB) y tarda ~0,5 s; una conexión activa suma lo que
  use la sesión, pero no hay navegador. `whatsapp-web.js` lleva Chromium (el README del servicio ya documentaba que llegó a
  `MemoryMax=2G`).
- `baileys/cargar.js`: **único lugar** que importa la librería (con `import()` dinámico: es ESM y el servicio es CommonJS) y
  devuelve una "fachada" con lo que se usa; si la versión instalada no expone algo, el servicio no arranca y lo dice.
- **Red de seguridad de los tests** (`test/helpers/sin-red.js`): `npm test` ahora carga con `--require` un bloqueo que hace
  fallar cualquier conexión o consulta DNS que no sea loopback (`ERR_RED_BLOQUEADA`), en cada archivo de test. Así ningún test
  puede hablar con WhatsApp ni con internet, ni por error. Sus pruebas usan direcciones de documentación (RFC 5737), que
  no existen en internet.
- **Pruebas** (`npm test`, 29 en total, 17 nuevas): contrato con la librería real (carga el paquete **sin conectar**), versión
  exacta, `override` y lock sin git, bloqueo de red, y una guardia que verifica que ningún test importa el paquete real ni
  llama a `makeWASocket`. Verificadas con mutaciones (`^` en la versión, sin `override`, `libsignal` por git, un test que importa
  el paquete real): cada una hace fallar la prueba correspondiente.

## Decisiones tomadas (para revisar)

1. **Dos backends, `webjs` por defecto.** `WHATSAPP_BACKEND=baileys` activa el nuevo; sin la variable, el servicio se comporta
   exactamente como hoy. Así el próximo deploy no cambia nada hasta que Agustín lo decida.
2. **`WhatsAppWebJs` se conserva en la API** además de `Baileys` (ver arriba).
3. **Variable propia para la sesión de Baileys** (`WHATSAPP_BAILEYS_SESSION_PATH`), obligatoria, sin valor por defecto: así
   nunca puede terminar dentro del repo por omisión ni mezclarse con la carpeta de `whatsapp-web.js`.

## Pasos manuales para Agustín

(Se completa etapa por etapa; nada de esto lo hice ni lo voy a hacer yo.)

- _Todavía ninguno._

## Qué falta / pendiente de decisión

- _Se completa al final de cada etapa._
