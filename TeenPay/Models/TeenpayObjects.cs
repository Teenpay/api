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

    public record RegisterDto(string Username, string Password, string? Email, string? FirstName, string? LastName);
    public record LoginDto(string Username, string Password, string? DeviceId);
    public record RefreshDto(string RefreshToken, string? DeviceId);
    public record UserDto(int Id, string Username, string? Email, string? FirstName, string? LastName);
    public record ForgotPasswordDto(string Username, string Role, string Phone);
    public record DevSetPasswordDto(string Username, string NewPassword);

}
