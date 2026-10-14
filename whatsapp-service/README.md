# whatsapp-service

Puente HTTP interno entre la API de YoSoy y WhatsApp, usando
[whatsapp-web.js](https://wwebjs.dev/) (no oficial — automatiza WhatsApp Web
por Puppeteer/Chromium, no es la API oficial de Meta). Pensado para un chip
dedicado, solo para esto; hay riesgo de baneo asumido a propósito, no es para
un número de uso personal.

Solo escucha en `127.0.0.1` — nunca se expone a internet. La API .NET lo
llama vía `CanalWhatsAppWebJs` (`Services/Mensajeria/`); este servicio le
reenvía los mensajes entrantes por su cuenta.

## Qué necesita el VPS

- **Node.js 22 LTS.** No estaba instalado al momento de escribir esto
  (confirmado por SSH) — instalar vía NodeSource:
  ```bash
  curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
  sudo apt install -y nodejs
  ```
- **Paquetes para Chromium headless.** En Ubuntu 24.04 varios de estos
  cambiaron de nombre por la migración a `time_t` de 64 bits — son los
  nombres con sufijo `t64`, no los de versiones anteriores de Ubuntu:
  ```bash
  sudo apt install -y libnss3 libatk1.0-0t64 libatk-bridge2.0-0t64 \
      libgtk-3-0t64 libgbm1 libasound2t64
  ```
  (Si en el momento de instalar `apt` no encuentra alguno de estos nombres,
  puede que la versión de Ubuntu haya cambiado el sufijo de nuevo —
  `apt-cache search` por el nombre base para encontrar el paquete real.)

## Primer deploy (manual — no entra en el script de deploy de la API)

Hecho así de verdad en la Etapa 2, confirmado en el VPS real:

```bash
sudo mkdir -p /opt/yosoy/whatsapp/{app,sesion,cache}
sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp
sudo chmod 750 /opt/yosoy/whatsapp/sesion /opt/yosoy/whatsapp/cache
```

Empaquetar esta carpeta **sin `node_modules`** (no hace falta — se instala
en el VPS, no se copia desde la PC) y copiarla:
```bash
# en la PC, parado en whatsapp-service/
tar --exclude=node_modules -czf whatsapp-service.tar.gz .
scp whatsapp-service.tar.gz agustin@179.199.155.50:/home/agustin/
# en el VPS
sudo tar -xzf /home/agustin/whatsapp-service.tar.gz -C /opt/yosoy/whatsapp/app
sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp/app
```

Instalar dependencias **como el usuario `yosoy`**, no como root, con
`npm ci` (no `npm install` — usa el `package-lock.json` tal cual quedó
versionado, sin resolver nada de nuevo) y con `HOME`/`PUPPETEER_CACHE_DIR`
apuntando los dos a la carpeta de caché (el home real de `yosoy` es de solo
lectura bajo `ProtectSystem=strict` — mismo motivo que la línea `Environment=HOME=`
de la unit; acá hace falta a mano porque todavía no está corriendo bajo esa
unit cuando se instala):
```bash
sudo -u yosoy bash -c '
  cd /opt/yosoy/whatsapp/app &&
  HOME=/opt/yosoy/whatsapp/cache PUPPETEER_CACHE_DIR=/opt/yosoy/whatsapp/cache \
  npm ci --omit=dev --cache /opt/yosoy/whatsapp/cache/npm
'
```
Este mismo comando aplica solo, en su propio `postinstall`, el parche de
`patches/` (ver "Diagnóstico" más abajo) — no hace falta ningún paso extra.
Verificado con `npm ci --omit=dev` real, dos veces seguidas desde cero, en
una carpeta aparte (no en el VPS).

### Secreto compartido

```bash
SECRETO=$(openssl rand -hex 32)
sudo install -m 600 -o yosoy -g yosoy /dev/null /opt/yosoy/config/whatsapp.env
sudo tee /opt/yosoy/config/whatsapp.env > /dev/null <<EOF
Mensajeria__WhatsApp__Secreto=$SECRETO
API_BASE_URL=http://127.0.0.1:5000
EOF
```
Nunca se escribe el secreto directo en el repo ni en ningún script
versionado — generarlo en el momento, en el VPS.

### Instalar las units

