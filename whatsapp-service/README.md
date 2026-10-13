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

`whatsapp.js` reacciona solo a dos casos:
- `disconnected` con un motivo distinto de `"LOGOUT"` (por ejemplo, un corte de red): recrea
  el cliente sin tocar la sesión guardada — si el corte fue pasajero, reconecta solo, sin
  pedir un escaneo nuevo.
- `disconnected` con motivo `"LOGOUT"` (alguien lo desvinculó desde el teléfono, en
  Dispositivos vinculados) o `auth_failure`: ahí la sesión está confirmada muerta — se borra
  (solo el contenido de `/opt/yosoy/whatsapp/sesion`, nunca el directorio en sí) y se fuerza
  un QR nuevo, igual que el botón "Desvincular" del Configurador.

Por esto, usar el chip también desde el teléfono físico en simultáneo ya no deja el servicio
colgado sin salida — va a pedir un escaneo nuevo la próxima vez que se abra el Configurador
—, pero sigue sin ser buena idea: invalida la sesión sin aviso, justo cuando puede estar
esperando salir un recordatorio de turno.

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

## Actualizar whatsapp-web.js

La versión queda **fijada exacta** en `package.json` (sin `^`/`~`) a
propósito: esta librería reversea el frontend de WhatsApp Web, así que se
rompe seguido cuando WhatsApp cambia algo de su lado — un rango se
actualizaría solo en el próximo `npm install`, sin que nadie lo decida ni lo
pruebe antes. Para actualizar: cambiar el número a mano, probar en un
entorno que no sea el chip de producción si es posible, recién entonces
`npm install` + redeploy.

## Vulnerabilidades conocidas en dependencias transitivas

`npm audit` reporta 9 altas, todas en la cadena de descarga de Chromium de
Puppeteer (`extract-zip`, `basic-ftp` vía `proxy-agent`/`pac-proxy-agent`) —
no en el código de WhatsApp en sí, solo en la herramienta que baja el
binario de Chromium la primera vez que se instala. Riesgo real bajo en este
despliegue: no se usa proxy PAC, y la descarga es HTTPS directo al CDN de
Google. `npm audit fix --force` "arregla" esto bajando `whatsapp-web.js` a
una versión anterior (cambio incompatible, deshace el fix del Puppeteer
viejo sin soporte de la sección de arriba) — no se aplicó a propósito.
Revisar `npm audit` de nuevo en cada actualización de `whatsapp-web.js`.

## Qué NO hacer

- No correr esto con el `.env` principal de la API completo — Chromium
  heredaría la cadena de conexión a SQL, la clave de JWT y la contraseña del
  mail. Solo `whatsapp.env`.
- No exponer el puerto 3002 (ni con una regla de UFW ni con `0.0.0.0` en
  `WHATSAPP_SERVICE_PORT`).
- No usar el chip dedicado desde un teléfono físico en simultáneo — invalida
  la sesión vinculada acá.
