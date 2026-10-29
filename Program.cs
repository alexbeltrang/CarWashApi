using Carwash.API.Context;
using Carwash.API.Configuration;
using Carwash.API.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOptions<WhatsAppOptions>()
    .Bind(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.AddHttpClient<IWhatsAppClient, MetaWhatsAppClient>(client =>
{
    client.BaseAddress = new Uri("https://graph.facebook.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<WhatsAppTemplateBuilder>();
builder.Services.AddScoped<WhatsAppNotificationService>();

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
        """);
}

// Swagger siempre activo para pruebas
app.UseSwagger();
app.UseSwaggerUI();

// Redirigir raíz a Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseAuthorization();

app.MapControllers();

app.Run();