```bash
sudo cp deploy/whatsapp-service.service /etc/systemd/system/
sudo mkdir -p /etc/systemd/system/yosoy-api.service.d
sudo cp deploy/yosoy-api-whatsapp.conf /etc/systemd/system/yosoy-api.service.d/whatsapp.conf
sudo systemctl daemon-reload
sudo systemctl enable --now whatsapp-service
sudo systemctl restart yosoy-api   # para que levante el drop-in con el secreto
```

## Vincular el chip (QR)

**Forma normal: desde el Configurador** (sección WhatsApp, rol administrador)
— muestra el estado y el QR como imagen, sin SSH. Ahí mismo está "Desvincular
y cambiar número". Lo que sigue es la forma manual/de respaldo, para cuando
no se puede usar el navegador (primer arranque antes de confirmar que el
servicio levantó, diagnóstico por SSH, etc.).

`GET /estado` y `POST /desvincular` son las dos rutas que usa esa pantalla —
ambas piden el mismo `X-Shared-Secret` que `/enviar` (no están pensadas para
pegarles directo salvo para diagnóstico):
```bash
curl -s http://127.0.0.1:3002/estado -H "X-Shared-Secret: $SECRETO"
# { "conectado": true, "numero": "549...", "qrPendiente": false, "qr": null }
```
Mientras falta escanear, `"qr"` trae el código como data-URL
(`data:image/png;base64,...`) en vez de `null` — se puede pegar ese valor en
la barra de direcciones de un navegador para verlo sin decodificar nada a
mano.

El QR también se sigue imprimiendo por journal como respaldo (si el servicio
recién arranca y todavía no hay forma de llegar a la pantalla del
Configurador, por ejemplo):
```bash
sudo journalctl -u whatsapp-service -f
```
Conviene tener la terminal maximizada — el QR como ASCII necesita bastante
ancho para que la cámara lo pueda leer sin que quede cortado o demasiado
chico (confirmado en el VPS real).

Una vez vinculado (por cualquiera de las dos formas), `LocalAuth` guarda la
sesión en `/opt/yosoy/whatsapp/sesion` — no hace falta repetirlo salvo que se
desvincule (desde el Configurador, desde el teléfono en Dispositivos
vinculados, o `POST /desvincular`) o WhatsApp invalide la sesión por su
cuenta (p. ej. por usar el chip también desde el teléfono físico al mismo
tiempo — no hacerlo). Una desconexión transitoria (corte de red) no borra la
sesión guardada — el servicio reintenta solo, sin pedir un escaneo nuevo,
salvo que la sesión en sí ya no sirva.

## Si se desconecta sola

`whatsapp.js` reacciona distinto según el motivo de `disconnected`:
- Motivo distinto de `"LOGOUT"` (por ejemplo, un corte de red): recrea el cliente sin tocar
  la sesión guardada — si el corte fue pasajero, reconecta solo, sin pedir un escaneo nuevo.
- `auth_failure`: la sesión está confirmada muerta — se borra (solo el contenido de
  `/opt/yosoy/whatsapp/sesion`, nunca el directorio en sí) y se fuerza un QR nuevo, igual que
  el botón "Desvincular" del Configurador.
- `disconnected` con motivo `"LOGOUT"` (alguien lo desvinculó desde el teléfono, en
  Dispositivos vinculados): acá **no se toca el cliente para nada** — ni se destruye ni se
  recrea. whatsapp-web.js ya maneja este caso internamente (borra la sesión y vuelve a
  inicializarse sobre la misma página) y hacerlo nosotros también, al mismo tiempo, generaba
  una carrera real: un crash en producción (`Execution context was destroyed`, Puppeteer).
  Solo se limpia el QR viejo que teníamos guardado; la librería termina emitiendo uno nuevo
  por su cuenta.

Por esto, usar el chip también desde el teléfono físico en simultáneo ya no deja el servicio
colgado sin salida — va a pedir un escaneo nuevo solo, sin reiniciar el proceso —, pero
sigue sin ser buena idea: invalida la sesión sin aviso, justo cuando puede estar esperando
salir un recordatorio de turno.

## Si el proceso no responde a nada de esto

Dos redes de seguridad adicionales, para cuando algo revienta del lado de Puppeteer/
whatsapp-web.js que no es ninguno de los eventos de arriba:
- `unhandledRejection`/`uncaughtException` a nivel proceso: se loguean (nunca el objeto
  entero del error, puede traer datos de la cuenta) y se intenta recuperar el cliente por
  el mismo camino que un corte transitorio — no tiran abajo el proceso.
