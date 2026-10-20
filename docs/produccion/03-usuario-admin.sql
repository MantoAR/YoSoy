/* ============================================================================
   03-usuario-admin.sql — Primer usuario administrador (Configurador) de YoSoy.

   Por qué hace falta este script: no hay ningún camino limpio para crear el
   primer administrador. POST /usuarios (el único lugar del código que
   inserta en la tabla Usuario) exige ya estar logueado como Configurador —
   y en una base nueva no existe ninguno. No hay semilla, no hay registro
   público de administrador; los únicos "usuarios de prueba" que existen
   son los de los scripts de demo (ver docs/CATALOGO_SCRIPTS_SQL.md), que no
   van a producción. Este script rompe ese círculo, una sola vez.

   Cómo decide la app que este usuario es "Configurador" (etapa 2 de roles y permisos): por los permisos que tiene asignados
   (UsuarioRol / UsuarioPermiso, migración 20). Este script le da los DOS módulos: "configurador" (usuarios, permisos y configuración) y
   "crm" (los chats y los casos), así el primer administrador puede además atender la bandeja de Chats. Al ingresar elige con cuál trabajar
   y puede cambiar sin volver a entrar. Sin esas filas el ingreso da 403 SIN_PERMISOS. Requiere haber corrido antes 01-produccion.sql.
   Hash de la contraseña: igual que Program.cs (POST /usuarios) —
   Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).
   En SQL Server: HASHBYTES('SHA2_256', ...) sobre un literal SIN el prefijo
   N (así es VARCHAR de un byte por carácter, no NVARCHAR de dos) + CONVERT
   con estilo 2 (hex en mayúsculas, sin "0x"). Verificado a mano: para una
   contraseña puramente ASCII, este cálculo en SQL Server da EXACTAMENTE el
   mismo hex que el C# de arriba.

   *** La contraseña tiene que ser ASCII puro: sin tildes, sin ñ, sin emojis,
   sin ningún carácter fuera de A-Z/a-z/0-9/símbolos comunes. *** Con un
   acento o una ñ, UTF-8 ocupa más de un byte por carácter y el hash de SQL
   Server (que acá asume un byte por carácter) ya no coincide con el de C#
   — lo confirmé a mano: la misma contraseña con una "ñ" da un hash distinto
   en cada lado. Tampoco puede contener una comilla simple ('): sqlcmd
   sustituye $(AdminPassword) como texto antes de mandar el lote al
   servidor, y una comilla ahí corta el literal a la mitad.

   La contraseña NUNCA va en este archivo — iguales reglas que
   02-usuario-app.sql: SIEMPRE por -v AdminPassword="..." en la línea de
   comandos, nunca con :setvar acá adentro (:setvar pisa sin condición lo
   que venga de -v). Usuario y mail SÍ están fijos en el script (no son
   secretos) — cambialos antes de correrlo si querés otros.

   Cómo correrlo:
     sqlcmd -S TU_SERVIDOR -d yosoy -C -f 65001 -v AdminPassword="LA_CONTRASEÑA_REAL" -i 03-usuario-admin.sql

   Idempotente: si el Username ya existe, actualiza el hash y el estado en
   vez de duplicar la fila (sirve para rotar la contraseña del admin:
   correrlo de nuevo con un valor distinto).
   ============================================================================ */

USE [yosoy];
GO

IF EXISTS (SELECT 1 FROM Usuario WHERE Username = N'admin')
BEGIN
    UPDATE Usuario
    SET PasswordHash = CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '$(AdminPassword)'), 2),
        Estado = 'Activo',
        Mail = N'admin@yosoytelemedicina.tech'
    WHERE Username = N'admin';
    PRINT 'Usuario "admin" ya existía: contraseña y estado actualizados.';
END
ELSE
BEGIN
    INSERT INTO Usuario (Username, PasswordHash, Estado, Mail)
    VALUES (
        N'admin',
        CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '$(AdminPassword)'), 2),
        'Activo',
        N'admin@yosoytelemedicina.tech'
    );
    PRINT 'Usuario "admin" creado.';
END
GO

-- Los dos módulos del administrador (idempotente: no repite lo que ya tiene).
DECLARE @IdAdmin INT = (SELECT IdUsuario FROM Usuario WHERE Username = N'admin');
INSERT INTO UsuarioRol (IdUsuario, IdRol)
SELECT @IdAdmin, r.IdRol FROM Rol r
WHERE r.Clave IN ('configurador', 'crm')
  AND NOT EXISTS (SELECT 1 FROM UsuarioRol x WHERE x.IdUsuario = @IdAdmin AND x.IdRol = r.IdRol);
INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso)
SELECT @IdAdmin, p.IdRol, p.IdPermiso FROM Permiso p
WHERE p.Clave IN ('configurador', 'crm')
  AND NOT EXISTS (SELECT 1 FROM UsuarioPermiso x WHERE x.IdUsuario = @IdAdmin AND x.IdPermiso = p.IdPermiso);
PRINT 'Usuario "admin" con los módulos Configurador y CRM.';
GO
