using Microsoft.EntityFrameworkCore;
using TeenPay.Models;

namespace TeenPay.Data;

// AppDbContext — EF Core datu konteksts, kas apraksta TeenPay datu bāzes tabulas un to konfigurāciju
public class AppDbContext : DbContext
{
    // Konstruktorā tiek padotas DB pieslēguma opcijas (connection string, provider, u.c.)
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    // ==========================================================
    // DB SETS — tabulu (entītiju) kolekcijas, ar kurām strādā EF Core
    // ==========================================================
    public DbSet<TeenpayUser> Users => Set<TeenpayUser>();                 // users — sistēmas lietotāji (vecāks/bērns/POS/admin)
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();       // refresh_tokens — refresh token glabāšana (sessijas atjaunošanai)
    public DbSet<School> Schools => Set<School>();                         // schools — skolas/organizācijas, kurām ir POS lietotājs
    public DbSet<StudentSchool> StudentSchools => Set<StudentSchool>();    // student_schools — bērnu piesaiste skolām (daudz-pret-daudz)
    public DbSet<Transaction> Transactions => Set<Transaction>();          // transactions — finanšu darījumu žurnāls (maksājumi, top-up u.c.)
    public DbSet<ParentChild> ParentChildren => Set<ParentChild>();        // parent_children — vecāka un bērna sasaistes
    public DbSet<TopUpRequest> TopUpRequests => Set<TopUpRequest>();       // topup_requests — bērna top-up pieprasījumi vecākam
    public DbSet<Receipt> Receipts => Set<Receipt>();                      // receipts — kvītis/čeku dati (PDF ģenerēšanai u.c.)

    // ==========================================================
    // OnModelCreating — tabulu nosaukumi, shēma, atslēgas, kolonnas,
    // indeksi un attiecības (EF Core konfigurācija)
    // ==========================================================
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Noklusējuma shēma visām tabulām (ja tabulai nav norādīta cita shēma)
        modelBuilder.HasDefaultSchema("teenpay");

        // StudentSchool tabulai tiek precizēts nosaukums un shēma
        modelBuilder.Entity<StudentSchool>().ToTable("student_schools", "teenpay");

        // ==========================================================
        // USERS TABLE (teenpay.users)
        // Funkcija: lietotāju profili, autentifikācijas dati un bilance
        // ==========================================================
        modelBuilder.Entity<TeenpayUser>(e =>
        {
            // Tabulas nosaukums (shēma tiek ņemta no HasDefaultSchema)
            e.ToTable("users");

            // Primārā atslēga
            e.HasKey(x => x.Id);

            // Username (obligāts, max 64, unikāls)
            e.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(64)
                .IsRequired();

            // Paroles hash (obligāts)
            e.Property(x => x.PasswordHash)
                .HasColumnName("password_hash")
                .IsRequired();

            // Papildu profila lauki (nav obligāti)
            e.Property(x => x.Email)
                .HasColumnName("email")
                .HasMaxLength(128);

            e.Property(x => x.FirstName)
                .HasColumnName("first_name")
                .HasMaxLength(64);

            e.Property(x => x.LastName)
                .HasColumnName("last_name")
                .HasMaxLength(64);

            // Tālrunis (izmanto, piemēram, paroles atjaunošanai)
            e.Property(x => x.PhoneNumber)
                .HasColumnName("phone");

            // Loma (PARENT/CHILD/POS/ADMIN)
            e.Property(x => x.Role)
                .HasColumnName("role");

            // Bilance (naudas atlikums), ar konkrētu numeric tipu DB līmenī
            e.Property(x => x.Balance)
                .HasColumnName("balance")
                .HasColumnType("numeric(12,2)");

            // Personas kods (papildu identifikators, ja nepieciešams)
            e.Property(x => x.PersonalCode)
                .HasColumnName("personal_code");

            // Unikālie indeksi: username un email (lai sistēmā nebūtu dublikāti)
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
        });

        // ==========================================================
        // REFRESH TOKENS (teenpay.refresh_tokens)
        // Funkcija: lietotāja refresh token, ierīces ID un revokācijas statuss
        // ==========================================================
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");

            // Primārā atslēga
            e.HasKey(x => x.Id);

            // Token vērtība (obligāta, unikāla, max 200)
            e.Property(x => x.Token)
                .HasColumnName("token")
                .HasMaxLength(200)
                .IsRequired();

            // Laika lauki un papildinformācija par ierīci
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            e.Property(x => x.DeviceId).HasColumnName("device_id");
            e.Property(x => x.Revoked).HasColumnName("revoked");
            e.Property(x => x.UserId).HasColumnName("user_id");

            // Unikāls indekss tokenam
            e.HasIndex(x => x.Token).IsUnique();

