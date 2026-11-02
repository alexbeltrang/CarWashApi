CREATE TABLE IF NOT EXISTS EmailNotifications (
    Id INTEGER NOT NULL CONSTRAINT PK_EmailNotifications PRIMARY KEY AUTOINCREMENT,
    IdTurno INTEGER NOT NULL,
    Destinatario TEXT NOT NULL,
    Estado TEXT NOT NULL,
    Intentos INTEGER NOT NULL,
    UltimoErrorCodigo TEXT NULL,
    CreadoEn TEXT NOT NULL,
    ActualizadoEn TEXT NOT NULL,
    ProximoIntentoEn TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_EmailNotifications_IdTurno
    ON EmailNotifications (IdTurno);
