/* ============================================================================
   02-usuario-app.sql — Login de SQL Server para la API (yosoy_app).

   Por qué un login de SQL y no Windows: en el VPS (Linux) no hay autenticación
   integrada de Windows — Conexion.cs ya no la usa (lee ConnectionStrings__YoSoy
   del entorno, ver docs/SERVIDOR.md). yosoy_app es ese usuario: solo puede
   leer y escribir datos en la base [yosoy], nada de permisos de servidor ni
   de otras bases.

   La contraseña NUNCA va en este archivo. Se pasa como variable SQLCMD al
   invocar sqlcmd — este script no sirve pegado en una ventana de consulta
   normal de SSMS, necesita modo SQLCMD (Query > SQLCMD Mode) o la línea de
   comandos, porque usa $(AppPassword).

   Cómo correrlo:
     sqlcmd -S TU_SERVIDOR -d master -C -f 65001 -v AppPassword="LA_CONTRASEÑA_REAL" -i 02-usuario-app.sql
   La contraseña la elige quien lo corre, en el momento, por la línea de
   comandos (no queda en ningún archivo ni en el historial de shell si se usa
   con cuidado) — tiene que cumplir la política de complejidad de contraseñas
   de SQL Server (mayúscula, minúscula, número o símbolo, 8+ caracteres) o el
   CREATE LOGIN de abajo falla con un mensaje claro.

   Idempotente: se puede correr más de una vez (por ejemplo, para rotar la
   contraseña — DROP LOGIN y volver a correr esto con el valor nuevo).

   La clave SIEMPRE se pasa con -v AppPassword="..." en la línea de comandos,
   nunca con :setvar acá adentro: :setvar pisa sin condición lo que venga de
   -v, así que si este archivo la declarara (aunque fuera vacía), siempre
   terminaría ganando ella y -v no serviría para nada. Sin -v, sqlcmd tira un
   error claro de variable sin definir en vez de usar un valor por defecto.
   ============================================================================ */

USE [master];
GO

IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'yosoy_app')
    DROP LOGIN [yosoy_app];
GO

CREATE LOGIN [yosoy_app]
    WITH PASSWORD = N'$(AppPassword)',
    CHECK_POLICY = ON,
    CHECK_EXPIRATION = OFF;
GO

USE [yosoy];
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'yosoy_app')
    DROP USER [yosoy_app];
GO

CREATE USER [yosoy_app] FOR LOGIN [yosoy_app];
GO

-- Lectura y escritura de datos en todas las tablas de [yosoy]. El código no
-- ejecuta DDL en tiempo de ejecución (no crea/altera tablas) ni usa stored
-- procedures propias, así que no hace falta db_ddladmin ni EXECUTE suelto.
ALTER ROLE [db_datareader] ADD MEMBER [yosoy_app];
GO
ALTER ROLE [db_datawriter] ADD MEMBER [yosoy_app];
GO