            // Attiecība: RefreshToken pieder lietotājam (1 lietotājs -> daudzi refresh tokeni)
            e.HasOne(x => x.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade); // ja dzēš lietotāju — dzēš arī tokenus
        });

        // ==========================================================
        // SCHOOLS (teenpay.schools)
        // Funkcija: skolu dati + POS lietotāja piesaiste skolai
        // ==========================================================
        modelBuilder.Entity<School>(e =>
        {
            e.ToTable("schools");

            // Primārā atslēga
            e.HasKey(x => x.Id);

            // Kolonnu mapings
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.City).HasColumnName("city");
            e.Property(x => x.Address).HasColumnName("address");
            e.Property(x => x.code).HasColumnName("code");

            // PosUserId — lietotāja ID, kurš ir POS darbinieks šai skolai
            e.Property(x => x.PosUserId).HasColumnName("pos_user_id");
        });

        // ==========================================================
        // TRANSACTIONS (teenpay.transactions)
        // Funkcija: darījumu žurnāls (maksājumi, top-up, ienākumi u.c.)
        // ==========================================================
        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");

            // Primārā atslēga
            e.HasKey(x => x.id);

            // Kolonnu mapings (saglabā “snake_case” DB pusē)
            e.Property(x => x.userid).HasColumnName("user_id");       // kam pieder ieraksts
            e.Property(x => x.amount).HasColumnName("amount");        // summa (+/-)
            e.Property(x => x.kind).HasColumnName("kind");            // tips (PAYMENT/TOPUP/...)
            e.Property(x => x.description).HasColumnName("description");
            e.Property(x => x.createdat).HasColumnName("created_at");

            // Papildu lauki sasaistēm/atskaitēm
            e.Property(x => x.childid).HasColumnName("child_id");     // bērna ID (ja attiecināms)
            e.Property(x => x.schoolid).HasColumnName("school_id");   // skolas ID (ja attiecināms)
        });

        // ==========================================================
        // PARENT_CHILDREN (teenpay.parent_children)
        // Funkcija: vecāku un bērnu sasaistes (kurš vecāks piesaistīts kuram bērnam)
        // ==========================================================
        modelBuilder.Entity<ParentChild>(e =>
        {
            // Tabulas nosaukums + shēma (šeit shēma norādīta explicit)
            e.ToTable("parent_children", "teenpay");

            // Primārā atslēga
            e.HasKey(x => x.Id);

            // Kolonnu mapings
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ParentUserId).HasColumnName("parent_user_id");
            e.Property(x => x.ChildUserId).HasColumnName("child_user_id");

            // Unikāls indekss: vienam vecākam vienu bērnu nevar piesaistīt divreiz
            e.HasIndex(x => new { x.ParentUserId, x.ChildUserId }).IsUnique();
        });

        // ==========================================================
        // TOPUP_REQUESTS (teenpay.topup_requests)
        // Funkcija: bērna pieprasījumi vecākam (statusi PENDING/APPROVED u.c.)
        // ==========================================================
        modelBuilder.Entity<TopUpRequest>(e =>
        {
            e.ToTable("topup_requests", "teenpay");

            // Primārā atslēga
            e.HasKey(x => x.Id);

            // Kolonnu mapings
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChildId).HasColumnName("child_id");
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.RequestedAt).HasColumnName("requested_at");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.Note).HasColumnName("note"); // komentārs/piezīme pie pieprasījuma
        });

        // ==========================================================
        // RECEIPTS (teenpay.receipts)
        // Funkcija: kvītis/čeku dati (numurs, tips, maksātājs/saņēmējs, summa, datums)
        // ==========================================================
        modelBuilder.Entity<Receipt>(e =>
        {
            e.ToTable("receipts", "teenpay");

            // Primārā atslēga
            e.HasKey(x => x.Id);

            // ReceiptNo — čeka numurs (8 cipari), unikāls
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ReceiptNo).HasColumnName("receipt_no").HasMaxLength(8).IsRequired();
            e.HasIndex(x => x.ReceiptNo).IsUnique();

            // Summa un tips
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
            e.Property(x => x.Kind).HasColumnName("kind").IsRequired();

            // Maksātājs un saņēmējs (lietotāju ID)
            e.Property(x => x.PayerUserId).HasColumnName("payer_user_id");
            e.Property(x => x.PayeeUserId).HasColumnName("payee_user_id");

            // Skola (ja čeks ir saistīts ar maksājumu skolai), citādi var būt null
            e.Property(x => x.SchoolId).HasColumnName("school_id");

            // Čeka izveides datums/laiks
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // Izsauc bāzes konfigurāciju (ja DbContext bāzē ir vēl kādi noteikumi)
        base.OnModelCreating(modelBuilder);
    }
}
