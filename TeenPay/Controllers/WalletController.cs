using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WalletController : ControllerBase
{
    private readonly AppDbContext _db;

    public WalletController(AppDbContext db) => _db = db;

    [Authorize] // важно: НЕ AllowAnonymous
    [HttpPost("topup-child")]
    public async Task<IActionResult> TopUpChild([FromBody] TopUpChildDto dto)
    {
        if (dto.Amount <= 0) return BadRequest(new { error = "amount_must_be_positive" });

        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idStr, out var parentId)) return Unauthorized();

        var linked = await _db.ParentChildren
            .AnyAsync(p => p.ParentUserId == parentId && p.ChildUserId == dto.ChildId);

        if (!linked) return BadRequest(new { error = "not_linked_to_child" });

        var parent = await _db.Users.SingleAsync(u => u.Id == parentId);
        var child = await _db.Users.SingleAsync(u => u.Id == dto.ChildId);

        if (parent.Balance < dto.Amount) return BadRequest(new { error = "insufficient_funds" });

        // 1) балансы
        parent.Balance -= dto.Amount;
        child.Balance += dto.Amount;

        // 2) транзакции (Kind строго из списка CHECK)
        _db.Transactions.Add(new Transaction
        {
            userid = parentId,
            kind = "TOPUP",
            amount = -dto.Amount,
            description = $"Top up child #{dto.ChildId}"
        });

        _db.Transactions.Add(new Transaction
        {
            userid = dto.ChildId,
            kind = "TOPUP",
            amount = dto.Amount,
            description = $"Top up from parent #{parentId}"
        });

        await _db.SaveChangesAsync();
        return Ok(new { ok = true, parentBalance = parent.Balance, childBalance = child.Balance });
    }
}
