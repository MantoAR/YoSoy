# Prueba: alta de un profesional por el Configurador

Requiere la migración 19. En los comandos, `TU_SERVIDOR` es el de siempre (`DESKTOP-Q065FTM\SQLEXPRESS`).

## 1. Migración 19

```bash
cd /d/YoSoy
sqlcmd -S 'TU_SERVIDOR' -d yosoy -E -C -f 65001 -i migrations/add_alta_equipo_invitaciones.sql
```

**Esperado:** "Migración 19 aplicada." Reiniciá la API (`dotnet run`).

La migración también repara los acentos que quedaron rotos en la 16 (por ejemplo "AdmisiÃ³n" pasa a "Admisión"). Para comprobarlo: `SELECT Clave, Nombre FROM Privilegio;`

## 2. Registrar al profesional

Ingresá como Configurador (ventana InPrivate la primera vez). **Usuarios › Registrar usuario**:

| Campo | Valor de prueba |
| --- | --- |
| Nombre / Apellido | Juan / Gómez |
| CUIL | 20-12345678-6 (si ya existe, 27-12345678-0) |
| Teléfono | 351 555-1234 |
| Email | un mail tuyo que no esté usado por otra cuenta |
| Usuario | se sugiere solo: `juan.gomez` |
| Rol | Profesional → **Profesional auditor**, con **Privilegio: Admisión** |
| Matrícula / Jurisdicción / Especialidad | MP 9999 / Córdoba / Psicología |

Probá también un CUIL con el último dígito mal (20-12345678-5): **Esperado:** "El CUIL no es válido".

**Registrar y enviar invitación.** **Esperado:**
- si el mail está configurado: "Le enviamos la invitación a …";
- si no (contraseña del correo vacía en `appsettings`): aviso amarillo y, por ser desarrollo, el **link** de la invitación para copiar.

## 3. Todavía no puede ingresar

En el ingreso, pestaña Profesional, `juan.gomez` con cualquier contraseña. **Esperado:** no entra (la cuenta está pendiente).
En **Usuarios › Permisos** aparece con estado *Pendiente* y el botón **Reenviar invitación**.

## 4. Confirmar la cuenta

Abrí el link del mail (o el que mostró el panel). **Esperado:** "Hola Juan…", con el usuario `juan.gomez` ya escrito. Elegí una contraseña de 8 o más caracteres con letras y números y **Confirmar cuenta**.

Volvé a abrir el mismo link. **Esperado:** "Esta cuenta ya está confirmada…" (un solo uso).

## 5. Ingresar como el profesional nuevo

Pestaña **Profesional**, `juan.gomez` y la contraseña elegida. **Esperado:** panel profesional con los grupos **Admisión** y **Auditoría**.

## 6. Privilegios

Como Configurador, **Usuarios › Privilegios**: Admisión muestra a `juan.gomez` en "Lo tienen". Quitáselo y volvé a agregarlo con **Agregar a**. En **Auditoría** quedan: "Alta de usuario del equipo", "Permiso asignado · Profesional auditor", "Privilegio asignado/quitado · Admisión", "Cuenta activada".

En la base:
```sql
SELECT Username, Estado, Telefono, Cuil FROM Usuario WHERE Username = 'juan.gomez';
SELECT * FROM vw_UsuarioAccesos WHERE Username = 'juan.gomez';
SELECT IdUsuario, Creada, Vence, Usada, Anulada FROM InvitacionCuenta ORDER BY IdInvitacion DESC;  -- no hay token, solo su hash
```
