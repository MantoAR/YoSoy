# Roles, permisos y módulos

Qué puede hacer cada persona del equipo y cómo se le da acceso. Viene de la rama `nuevo-circuito1` de Alearmas01 (etapa 2 de la integración). La tabla de endpoints está en [`MATRIZ_ENDPOINTS.md`](MATRIZ_ENDPOINTS.md); el despliegue, en [`DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md`](DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md).

## Los módulos

Cada **rol** es un **módulo** del sistema. Al ingresar se trabaja con **uno**; quien tiene más de uno elige y puede cambiar desde el menú de usuario → **Cambiar de módulo**, sin volver a ingresar (el servidor vuelve a mirar los permisos en la base cada vez).

| Módulo (clave) | Para qué | Qué NO hace |
|---|---|---|
| **Profesional** (`profesional`) | Sus pacientes, su agenda, sus casos, la sala de video, las historias clínicas de quien atiende | Nunca ve `/mensajeria` (403) |
| **CRM y atención al cliente** (`crm`) | Chats (`/mensajeria`), casos, derivaciones de casos, etiquetas y respuestas rápidas | Nunca ve historias clínicas |
| **Configurador** (`configurador`) | Usuarios (registrar usuario, permisos con sus privilegios, lista de usuarios, solicitudes de registro), configuración (WhatsApp con el código QR de conexión y especialidades), auditoría administrativa. Sin pantalla de inicio. Grupos, reglas de derivación, coberturas, roles, privilegios y la recuperación de contraseña **no están en el menú** (el código, los endpoints y los datos siguen) | Nada clínico ni operativo; no ve los chats si no tiene también el módulo CRM |
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

## El ingreso

`POST /login` verifica usuario y contraseña (el bloqueo es el de siempre: 3 intentos, 15 minutos, UTC) y después mira los permisos: sin ninguno, 403 `SIN_PERMISOS`; con un módulo, entra directo; con más de uno, responde `requiereModulo` y un token de **selección** (5 minutos, solo sirve para `POST /sesion/modulo`). Cada token de trabajo es de **un** módulo y lleva sus permisos y privilegios. Cada ingreso, rechazo, bloqueo y cambio de módulo queda en la auditoría, sin la contraseña.
