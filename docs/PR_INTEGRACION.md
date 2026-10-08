# Integración de la cadena de ramas a la rama principal

**Rama de origen:** `integracion-main` · **Rama destino:** `main`
**Tipo de merge en GitHub: "Create a merge commit"** — no usar "Squash and merge" ni
"Rebase and merge": esta rama trae una cadena larga de commits con su propia historia
(cada fase, cada corrección) que vale la pena conservar, no aplastar en uno solo.

## Qué incluye

Una cadena lineal de ramas, cada una construida sobre la anterior, más dos correcciones
que se integran aparte:

1. **Fases 1, 2 y 3a** (ya en la base de la cadena): gestión de pacientes/profesionales/
   turnos, Grupos/Casos/Reglas de derivación con asignación en cascada, reglas de negocio
   de turnos (RN-01/02/05/07) y link público de reserva.
2. **`Cambios-Agus-jwt`:** autenticación real con JWT (`POST /login` emite el token; los
   tres roles lo usan) en vez de identidad autodeclarada por un header sin firmar.
3. **`mejora-chats-y-centrado`:** mensajería/chatbot de reservas y recordatorios, con
   pertenencia de conversaciones por rol (quién ve qué chat escalado, con auditoría de
   apertura) y mejoras de interfaz.
4. **`cierre-seguridad`:** historia clínica y datos clínicos filtrados por el equipo
   tratante vigente del paciente (ya no "cualquier profesional"), identidad de turnos y
   agendas siempre desde el token, corrección del CB-04 de reportes, cabeceras de
   seguridad globales, guardia automática que falla si aparece un endpoint nuevo sin
   decidir, y la contraseña de correo fuera de `appsettings.json`.
5. **`fix-xss`** (mergeada aparte, no es parte de la cadena lineal): escapes de HTML en
   pantallas que armaban contenido con datos del servidor. Verificado que ninguna
   protección se perdió al resolver los conflictos con la cadena — varias ya estaban
   resueltas de raíz por cambios posteriores de la propia cadena (detalle completo en
   `CAMBIOS_AGUS.md`, sección "Integración a la rama principal", Paso 1).
6. **`fix-registro-publico`:** ya incluida de forma transitiva (es la primera rama de la
   cadena) — sin un paso de merge propio.

## Configuración obligatoria

La app **no arranca** sin esto — ningún valor vive en un archivo versionado:

```bash
dotnet user-secrets set "Jwt:Key" "una-clave-aleatoria-de-32-caracteres-o-mas"
```
(o la variable de entorno `Jwt__Key` fuera de Development).

Para que los recordatorios/avisos manden mail de verdad (opcional en desarrollo: sin
esto, el envío queda registrado como "Omitido", sin mandar nada real):
```bash
dotnet user-secrets set "Email:Password" "la-contraseña-de-aplicación-real"
```
(o `Email__Password`).

**Migraciones, en orden** (las 14, ver la tabla completa en el `README.md` — todas
idempotentes, se pueden correr más de una vez sin romper nada): primero el script de
esquema de la rama `DataBase`, después cada script de `migrations/` en el orden que
indica el README. El detalle de qué agrega cada una y por qué ese orden específico está
documentado ahí, no se repite acá.

## Qué queda pendiente a propósito (no es un olvido)

- **CSP (Content-Security-Policy):** no se activa. Hay demasiado JavaScript y CSS inline
  en las pantallas; una política restrictiva las rompería sin un trabajo de migración
  propio y más grande que este. Las demás cabeceras de seguridad (`X-Content-Type-Options`,
  `Referrer-Policy`, `X-Frame-Options`) sí están, globales.
- **`signaling-server` (Node, WebRTC) no autentica nada.** Lo trabaja otra persona en la
  rama `videollamadas-seguras`, fuera del alcance de este PR — ese código no se tocó.
- **Cierre de sesión por inactividad:** pendiente de una decisión de equipo, sin tocar.
- **La contraseña de correo que ya estaba expuesta en `main` antes de este PR** (ver
  `CAMBIOS_AGUS.md`, Paso 3.1): este PR corrige el valor hacia adelante, pero no la borra
  de la historia existente de `main` — eso requeriría reescribir historia ya publicada,
  una decisión de equipo aparte. **Rotar esa contraseña en el proveedor (Gmail) sigue
  pendiente**, y no depende de este merge.

## Cómo probarlo

`docs/DEMO.md` tiene el guion completo paso a paso (cómo levantar la app, usuarios de
prueba, datos de demo opcionales, y un recorrido de ~10-15 minutos por los tres roles:
administrador, profesional y paciente, incluida la reserva pública con login de retorno).

Antes de aprobar el merge, conviene al menos:
- `dotnet build` sin errores y `dotnet test` en verde (160 pruebas).
- Login de los tres roles y un vistazo rápido al menú de cada uno.
- CRM > Chats con dos profesionales distintos, para ver que uno no se entera de lo del
  otro (la pertenencia de conversaciones).
- Abrir la historia clínica de un paciente como el profesional correcto (la ve) y como
  uno sin esa asociación (404, sin romper la pantalla).

## Detalle completo

Cada decisión de este PR (por qué cada regla de pertenencia, qué conflictos de `fix-xss`
se resolvieron y cómo se verificó que no se perdió ninguna protección, los hallazgos de
la auditoría de secretos y de migraciones) está documentado con el detalle completo en
`CAMBIOS_AGUS.md`, sección "Integración a la rama principal".
