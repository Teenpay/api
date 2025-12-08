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
            /*e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.HasIndex(x => x.Username).IsUnique();*/

            e.ToTable("users");
            e.HasKey(x => x.Id);

            e.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(64)
                .IsRequired();

            e.Property(x => x.PasswordHash)
                .HasColumnName("password_hash")
                .IsRequired();

            e.Property(x => x.Email)
                .HasColumnName("email")
                .HasMaxLength(128);

            e.Property(x => x.FirstName)
                .HasColumnName("first_name")
                .HasMaxLength(64);

            e.Property(x => x.LastName)
                .HasColumnName("last_name")
                .HasMaxLength(64);

            e.Property(x => x.PhoneNumber)
                .HasColumnName("phone");

            e.Property(x => x.Role)
                .HasColumnName("role");

            // если есть колонка balance в таблице users:
             e.Property(x => x.Balance)
                 .HasColumnName("balance")
                 .HasColumnType("numeric(12,2)");

            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            /* e.ToTable("refresh_tokens");
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
              .HasForeignKey(x => x.UserId) */

            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);

            e.Property(x => x.Token)
                .HasColumnName("token")
                .HasMaxLength(200)
                .IsRequired();

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

       /* modelBuilder.Entity<TeenpayUser>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);

            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();

            // NEW:
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(128);
            e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(64);
            e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(64);

            e.HasIndex(x => x.Username).IsUnique();
            // Если хочешь уникальность email — раскомментируй:
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PhoneNumber).HasColumnName("phone");
            e.Property(x => x.Role).HasColumnName("role");

        }); */

    }
}

