using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TeenPay.Models
{
    public class TeenpayUserT
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Surname { get; set; }
        public int? Age { get; set; }
        public string? Child { get; set; }
        public float? Balance { get; set; }
    }

    public class TeenpayID
    {
        public int? ID { get; set; }
    }

    // DTO для auth (как у тебя было)
    public record RegisterDto(string Username, string Password, string? Email, string? FirstName, string? LastName);
    public record LoginDto(string Username, string Password, string? DeviceId);
    public record RefreshDto(string RefreshToken, string? DeviceId);
    public record UserDto(int Id, string Username, string? Email, string? FirstName, string? LastName);
    public record ForgotPasswordDto(string Username, string Role, string Phone);
    public record DevSetPasswordDto(string Username, string NewPassword);

    // DTO для платежа — ОБРАТИ ВНИМАНИЕ: тут уже int, не string
    public record CreatePaymentDto(
        int ChildUserId,
        int MerchantUserId,
        decimal Amount
    );

    public record RejectDto(string Reason);
}
