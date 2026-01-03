using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.Eventing.Reader;
using System.Security.Cryptography;
using System.Text.Json;
using TeenPay.Models;
using Npgsql;
using NpgsqlTypes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using System.Text;
using TeenPay.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;

namespace TeenPay.Controllers
{
   [Route("api/user")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly NpgsqlDataSource _ds;
        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;
        public UsersController(NpgsqlDataSource ds, AppDbContext db, IConfiguration cfg)
        {
            _db = db; _cfg = cfg;
        }

            [HttpGet]
        [Route("getuser")]
        public IActionResult GetUsers(int? id)
        {
            if (id == null)
            {
                return Ok("Enter payment id");
            }
            else
            {
                float balance = (float)3.5;
                return Ok($"{id} {balance}");
            }
        }
        [HttpPost]
        [Route("save")]
        public IActionResult SaveUser([FromBody] TeenpayUserT user)
        {
            if (user.Name == null || user.Surname == null || user.Age == null || user.Child == null)
            {
                return BadRequest("Enter information!");
            }
            TeenpayID id = new TeenpayID();
            id.ID = 123;
            return Ok(JsonSerializer.Serialize(id));
        }

        [HttpGet("person/{id:int}")]
        public async Task<IActionResult> GetPerson(int id, CancellationToken ct)
        {
            TeenpayID Tid = new TeenpayID();
            Tid.ID = id;

            const string sql = "CALL teenpay.get_person($1, $2)";

            await using var cmd = _ds.CreateCommand(sql);

            cmd.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Json,
                Value = (object)(JsonSerializer.Serialize(Tid) ?? "{}")
            });

            cmd.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Json,
                Value = (object)("{}")
            });

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) return Problem("No result row.");

                string poDataJson = reader.IsDBNull(0) ? "{}" : reader.GetString(0);
                return Content(poDataJson, "application/json");
            }
            catch (Exception ex)
            {
                return Problem($"Procedure call failed: {ex.Message}");
            }
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
            [HttpGet("me/balance")]
            [Authorize]
        public async Task<IActionResult> GetMyBalance()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out var userId)) return Unauthorized();

            var balance = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Balance)
                .SingleAsync();

            return Ok(new { balance });
        }
        // ================== UPDATE EMAIL ==================
        [Authorize]
        [HttpPut("me/email")]
        public async Task<IActionResult> UpdateMyEmail([FromBody] UpdateEmailDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains("@"))
                return BadRequest("Invalid email.");

            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out var userId))
                return Unauthorized();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found.");

            user.Email = dto.Email.Trim();
            await _db.SaveChangesAsync();

            return Ok(new { message = "Email updated" });
        }

        // ================== CHANGE PASSWORD ==================
        [Authorize]
        [HttpPut("me/password")]
        public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.OldPassword) ||
                string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest("Password fields are required.");

            if (dto.NewPassword.Length < 6)
                return BadRequest("Password must be at least 6 characters.");

            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out var userId))
                return Unauthorized();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found.");

            var hasher = new PasswordHasher<TeenpayUser>();
            var check = hasher.VerifyHashedPassword(user, user.PasswordHash, dto.OldPassword);
            if (check == PasswordVerificationResult.Failed)
                return BadRequest("Current password is incorrect.");

            user.PasswordHash = hasher.HashPassword(user, dto.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password updated" });
        }
        public sealed class UpdateEmailDto
        {
            public string Email { get; set; } = "";
        }

        public sealed class ChangePasswordDto
        {
            public string OldPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
        }


    }

}
