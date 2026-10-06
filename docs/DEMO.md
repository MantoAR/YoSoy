# Kit de demo — rama `Cambios-Agus`

Para mostrarle a mi equipo cómo viene la rama antes de pushear a `main`. Todo corre local, nada se envía a ningún servidor externo.

## Cómo levantar todo

Dos procesos: la app (.NET, puerto 5000) y el servidor de señalización para videollamadas (Node, puerto 3001). El segundo solo hace falta si vas a mostrar una sesión de video; todo lo demás (agenda, chats, casos, link público) funciona sin él.

**PowerShell, parado en `D:\Claude\YoSoy\YoSoy`:**

```powershell
$env:Email__Password = " "              # un espacio: no manda mails reales, pero no rompe el envío
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run
```

Dejalo corriendo en esa ventana. La app queda en **http://localhost:5000**.

**Para la videollamada (otra ventana de PowerShell), solo si la vas a mostrar:**

```powershell
cd D:\Claude\YoSoy\YoSoy\signaling-server
npm install        # solo la primera vez
node server.js
```

Queda escuchando en el puerto 3001 (lo usa `Pantalla_Paciente/sesion_video.html` y la versión del profesional, no hace falta abrir nada ahí a mano).

**Para cortar todo** al terminar: `Ctrl+C` en cada ventana, o `Get-Process -Name YoSoy,node -ErrorAction SilentlyContinue | Stop-Process -Force`.

## URLs de cada pantalla

| Pantalla | URL |
|---|---|
| Login (los tres roles) | http://localhost:5000/pantallas/index.html |
| Panel del administrador/configurador | http://localhost:5000/pantallas/Pantalla_Configurador/dashboard.html |
| Panel del profesional | http://localhost:5000/pantallas/Pantalla_Profesional/nuevo_dashboard.html |
| Panel del paciente | http://localhost:5000/pantallas/Pantalla_Paciente/dashboard.html |
| Simulador de chat (lado paciente, para mandar un mensaje en vivo) | http://localhost:5000/pantallas/Pantalla_ChatSimulado/index.html |
| Página pública de reserva | http://localhost:5000/reservar/**{slug}** — el slug lo genera la app la primera vez que un profesional activa su link (ver el guion del profesional, más abajo); no hay uno fijo de antemano. |

CRM > Chats **no es una pantalla aparte**: es una pestaña dentro del panel del administrador (`dashboard.html#chats`); el simulador de chat de la tabla es otra cosa — es la pantalla para *actuar como paciente* y mandar un mensaje, útil para mostrar algo llegando en vivo a la bandeja del administrador.

## Usuarios de prueba

Leído de la base local (`Usuario`, `UsuarioProfesional`, `UsuarioPaciente`) — no hay forma de leer una contraseña ya cargada (está hasheada) ni correspondía intentar adivinarla contra la base.

| Rol | Usuario | Para qué sirve en el guion |
|---|---|---|
| Configurador/administrador | `admin` | Todo el panel de administrador |
| Profesional | `smanto` | Santiago Manto, Psicología — ya tiene horarios de lunes a viernes 9 a 18 y turnos reales en la agenda |
| Paciente | `paciente1` | Juan García — hoy **no tiene ningún turno propio** (los turnos reales de la agenda de Santiago Manto son de otros dos pacientes, Camila Vega y Emiliano Salas, que no tienen cuenta de login); si se aplica `docs/datos-demo.sql`, sí tiene una conversación escalada |

**No sé la contraseña real de ninguno de los tres.** Dos caminos:

1. **Si ya las tenés anotadas en algún lado no versionado** (un gestor de contraseñas, una nota tuya), usalas y listo — no hace falta tocar nada.
2. **Si no**, armé `docs/credenciales-demo.sql` (sin ejecutar): le pone a esos 3 usuarios la que elijas al ejecutar `credenciales-demo.sql` (hay que completarla en el script antes de correrlo) y les resetea cualquier bloqueo por intentos fallidos. **Pisa la contraseña real de esos 3** — no hay vuelta atrás una vez aplicado, por eso no lo corrí solo. Lo revisás y, si te sirve, le cambiás el `ROLLBACK` final por `COMMIT` y lo corrés vos:
   ```powershell
   sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\credenciales-demo.sql
   ```

Ninguna contraseña (ni la de prueba, ni ninguna otra) quedó escrita en ningún archivo que vaya a un commit aparte de este `.sql` sin ejecutar, que tiene la que vos decidas aplicar.

## Datos de demo

La base local hoy **no tiene** grupos, reglas de derivación ni casos cargados (la Fase 2 dejó el script sin ejecutar) ni ninguna conversación escalada. Para que el guion de abajo tenga algo que mostrar en esas pantallas, armé — **ninguno ejecutado, los revisás vos**:

