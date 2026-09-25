# ⚠️ Notas de merge — leer antes de exponer YoSoy públicamente

## `mensajeria-whatsapp-reservas` + `videollamadas-seguras` (Caddy / túnel)

**Antes de exponer esto públicamente: verificar que `/mensajeria` siga devolviendo 404 detrás del proxy.**

- El `Caddyfile` de `videollamadas-seguras` publica todo lo que atiende `127.0.0.1:5000`, incluido `/mensajeria/*` (conversaciones de pacientes con DNI, teléfono y turnos) y la pantalla `/pantallas/Pantalla_ChatSimulado/`.
- Caddy (y `cloudflared`) se conectan desde `127.0.0.1`, así que el chequeo de "solo local" (`Mensajeria:SoloLocal`) **no alcanza por sí solo**. Como segunda traba, un pedido que trae `X-Forwarded-For` o `CF-Connecting-IP` se trata como remoto y recibe 404. Caddy agrega `X-Forwarded-For` por defecto; si se cambia el proxy o su configuración, volver a verificarlo.
- Estos endpoints **no tienen autenticación real** (ver `TODO(JWT)` en `Endpoints/MensajeriaEndpoints.cs`, método `RequiereMensajeriaHabilitada`). En cualquier despliegue público, `Mensajeria:ChatSimuladoHabilitado` tiene que estar en `false` hasta que el módulo exija JWT y rol de administrador.

### Verificación después del merge

Con el proxy levantado, desde **otra** máquina (o a través del dominio público):

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://TU-DOMINIO/mensajeria/conversaciones
curl -s -o /dev/null -w "%{http_code}\n" https://TU-DOMINIO/mensajeria/conversaciones/1/paciente
curl -s -o /dev/null -w "%{http_code}\n" https://TU-DOMINIO/pantallas/Pantalla_ChatSimulado/index.html
```

Las tres tienen que responder **404**. Si alguna responde 200, no publicar.

Más detalle: sección "Mensajería: chatbot de reservas y recordatorios" del `README.md`.
