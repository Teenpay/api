using Microsoft.EntityFrameworkCore;
using TeenPay.Models;

namespace TeenPay.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    // --- DB SETS ---
    public DbSet<TeenpayUser> Users => Set<TeenpayUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<StudentSchool> StudentSchools => Set<StudentSchool>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ParentChild> ParentChildren => Set<ParentChild>();
    public DbSet<TopUpRequest> TopUpRequests => Set<TopUpRequest>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // схема
        modelBuilder.HasDefaultSchema("teenpay");
        modelBuilder.Entity<StudentSchool>().ToTable("student_schools", "teenpay");

        // -------- USERS TABLE --------
        modelBuilder.Entity<TeenpayUser>(e =>
        {
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

            e.Property(x => x.Balance)
                .HasColumnName("balance")
                .HasColumnType("numeric(12,2)");

            e.Property(x => x.PersonalCode)
            .HasColumnName("personal_code");

            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
        });


        // -------- REFRESH TOKENS --------
        modelBuilder.Entity<RefreshToken>(e =>
        {
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

        // -------- SCHOOLS --------
        modelBuilder.Entity<School>(e =>
        {
            e.ToTable("schools");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.City).HasColumnName("city");
            e.Property(x => x.Address).HasColumnName("address");
            e.Property(x => x.PosUserId).HasColumnName("pos_user_id");
        });

        // -------- TRANSACTIONS --------
        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");

            e.HasKey(x => x.id);

            e.Property(x => x.userid).HasColumnName("user_id");
            e.Property(x => x.amount).HasColumnName("amount");
            e.Property(x => x.kind).HasColumnName("kind");
            e.Property(x => x.description).HasColumnName("description");
            e.Property(x => x.createdat).HasColumnName("created_at");
            e.Property(x => x.childid).HasColumnName("child_id");
            e.Property(x => x.schoolid).HasColumnName("school_id");
        });

        // -------- TRANSACTIONS Parent&Child --------
        modelBuilder.Entity<ParentChild>(e =>
        {
            e.ToTable("parent_children", "teenpay");  // <- проверь название таблицы в БД
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ParentUserId).HasColumnName("parent_user_id");
            e.Property(x => x.ChildUserId).HasColumnName("child_user_id");

            e.HasIndex(x => new { x.ParentUserId, x.ChildUserId }).IsUnique();
        });

        // --------Parent&Child Topups --------
        modelBuilder.Entity<TopUpRequest>(e =>
        {
            e.ToTable("topup_requests", "teenpay");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChildId).HasColumnName("child_id");
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.RequestedAt).HasColumnName("requested_at");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.Note).HasColumnName("note");
        });

        base.OnModelCreating(modelBuilder);
    }
}
