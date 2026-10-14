# Parches sobre whatsapp-web.js

Aplicados con [patch-package](https://github.com/ds300/patch-package) — corren solos en
`postinstall` (`npm install`/`npm ci`, también en producción: `patch-package` está en
`dependencies`, no en `devDependencies`, porque el deploy real usa `npm ci --omit=dev`).

## `whatsapp-web.js+1.34.7.patch`

**Qué corrige:** en `Client.js`, `inject()`, el bloque que espera a que la sesión
termine de sincronizar (`AppState`/`change:hasSynced`) antes de disparar el evento
`ready`. El código original de esta versión solo reacciona a **cambios futuros** del
evento `change:hasSynced` — si `AppState.hasSynced` ya es `true` para cuando se llega a
esa línea (pasa con restauración de sesión rápida), el evento no se vuelve a emitir
nunca y `ready` no dispara, aunque `authenticated` sí. Confirmado con logs reales de
producción (Ubuntu 24.04, Node 22.23, whatsapp-web.js 1.34.7): `loading_screen 100%` y
`authenticated` (tres veces) sin ningún `ready` ni error.

El parche agrega el chequeo inmediato de `AppState.hasSynced` antes de registrar el
listener, además del listener para cambios futuros — mismo patrón, corroborado de forma
independiente en tres fuentes:

- [pedroslopez/whatsapp-web.js#5748](https://github.com/pedroslopez/whatsapp-web.js/pull/5748)
  (axlwild, 24 ene 2026) — la fuente original del fix. **GitHub la muestra como mergeada
  río arriba, pero el código fuente instalado de 1.34.7 (publicada en npm el 24 abr 2026,
  meses después) NO tiene este chequeo** — confirmado leyendo
  `node_modules/whatsapp-web.js/src/Client.js` directamente, no solo por el estado de la
  PR en GitHub. Sea cual sea la razón (rama distinta, release cortado de un commit
  anterior), el parche sigue haciendo falta para esta versión puntual.
- [wwebjs/whatsapp-web.js#5758](https://github.com/wwebjs/whatsapp-web.js/issues/5758)
  (28 ene 2026) — mismo síntoma reportado por otro usuario 4 días después, con el mismo
  fix sugerido en los comentarios (axlwild).
- Rama `timothydillan/whatsapp-web.js#fix/ready-event-not-firing` — misma lógica,
  cita la misma PR #5748 en un comentario propio.

(También se revisó `wwebjs/whatsapp-web.js#127084` y la rama
`DouglasReisofc/whatsapp-web.js#fix/events-ready-messages`, que tocan el mismo problema
pero con cambios mucho más amplios — no se tomó nada de ahí para este parche, que se
mantuvo deliberadamente mínimo y acotado a este único bloque.)

**No se afirma que esto esté "arreglado" en ninguna versión publicada de
whatsapp-web.js** — lo único confirmado es: el síntoma es real y conocido, el fix de
arriba lo corrige, y la versión 1.34.7 que tenemos pineada no lo incluye.

### Cómo regenerar este parche si hace falta tocarlo

```bash
# Editar directo en el código ya instalado:
#   node_modules/whatsapp-web.js/src/Client.js
npx patch-package whatsapp-web.js
```
Sobrescribe `whatsapp-web.js+1.34.7.patch`. Si se actualiza la versión de
whatsapp-web.js (ver el README principal, "Actualizar whatsapp-web.js"), hay que
revisar a mano si este parche sigue aplicando limpio — el nombre del archivo lleva la
versión, así que un cambio de versión deja un patch con el nombre viejo sin aplicarse
a la nueva carpeta de `node_modules` (patch-package avisa si no puede aplicarlo, no
falla en silencio).

## Problemas relacionados, investigados pero NO corregidos acá (fuera del alcance de
## este parche mínimo)

- **`authenticated` se emite varias veces (tres, en los logs de producción
  revisados):** `inject()` se vuelve a ejecutar en cada navegación de la página
  (`Client.js`, el listener de `framenavigated`, corre `await this.inject()`
  incondicionalmente) y ese bloque no tiene ninguna guarda contra registrarse más de
  una vez — cada ejecución agrega otro listener de `change:hasSynced`/`change:state`
  sin sacar los anteriores, así que un solo cambio real dispara N veces el callback que
  emite `authenticated`. No es ruido inofensivo: es una duplicación de listeners real.
  No se corrigió acá porque arreglarlo bien implica envolver un bloque bastante más
  grande que el de este parche (ver cómo lo resuelve
  `timothydillan/whatsapp-web.js#fix/ready-event-not-firing`, con una bandera
  `_authEventListenersInjected` alrededor de todo el registro de listeners de
  autenticación) — más alcance del que pidió este parche puntual.
- **`Failed to add page binding with name onQRChangedEvent: window['onQRChangedEvent']
  already exists!`** (visto en producción tras un LOGOUT, como `unhandledRejection`
  atrapado por la red de seguridad del servicio): confirmado en
  `util/Puppeteer.js`, `exposeFunctionIfAbsent` — hace `if (await existe) return;` y
  después `page.exposeFunction(...)`, sin que esas dos cosas sean atómicas. Como
  `inject()` se repite (mismo motivo que el punto de arriba), dos ejecuciones pueden
  ver "no existe" casi al mismo tiempo y las dos intentan exponerla. En el caso
  observado fue inofensivo (la ejecución que ganó la carrera dejó la función andando
  igual, por eso "igual se emite un QR nuevo") — pero la misma función se usa para
  registrar `onAppStateHasSyncedEvent`, `onAuthAppStateChangedEvent` y otras: no hay
  garantía de que una falla ahí sea siempre igual de inofensiva. No se tocó
  `exposeFunctionIfAbsent` en este parche — es compartida por mucho más código que lo
  que pidió este arreglo puntual.
