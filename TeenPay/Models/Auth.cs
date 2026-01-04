namespace TeenPay.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// ==========================================================
// MODEĻI (Entities) — datu bāzes tabulu atspoguļojums C# klasēs
// Šeit ir 2 tabulas: users un refresh_tokens (shēma: teenpay)
// ==========================================================


// ==========================================================
// TeenpayUser — tabula "teenpay.users"
// Funkcija: glabā lietotāja profilu, autentifikācijas datus un bilanci
// ==========================================================
[Table("users", Schema = "teenpay")]                     // Norāda tabulas nosaukumu un shēmu DB pusē
[Index(nameof(Username), IsUnique = true)]               // Unikāls indekss: username nedrīkst atkārtoties
public class TeenpayUser
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("username")]
    public string Username { get; set; } = default!;

    [Required]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = default!;

    // E-pasts (projektā tiek izmantots profilam/komunikācijai)
    // (Šeit ir [Required], bet tips ir nullable — tas nozīmē, ka kodā to var atstāt null,
    //  bet validācijas līmenī tas ir paredzēts kā obligāts)
    [Required]
    [Column("email")]
    public string? Email { get; set; }

    [Required]
    [Column("first_name")]
    public string? FirstName { get; set; }

    [Required]
    [Column("last_name")]
    public string? LastName { get; set; }

    [Required]
    [Column("phone")]
    public string? PhoneNumber { get; set; }

    [Column("personal_code")]
    public string? PersonalCode { get; set; }

    [Required]
    [Column("role")]
    public string? Role { get; set; }         

    [Required]
    [Column("balance")]
    public decimal Balance { get; set; }

    [InverseProperty(nameof(RefreshToken.User))]
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}


// ==========================================================
// RefreshToken — tabula "teenpay.refresh_tokens"
// Funkcija: saglabā refresh tokenus sesijas atjaunošanai (login/refresh flow)
// ==========================================================
[Table("refresh_tokens", Schema = "teenpay")]            // Tabulas nosaukums un shēma
[Index(nameof(Token), IsUnique = true)]                  // Unikāls indekss: viens token nedrīkst dublēties
public class RefreshToken
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    [Column("token")]
    public string Token { get; set; } = default!;

    [Required]
    [Column("expires_at_utc", TypeName = "timestamptz")]
    public DateTime ExpiresAtUtc { get; set; }

    [Required]
    [Column("created_at_utc", TypeName = "timestamptz")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("device_id")]
    public string? DeviceId { get; set; }

    [Required]
    [Column("revoked")]
    public bool Revoked { get; set; }

    [Required]
    [ForeignKey(nameof(User))]
    [Column("user_id")]
    public int UserId { get; set; }

    [InverseProperty(nameof(TeenpayUser.RefreshTokens))]
    public TeenpayUser User { get; set; } = default!;
}
