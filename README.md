# CarWash API

## Reporte de ventas y sesiones temporales

Ventas representa cobros de TurnosMovimientos, incluidos parciales y Credito.
GET /api/Reportes/Ventas requiere fechaInicial/fechaFinal en yyyy-MM-dd y acepta
idFormaPago, idOperario, placa completa e idTipoVehiculo opcionales por AND.
Periodo por fecha del cobro, inclusivo en dias de la zona operativa. Resumen
global (cantidad e importe decimal textual), detalle descendente por fecha/id,
pageSize 50 por defecto (maximo 200) y nextCursor opaco ligado a criterios.
GET /api/Reportes/Ventas/Filtros recibe solo fechas y devuelve opciones historicas,
incluidas relaciones inactivas. No altera datos ni calcula desde servicios.
Las paginas son vivas; no hay snapshot entre peticiones.

Ambos GET y los GET raiz, {id} y GetHistoricoMovimientos de
/api/TurnosMovimientos requieren Bearer y Administrador actual en base.
Ausente/invalido/vencido/revocado: 401 SESSION_REQUIRED; perfil sin permiso: 403
ADMIN_REQUIRED. Datos invalidos: 400; fuente indisponible o esencial invalida:
503; errores inesperados: 500. ProblemDetails seguro y Cache-Control: no-store.
No cambian contratos/permisos de escrituras ni la fecha de ingreso del historico.

POST /api/Auth/login conserva cifrado y respuesta previa; agrega accessToken,
tokenType=Bearer, expiresAt e idleTimeoutSeconds a login valido. Credencial
aleatoria, solo hash en memoria del servidor, sin JWT ni refresh token.
POST /api/Auth/logout con Bearer canonico devuelve 204 incluso si ya no existe;
sin credencial o malformada devuelve 401. Reiniciar proceso invalida sesiones.
No guardar tokens/contrasenas en disco ni logs; clientes requieren reingreso.

Configuracion opcional no secreta (seccion Session): IdleTimeoutMinutes=30,
AbsoluteLifetimeHours=8, MaxSessions=10000. BusinessTimeZoneId usa zona local del
servidor si se omite; identificador valido, sin migrar fechas locales existentes
a UTC. X-Server-Date informa dia operativo en login/reporte. No activar
SensitiveDataLogging ni registro de cuerpos/URLs con placas.

Actualizar API, escritorio y APK coordinadamente: clientes anteriores reciben
401 en historicos, sin excepciones por version. Probar primero en entorno aislado;
la documentacion de Mobile contiene quickstart y gates reproducibles. HTTPS con
certificado confiable recomendado; HTTP privado permite interceptar credenciales.
No desactivar TLS ni exponer la API publicamente. Esto no es una auditoria global.

## Configuración de correo electrónico

La API puede notificar la recepción de un vehículo usando SMTP. Las
credenciales deben configurarse únicamente en el servidor (por ejemplo, con
variables de entorno o el almacén de secretos de IIS), nunca en el APK ni en
Git. Use `appsettings.example.json` como referencia sin colocar allí valores
reales.

Configuración requerida:

Para Gmail, use estos valores:

- `Email:Host`: `smtp.gmail.com`
- `Email:Port`: `587`
- `Email:Username`: la cuenta Gmail emisora
- `Email:Password`: contraseña de aplicación de Google, no la contraseña normal
- `Email:FromAddress`: la misma cuenta Gmail emisora
- `Email:FromName`: `CarWash`
- `Email:EnableSsl`: `true`
- `Email:MaxAttempts`: `3`

La cuenta debe tener la verificación en dos pasos habilitada y una contraseña
de aplicación creada en la configuración de seguridad de Google. Configure los
valores sensibles en el servidor mediante variables de entorno de ASP.NET Core,
sin guardarlos en `appsettings.json` ni en Git:

```powershell
[Environment]::SetEnvironmentVariable("Email__Username", "alexbeltrangarz@gmail.com", "Machine")
[Environment]::SetEnvironmentVariable("Email__Password", "CONTRASENA_DE_APLICACION", "Machine")
[Environment]::SetEnvironmentVariable("Email__FromAddress", "alexbeltrangarz@gmail.com", "Machine")
```

Después de configurar las variables, reinicie el proceso o el sitio de IIS.
No comparta la contraseña de aplicación por chat ni la incluya en capturas.

El correo contiene los datos del ingreso confirmado y no se envía si el correo
del cliente es inválido.

Endpoints:

- `POST /api/Turnos/{idTurno}/notificacion-email`
- `GET /api/Turnos/{idTurno}/notificacion-email`
- `POST /api/Turnos/{idTurno}/notificacion-email/reintentar`

La tabla `EmailNotifications` se crea automáticamente al iniciar la API. Para
instalaciones existentes también puede ejecutarse
`Migrations/202610290002_AddEmailNotifications.sql` sobre la base configurada
en `ConnectionStrings:DefaultConnection`. No se deben enviar correos reales
desde pruebas automatizadas.
