/* ============================================================================
   deshacer-migracion-35.sql — Saca de la base lo que agregó la migración 35 (documentos clínicos).

   CUÁNDO: solo si hay que volver atrás ANTES de usar los documentos en serio. Lo normal es volver a la API anterior
   (ignora estas tablas) y dejar la base como está.

   QUÉ PIERDE: TODOS los documentos clínicos emitidos, sus PDF y sus addenda, las plantillas y sus versiones, y las
   firmas cargadas por los profesionales. Un documento emitido es parte de la historia clínica: si ya hay documentos
   reales, NO corras esto; restaurá el backup o dejá la base migrada. El primer SELECT muestra cuántos hay.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-35.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.DocumentoClinico', 'U') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS documentos_emitidos_que_se_pierden FROM dbo.DocumentoClinico;');
GO

DROP TRIGGER IF EXISTS dbo.TR_DocumentoClinicoAddendum_SoloAgregar;
DROP TRIGGER IF EXISTS dbo.TR_DocumentoClinico_SoloAgregar;
DROP TRIGGER IF EXISTS dbo.TR_PlantillaDocumentoVersion_Proteger;
GO
DROP TABLE IF EXISTS dbo.DocumentoClinicoAddendum;
DROP TABLE IF EXISTS dbo.DocumentoClinico;
DROP TABLE IF EXISTS dbo.PlantillaDocumentoVersion;
DROP TABLE IF EXISTS dbo.PlantillaDocumento;
GO
IF COL_LENGTH('dbo.Profesional', 'FirmaImagen') IS NOT NULL
    ALTER TABLE dbo.Profesional DROP COLUMN FirmaImagen;
GO
-- 'Organizacion.Localidad' queda en ConfiguracionOrganizacion: no molesta y la puede usar otra pantalla.
DELETE FROM dbo.VersionEsquema WHERE Numero = 35;
GO

PRINT N'Migración 35 deshecha en la base. Falta volver a la API anterior si todavía no se hizo.';
SELECT (SELECT MAX(Numero) FROM dbo.VersionEsquema) AS ultima_version;
GO
