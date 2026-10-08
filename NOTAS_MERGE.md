# ⚠️ Notas de merge — leer antes de exponer YoSoy públicamente

## `mensajeria-whatsapp-reservas` + `videollamadas-seguras` (Caddy / túnel)

**Antes de exponer esto públicamente: verificar que `/mensajeria` siga devolviendo 404 detrás del proxy.**

- El `Caddyfile` de `videollamadas-seguras` publica todo lo que atiende `127.0.0.1:5000`, incluido `/mensajeria/*` (conversaciones de pacientes con DNI, teléfono y turnos) y la pantalla `/pantallas/Pantalla_ChatSimulado/`.
- Caddy (y `cloudflared`) se conectan desde `127.0.0.1`, así que el chequeo de "solo local" (`Mensajeria:SoloLocal`) **no alcanza por sí solo**. Como segunda traba, un pedido que trae `X-Forwarded-For` o `CF-Connecting-IP` se trata como remoto y recibe 404. Caddy agrega `X-Forwarded-For` por defecto; si se cambia el proxy o su configuración, volver a verificarlo.
- **Actualizado en `Cambios-Agus-jwt`:** `/mensajeria/*` ya exige JWT con rol Profesional o Configurador (el `TODO(JWT)` que tenía `Endpoints/MensajeriaEndpoints.cs` se resolvió ahí — ver "Autenticación con JWT" en el `README.md`). Las dos rutas que simulan un mensaje entrante del paciente (`/mensajeria/simulado/entrante` y `/simulado/conversacion`) quedan abiertas a propósito, como un webhook. **Esto es de esa rama, no de `main` ni de `videollamadas-seguras`/`mensajeria-whatsapp-reservas`:** esa última ya tiene su propio `JwtService` hecho a mano, con otro formato de token — van a necesitar reconciliarse recién en el merge real; hasta entonces, seguí tratando esto como sin autenticación real en cualquier rama que no sea `Cambios-Agus-jwt`, y repetí la verificación de abajo ahí también.

### Verificación después del merge

Con el proxy levantado, desde **otra** máquina (o a través del dominio público):

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://TU-DOMINIO/mensajeria/conversaciones
curl -s -o /dev/null -w "%{http_code}\n" https://TU-DOMINIO/mensajeria/conversaciones/1/paciente
curl -s -o /dev/null -w "%{http_code}\n" https://TU-DOMINIO/pantallas/Pantalla_ChatSimulado/index.html
```

Las tres tienen que responder **404**. Si alguna responde 200, no publicar.

Más detalle: sección "Mensajería: chatbot de reservas y recordatorios" del `README.md`.
