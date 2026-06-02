ALTER TABLE RecuperacionPassword
ADD Token NVARCHAR(64) NULL,
    TokenExpiracion DATETIME NULL;
