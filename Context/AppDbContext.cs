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
        public DbSet<WhatsAppNotification> WhatsAppNotifications { get; set; }
        public DbSet<EmailNotification> EmailNotifications { get; set; }
        public DbSet<ComprobanteRetiro> ComprobantesRetiro { get; set; }
        public DbSet<ImpresionComprobante> ImpresionesComprobante { get; set; }
        public DbSet<Empresa> Empresas { get; set; }
        public DbSet<ConfiguracionFacturacion> ConfiguracionFacturacion { get; set; }
        public DbSet<Factura> Facturas { get; set; }

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
            modelBuilder.Entity<Empresa>().ToTable("Empresa", table => table.HasCheckConstraint("CK_Empresa_Unica", "Id = 1"));
            modelBuilder.Entity<ConfiguracionFacturacion>().ToTable("ConfiguracionFacturacion",
                table => table.HasCheckConstraint("CK_Facturacion_Unica", "Id = 1"));
            modelBuilder.Entity<Factura>().HasIndex(invoice => invoice.IdTurno).IsUnique();
            modelBuilder.Entity<Factura>().HasIndex(invoice => new { invoice.Prefijo, invoice.Numero }).IsUnique();
            modelBuilder.Entity<WhatsAppNotification>()
                .HasIndex(notification => notification.IdTurno)
                .IsUnique();
            modelBuilder.Entity<EmailNotification>()
                .HasIndex(notification => notification.IdTurno)
                .IsUnique();
            modelBuilder.Entity<ComprobanteRetiro>()
                .HasIndex(receipt => receipt.Codigo)
                .IsUnique();
            modelBuilder.Entity<ImpresionComprobante>().HasIndex(job => new { job.IdTurno, job.CreadoEn });

            base.OnModelCreating(modelBuilder);
        }
    }
}
