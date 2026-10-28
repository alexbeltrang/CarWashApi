PRAGMA foreign_keys = OFF;
BEGIN IMMEDIATE;

CREATE TEMP TABLE TurnoServiciosRepairGuard (compatible INTEGER NOT NULL CHECK (compatible = 1));
INSERT INTO TurnoServiciosRepairGuard
SELECT CASE WHEN
    (SELECT COUNT(*) FROM pragma_table_info('TurnoServicios')) = 4
    AND (SELECT COUNT(*) FROM pragma_table_info('TurnoServicios')
         WHERE name IN ('idTunoServicios', 'idServicios', 'IdTurno', 'IsDeleted')) = 4
    AND EXISTS (SELECT 1 FROM pragma_table_info('TurnoServicios') WHERE name = 'idTunoServicios' AND pk = 0)
    AND NOT EXISTS (SELECT 1 FROM sqlite_master WHERE tbl_name = 'TurnoServicios'
                    AND type IN ('index', 'trigger') AND name <> 'TurnoServicios_IdTurno')
    THEN 1 ELSE 0 END;

CREATE TABLE TurnoServiciosRepaired (
    idTunoServicios INTEGER PRIMARY KEY AUTOINCREMENT,
    idServicios INTEGER,
    IdTurno INTEGER,
    IsDeleted INTEGER
);

INSERT INTO TurnoServiciosRepaired (idTunoServicios, idServicios, IdTurno, IsDeleted)
SELECT idTunoServicios, idServicios, IdTurno, IsDeleted
FROM TurnoServicios WHERE idTunoServicios IS NOT NULL;

INSERT INTO TurnoServiciosRepaired (idServicios, IdTurno, IsDeleted)
SELECT idServicios, IdTurno, IsDeleted
FROM TurnoServicios WHERE idTunoServicios IS NULL ORDER BY rowid;

DROP TABLE TurnoServicios;
ALTER TABLE TurnoServiciosRepaired RENAME TO TurnoServicios;
CREATE INDEX TurnoServicios_IdTurno ON TurnoServicios (IdTurno);
DROP TABLE TurnoServiciosRepairGuard;

COMMIT;
PRAGMA foreign_keys = ON;