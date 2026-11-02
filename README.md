# CarWash API

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
