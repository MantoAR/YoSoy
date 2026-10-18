# Prueba de la etapa 2: acceso por módulo y auditoría inalterable

Qué se prueba:
- el ingreso con una pestaña por rol (Paciente, Profesional, CRM, Facturación, Configurador);
- el aviso para usuarios sin permisos;
- la elección y el cambio de módulo;
- los registros de ingresos y de accesos a historias clínicas;
- que la auditoría no se pueda modificar.

Requiere la etapa 1 aplicada (migración 16). En los comandos, `TU_SERVIDOR` es el nombre de tu servidor (en SSMS: `SELECT @@SERVERNAME;`).

## 0. Rama

```bash
cd /d/YoSoy
git status                                  # sin cambios pendientes
git switch feature/permisos-etapa1          # con la etapa 1 ya commiteada
git switch -c feature/permisos-etapa2
git apply --3way /ruta/al/etapa2.patch      # o descomprimir el zip sobre la carpeta
git add -A
git commit -m "Etapa 2 de roles: acceso por módulo y auditoría inalterable"
```

## 1. La API pide la migración 17

1. Arrancá la API con `dotnet run`.
2. **Esperado:** la consola dice que falta la migración 17 (`add_auditoria_inalterable.sql`), y el ingreso muestra el mismo mensaje.
3. Corré la migración:
   ```bash
   sqlcmd -S 'TU_SERVIDOR' -d yosoy -E -C -f 65001 -i migrations/add_auditoria_inalterable.sql
   ```
4. **Esperado:** "Migración 17 aplicada.". A los 30 segundos se puede ingresar, sin reiniciar la API.

## 2. Los mismos usuarios entran igual que antes

| Usuario | Pestaña | Esperado |
| --- | --- | --- |
| Un paciente | Paciente | Su panel, como siempre |
| Un profesional | Profesional | Panel profesional, como siempre |
| Un configurador | Configurador | Panel del configurador, como siempre |
| Un profesional | Configurador | "Tu usuario no tiene el rol Configurador. Elegí la pestaña de tu rol." |
| Un profesional | Paciente | "Esta cuenta no es de paciente: elegí la pestaña de tu rol." |
| Un paciente | Profesional | "Esta es una cuenta de paciente: ingresá desde la pestaña Paciente." |

## 3. Usuario sin permisos

En SSMS, creá un usuario sin rol ni permiso (contraseña `1234`):
```sql
INSERT INTO Usuario (Username, PasswordHash, Estado, Mail, IntentosFallidos)
VALUES ('prueba.sinpermisos', UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2)), 'Activo', 'sinpermisos@yosoy.test', 0);
```
Ingresá con él en la pestaña Profesional (o cualquier otra del equipo). **Esperado:** "Tu usuario todavía no tiene permisos asignados. Comunicate con el administrador de tu organización." Antes de esta etapa, ese usuario entraba como Configurador.

## 4. Dos roles: entrar por cada uno y cambiar

Dale a un profesional también el módulo Configurador (como el profesional individual):
```sql
DECLARE @u INT = (SELECT IdUsuario FROM Usuario WHERE Username = 'USUARIO_DEL_PROFESIONAL');
DECLARE @rol INT = (SELECT IdRol FROM Rol WHERE Clave = 'configurador');
IF NOT EXISTS (SELECT 1 FROM UsuarioRol WHERE IdUsuario = @u AND IdRol = @rol)
    INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (@u, @rol);
INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso)
SELECT @u, @rol, IdPermiso FROM Permiso WHERE Clave = 'configurador'
  AND NOT EXISTS (SELECT 1 FROM UsuarioPermiso x JOIN Permiso p ON p.IdPermiso = x.IdPermiso WHERE x.IdUsuario = @u AND p.Clave = 'configurador');
SELECT * FROM vw_UsuarioAccesos WHERE IdUsuario = @u;
```
1. Ingresá con ese profesional por la pestaña **Profesional**. **Esperado:** panel profesional. En el menú del usuario (arriba a la derecha) aparece **Cambiar de módulo**.
2. Cambiá a Configurador. **Esperado:** entra al panel del Configurador sin volver a iniciar sesión.
3. Cerrá sesión e ingresá por la pestaña **Configurador**. **Esperado:** entra directo al panel del Configurador.

