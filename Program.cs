using Carwash.API.Context;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger siempre activo para pruebas
app.UseSwagger();
app.UseSwaggerUI();

// Redirigir raíz a Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseAuthorization();

app.MapControllers();

app.Run();