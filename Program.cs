using Carwash.API.Context;
using Carwash.API.Configuration;
using Carwash.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using System.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "SplashCar", "printing.json"), optional: true, reloadOnChange: false);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<Carwash.API.Configuration.SessionOptions>()
    .Bind(builder.Configuration.GetSection(Carwash.API.Configuration.SessionOptions.SectionName))
    .Validate(options => options.IdleTimeoutMinutes is > 0 and <= 1440
        && options.AbsoluteLifetimeHours is > 0 and <= 168 && options.MaxSessions is > 0 and <= 10000)
    .ValidateOnStart();
builder.Services.AddSingleton<ApiSessionStore>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddScoped<IAuthorizationHandler, AdministradorAuthorizationHandler>();
builder.Services.AddAuthorization(options => options.AddPolicy(AdministradorAuthorizationHandler.PolicyName, policy =>
    policy.AddAuthenticationSchemes(SessionAuthenticationHandler.SchemeName).RequireAuthenticatedUser()
        .AddRequirements(new AdministradorRequirement())));
builder.Services.AddOptions<WhatsAppOptions>()
    .Bind(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddHttpClient<IWhatsAppClient, MetaWhatsAppClient>(client =>
{
    client.BaseAddress = new Uri("https://graph.facebook.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<WhatsAppTemplateBuilder>();
builder.Services.AddScoped<WhatsAppNotificationService>();
builder.Services.AddScoped<EmailNotificationService>();
builder.Services.AddScoped<RetiroVehiculosService>();
builder.Services.AddScoped<EmpresaService>();
builder.Services.AddScoped<FacturacionService>();
builder.Services.AddScoped<IFacturacionService>(services => services.GetRequiredService<FacturacionService>());
builder.Services.AddOptions<PrintingOptions>().Bind(builder.Configuration.GetSection("Printing"))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.PrinterName) && options.PrinterName.Length <= 256)
    .ValidateOnStart();
if (OperatingSystem.IsWindows()) builder.Services.AddSingleton<IReceiptPrinter, WindowsReceiptPrinter>();
else builder.Services.AddSingleton<IReceiptPrinter, UnavailableReceiptPrinter>();
builder.Services.AddScoped<ImpresionComprobantesService>();
builder.Services.AddScoped<IImpresionComprobantesService>(services => services.GetRequiredService<ImpresionComprobantesService>());
builder.Services.AddHostedService<ReceiptPrintWorker>();
builder.Services.AddScoped<IEmailClient, SmtpEmailClient>();
builder.Services.AddScoped<ReporteVentasService>();

// SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.ExecuteSqlRaw("""
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
        CREATE TABLE IF NOT EXISTS ComprobantesRetiro (
            IdTurno INTEGER NOT NULL CONSTRAINT PK_ComprobantesRetiro PRIMARY KEY,
            Codigo TEXT NOT NULL,
            CreadoEn TEXT NOT NULL,
            EntregadoEn TEXT NULL,
            EntregadoPor INTEGER NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ComprobantesRetiro_Codigo
            ON ComprobantesRetiro (Codigo);
        CREATE TABLE IF NOT EXISTS ImpresionesComprobante (
            SolicitudId TEXT NOT NULL CONSTRAINT PK_ImpresionesComprobante PRIMARY KEY,
            IdTurno INTEGER NOT NULL,
            AnchoMm INTEGER NOT NULL,
            SolicitadoPor INTEGER NOT NULL,
            Impresora TEXT NOT NULL,
            Estado TEXT NOT NULL,
            Texto TEXT NOT NULL,
            CreadoEn TEXT NOT NULL,
            ActualizadoEn TEXT NOT NULL,
            ErrorCodigo TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_ImpresionesComprobante_IdTurno_CreadoEn
            ON ImpresionesComprobante (IdTurno, CreadoEn);
        CREATE TABLE IF NOT EXISTS Empresa (
            Id INTEGER NOT NULL CONSTRAINT PK_Empresa PRIMARY KEY CHECK (Id = 1),
            DatosJson TEXT NOT NULL,
            Revision INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS ConfiguracionFacturacion (
            Id INTEGER NOT NULL CONSTRAINT PK_ConfiguracionFacturacion PRIMARY KEY CHECK (Id = 1),
            DatosJson TEXT NOT NULL,
            Revision INTEGER NOT NULL,
            SiguienteNumero INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS Facturas (
            Id TEXT NOT NULL CONSTRAINT PK_Facturas PRIMARY KEY,
            IdTurno INTEGER NOT NULL,
            Prefijo TEXT NOT NULL,
            Numero INTEGER NOT NULL,
            Fecha TEXT NOT NULL,
            Placa TEXT NOT NULL,
            DatosJson TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Facturas_IdTurno ON Facturas (IdTurno);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Facturas_Prefijo_Numero ON Facturas (Prefijo, Numero);
        """);
    using var command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = "PRAGMA table_info(Turnos)";
    db.Database.OpenConnection();
    var hasEmailColumn = false;
    using (var reader = command.ExecuteReader())
    {
        while (reader.Read())
            hasEmailColumn |= string.Equals(reader.GetString(1), "CorreoElectronico", StringComparison.OrdinalIgnoreCase);
    }
    if (!hasEmailColumn)
        db.Database.ExecuteSqlRaw("ALTER TABLE Turnos ADD COLUMN CorreoElectronico TEXT NULL");
    db.Database.CloseConnection();
}

// Swagger siempre activo para pruebas
app.UseSwagger();
app.UseSwaggerUI();

// Redirigir raíz a Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }