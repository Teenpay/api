using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers;

[ApiController] // Kontrolieris ir REST API (automātiska 400/validācija u.c. Web API uzvedība)
[Route("api/[controller]")] // Maršruts: /api/wallet
public class WalletController : ControllerBase
{
    // EF Core datu bāzes konteksts darbam ar lietotājiem, transakcijām un sasaistēm
    private readonly AppDbContext _db;

    // Konstruktorā injicē DB kontekstu
    public WalletController(AppDbContext db) => _db = db;

    // ==========================================================
    // POST /api/wallet/topup-child
    // Funkcija: vecāks pārskaita naudu bērna makam (TopUp / Pārvedums vecāks -> bērns)
    // ==========================================================
    [Authorize] // Pieejams tikai autorizētam lietotājam (vecākam ar JWT)
    [HttpPost("topup-child")]
    public async Task<IActionResult> TopUpChild([FromBody] TopUpChildDto dto)
    {
        // 1) Validācija: summa nedrīkst būt 0 vai negatīva
        if (dto.Amount <= 0)
            return BadRequest(new { error = "summa_jābūt_pozitīvai" });

        // 2) Nosaka vecāka ID no JWT claimiem
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idStr, out var parentId))
            return Unauthorized();

        // 3) Pārbauda, vai šis vecāks ir piesaistīts konkrētajam bērnam
        // (drošības nolūkiem, lai nevarētu papildināt “svešu” bērnu)
        var linked = await _db.ParentChildren
            .AnyAsync(p => p.ParentUserId == parentId && p.ChildUserId == dto.ChildId);

        if (!linked)
            return BadRequest(new { error = "nav_saistīts_ar_bērnu" });

        // 4) Ielādē vecāka un bērna kontus no DB
        var parent = await _db.Users.SingleAsync(u => u.Id == parentId);
        var child = await _db.Users.SingleAsync(u => u.Id == dto.ChildId);

        // 5) Pārbaude: vecākam pietiek līdzekļu pārskaitījumam
        if (parent.Balance < dto.Amount)
            return BadRequest(new { error = "nepietiekami līdzekļi" });

        // 6) Bilances atjaunošana:
        //    - vecākam samazina atlikumu
        //    - bērnam palielina atlikumu
        parent.Balance -= dto.Amount;
        child.Balance += dto.Amount;

        // 7) Darījuma reģistrēšana transakciju žurnālā (2 ieraksti):
        //    a) vecākam (izejošais, negatīva summa)
        _db.Transactions.Add(new Transaction
        {
            userid = parentId,
            kind = "TOPUP", // Kind vērtība paredzēta no noteiktā saraksta (iekšēja validācija DB līmenī)
            amount = -dto.Amount,
            description = $"Papildināts bērnam #{dto.ChildId}"
        });

        //    b) bērnam (ienākošais, pozitīva summa)
        _db.Transactions.Add(new Transaction
        {
            userid = dto.ChildId,
            kind = "TOPUP",
            amount = dto.Amount,
            description = $"Papildināt no vecākiem #{parentId}"
        });

        // 8) Saglabā visas izmaiņas DB (bilances + transakcijas)
        await _db.SaveChangesAsync();

        // 9) Atgriež rezultātu ar jauniem bilances atlikumiem (klienta UI atjaunināšanai)
        return Ok(new { ok = true, parentBalance = parent.Balance, childBalance = child.Balance });
    }
}
