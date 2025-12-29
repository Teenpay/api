// SchoolsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeenPay.Data;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SchoolsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SchoolsController(AppDbContext db) => _db = db;

    private int MeId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("id");
        return int.Parse(raw!);
    }

    // ✅ РЕБЁНОК: школы, к которым он привязан
    [HttpGet("my")]
    public async Task<IActionResult> MySchools()
    {
        var meId = MeId();

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

        return Ok(list);
    }

    // ✅ POS: какая школа привязана к этому работнику
    [HttpGet("pos-school")]
    public async Task<IActionResult> PosSchool()
    {
        var meId = MeId();

        var s = await _db.Schools
            .Where(x => x.PosUserId == meId)
            .Select(x => new { id = x.Id, name = x.Name, code = x.code })
            .FirstOrDefaultAsync();

        if (s == null) return NotFound(new { error = "pos_not_linked_to_school" });
        return Ok(s);
    }
}
