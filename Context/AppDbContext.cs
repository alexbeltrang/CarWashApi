namespace Carwash.API.Context
{
    using Carwash.API.Controllers;
    using Carwash.API.Modelos;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

    public class AppDbContext : DbContext
    {
        public DbSet<CajaDiaria> CajaDiaria { get; set; }
        public DbSet<TipoVehiculo> TipoVehiculo { get; set; }
        public DbSet<PrecioServicioVehiculo> PrecioServicioVehiculo { get; set; }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Servicios> Servicios { get; set; }
        public DbSet<Turnos> Turnos { get; set; }
        public DbSet<TurnoServicios> TurnoServicios { get; set; }
        public DbSet<TurnosDiarios> TurnosDiarios { get; set; }
        public DbSet<Operarios> Operarios { get; set; }
        public DbSet<TurnosMovimientos> TurnosMovimientos { get; set; }
        public DbSet<ClienteCredito> ClienteCredito { get; set; }
        public DbSet<OperarioComisiones> OperarioComisiones { get; set; }
        public DbSet<FormaPago> FormaPagos { get; set; }
        public DbSet<ValesOperarios> valesOperarios { get; set; }
        public DbSet<GastosCaja> GastosCaja { get; set; }
        public DbSet<RegistroPropinasOperarios> RegistroPropinasOperarios { get; set; }
        public DbSet<AsistenciaOperario> AsistenciaOperario { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var dateTimeConverter = new ValueConverter<DateTime, string>(
                v => v.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                v => DateTime.Parse(v));

            var nullableDateTimeConverter = new ValueConverter<DateTime?, string?>(
                v => v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss.fff") : null,
                v => v != null ? DateTime.Parse(v) : (DateTime?)null);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                        property.SetValueConverter(dateTimeConverter);
                    else if (property.ClrType == typeof(DateTime?))
                        property.SetValueConverter(nullableDateTimeConverter);
                }
            }

            modelBuilder.Entity<FormaPago>().ToTable("FormaPago");

            base.OnModelCreating(modelBuilder);
        }
    }
}
