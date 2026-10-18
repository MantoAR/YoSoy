-- Usuario de base propio para la API, con permisos mínimos (etapa 2, auditoría inalterable).
--
-- Hoy la API se conecta con la cuenta de Windows de quien la ejecuta (Trusted_Connection),
-- que en una PC de desarrollo suele ser administradora de SQL Server: con esa cuenta se
-- pueden desactivar los triggers que protegen la auditoría. Con este usuario, la API:
--   - lee y escribe en todas las tablas (db_datareader + db_datawriter);
--   - ejecuta los procedimientos (sp_VerificarCadenaAuditoria);
--   - NO puede modificar ni borrar LogAuditoria, LogError ni VersionEsquema (DENY);
--   - NO puede alterar tablas ni desactivar triggers (no es db_owner ni db_ddladmin).
-- Las migraciones se siguen corriendo con un usuario administrador, nunca con este.
--
-- OBLIGATORIO EN PRODUCCIÓN. En desarrollo es opcional.
--
-- Antes de correrlo:
--  1. SQL Server tiene que aceptar usuarios de SQL Server además de Windows: en SSMS, clic
--     derecho en el servidor → Propiedades → Seguridad → "Modo de autenticación de SQL
--     Server y de Windows", y reiniciar el servicio.
--  2. Cambiar la contraseña de abajo por una larga y aleatoria, y no versionarla.
-- Después: cargar la cadena de conexión con ese usuario en user-secrets o en la variable
-- de entorno ConnectionStrings__YoSoy (ver README), por ejemplo:
--   Server=TU_SERVIDOR;Database=yosoy;User Id=yosoy_app;Password=...;TrustServerCertificate=True;

USE master;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'yosoy_app')
    CREATE LOGIN yosoy_app WITH PASSWORD = 'CAMBIAR-por-una-clave-larga-y-aleatoria', CHECK_POLICY = ON;
GO

USE yosoy;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'yosoy_app')
    CREATE USER yosoy_app FOR LOGIN yosoy_app;
GO
ALTER ROLE db_datareader ADD MEMBER yosoy_app;
ALTER ROLE db_datawriter ADD MEMBER yosoy_app;
GRANT EXECUTE TO yosoy_app;
-- Ver la estructura (no cambiarla): lo necesita el aviso de migraciones faltantes, que
-- busca tablas, columnas y triggers por nombre (Services/VerificadorEsquema.cs).
GRANT VIEW DEFINITION TO yosoy_app;
GO
-- Solo agregar en los registros (además de los triggers de la migración 17).
DENY UPDATE, DELETE ON dbo.LogAuditoria TO yosoy_app;
DENY UPDATE, DELETE ON dbo.LogError TO yosoy_app;
DENY UPDATE, DELETE ON dbo.VersionEsquema TO yosoy_app;
GO

-- Comprobación: tiene que listar SELECT/INSERT y los DENY de arriba.
SELECT dp.name AS Usuario, o.name AS Objeto, p.permission_name AS Permiso, p.state_desc AS Estado
FROM sys.database_permissions p
JOIN sys.database_principals dp ON dp.principal_id = p.grantee_principal_id
LEFT JOIN sys.objects o ON o.object_id = p.major_id
WHERE dp.name = 'yosoy_app';
GO
