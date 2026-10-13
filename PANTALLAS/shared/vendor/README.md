# Librerías de terceros servidas por la propia app

Se versionan acá, en vez de cargarlas desde un CDN, para que las pantallas con cámara y
micrófono no dependan de un tercero (ni le avisen a nadie que alguien entra a una sesión) y
para que el código que se ejecuta sea el que revisamos.

## socket.io.min.js

- **Qué es:** cliente de Socket.IO 4.8.3 (licencia MIT). Lo usan las dos `sesion_video.html`.
- **De dónde sale:** `client-dist/socket.io.min.js` del paquete `socket.io@4.8.3`, la versión que
  fija `signaling-server/package-lock.json` (el tarball coincide con su `integrity`). Es byte a
  byte el `dist/socket.io.min.js` de `socket.io-client@4.8.3`.
- **Diferencia con el original:** se le quitó la última línea (`//# sourceMappingURL=...`).
  No versionamos el `.map` (196 KB) y sin él DevTools avisa de un 404. Nada más.
- **Cómo verificarlo:**
  ```bash
  npm pack socket.io@4.8.3 && tar -xzf socket.io-4.8.3.tgz package/client-dist/socket.io.min.js
  head -n -1 package/client-dist/socket.io.min.js | sha256sum
  git show HEAD:PANTALLAS/shared/vendor/socket.io.min.js | sha256sum
  ```
  Los dos hashes tienen que dar `88c41571e3282ae0f3fbc874f4acb1326f99c8b4e88967bb311687c600125229`.
  (Se compara contra el blob de git y no contra el archivo del disco, porque con
  `core.autocrlf=true` en Windows el del disco puede tener CRLF.)
- **Cómo actualizarlo:** cuando cambie la versión de `socket.io` en el lock del signaling, bajar
  la misma acá y actualizar esta nota. Cliente y servidor 4.x son compatibles entre sí, pero
  conviene que coincidan.