- `SIGTERM`/`SIGINT` (lo manda systemd al hacer `stop`/`restart`): deja de aceptar pedidos
  nuevos, cierra el cliente de WhatsApp con un tope de 10s, y recién ahí sale. Si ese cierre
  se cuelga, sale igual pasado el tope — nunca bloquea un restart/deploy.

## Diagnóstico: vinculado en el teléfono pero la web no llega a "ready"

Si el celular ya muestra el dispositivo como vinculado pero `whatsapp-service` sigue
mostrando (o pidiendo) un QR sin parar, revisar el log por estos eventos, en orden — todos
quedan con una línea propia, sin datos de la cuenta:
```
WhatsApp: authenticated
WhatsApp: loading_screen 0%
...
WhatsApp: loading_screen 100%
WhatsApp conectado (número 549...).     ← esto es "ready"
```

**Caso real ya visto y parcheado** (ver `patches/whatsapp-web.js+1.34.7.patch` y
`patches/README.md` para las fuentes): `loading_screen 100%` y `authenticated` —
repetido varias veces— sin que `ready` llegue nunca, y sin ningún error en el medio. Es
una carrera conocida de whatsapp-web.js (si la sesión sincroniza más rápido de lo que
tarda en registrarse el listener que espera ese evento, nunca se entera). El parche ya
corrige esto para 1.34.7 — si de nuevo se ve exactamente este patrón después de aplicado
el parche, algo cambió del lado de WhatsApp Web otra vez.

`authenticated` repitiéndose varias veces **no es inofensivo ni está arreglado por este
parche** — es un síntoma de que `inject()` se ejecutó más de una vez y quedaron
listeners duplicados (detalle y por qué no se corrigió acá en `patches/README.md`); no
debería impedir que `ready` dispare una vez aplicado el parche de arriba, pero si se ve
mucho (más de 2-3 veces seguidas) vale la pena mirarlo con más cuidado.

Si el log se corta en algún `loading_screen` (sin llegar al 100%) o directamente nunca
aparece `authenticated`, es una situación distinta a la de arriba — señal más genérica de
que whatsapp-web.js y la versión de WhatsApp Web que sirven los servidores en ese momento
no se llevan bien — ver la sección siguiente (`WHATSAPP_WEB_VERSION`).

(`change_state` también se loguea, por completitud; `remote_session_saved` está cableado
pero no lo usa esta configuración — ver el comentario en el código.)

## Prender el canal en la API

Recién con `conectado:true` confirmado, en `/opt/yosoy/config/.env` (el
principal, no `whatsapp.env`):
```
Mensajeria__CanalActivo=WhatsAppWebJs
```
y `sudo systemctl restart yosoy-api`.

## Problemas esperables (Ubuntu 24.04)

- **AppArmor y el sandbox de Chromium:** Ubuntu 24.04 restringe por default
  la creación de user namespaces sin privilegios, que es lo que usa el
  sandbox normal de Chromium — sin esto, Puppeteer falla al arrancar.
  `whatsapp.js` ya lanza el cliente con `--no-sandbox`; si igual falla,
  revisar `journalctl -u whatsapp-service` por el error exacto de Puppeteer.
- **`ProtectSystem=strict` y la caché de Puppeteer:** con esa directiva la
  unit no puede escribir en `~/.cache/puppeteer` (la ruta por default), por
  eso `PUPPETEER_CACHE_DIR` está fijado a `/opt/yosoy/whatsapp/cache` en la
  unit, que también está en `ReadWritePaths`. Lo mismo pasa con `$HOME` en
  general (no solo la caché de Puppeteer) — por eso la unit también fija
  `Environment=HOME=` al mismo directorio. Si se cambia esa ruta, hay que
  actualizar las tres cosas juntas: `PUPPETEER_CACHE_DIR`, `HOME` y
  `ReadWritePaths`.
- **La sesión no sobrevive un deploy de la API:** a propósito —
  `/opt/yosoy/whatsapp/` está fuera de `/opt/yosoy/app`, que el script de
  deploy de la API borra y reescribe entero en cada publish. Si algún día se
  automatiza el deploy de este servicio también, nunca debe tocar
  `/opt/yosoy/whatsapp/sesion`.
- **Cuánta memoria hace falta:** `MemoryMax` quedó en `2G` (antes `768M`) — con el valor
  viejo, el proceso llegó al tope real (contando swap) durante la vinculación del chip en
  el VPS. Chromium es el grueso de esto, no Node; si se nota que igual anda justo, revisar
  `systemctl status whatsapp-service` por un `OOMKilled` antes de asumir que es otra cosa.

