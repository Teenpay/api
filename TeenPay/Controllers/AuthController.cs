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

namespace TeenPay.Controllers
{
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

        public record RegisterDto(string Username, string Password);
        public record LoginDto(string Username, string Password, string? DeviceId);
        public record RefreshDto(string RefreshToken, string? DeviceId);

        // --- публичные (без логина) ---
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Username and password are required.");

            if (await _db.Set<TeenpayUser>().AnyAsync(u => u.Username == dto.Username))
                return Conflict("Username already taken.");

            var user = new TeenpayUser { Username = dto.Username };
            user.PasswordHash = _hasher.HashPassword(user, dto.Password);

            _db.Add(user);
            await _db.SaveChangesAsync();

            return Ok(new { user.Id, user.Username });
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
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
            return Ok(new { accessToken = access, refreshToken = refresh, expiresIn = (int)expires.TotalSeconds });
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

            // Ротация refresh-токена
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

        // --- только для залогиненных ---
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
    }
}

