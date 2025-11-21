using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TeenPay.Data;
using TeenPay.Models;
using System.Diagnostics;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly PasswordHasher<TeenpayUser> _hasher = new();

    public AuthController(AppDbContext db, IConfiguration cfg)
    {
        _db = db; _cfg = cfg;
    }

    // DTO
    public record RegisterDto(string Username, string Password, string? Email, string? FirstName, string? LastName);
    public record LoginDto(string Username, string Password, string? DeviceId);
    public record RefreshDto(string RefreshToken, string? DeviceId);
    public record UserDto(int Id, string Username, string? Email, string? FirstName, string? LastName);
    public record ForgotPasswordDto(string Username, string Role, string Phone);
    public record DevSetPasswordDto(string Username, string NewPassword);

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Username and password are required.");

        if (await _db.Set<TeenpayUser>().AnyAsync(u => u.Username == dto.Username))
            return Conflict("Username already taken.");

        var user = new TeenpayUser
        {
            Username = dto.Username,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName
        };
        user.PasswordHash = _hasher.HashPassword(user, dto.Password);

        _db.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new { user.Id, user.Username });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        //brake point nada
        var user = await _db.Set<TeenpayUser>()
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Username == dto.Username);

        if (user is null) return Unauthorized("Invalid credentials.");

        var vr = _hasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        if (vr == PasswordVerificationResult.Failed) return Unauthorized("Invalid credentials.");

        var (access, expires) = GenerateAccessToken(user);
        var refresh = GenerateRefreshToken();

        user.RefreshTokens.Add(new RefreshToken
        {
            Token = refresh,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(14),
            DeviceId = dto.DeviceId
        });

        await _db.SaveChangesAsync();

        // вернём профиль в ответе
        return Ok(new
        {
            accessToken = access,
            refreshToken = refresh,
            expiresIn = (int)expires.TotalSeconds,
            user = new { user.Id, user.Username, user.Email, user.FirstName, user.LastName }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idStr, out var userId)) return Unauthorized(); // ← int, чтобы не было Guid-int ошибки

        var u = await _db.Set<TeenpayUser>().FirstOrDefaultAsync(x => x.Id == userId);
        if (u is null) return NotFound();

        return Ok(new UserDto(u.Id, u.Username, u.Email, u.FirstName, u.LastName));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshDto dto)
    {
        var rt = await _db.Set<RefreshToken>()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == dto.RefreshToken && !t.Revoked);

        if (rt is null || rt.ExpiresAtUtc < DateTime.UtcNow)
            return Unauthorized("Invalid refresh token.");

        if (!string.IsNullOrEmpty(dto.DeviceId) && rt.DeviceId != dto.DeviceId)
            return Unauthorized("Device mismatch.");

        var (access, expires) = GenerateAccessToken(rt.User);

        // refresh-token rotation
        rt.Revoked = true;
        var newRt = new RefreshToken
        {
            Token = GenerateRefreshToken(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(14),
            DeviceId = rt.DeviceId,
            UserId = rt.UserId
        };
        _db.Add(newRt);
        await _db.SaveChangesAsync();

        return Ok(new { accessToken = access, refreshToken = newRt.Token, expiresIn = (int)expires.TotalSeconds });
    }

    // --- For login-users ---
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshDto dto)
    {
        var rt = await _db.Set<RefreshToken>().FirstOrDefaultAsync(t => t.Token == dto.RefreshToken);
        if (rt != null) { rt.Revoked = true; await _db.SaveChangesAsync(); }
        return Ok();
    }

    [Authorize]
    [HttpGet("check")]
    public IActionResult Check()
    {
        var p = HttpContext.User;
        return Ok(new { authenticated = p.Identity?.IsAuthenticated == true });
    }

    // --- helpers ---
    private (string token, TimeSpan expires) GenerateAccessToken(TeenpayUser user)
    {
        var jwt = _cfg.GetSection("Jwt");
        var claims = new[]
        {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
            };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expires = TimeSpan.FromMinutes(60); // 1 час
        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(expires),
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncoder.Encode(bytes);
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) ||
            string.IsNullOrWhiteSpace(dto.Role) ||
            string.IsNullOrWhiteSpace(dto.Phone))
            return BadRequest("Nepilnīgi dati.");

        var user = await _db.Set<TeenpayUser>()
            .FirstOrDefaultAsync(u => u.Username == dto.Username);

        // роль сравниваем гибко (EN + LV)
        static bool RoleMatches(string? dbRole, string requested)
        {
            var req = requested.Trim().ToLowerInvariant();
            return dbRole?.ToLowerInvariant() switch
            {
                "parent" => req is "parent" or "vecāks" or "vecaks",
                "child" => req is "child" or "bērns" or "berns",
                _ => false
            };
        }

        if (user is null || !RoleMatches(user.Role, dto.Role))
            return BadRequest("Dati nesakrīt.");

        // телефон
        string Normalize(string s) => new string(s.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(user.PhoneNumber) ||
            Normalize(user.PhoneNumber) != Normalize(dto.Phone))
            return BadRequest("Numurs nav reģistrēts sistēmā");

        // генерим временный пароль, хэшируем и сохраняем (как было)
        var temp = GenerateReadablePassword(10);
        var hasher = new PasswordHasher<TeenpayUser>();
        user.PasswordHash = hasher.HashPassword(user, temp);
        await _db.SaveChangesAsync();

        // TODO: отправка SMS с паролем
        return Ok(new { message = "Parole tiks nosūtīta uz norādīto numuru." });
    }
    // Pagaidu parole (bez 0/O/I/l)
    private static string GenerateReadablePassword(int len)
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[len];
        rng.GetBytes(bytes);
        var sb = new System.Text.StringBuilder(len);
        foreach (var b in bytes)
            sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    [AllowAnonymous]
    [HttpPost("dev-set-password")]
    public async Task<IActionResult> DevSetPassword([FromBody] DevSetPasswordDto dto)
    {
        var u = await _db.Set<TeenpayUser>().FirstOrDefaultAsync(x => x.Username == dto.Username.Trim());
        if (u == null) return NotFound("User not found");
        u.PasswordHash = new PasswordHasher<TeenpayUser>().HashPassword(u, dto.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Password updated" });
    }  

}

