namespace TeenPay.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Таблица users в схеме teenpay
[Table("users", Schema = "teenpay")]
    [Index(nameof(Username), IsUnique = true)]

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
        [Column("phone_number")]
        public string? PhoneNumber { get; set; }  
    
        [Required]
        [Column("role")]
        public string? Role { get; set; }          // "Child" vai "Parent"

    [InverseProperty(nameof(RefreshToken.User))]
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }

    // Таблица refresh_tokens
    [Table("refresh_tokens", Schema = "teenpay")]
    [Index(nameof(Token), IsUnique = true)]
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

        // timestamptz
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