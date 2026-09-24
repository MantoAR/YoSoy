-- Columnas que el código ya usa pero que ningún script del repo crea
-- (ni migrations/ ni el "Script de la Base de Datos" de la rama DataBase).
-- Tipos deducidos del uso en el código. Idempotente: se puede correr más de una vez.

-- Agenda.IntervaloMinutos: AgendaDAO lo lee con (int) y usa 30 si viene NULL.
IF COL_LENGTH('dbo.Agenda', 'IntervaloMinutos') IS NULL
    ALTER TABLE Agenda
    ADD IntervaloMinutos INT NOT NULL CONSTRAINT DF_Agenda_IntervaloMinutos DEFAULT 30;
GO

-- Turno.ConfirmadoPaciente: se setea en 1 desde /turnos/{id}/confirmar-paciente
-- y TurnoDetalleDAO lo lee con ISNULL(..., 0).
IF COL_LENGTH('dbo.Turno', 'ConfirmadoPaciente') IS NULL
    ALTER TABLE Turno
    ADD ConfirmadoPaciente BIT NOT NULL CONSTRAINT DF_Turno_ConfirmadoPaciente DEFAULT 0;
GO

-- Turno.CodigoSala: "SALA-{id}-{ticks hex}" generado en /turnos/{id}/generar-sala.
-- Mismo tipo que InformeAdmision.CodigoSala.
IF COL_LENGTH('dbo.Turno', 'CodigoSala') IS NULL
    ALTER TABLE Turno
    ADD CodigoSala NVARCHAR(50) NULL;
GO

-- PacienteProfesional.EstadoAsignacion: 'Nueva', 'Confirmada', 'Rechazada'.
-- Queda NULL para las filas existentes: el código trata NULL como 'Confirmada'.
IF COL_LENGTH('dbo.PacienteProfesional', 'EstadoAsignacion') IS NULL
    ALTER TABLE PacienteProfesional
    ADD EstadoAsignacion NVARCHAR(50) NULL;
GO
