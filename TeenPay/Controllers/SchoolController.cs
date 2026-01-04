// SchoolsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeenPay.Data;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Visi šī kontroliera endpointi pieejami tikai autorizētam lietotājam
public class SchoolsController : ControllerBase
{
    // DB konteksts skolu un lietotāju–skolu saistību izgūšanai
    private readonly AppDbContext _db;
    public SchoolsController(AppDbContext db) => _db = db;

    // ==========================================================
    // Palīgmetode: atgriež pašreizējā lietotāja ID no JWT claimiem
    // ==========================================================
    private int MeId()
    {
        // Lietotāja ID var būt dažādos claimos atkarībā no tokena konfigurācijas
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("id");

        // Pārveido uz int (ID tiek glabāts kā skaitlis)
        return int.Parse(raw!);
    }

    // ==========================================================
    // GET /api/schools/my
    // Funkcija: bērnam atgriež skolu sarakstu, ar kurām viņš ir piesaistīts
    // ==========================================================
    [HttpGet("my")]
    public async Task<IActionResult> MySchools()
    {
        var meId = MeId();

        // Meklē StudentSchools saites pēc lietotāja ID un pievieno (JOIN) skolu datus
        // Rezultātā atgriež tikai nepieciešamos laukus: id, name, code
        var list = await _db.StudentSchools
            .Where(ss => ss.UserId == meId)
            .Join(_db.Schools, ss => ss.SchoolId, s => s.Id, (ss, s) => new
            {
                id = s.Id,
                name = s.Name,
                code = s.code
            })
            .OrderBy(x => x.name)
            .ToListAsync();

        // Atgriež skolām piesaistīto sarakstu (tukšs saraksts, ja nav piesaistes)
        return Ok(list);
    }

    // ==========================================================
    // GET /api/schools/pos-school
    // Funkcija: POS darbiniekam atgriež skolu, kas piesaistīta šim POS lietotājam
    // ==========================================================
    [HttpGet("pos-school")]
    public async Task<IActionResult> PosSchool()
    {
        var meId = MeId();

        // Atrod skolu, kurai PosUserId sakrīt ar pašreizējo lietotāju (POS)
        var s = await _db.Schools
            .Where(x => x.PosUserId == meId)
            .Select(x => new { id = x.Id, name = x.Name, code = x.code })
            .FirstOrDefaultAsync();

        // Ja POS nav piesaistīts nevienai skolai, atgriež kļūdu
        if (s == null) return NotFound(new { error = "pārdevējs_nav_saistīts_ar_skolu" });

        // Atgriež POS skolai atbilstošo ierakstu
        return Ok(s);
    }
}
