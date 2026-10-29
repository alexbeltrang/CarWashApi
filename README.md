# CarWash API

## Configuración de WhatsApp

La API puede notificar la recepción de un vehículo usando Meta Cloud API. Las
credenciales deben configurarse únicamente en el servidor (por ejemplo, con
variables de entorno o el almacén de secretos de IIS), nunca en el APK ni en
Git. Use `appsettings.example.json` como referencia sin colocar allí valores
reales.

Configuración requerida:

- `WhatsApp:GraphVersion`
- `WhatsApp:PhoneNumberId`
- `WhatsApp:AccessToken`
- `WhatsApp:TemplateName`
- `WhatsApp:TemplateLanguage`
- `WhatsApp:CarWashName`
- `WhatsApp:WebhookVerifyToken`
- `WhatsApp:MaxAttempts`

La plantilla debe estar aprobada en español y contener todos los parámetros
esperados. El modelo actual no tiene color; por seguridad, la notificación
queda `NOT_APPLICABLE` hasta que exista una fuente legítima para ese dato.

Endpoints:

- `POST /api/Turnos/{idTurno}/notificacion-whatsapp`
- `GET /api/Turnos/{idTurno}/notificacion-whatsapp`
- `POST /api/Turnos/{idTurno}/notificacion-whatsapp/reintentar`
- `GET|POST /api/webhooks/whatsapp`

Las pruebas deben usar un cliente Meta simulado. No se deben enviar mensajes
reales desde pruebas automatizadas.
