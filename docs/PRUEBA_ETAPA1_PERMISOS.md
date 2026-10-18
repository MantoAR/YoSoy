# Prueba de la etapa 1: permisos, especialidades y errores con código

Esta etapa no cambia lo que ve ningún usuario. Lo que se prueba es:
- que la base quedó con roles, permisos, privilegios y especialidades;
- que la base rechaza combinaciones inválidas;
- que la API avisa cuando falta una migración;
- que cada error deja un código que se puede buscar.

En los comandos, reemplazá `TU_SERVIDOR` por el nombre de tu servidor (en SSMS: `SELECT @@SERVERNAME;`).

## 0. Rama

```bash
cd /d/YoSoy
git status                          # que no haya cambios sin guardar
git switch feature/circuito-atencion
git switch -c feature/permisos-etapa1
```
Descomprimí el zip sobre la carpeta `YoSoy` y hacé el commit:
```bash
git add -A
git commit -m "Etapa 1 de roles: permisos, especialidades, versión de esquema y errores con código"
```

## 1. La API avisa que falta la migración 16

1. **Todavía sin correr la migración 16**, arrancá la API con `dotnet run`.
2. **Esperado en la consola**, en rojo: `La base de datos no está actualizada. Falta la migración 16 (add_permisos_especialidades_errores.sql)...`.
3. Abrí `http://localhost:5000/pantallas/index.html` e intentá ingresar con cualquier usuario.
4. **Esperado:** en lugar de "Usuario o contraseña incorrectos", aparece el mismo mensaje con la migración que falta.

Si la consola nombra otras migraciones además de la 16, corré esas primero, en orden.

## 2. Correr la migración 16

Dejá la API andando. En otra ventana de Git Bash:
```bash
cd /d/YoSoy
sqlcmd -S 'TU_SERVIDOR' -d yosoy -E -C -f 65001 -i migrations/add_permisos_especialidades_errores.sql
```
**Esperado** al final de la salida:
- `Migración 16 aplicada.`
- Una tabla **MigracionFaltante** vacía (0 filas).
- Una tabla con los profesionales a completar. Si aparece alguno, es porque le falta la matrícula o porque su especialidad es un servicio ("Admision"). No es un error: se completa en la etapa 4.

Sin reiniciar la API, esperá 30 segundos y volvé a ingresar. **Esperado:** entra como siempre.

## 3. Revisar la base

En SSMS:
```sql
SELECT * FROM VersionEsquema ORDER BY Numero;   -- 16 filas: 1 a 15 'Detectada', 16 'Script'
SELECT Clave, Nombre FROM Rol WHERE Clave IS NOT NULL;
SELECT p.Clave, p.Nombre, r.Clave AS Rol FROM Permiso p JOIN Rol r ON r.IdRol = p.IdRol;
SELECT * FROM vw_UsuarioAccesos;                -- cada usuario con sus roles y permisos
SELECT Nombre, Disciplina, Tipo, RequiereMatriculaEspecialista, Estado FROM Especialidad ORDER BY Tipo, Disciplina, Nombre;
```
**Esperado:**
- En `vw_UsuarioAccesos`, cada profesional tiene el permiso **Profesional** y cada configurador el permiso **Configurador**.
- Los pacientes tienen `TipoCuenta = Paciente`, y nadie del equipo tiene `SinPermisos = 1`, salvo los profesionales que listó el paso 2.

## 4. La base rechaza lo que no corresponde

Cada bloque deshace lo que hace (`ROLLBACK`), así que no deja nada cambiado.

**a) Admisión en un permiso que no la admite.**
```sql
BEGIN TRAN;
DECLARE @u INT = (SELECT TOP 1 IdUsuario FROM UsuarioPermiso up JOIN Permiso p ON p.IdPermiso = up.IdPermiso WHERE p.Clave = 'configurador');
INSERT INTO UsuarioPrivilegio (IdUsuario, IdPermiso, IdPrivilegio)
SELECT @u, (SELECT IdPermiso FROM Permiso WHERE Clave = 'configurador'), (SELECT IdPrivilegio FROM Privilegio WHERE Clave = 'admision');
ROLLBACK;
```
**Esperado:** error de clave foránea `FK_UsuarioPrivilegio_PermisoPrivilegio`.

