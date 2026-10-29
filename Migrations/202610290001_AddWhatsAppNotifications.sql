-- Aplicar una sola vez sobre la base SQLite de CarWash.
CREATE TABLE IF NOT EXISTS WhatsAppNotifications (
    Id INTEGER NOT NULL CONSTRAINT PK_WhatsAppNotifications PRIMARY KEY AUTOINCREMENT,
    IdTurno INTEGER NOT NULL,
    DestinatarioNormalizado TEXT NOT NULL,
    Estado TEXT NOT NULL,
    Intentos INTEGER NOT NULL,
    MessageIdExterno TEXT NULL,
    FbTraceId TEXT NULL,
    UltimoErrorCodigo TEXT NULL,
    CreadoEn TEXT NOT NULL,
    ActualizadoEn TEXT NOT NULL,
    ProximoIntentoEn TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_WhatsAppNotifications_IdTurno
    ON WhatsAppNotifications (IdTurno);
