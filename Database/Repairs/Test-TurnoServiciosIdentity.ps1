param([string]$Sqlite = 'sqlite3')

$ErrorActionPreference = 'Stop'
$database = Join-Path ([IO.Path]::GetTempPath()) ("CarWash-IdentityTest-{0}.db" -f [Guid]::NewGuid())
$repair = Get-Content (Join-Path $PSScriptRoot '20261028_TurnoServiciosIdentity.sql') -Raw

function Invoke-Sql([string]$Sql) {
    $result = & $Sqlite -bail $database $Sql
    if ($LASTEXITCODE -ne 0) { throw 'Fallo SQL en base aislada.' }
    return ($result -join "`n").Trim()
}

try {
    Invoke-Sql @'
CREATE TABLE TurnoServicios (idTunoServicios INTEGER, idServicios INTEGER, IdTurno INTEGER, IsDeleted INTEGER);
CREATE INDEX TurnoServicios_IdTurno ON TurnoServicios (IdTurno);
INSERT INTO TurnoServicios VALUES (NULL,1,100,0),(50,2,100,0),(NULL,3,101,0),(NULL,4,102,1);
'@ | Out-Null
    Invoke-Sql $repair | Out-Null

    $summary = Invoke-Sql 'SELECT COUNT(*),COUNT(idTunoServicios),COUNT(DISTINCT idTunoServicios),SUM(idServicios),SUM(IdTurno),SUM(IsDeleted) FROM TurnoServicios;'
    if ($summary -ne '4|4|4|10|403|1') { throw 'La reparacion altero o perdio asociaciones.' }
    if ((Invoke-Sql 'SELECT idTunoServicios FROM TurnoServicios WHERE idServicios=2;') -ne '50') {
        throw 'Cambio un identificador existente.'
    }
    if ((Invoke-Sql 'SELECT COUNT(*) FROM sqlite_master WHERE type=''index'' AND name=''TurnoServicios_IdTurno'';') -ne '1') {
        throw 'No se conservo el indice.'
    }
    $generated = Invoke-Sql 'BEGIN; INSERT INTO TurnoServicios (idServicios,IdTurno,IsDeleted) VALUES (9,200,0) RETURNING idTunoServicios; ROLLBACK;'
    if ($generated -ne '54') { throw 'INSERT RETURNING no genero la identidad esperada.' }
    if ((Invoke-Sql 'SELECT COUNT(*) FROM TurnoServicios;') -ne '4') { throw 'La prueba de insercion no revirtio.' }
    if ((Invoke-Sql 'PRAGMA quick_check;') -ne 'ok') { throw 'Fallo integridad SQLite.' }

    $rejected = $false
    try { Invoke-Sql $repair | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'La reparacion debe rechazar una tabla ya reparada.' }
    if ((Invoke-Sql 'SELECT COUNT(*) FROM TurnoServicios;') -ne '4') { throw 'Reejecucion altero datos.' }
    Write-Output 'PASS: conserva asociaciones e IDs existentes, repara NULLs, restaura indice, genera identidad y revierte pruebas.'
} finally {
    Remove-Item -LiteralPath $database -Force -ErrorAction SilentlyContinue
}