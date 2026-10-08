-- Fase 3a (Cambios-Agus), Paso 0: el "Motivo" de una reasignación manual de caso pasa de
-- texto libre a una lista cerrada, más una nota corta opcional que NUNCA se copia a
-- auditoría ni a logs (ver Services/AsignacionService.cs). Idempotente.
--
-- CasoDerivacion.Motivo ya existía (NVARCHAR(200), sin CHECK: hoy sirve para dos cosas —
-- el motivo cerrado de la cascada automática, AutomaticaPorCarga/AutomaticaAleatoria/
-- SinDisponibles, para Origen='Regla'; y el motivo libre que escribía el administrador,
-- para Origen='Manual'). Esta migración no toca esa columna ni sus filas existentes: solo
-- agrega la nota aparte y dejar la validación de la lista cerrada en la capa de aplicación
-- (AsignacionService), no un CHECK — la columna sigue sirviendo para los dos orígenes y un
-- CHECK que mezcle ambos vocabularios sería más confuso que útil.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CasoDerivacion') AND name = 'Nota')
    ALTER TABLE CasoDerivacion ADD Nota NVARCHAR(140) NULL;
GO
