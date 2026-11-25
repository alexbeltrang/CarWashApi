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
        public DbSet<CajaControl> CajaControl { get; set; }
        public DbSet<CajaCierre> CajaCierres { get; set; }
        public DbSet<CajaAuditoria> CajaAuditoria { get; set; }
        public DbSet<CajaOperacion> CajaOperaciones { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var decimalTextConverter = new ValueConverter<decimal, string>(
                value => DecimalText.Format(value), text => DecimalText.Parse(text));
            modelBuilder.Entity<CajaDiaria>().Property(row => row.MontoInicial).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<CajaDiaria>().Property(row => row.TotalIngresosEfectivo).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<CajaDiaria>().Property(row => row.TotalIngresosTransferencias).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<CajaDiaria>().Property(row => row.TotalIngresosDatafono).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<CajaDiaria>().Property(row => row.TotalIngresosCredito).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<CajaDiaria>().Property(row => row.TotalEgresos).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<CajaDiaria>().Property(row => row.TotalValesOperarios).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<CajaDiaria>().Property(row => row.TotalFinal).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<Turnos>().Property(row => row.Valor).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<Turnos>().Property(row => row.ValorBaseComision).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<Turnos>().Property(row => row.PorcentajeComision).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<Turnos>().Property(row => row.ValorComision).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<TurnosMovimientos>().Property(row => row.MontoPagado).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<GastosCaja>().Property(row => row.Valor).HasConversion(decimalTextConverter).HasColumnType("TEXT");
            modelBuilder.Entity<ValesOperarios>().Property(row => row.Valor).HasConversion(decimalTextConverter).HasColumnType("TEXT");

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

            modelBuilder.Entity<CajaControl>(entity =>
            {
                entity.ToTable("CajaControl", table => table.HasCheckConstraint("CK_CajaControl_Revision", "Revision >= 0"));
                entity.Property(row => row.IdCaja).ValueGeneratedNever();
                entity.Property(row => row.Revision).IsConcurrencyToken();
                entity.HasOne<CajaDiaria>().WithOne().HasForeignKey<CajaControl>(row => row.IdCaja).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<CajaCierre>().WithMany().HasForeignKey(row => row.IdCierreVigente).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<CajaCierre>(entity =>
            {
                entity.ToTable("CajaCierres", table => table.HasCheckConstraint("CK_CajaCierres_Version", "Version >= 1 AND SnapshotVersion >= 1"));
                entity.HasIndex(row => new { row.IdCaja, row.Version }).IsUnique();
                entity.HasOne<CajaDiaria>().WithMany().HasForeignKey(row => row.IdCaja).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<CajaCierre>().WithMany().HasForeignKey(row => row.IdCierreAnterior).OnDelete(DeleteBehavior.Restrict);
                entity.Property(row => row.EfectivoContado).HasConversion<string>();
                entity.Property(row => row.Diferencia).HasConversion<string>();
            });
            modelBuilder.Entity<CajaOperacion>().ToTable("CajaOperaciones");
            modelBuilder.Entity<CajaAuditoria>(entity =>
            {
                entity.ToTable("CajaAuditoria");
                entity.HasIndex(row => row.IdOperacion).IsUnique();
                entity.HasIndex(row => new { row.IdsCajaAfectadasJson, row.FechaUtc });
                entity.HasOne<CajaOperacion>().WithOne().HasForeignKey<CajaAuditoria>(row => row.IdOperacion).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<TurnosMovimientos>().HasIndex(row => new { row.IdCaja, row.FechaMovimiento, row.IdTurnoMovimientos });
            modelBuilder.Entity<TurnosMovimientos>().HasOne<CajaDiaria>().WithMany().HasForeignKey(row => row.IdCaja).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GastosCaja>().HasIndex(row => row.idCaja);
            modelBuilder.Entity<ValesOperarios>().HasIndex(row => row.idCaja);
            modelBuilder.Entity<CajaDiaria>().HasIndex(row => row.Estado).IsUnique().HasFilter("Estado = 1");

            base.OnModelCreating(modelBuilder);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ValidateCashEvidence();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ValidateCashEvidence();
            return SaveWithParentsAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private async Task<int> SaveWithParentsAsync(bool acceptAllChangesOnSuccess, CancellationToken token)
        {
            var parents = ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                .Select(entry => entry.Entity switch
                {
                    TurnoServicios row => row.IdTurno,
                    EmailNotification row => row.IdTurno,
                    WhatsAppNotification row => row.IdTurno,
                    ComprobanteRetiro row => row.IdTurno,
                    ImpresionComprobante row => row.IdTurno,
                    Factura row => row.IdTurno,
                    _ => 0
                }).Where(identifier => identifier != 0).Distinct().ToArray();
            await using var owned = parents.Length > 0 && Database.CurrentTransaction == null
                ? await Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            foreach (var identifier in parents)
            {
                var local = ChangeTracker.Entries<Turnos>().SingleOrDefault(entry => entry.Entity.IdTurno == identifier);
                if (local?.State == EntityState.Deleted || local?.State != EntityState.Added && !await Turnos.AsNoTracking().AnyAsync(row => row.IdTurno == identifier, token))
                    throw new DbUpdateException("TURN_NOT_FOUND");
            }
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, token);
            if (owned != null) await owned.CommitAsync(token);
            return result;
        }

        private void ValidateCashEvidence()
        {
            ChangeTracker.DetectChanges();
            if (ChangeTracker.Entries().Any(entry => entry.Entity is Modelos.CajaCierre or Modelos.CajaAuditoria or Modelos.CajaOperacion
                && entry.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("CAJA_EVIDENCE_IMMUTABLE");
            if (ChangeTracker.Entries<CajaControl>().Any(entry => entry.State == EntityState.Modified
                && entry.Entity.Revision <= entry.OriginalValues.GetValue<long>(nameof(Modelos.CajaControl.Revision))))
                throw new InvalidOperationException("CAJA_REVISION_REQUIRED");
        }
    }
}