**b) Rol Profesional a alguien sin matrícula.**
```sql
BEGIN TRAN;
DECLARE @u INT = (SELECT TOP 1 IdUsuario FROM UsuarioPermiso up JOIN Permiso p ON p.IdPermiso = up.IdPermiso WHERE p.Clave = 'configurador');
DECLARE @rol INT = (SELECT IdRol FROM Rol WHERE Clave = 'profesional');
INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (@u, @rol);
INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) VALUES (@u, @rol, (SELECT IdPermiso FROM Permiso WHERE Clave = 'auditor'));
ROLLBACK;
```
**Esperado:** error 50017, `RN-17: el rol Profesional requiere una ficha de profesional con matrícula y especialidad.`

**c) Permiso de un rol que el usuario no tiene.**
```sql
BEGIN TRAN;
DECLARE @u INT = (SELECT TOP 1 IdUsuario FROM UsuarioPermiso up JOIN Permiso p ON p.IdPermiso = up.IdPermiso WHERE p.Clave = 'configurador');
INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso)
SELECT @u, IdRol, IdPermiso FROM Permiso WHERE Clave = 'crm';
ROLLBACK;
```
**Esperado:** error de clave foránea `FK_UsuarioPermiso_UsuarioRol`.

## 5. Error con código, con la base andando

1. Ingresá como **Configurador**.
2. Averiguá el id del rol Configurador: `SELECT IdRol FROM Rol WHERE Clave = 'configurador';`.
3. En el navegador, abrí la consola (F12 → Console) y pegá esto, cambiando `ID_ROL` por ese número. Asigna dos veces el mismo rol al mismo usuario, así que la base lo rechaza:
   ```js
   const r = await YS.fetch("/usuarioroles", { method: "POST", json: { idUsuario: Number(localStorage.getItem("idUsuario")), idRol: ID_ROL } });
   console.log(r.status, await YS.leerMensaje(r));
   ```
4. **Esperado:** `500 Ocurrió un error en el sistema. Código: XXXX-XXXX. ...`
5. Buscalo en la base:
   ```sql
   SELECT * FROM LogError WHERE Codigo = 'XXXX-XXXX';
   ```
   Tiene que aparecer una fila con fecha, ruta `/usuarioroles`, tu usuario, el tipo `SqlException` y el mensaje de clave duplicada.
6. También tiene que estar en el archivo `D:\YoSoy\logs\errores-AAAAMMDD.log`.

## 6. Error con código, con la base caída

Esta prueba es la que justifica el archivo de respaldo.

1. Detené SQL Server: Windows → `services.msc` → **SQL Server (SQLEXPRESS)** → Detener.
2. Intentá ingresar al sistema.
3. **Esperado:**
   - un mensaje con código, no "contraseña incorrecta" (puede tardar 15 a 30 segundos, lo que espera la conexión);
   - el error está en `logs/errores-AAAAMMDD.log`;
   - en la consola de la API aparece "No se pudo guardar el error ... en LogError", porque la base estaba caída.
4. Volvé a iniciar el servicio de SQL Server.

## 7. Pruebas automáticas

```bash
dotnet test YoSoy.Tests
```
**Esperado:** pasan las de siempre más `VerificadorEsquemaTests` y `RegistroErroresTests`. Las pruebas que usan la base necesitan la migración 16 aplicada.

## 8. Que todo siga igual

Ingresá con un paciente, un profesional y un configurador. Cada uno tiene que ver exactamente lo mismo que antes de esta etapa.

## Opcional, para la demo de la etapa 2

Darle a un profesional los permisos de auditor y el privilegio de Admisión. Esto **sí** queda guardado:
```sql
DECLARE @user NVARCHAR(50) = 'USUARIO_DEL_PROFESIONAL';
DECLARE @u INT = (SELECT IdUsuario FROM Usuario WHERE Username = @user);
DECLARE @rol INT = (SELECT IdRol FROM Rol WHERE Clave = 'profesional');
DECLARE @aud INT = (SELECT IdPermiso FROM Permiso WHERE Clave = 'auditor');
IF NOT EXISTS (SELECT 1 FROM UsuarioPermiso WHERE IdUsuario = @u AND IdPermiso = @aud)
    INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) VALUES (@u, @rol, @aud);
INSERT INTO UsuarioPrivilegio (IdUsuario, IdPermiso, IdPrivilegio)
SELECT @u, @aud, IdPrivilegio FROM Privilegio
WHERE Clave = 'admision'
  AND NOT EXISTS (SELECT 1 FROM UsuarioPrivilegio x JOIN Privilegio p ON p.IdPrivilegio = x.IdPrivilegio
                  WHERE x.IdUsuario = @u AND x.IdPermiso = @aud AND p.Clave = 'admision');
SELECT * FROM vw_UsuarioAccesos WHERE Username = @user;
```
