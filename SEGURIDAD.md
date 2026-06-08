# SEGURIDAD — Videollamadas seguras (YoSoy, Fase 1)

Resumen del modelo de seguridad de las sesiones de video y de sus límites conscientes
para la Fase 1.

---

## 1. Modelo de autenticación

| Token | Para qué | Vida | Notas |
|-------|----------|------|-------|
| **login-JWT** | Identifica al usuario en el path de sesión | 8 h | HS256, emitido por `/login`. |
| **JWT de sesión** | Acceso a una sala concreta (signaling + endpoints de sesión) | atado a `finProgramado` | Lleva `roomId`, `idTurno`, `idSesion`, `rol`. |
| **Credenciales TURN** | Relay WebRTC | TTL corto (~600 s) | REST efímeras: `username = exp:idUsuario`, `credential = HMAC-SHA1(secret)`. |
| **Link de email** | Entrada sin login previo | hasta el fin del turno | **Un solo uso** (`MarcarUsadoSiNoUsado`). |

- El **JWT de sesión** se obtiene por dos vías: `/sesiones/turno/{idTurno}/join`
  (con login-JWT) o `/sesiones/canjear-link` (con el token del email). Ambas pasan por
  `AbrirSesion`, que valida la ventana horaria contra `finProgramado`.
- **Refresco con gracia:** `/sesiones/{id}/refrescar-token` acepta el token vencido con
  300 s de gracia para reemitirlo cuando el profesional prolonga la sesión. El front
  escucha `sesion-actualizada` y reinicia la cuenta regresiva.

## 2. Signaling (servidor de señalización)

- **HS256 fijado:** verificación manual del JWT con el algoritmo **clavado a HS256**;
  se rechaza `alg:none` y cualquier otro (anti *algorithm-confusion*). Firma comparada
  en **tiempo constante**.
- **Sala desde el token:** el `roomId` autoritativo es el del token; si el cliente pide
  otra sala, se rechaza y se desconecta. **Máximo 2** participantes por sala.
- **Verdad en la DB:** antes de unir, el signaling consulta
  `GET /sesiones/{id}/estado` (con `X-Internal-Secret`) — si la sesión no está activa,
  rechaza. En modo LAN, si el backend no responde, permite (solo demo).
- **Sanitización:** se validan tamaños/forma de `offer`/`answer`/`candidate` antes de
  reenviarlos. CORS por lista de orígenes (nunca `*`).

## 3. Datos en reposo y auditoría

- **Cifrado AES-GCM** (nonce por registro) sobre el resumen e historial de emociones
  (`CryptoService`). Requiere `Sesion__ClaveCifrado` (base64, 32 bytes); sin clave no se
  cifra (degradación visible vía flag `cifrado`).
- **Consentimiento:** `/sesiones-emociones` exige consentimiento activo del paciente.
- **Audit log:** los eventos de sesión (`creada`, `ingreso`, `extension`, `finalizada`,
  `emociones`) quedan registrados en `SesionVideoEvento`.

## 4. Rate limiting

In-memory por IP, ventana de 5 minutos (configurable por env):
- `/login`: 3 intentos.
- `/sesiones/canjear-link`, `/sesiones/turno/{id}/join` y el `join-room` del signaling: 5 intentos.
- Al exceder: **429 Too Many Requests** (o rechazo + desconexión en el signaling).

## 5. Límites CONSCIENTES de Fase 1

- **login-JWT acotado al path de sesión:** el resto de la app sigue usando `localStorage`
  (idUsuario/idProfesional/idPaciente) por ahora; el token solo se usa para entrar a las
  videollamadas.
- **Fallback de texto plano solo en modo LAN:** sin `Sesion__ClaveCifrado` los datos de
  emociones se guardan sin cifrar (aceptable solo en demo local).
- **Rate limiting in-memory:** no sobrevive reinicios ni escala a multi-instancia; se
  revisa al pasar a cloud (store compartido tipo Redis).
- **SMTP y rotación de secretos diferidos:** el envío de emails y la rotación periódica
  de `Jwt__Secret` / `Turn__StaticAuthSecret` quedan para la etapa cloud.
- **TURN sin TLS verificado en demo:** los certificados de coturn son placeholders hasta
  el despliegue online.
