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
    [ApiController] // Kontrolieris tiek izmantots kā REST API (automātiska modeļu validācija, 400 u.c.)
    public class UsersController : ControllerBase
    {
        // PostgreSQL savienojums (izmantošanai ar procedūrām/komandām caur Npgsql)
        private readonly NpgsqlDataSource _ds;

        // EF Core DB konteksts (lietotāju, bilances, profila datu darbībām)
        private readonly AppDbContext _db;

        // Konfigurācija (piemēram, JWT iestatījumi u.c.)
        private readonly IConfiguration _cfg;

        // Kontroliera konstruktorā tiek injicēti nepieciešamie servisi (DB, konfigurācija u.c.)
        public UsersController(NpgsqlDataSource ds, AppDbContext db, IConfiguration cfg)
        {
            _db = db; _cfg = cfg;
        }

        // ==========================================================
        // GET /api/user/getuser?id=...
        // Funkcija: vienkāršs testa piemērs, kas atgriež ievadīto id un “demo” bilanci
        // ==========================================================
        [HttpGet]
        [Route("getuser")]
        public IActionResult GetUsers(int? id)
        {
            // Ja nav padots id — atgriež ziņojumu (nav kļūda, bet informatīvs teksts)
            if (id == null)
            {
                return Ok("Ievadiet maksājuma identifikācijas numuru");
            }
            else
            {
                // Demo vērtība (fiksēta bilance)
                float balance = (float)3.5;
                return Ok($"{id} {balance}");
            }
        }

        // ==========================================================
        // POST /api/user/save
        // Funkcija: demo saglabāšanas piemērs — pārbauda obligātos laukus un atgriež “id”
        // ==========================================================
        [HttpPost]
        [Route("save")]
        public IActionResult SaveUser([FromBody] TeenpayUserT user)
        {
            // Minimāla validācija: pārbauda, vai nav tukši obligātie lauki
            if (user.Name == null || user.Surname == null || user.Age == null || user.Child == null)
            {
                return BadRequest("Ievadiet informāciju!");
            }

            // Demo atbilde: izveido objektu ar fiksētu ID un atgriež JSON
            TeenpayID id = new TeenpayID();
            id.ID = 123;

            return Ok(JsonSerializer.Serialize(id));
        }

        // ==========================================================
        // GET /api/user/person/{id}
        // Funkcija: izsauc PostgreSQL procedūru teenpay.get_person ar JSON parametriem
        // un atgriež rezultātu kā JSON (Content-Type: application/json)
        // ==========================================================
        [HttpGet("person/{id:int}")]
        public async Task<IActionResult> GetPerson(int id, CancellationToken ct)
        {
            // Iepako ID objektā, ko serializē kā JSON procedūras parametram
            TeenpayID Tid = new TeenpayID();
            Tid.ID = id;

            // Procedūras izsaukums (CALL ...)
            const string sql = "CALL teenpay.get_person($1, $2)";

            // Izveido komandu no DataSource (Npgsql)
            await using var cmd = _ds.CreateCommand(sql);

            // 1. parametrs: JSON ar ievades datiem (ID)
            cmd.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Json,
                Value = (object)(JsonSerializer.Serialize(Tid) ?? "{}")
            });

            // 2. parametrs: JSON “out” vietturis (šajā shēmā tiek padots "{}")
            cmd.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Json,
                Value = (object)("{}")
            });

            try
            {
                // Izpilda procedūru un lasa pirmo rezultāta rindu
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) return Problem("Nav rezultātu rindas.");

                // Saņem procedūras atgriezto JSON no 0. kolonnas
                string poDataJson = reader.IsDBNull(0) ? "{}" : reader.GetString(0);

                // Atgriež kā JSON saturu (lai klients to var normāli parsēt)
                return Content(poDataJson, "application/json");
            }
            catch (Exception ex)
            {
                // Ja procedūras izsaukums neizdodas, atgriež Problem ar kļūdas tekstu
                return Problem($"Procedūras izsaukums neizdevās: {ex.Message}");
            }
        }

        // ==========================================================
        // GET /api/user/me
        // Funkcija: atgriež pašreizējā autorizētā lietotāja profilu (UserDto)
        // ==========================================================
        [Authorize] // Pieejams tikai autorizētam lietotājam (ar derīgu JWT)
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            // Paņem lietotāja ID no JWT claimiem
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Pārbauda, vai ID ir korekts int (lai izvairītos no int/Guid kļūdām)
            if (!int.TryParse(idStr, out var userId)) return Unauthorized();

            // Atrod lietotāju DB pēc ID
            var u = await _db.Set<TeenpayUser>().FirstOrDefaultAsync(x => x.Id == userId);
            if (u is null) return NotFound();

            // Atgriež tikai nepieciešamos profila laukus (DTO)
            return Ok(new UserDto(u.Id, u.Username, u.Email, u.FirstName, u.LastName));
        }

        // ==========================================================
        // GET /api/user/me/balance
        // Funkcija: atgriež pašreizējā autorizētā lietotāja bilanci
        // ==========================================================
        [HttpGet("me/balance")]
        [Authorize]
        public async Task<IActionResult> GetMyBalance()
        {
            // Nosaka lietotāja ID no JWT claimiem
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out var userId)) return Unauthorized();

            // Atlasa tikai Balance lauku (efektīvāk nekā vilkt visu lietotāju)
            var balance = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Balance)
                .SingleAsync();

            // Atgriež bilanci kā JSON objektu
            return Ok(new { balance });
        }

        // ==========================================================
        // PUT /api/user/me/email
        // Funkcija: autorizēts lietotājs var atjaunināt savu e-pastu
        // ==========================================================
        [Authorize]
        [HttpPut("me/email")]
        public async Task<IActionResult> UpdateMyEmail([FromBody] UpdateEmailDto dto)
        {
            // Vienkārša validācija: e-pastam jābūt un jāiekļauj "@"
            if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains("@"))
                return BadRequest("Nepareizs e-pasts.");

            // Nosaka lietotāja ID no JWT claimiem
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out var userId))
                return Unauthorized();

            // Atrod lietotāju DB
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("Lietotājs nav atrasts.");

            // Saglabā jauno e-pastu (ar trim)
            user.Email = dto.Email.Trim();
            await _db.SaveChangesAsync();

            // Atgriež apstiprinājuma ziņojumu
            return Ok(new { message = "E-pasts atjaunināts" });
        }

        // ==========================================================
        // PUT /api/user/me/password
        // Funkcija: autorizēts lietotājs var nomainīt paroli,
        // pārbaudot veco paroli un saglabājot jauno (hash veidā)
        // ==========================================================
        [Authorize]
        [HttpPut("me/password")]
        public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordDto dto)
        {
            // Validācija: obligāti jābūt gan vecajai, gan jaunajai parolei
            if (string.IsNullOrWhiteSpace(dto.OldPassword) ||
                string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest("Paroles lauki ir obligāti.");

            // Validācija: minimālais garums (šajā loģikā 6 simboli)
            if (dto.NewPassword.Length < 6)
                return BadRequest("Parolei jābūt vismaz 6 rakstzīmēm garai.");

            // Nosaka lietotāja ID no JWT claimiem
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out var userId))
                return Unauthorized();

            // Atrod lietotāju DB
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("Lietotājs nav atrasts.");

            // Pārbauda veco paroli, izmantojot PasswordHasher
            var hasher = new PasswordHasher<TeenpayUser>();
            var check = hasher.VerifyHashedPassword(user, user.PasswordHash, dto.OldPassword);
            if (check == PasswordVerificationResult.Failed)
                return BadRequest("Pašreizējais parolis ir nepareizs.");

            // Ja vecā parole pareiza — hashē un saglabā jauno paroli
            user.PasswordHash = hasher.HashPassword(user, dto.NewPassword);
            await _db.SaveChangesAsync();

            // Atgriež apstiprinājuma ziņojumu
            return Ok(new { message = "Parole atjaunināta" });
        }

        // ==========================================================
        // DTO: e-pasta maiņai
        // ==========================================================
        public sealed class UpdateEmailDto
        {
            public string Email { get; set; } = "";
        }

        // ==========================================================
        // DTO: paroles maiņai
        // ==========================================================
        public sealed class ChangePasswordDto
        {
            public string OldPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
        }
    }
}
