using Microsoft.EntityFrameworkCore;
using TeenPay.Models;

namespace TeenPay.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TeenpayUser> Users => Set<TeenpayUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("teenpay");

        modelBuilder.Entity<TeenpayUser>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasColumnName("token").HasMaxLength(200).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            e.Property(x => x.DeviceId).HasColumnName("device_id");
            e.Property(x => x.Revoked).HasColumnName("revoked");
            e.Property(x => x.UserId).HasColumnName("user_id");

            e.HasIndex(x => x.Token).IsUnique();

            e.HasOne(x => x.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

