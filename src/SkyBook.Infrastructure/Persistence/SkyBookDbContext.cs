using Microsoft.EntityFrameworkCore;
using SkyBook.Domain.Entities;

namespace SkyBook.Infrastructure.Persistence;

public class SkyBookDbContext(DbContextOptions<SkyBookDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSegment> BookingSegments => Set<BookingSegment>();
    public DbSet<Passenger> Passengers => Set<Passenger>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<SupplierLog> SupplierLogs => Set<SupplierLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(20);
        });

        b.Entity<Agent>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.AgencyName).HasMaxLength(150);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.WalletBalance).HasPrecision(18, 2);
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
            e.Property(x => x.MarkupPercent).HasPrecision(5, 2);
        });

        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => x.Pnr).IsUnique();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.Property(x => x.Pnr).HasMaxLength(6).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Supplier).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.SupplierRef).HasMaxLength(50);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.TotalFare).HasPrecision(18, 2);
            e.HasIndex(x => new { x.AgentId, x.CreatedAt })
             .IsDescending(false, true)
             .IncludeProperties(x => new { x.Pnr, x.Status, x.TotalFare })
             .HasDatabaseName("IX_Bookings_Agent_Created");
            e.HasIndex(x => new { x.Status, x.HoldExpiresAt })
             .HasFilter("[Status] = 'Held'")
             .HasDatabaseName("IX_Bookings_Status_HoldExpires");
        });

        b.Entity<Passenger>().Property(x => x.Type).HasConversion<string>().HasMaxLength(3).IsUnicode(false);
        b.Entity<Refund>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.RefundAmount).HasPrecision(18, 2);
            e.Property(x => x.Penalty).HasPrecision(18, 2);
        });
        b.Entity<WalletTransaction>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Amount).HasPrecision(18, 2);
        });
        b.Entity<SupplierLog>().HasIndex(x => x.CorrelationId);
    }
}