1. `docs/datos-prueba-grupos-fase2.sql` (ya existía, de la Fase 2): 4 grupos con miembros y 3 reglas de derivación.
2. `docs/datos-demo.sql` (nuevo, **requiere que el anterior ya esté aplicado**): un paciente ficticio obvio ("Paciente de Prueba") con un caso pendiente de confirmar en la cola de Derivaciones, y marca la conversación de Juan García como escalada (así aparece algo en CRM > Escalamientos).
3. `docs/reset-demo.sql`: deshace el punto 2 (no toca los grupos/reglas del punto 1 — esos quedan, son infraestructura más que datos de demo).

Los tres terminan en `ROLLBACK` por defecto: correlos así primero para ver qué harían, y cambiar `ROLLBACK` por `COMMIT` cuando estés de acuerdo:

```powershell
sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\datos-prueba-grupos-fase2.sql
sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\datos-demo.sql
```

**No hace falta tocar nada a mano para el link público de reserva ni para "Mis horarios de trabajo"**: Santiago Manto ya tiene horarios cargados, y activar el link es un paso del guion en vivo (un clic en "Mi perfil") — no se precargó por SQL a propósito, para no duplicar en un script lo que la propia pantalla ya hace bien.

## Guion de demo (10–15 minutos)

Orden sugerido: administrador → profesional → página pública → paciente (en ese orden: el paso de la página pública es el que le crea a `paciente1` el único turno que va a tener para mostrar en su propio panel). Cada bloque es independiente, podés cortar si se te acaba el tiempo — salvo el último, que depende del paso 3.

### 1. Administrador (`admin`) — ~5 min

1. **Inicio**: las tarjetas de resumen (profesionales activos, solicitudes pendientes, etc.).
2. **CRM > Chats**: la bandeja en 3 columnas. Si querés mostrar algo llegando en vivo, abrí el simulador de chat en otra pestaña y mandá un mensaje como paciente — aparece en la bandeja sin recargar.
3. **CRM > Escalamientos**: si aplicaste `datos-demo.sql`, la conversación de Juan García aparece ahí.
4. **Casos**: el caso de "Paciente de Prueba" en la cola, filtro "Sin asignar".
5. **Derivaciones**: la misma derivación, pendiente de confirmar — mostrá "Confirmar" (o "Corregir" para elegir otro profesional).
6. **Grupos** y **Reglas de derivación**: cómo se armó la cascada.
7. **Auditoría**: alguna fila reciente con el usuario resuelto (no un id pelado).

### 2. Profesional (`smanto`) — ~4 min

1. **Agenda**: vista semana/mes, los turnos reales que ya existen.
2. **Mis horarios de trabajo**: franjas cargadas + la vista previa "Así te ven los pacientes".
3. **Mi perfil**: activar el link público de reserva ahí mismo, en vivo — copiá la URL que aparece, la vas a usar en el paso 4.
4. **Mis casos**: los propios, si confirmaste el caso de "Paciente de Prueba" en el paso del administrador.

### 3. Página pública de reserva — ~3 min

1. Abrí en una ventana **sin sesión** (o en una pestaña privada) la URL que copiaste en el paso del profesional: `http://localhost:5000/reservar/{slug}`.
2. Mostrá los horarios libres — es el mismo cálculo que usa la reserva real, no una maqueta.
3. Elegí un horario: te manda a loguearte. Entrá como **`paciente1`** (tiene que ser este — es el que vas a mostrar en el paso siguiente) y mostrá que vuelve derecho a confirmar ese mismo horario, sin tener que elegirlo de nuevo.
4. Volvé al panel del administrador o del profesional y mostrá que el turno nuevo quedó pendiente de confirmar.

### 4. Paciente (`paciente1`) — ~2 min

1. **Mis turnos**: ahora sí aparece el turno que se acaba de pedir desde la página pública, pendiente de confirmar — es la continuación directa del paso anterior.
2. Si hay algo pendiente de consentimiento, mostrá el aviso y el link para firmarlo.
3. La campanita de notificaciones.

## Plan B — si algo falla en vivo

- **La app no arranca o se cuelga a mitad de demo**: `Ctrl+C` en la ventana de PowerShell, volvé a correr `dotnet run` (los tres comandos del paso "Cómo levantar todo" en una sola tanda si hace falta). Si algo quedó en un estado raro, las capturas de `docs/capturas-cambios-agus/demo/` muestran cada pantalla del guion ya armada — mostralas como respaldo mientras reiniciás.
- **Una pantalla muestra un error o queda vacía**: pasá a la siguiente del guion y volvé después si da tiempo. El único que de verdad depende de otro es el bloque 4 (necesita el turno que crea el bloque 3) — si el 3 falla, mostrá en el bloque 4 que "Mis turnos" arranca vacío y explicá que ahí se vería el turno recién pedido.
- **La videollamada no conecta**: confirmá que `node server.js` sigue corriendo en su ventana; si no, es el único bloque que se puede saltar entero sin que se note — no está en el guion de arriba a propósito.
- **Alguien pide ver código**: tené `CAMBIOS_AGUS.md` abierto en otra ventana, tiene el detalle de cada fase.
