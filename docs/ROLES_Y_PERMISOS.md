# Roles, permisos y módulos

Qué puede hacer cada persona del equipo y cómo se le da acceso. Viene de la rama `nuevo-circuito1` de Alearmas01 (etapa 2 de la integración). La tabla de endpoints está en [`MATRIZ_ENDPOINTS.md`](MATRIZ_ENDPOINTS.md); el despliegue, en [`DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md`](DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md).

## Los módulos

Cada **rol** es un **módulo** del sistema. Al ingresar se trabaja con **uno**; quien tiene más de uno elige y puede cambiar desde el menú de usuario → **Cambiar de módulo**, sin volver a ingresar (el servidor vuelve a mirar los permisos en la base cada vez).

| Módulo (clave) | Para qué | Qué NO hace |
|---|---|---|
| **Profesional** (`profesional`) | Sus pacientes, su agenda, sus casos, la sala de video, las historias clínicas de quien atiende | Nunca ve `/mensajeria` (403) |
| **CRM y atención al cliente** (`crm`) | Chats (`/mensajeria`), casos, derivaciones de casos, etiquetas y respuestas rápidas | Nunca ve historias clínicas |
| **Configurador** (`configurador`) | Usuarios (registrar usuario, permisos con sus privilegios, la **lista única de usuarios** con restablecer contraseña y asignación de profesionales a un paciente, solicitudes de registro), configuración (WhatsApp con el código QR de conexión y especialidades), auditoría administrativa. Sin pantalla de inicio. Grupos, reglas de derivación, coberturas, roles, privilegios y la recuperación de contraseña **no están en el menú** (el código, los endpoints y los datos siguen) | Nada clínico ni operativo; no ve los chats si no tiene también el módulo CRM |
| **Facturación** (`facturacion`) | Todavía una página "en construcción" | — |

## Permisos y privilegios

Dentro de un módulo hay **permisos**; un **privilegio** es una capacidad puntual que se suma a un permiso.

- **Profesional**: permiso `profesional` (lo suyo) o `auditor` (**Profesional auditor**: además lee todas las historias clínicas, los reportes de la organización y la auditoría clínica, siempre en solo lectura; no lee las emociones de las sesiones de otros).
  Privilegio **Admisión** (sobre `profesional` o `auditor`): informes de admisión, primera entrevista, derivaciones, asignación de pacientes a profesionales y solicitudes de cambio.
- **CRM**: permiso `crm`. Privilegio **Gestión de agendas**: turnos y horarios de trabajo de todos los profesionales (no da acceso a historias clínicas).
- Quien tiene el rol Profesional **tiene que tener** ficha de profesional con matrícula y especialidad (RN-17; la API y un trigger de la base lo exigen).

## Cómo se asigna

- **Configurador › Registrar usuario**: da de alta a alguien del equipo (nombre, CUIL, mail, usuario, rol y privilegios) y le manda un mail con un link (un solo uso, 72 horas) para confirmar la cuenta y elegir la contraseña. La cuenta queda `Pendiente` hasta entonces.
- **Configurador › Permisos**: los módulos, permisos y privilegios de cada usuario (`PUT /usuarios/{id}/accesos`). Para darle un **segundo módulo** a alguien (por ejemplo, el CRM al administrador), se tilda en esa pantalla; se aplica en el **próximo ingreso o cambio de módulo** de esa persona. Nadie puede quitarse a sí mismo el permiso de Configurador.
- **Privilegios** (Admisión, Gestión de agendas): se asignan desde **Configurador › Permisos**, junto con el permiso que los admite. La pantalla "Privilegios", que listaba quién tiene cada uno, ya no está en el menú (su código y sus endpoints siguen).
- Todo cambio queda en la auditoría administrativa (lo registra la base, aunque se haga con SQL).
- Soporte: `SELECT * FROM vw_UsuarioAccesos WHERE Username = '...'` dice qué tiene una persona y si está sin permisos.

## Usuarios: la lista única, restablecer la contraseña y asignar profesionales

**Configurador › Usuarios** es una sola lista con todas las cuentas (pacientes, profesionales y equipo, cada una con su tipo), con buscador (usuario, nombre, DNI, matrícula o mail), filtros por tipo y por estado (activos, dados de baja, sin confirmar, bloqueados) y paginación, todo en el servidor. Los pacientes y profesionales que todavía no tienen usuario están en el filtro **Sin cuenta**. Cada fila tiene un menú de acciones. La lista no trae nada clínico ni el hash de la contraseña.

- **Restablecer contraseña** (cualquier tipo de cuenta, menos la propia): genera una **clave temporal** que se muestra **una sola vez** (con botón de copiar), desbloquea la cuenta y obliga a la persona a elegir una clave nueva al ingresar. Queda en la auditoría administrativa quién lo hizo y a quién; **nunca la clave**. La clave temporal no vence. **Límite conocido: no se cierran las sesiones que esa persona ya tenía abiertas** (no hay revocación de tokens: siguen valiendo hasta que vencen, 8 horas).
- **Primer ingreso con la clave temporal**: el ingreso (cualquier tipo de cuenta, pacientes incluidos) verifica la contraseña y el bloqueo como siempre, pero en vez de una sesión devuelve un token de **cambio de clave** (5 minutos) que solo sirve para `POST /sesion/cambiar-password`; en cualquier otra ruta se rechaza. La pantalla de ingreso muestra el paso **Elegí tu clave nueva** (mínimo 6 caracteres, distinta de la temporal); al guardarla se entrega la sesión normal, con el mismo camino de módulos y permisos del ingreso.
- **Asignar profesionales a un paciente**: uno o varios, con buscador; se ven los ya asignados (solo nombre, matrícula y especialidad) y se pueden quitar. El profesional tiene que estar activo, el paciente también, y **el Configurador no puede asignarse a sí mismo** (si su usuario tiene ficha de profesional). Es aparte de la asignación de Admisión (que no cambia) y queda en la auditoría administrativa.

## El ingreso

`POST /login` verifica usuario y contraseña (el bloqueo es el de siempre: 3 intentos, 15 minutos, UTC) y después mira los permisos: sin ninguno, 403 `SIN_PERMISOS`; con un módulo, entra directo; con más de uno, responde `requiereModulo` y un token de **selección** (5 minutos, solo sirve para `POST /sesion/modulo`). Si la cuenta tiene una **clave temporal** (la restableció el Configurador), después de verificar la contraseña no entrega sesión: responde `requiereCambioPassword` y un token de cambio de clave (ver arriba). Cada token de trabajo es de **un** módulo y lleva sus permisos y privilegios. Cada ingreso, rechazo, bloqueo y cambio de módulo queda en la auditoría, sin la contraseña.