## 5. Módulo sin pantalla todavía (CRM)

Dale el rol y el permiso CRM al usuario del paso 3:
```sql
DECLARE @u INT = (SELECT IdUsuario FROM Usuario WHERE Username = 'prueba.sinpermisos');
DECLARE @rol INT = (SELECT IdRol FROM Rol WHERE Clave = 'crm');
INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (@u, @rol);
INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) SELECT @u, @rol, IdPermiso FROM Permiso WHERE Clave = 'crm';
```
Ingresá con él por la pestaña **CRM**. **Esperado:** la página "CRM y atención al cliente" con el aviso de que se habilita en una próxima etapa.

## 6. La auditoría registra los ingresos y los accesos

1. Equivocate una vez de contraseña con un usuario y después ingresá bien.
2. Como profesional, abrí la historia clínica de un paciente tuyo.
3. Como configurador, abrí la de un paciente.
4. En SSMS:
   ```sql
   SELECT TOP 20 IdLog, FechaHora, IdUsuario, Categoria, Espacio, Accion, Detalle
   FROM LogAuditoria ORDER BY IdLog DESC;
   ```
5. **Esperado:**
   - "Ingreso fallido · Contraseña incorrecta" e "Ingreso", en la categoría **Administrativa**;
   - "Cambio de módulo", si hiciste el paso 4;
   - "Apertura de historia clínica" en la categoría **Clinica**: la del profesional dice `como tratante` y la del configurador, `como configurador`;
   - ningún contenido clínico en Detalle.

## 7. La auditoría no se puede modificar

```sql
UPDATE LogAuditoria SET Detalle = 'cambio' WHERE IdLog = (SELECT MAX(IdLog) FROM LogAuditoria);
DELETE FROM LogAuditoria WHERE IdLog = (SELECT MAX(IdLog) FROM LogAuditoria);
EXEC sp_VerificarCadenaAuditoria;
```
**Esperado:** el `UPDATE` y el `DELETE` dan el error 50018 (RN-18), y la verificación dice **Íntegra**.

Ahora, para ver que la cadena detecta una alteración hecha por un administrador que desactiva el trigger, corré esto completo. El `ROLLBACK` deshace todo, el trigger incluido:
```sql
BEGIN TRAN;
DISABLE TRIGGER TR_LogAuditoria_SoloAgregar ON LogAuditoria;
UPDATE LogAuditoria SET Detalle = 'alterado' WHERE IdLog = (SELECT MAX(IdLog) FROM LogAuditoria WHERE Hash IS NOT NULL);
EXEC sp_VerificarCadenaAuditoria;      -- Esperado: 'Rota', "El contenido del registro cambió..."
ROLLBACK;
EXEC sp_VerificarCadenaAuditoria;      -- Esperado: 'Íntegra' otra vez
```

## 8. Usuario de base de la aplicación (opcional en desarrollo)

`Scripts/crear_usuario_aplicacion.sql` crea `yosoy_app`, que no puede modificar ni borrar la auditoría ni desactivar triggers. Para probarlo:
1. Seguí las instrucciones del encabezado del script.
2. Configurá la cadena con ese usuario:
   ```bash
   dotnet user-secrets set "ConnectionStrings:YoSoy" "Server=TU_SERVIDOR;Database=yosoy;User Id=yosoy_app;Password=...;TrustServerCertificate=True;"
   ```
3. Repetí los pasos 2 y 6.

Para volver a la conexión de siempre: `dotnet user-secrets remove "ConnectionStrings:YoSoy"`.

## 9. Pruebas automáticas

```bash
dotnet test YoSoy.Tests
```
**Esperado:** pasan las de siempre más `AccesoPorModuloTests`. Las pruebas que antes borraban registros de `LogAuditoria` al terminar ya no lo hacen.

## Limpieza (opcional)

Para sacarle los permisos al usuario de prueba, borrá su rol (los permisos se van en cascada):
```sql
DELETE ur FROM UsuarioRol ur JOIN Usuario u ON u.IdUsuario = ur.IdUsuario WHERE u.Username = 'prueba.sinpermisos';
```
El usuario no se puede borrar, porque tiene registros de auditoría: queda en la base como cualquier cuenta real dada de baja.
