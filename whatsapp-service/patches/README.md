# Parches sobre whatsapp-web.js

Aplicados con [patch-package](https://github.com/ds300/patch-package) — corren solos en
`postinstall` (`npm install`/`npm ci`, también en producción: `patch-package` está en
`dependencies`, no en `devDependencies`, porque el deploy real usa `npm ci --omit=dev`).

**Carpeta vacía por ahora** (`patch-package` sigue instalado, sin ningún `.patch` que
aplicar — confirmado que el `postinstall` no falla así, "No patch files found"). El
parche que había acá (`whatsapp-web.js+1.34.7.patch`) se sacó al cambiar
`whatsapp-web.js` a un commit puntual de `main` que ya incluye ese arreglo y más — ver
"`main` ya incluye esto" más abajo y el README principal, "Actualizar whatsapp-web.js".
Se deja toda la historia de abajo porque documenta el diagnóstico real que llevó hasta
ahí — si en algún momento hace falta un parche de nuevo (otra versión, otro problema),
el mismo mecanismo sigue andando.

## `whatsapp-web.js+1.34.7.patch` (histórico — ya no está en el repo)

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

## Diagnóstico de la página: qué pasa entre "authenticated" y "ready"

Para la vuelta de `WHATSAPP_DEBUG=1` (ver el README principal). Esto es lo que corre
dentro de `Client.js`, `inject()`, en el callback de `onAppStateHasSyncedEvent`
(líneas ~298-377 de la 1.34.7, **después** de nuestro parche de arriba — nuestro parche
solo adelanta CUÁNDO se llama a este callback, no cambia nada de lo que sigue acá
adentro), en orden, con qué depende cada paso y si podría colgarse sin tirar un error
que lleguemos a ver:

1. `this.emit(Events.AUTHENTICATED, ...)` — síncrono, no puede colgarse.
2. `await this.pupPage.evaluate(() => typeof window.WWebJS !== 'undefined')` — depende
   de que el contexto de ejecución de la página responda. `page.evaluate()` no tiene
   timeout propio en Puppeteer: si la página está trabada, esto espera para siempre,
   sin ningún error.
3. Si `window.WWebJS` todavía no existe (caso normal, primera vez):
   - `await webCache.persist(...)` (solo si `webVersionCache.type === 'local'`, que es
     nuestro caso por default sin `WHATSAPP_WEB_VERSION`) — depende del disco local.
   - `await this.pupPage.evaluate(LoadUtils)` — **el candidato más fuerte.** `LoadUtils`
     (`util/Injected/Utils.js`) es un script grande que arma `window.WWebJS` enganchando
     un montón de modelos internos de WhatsApp Web (`WAWebChatCollection`,
     `WAWebMsgKey`, etc.) — exactamente el tipo de código que un cambio interno de
     WhatsApp Web (como el renombre `_serialized`→`$1` de 2.3000.x, aunque confirmamos
     que ESE caso puntual no es esto — ver más abajo) podría romper en silencio si algo
     que `LoadUtils` espera encontrar ya no está donde espera.
   - Un `while` que sondea `window.WWebJS != undefined` cada 200ms, hasta 30s — **acá sí
     hay un timeout explícito**, pero el error que tira (`throw 'ready timeout'`) nunca
     llega a nuestro proceso de Node: esta función corre del lado de Node pero la llama
     la página con un `window.onAppStateHasSyncedEvent()` sin `await` ni `.catch()` del
     lado de la página (es un listener de evento tipo Backbone) — un rechazo acá queda
     como una promesa sin manejar **dentro del contexto del browser**, visible solo por
     `pageerror`/`console` (de ahí el pedido explícito de escucharlos). Antes de esta
     vuelta, nada escuchaba esos dos eventos — por eso este timeout, si es lo que está
     pasando, no dejaba ningún rastro en nuestros logs.
   - `await this.pupPage.evaluate(() => ...Conn.serialize()...)` para armar
     `this.info` — depende de `WAWebConnModel`/`WAWebUserPrefsMeUser`.
   - `await this.attachEventListeners()` — no mostrado acá (está en otra parte del
     archivo), depende de más modelos internos de la página.
4. Recién al final, `this.emit(Events.READY)`.

**Cualquier excepción en los pasos 3 (antes de llegar al final) deja a `ready` sin
disparar — y, por el mismo motivo que el timeout, probablemente sin ningún rastro en
nuestros logs actuales si la excepción no pasa por un `catch` nuestro.** Esto es
justamente lo que la sonda de `WHATSAPP_DEBUG` (60s/180s) intenta correlacionar:
`hasSynced`/`state` de `AppState`, y si `window.Store`/`window.WWebJS` llegaron a
existir, da una pista de en qué paso del 3 se quedó colgado.

**`authenticated` repetido (2 a 4 veces) hace esto más probable, no menos:** como
`inject()` se re-ejecuta en cada navegación sin ninguna guarda (ver el hallazgo de
arriba), es plausible que haya 2 a 4 ejecuciones de esta MISMA cadena corriendo a la
vez — si dos de ellas pisan el mismo `window.WWebJS` a mitad de camino, o
`attachEventListeners()` se llama dos veces y la segunda choca con algo que la primera
ya registró (mismo patrón de `exposeFunctionIfAbsent` del hallazgo de arriba, pero dentro
de `attachEventListeners`), cualquiera de los pasos 3 podría fallar en silencio. No es
una prueba, es la hipótesis más fuerte a partir de lo que se puede leer en el código —
la sonda está pensada para confirmarla o descartarla con datos reales.

### #201919 / #201901 / #201910 (renombre `_serialized`→`$1`): no explica esto

Reverificado con WebFetch, mirando el contenido real de los dos PRs, no solo la
descripción del issue: los tres tocan exclusivamente código que corre **después** de
`ready` (`sendMessage`, `getChats`, `getChatById`, `editMessage`, `LoadUtils` — pero
específicamente la parte de `LoadUtils` que instala un getter de `_serialized` en
`WAWebMsgKey.prototype` para esas funciones, no nada que corra durante la carga inicial
de `LoadUtils` en sí). El propio issue #201919 lo dice explícito: "authenticated" y
"ready" disparan normal ahí, el problema es posterior. No hay parche mínimo que
proponer desde estas fuentes para el síntoma de este diagnóstico — quedan descartadas
como explicación, no como algo a corregir.

## `main` ya incluye esto — confirmado leyendo el código, no supuesto

Con `WHATSAPP_DEBUG=1` puesto en producción (con el parche de `hasSynced` de arriba ya
aplicado), las sondas mostraron `AppState` en `CONNECTED` con `hasSynced:true` — o sea,
exactamente la condición que el parche ya cubría — pero `window.WWebJS` seguía
`undefined`. El parche de arriba corrige CUÁNDO se llama al callback que lleva a
"ready"; no corrige qué pasa si ese callback se ejecuta más de una vez a la vez.

Leyendo `node_modules/whatsapp-web.js/src/Client.js` en el commit
`064a3d5a5a3dce1281a6a12740b5a7051339d154` de `main` (ver README principal, "Actualizar
whatsapp-web.js"), cuatro cambios, todos verificados por lectura directa del código (no
por el historial de issues/PRs en GitHub, que ya demostró no ser confiable para saber
qué hay realmente en una versión dada):

1. **El mismo chequeo inmediato de `hasSynced`** que agregaba nuestro parche
   (`Client.js:448-454`, comentario propio: *"Atomic hasSynced check in the same
   synchronous block as listener registration"*) — con un agregado: solo dispara si
   `!storeInjected` (`window.WWebJS` todavía no existe), para no volver a correr la
   cadena completa si ya corrió una vez.
2. **`framenavigated` ya no llama a `inject()` en cada navegación**
   (`Client.js:562`): `if (!isLogout && storeAvailable) return;` — si no es un logout y
   `window.WWebJS` ya existe, no hace nada. Esta es la causa real de que hubiera 2 a 4
   `authenticated` seguidos: antes, CUALQUIER navegación volvía a llamar a `inject()`
   sin este chequeo.
3. **Cancelación cooperativa de `inject()`** (`Client.js:114-118`,
   `this._injectAbort`/`AbortController`): si se llama a `inject()` de nuevo mientras
   uno anterior sigue corriendo, el anterior se cancela (`abort.signal.aborted` →
   `return` limpio, comentario propio *"superseded by newer inject"*) en vez de seguir
   corriendo en paralelo con el nuevo. Esto es justo lo que hacía falta para la
   hipótesis de "ejecuciones concurrentes se pisan entre sí" de la sección de arriba.
4. **Limpieza de listeners antes de volver a registrarlos**
   (`Client.js:436-446`, `window._wwjsListeners`): saca los listeners viejos
   (`.off(...)`) antes de agregar los nuevos — ya no se acumulan versiones duplicadas de
   los mismos listeners en cada `inject()` que sí llega a correr.

De paso, también se agregó `_registerFramenavigatedHandler()` con su propia guarda
(`this._framenavigatedRegistered`) para no registrar el listener de `framenavigated` en
sí más de una vez, y ahora ignora navegaciones de iframes (`frame.parentFrame() !==
null`).

**Conclusión:** los cuatro puntos atacan directamente la causa que señalaban los datos
reales de producción (ejecuciones repetidas/concurrentes de la cadena de
inicialización) — es un indicio fuerte de que esto resuelve el síntoma, no una
confirmación. Solo probarlo contra WhatsApp real (vincular o restaurar sesión de
verdad) lo confirma. También se verificó que el resto de la API que usa
`whatsapp-service/whatsapp.js` no cambió: mismos nombres de evento
(`authenticated`/`auth_failure`/`ready`/`qr`/`loading_screen`/`disconnected`/
`change_state`/`remote_session_saved`), `LocalAuth` (mismo constructor, mismo `logout()`
que solo borra `dataPath/session/`), `getNumberId(numero)` (misma firma, devuelve el wid
o `null`), `client.logout()` (misma secuencia: `Socket.logout()` → cerrar el browser →
`authStrategy.logout()`), `sendMessage(chatId, texto)`, y `msg.fromMe`/`msg.from`/
`msg.getContact()` en los mensajes entrantes — no hizo falta tocar `whatsapp.js`,
`index.js` ni `cola.js`.

**Nota aparte, no relacionada con el síntoma:** `window.Store` no se asigna en ningún
lado de este código (ni en `Client.js` ni en `util/Injected/Utils.js`) — es un nombre
viejo de versiones muy anteriores de la librería. La sonda de `WHATSAPP_DEBUG` lo va a
reportar como `"undefined"` siempre, en cualquier corrida, exista o no un problema real
— no es una señal útil, a diferencia de `window.WWebJS`.