## Actualizar whatsapp-web.js

La versión queda **fijada exacta** en `package.json` (sin `^`/`~`) a
propósito: esta librería reversea el frontend de WhatsApp Web, así que se
rompe seguido cuando WhatsApp cambia algo de su lado — un rango se
actualizaría solo en el próximo `npm install`, sin que nadie lo decida ni lo
pruebe antes. Para actualizar: cambiar el número a mano, probar en un
entorno que no sea el chip de producción si es posible, recién entonces
`npm install` + redeploy.

Al actualizar, revisar también `patches/` — el parche que hay ahí (ver
`patches/README.md`) tiene el número de versión en el nombre del archivo
(`whatsapp-web.js+1.34.7.patch`) y apunta a líneas puntuales de esa versión
exacta de `Client.js`. `patch-package` avisa en el `postinstall` si no puede
aplicarlo (no falla en silencio), pero hay que revisar a mano si ese código
sigue necesitando el mismo arreglo en la versión nueva, y regenerar el
archivo con el número de versión que corresponda.

## Fijar la versión de WhatsApp Web

whatsapp-web.js trae pineada una versión de WhatsApp Web por default (la que probó al
publicar esa versión de la librería), pero WhatsApp cambia su web seguido y a veces rompe
algo del lado de ellos antes de que haya una versión nueva de whatsapp-web.js con el
parche — pasó más de una vez en 2026 (cliente que nunca llega a "ready" aunque el teléfono
ya lo muestre vinculado; ver el diagnóstico más arriba). El mitigador manual, ya usado por
la comunidad de whatsapp-web.js para esto: fijar una build anterior conocida, en vez de la
que sirven los servidores de WhatsApp en ese momento.

Variable opcional `WHATSAPP_WEB_VERSION` (sin definir: comportamiento actual, sin cambios).
Si se define, por ejemplo:
```
WHATSAPP_WEB_VERSION=<version-conocida-buena, ej. 2.3000.1037753511-alpha>
```
el servicio arranca con esa build exacta, bajada de un índice de versiones público
(`wppconnect-team/wa-version` en GitHub — no un servidor propio). Para encontrar una build
conocida como buena: revisar los comentarios de los issues de
[wwebjs/whatsapp-web.js](https://github.com/wwebjs/whatsapp-web.js/issues) sobre el
problema puntual que se esté viendo — suelen mencionar una versión anterior que sigue
andando.

Nunca se aplicó un cambio de versión de WhatsApp Web sin probarlo antes fuera del chip de
producción, mismo criterio que actualizar whatsapp-web.js en sí (sección de arriba).

## Vulnerabilidades conocidas en dependencias transitivas

`npm audit` reporta 13 altas (eran 9 antes de agregar `patch-package`):

- 9 en la cadena de descarga de Chromium de Puppeteer (`extract-zip`,
  `basic-ftp` vía `proxy-agent`/`pac-proxy-agent`) — no en el código de
  WhatsApp en sí, solo en la herramienta que baja el binario de Chromium la
  primera vez que se instala. Riesgo real bajo en este despliegue: no se usa
  proxy PAC, y la descarga es HTTPS directo al CDN de Google.
- 4 nuevas, de `patch-package` → `find-yarn-workspace-root` → `micromatch` →
  `braces` (denegación de servicio por "stack exhaustion" con patrones glob
  anidados). Riesgo real bajo acá también: ese código solo corre durante
  `npm install`/`npm ci` (el propio `postinstall`), nunca en el proceso ya
  arrancado — no hay ninguna entrada externa que le llegue a esos patrones
  glob en este despliegue.

`npm audit fix --force` "arregla" la parte de Puppeteer bajando
`whatsapp-web.js` a una versión anterior (cambio incompatible, deshace el
fix del Puppeteer viejo sin soporte de la sección de arriba) — no se aplicó
a propósito. Revisar `npm audit` de nuevo en cada actualización de
`whatsapp-web.js` o de `patch-package`.

## Qué NO hacer

- No correr esto con el `.env` principal de la API completo — Chromium
  heredaría la cadena de conexión a SQL, la clave de JWT y la contraseña del
  mail. Solo `whatsapp.env`.
- No exponer el puerto 3002 (ni con una regla de UFW ni con `0.0.0.0` en
  `WHATSAPP_SERVICE_PORT`).
- No usar el chip dedicado desde un teléfono físico en simultáneo — invalida
  la sesión vinculada acá.
