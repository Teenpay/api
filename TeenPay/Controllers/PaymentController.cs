using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using System.Text;
using TeenPay.Data;
using TeenPay.Models;
using TeenPay_App.Models;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly AppDbContext _db;
    public PaymentController(AppDbContext db) => _db = db;

    [HttpPost("pay")]
    [AllowAnonymous]
    public async Task<IActionResult> PayByQr(
      [FromQuery]  string data)
    {
        QrPaymentPayload obj = JsonSerializer.Deserialize<QrPaymentPayload>(data);

        if (string.IsNullOrWhiteSpace(obj.OrgCode))
            return BadRequest(new { error = "schoolcode_required" });

        if (obj.Amount == null || obj.Amount <= 0)
            return BadRequest(new { error = "amount_required" });

        var child = await _db.Users.SingleOrDefaultAsync(u => u.Id == obj.UserId);
        if (child == null)
            return NotFound(new { error = "person_not_found" });

        var school = await _db.Schools.SingleOrDefaultAsync(s => s.code == obj.OrgCode);
        if (school == null)
            return NotFound(new { error = "school_not_found" });

        // ребёнок должен быть привязан к школе
        var linked = await _db.StudentSchools
            .AnyAsync(ss => ss.UserId == child.Id && ss.SchoolId == school.Id);
        if (!linked)
            return BadRequest(new { error = "not_linked_to_school" });

        // у школы должен быть POS
        if (school.PosUserId == null)
            return BadRequest(new { error = "school_has_no_pos" });

        var pos = await _db.Users.SingleOrDefaultAsync(u => u.Id == school.PosUserId.Value);
        if (pos == null)
            return BadRequest(new { error = "pos_user_not_found" });

        if (child.Balance < obj.Amount)
            return BadRequest(new { error = "insufficient_funds" });

        await using var tx = await _db.Database.BeginTransactionAsync();

        // перевод
        child.Balance -= (decimal)obj.Amount;
        pos.Balance += (decimal)obj.Amount;

        // транзакция ребёнка (минус)
        _db.Transactions.Add(new Transaction
        {
            userid = child.Id,
            childid = child.Id,
            schoolid = school.Id,
            amount = (decimal)obj.Amount,
            kind = "PAYMENT",
            description = $"Payment to {school.Name} ({school.code})",
            createdat = DateTime.UtcNow
        });

        // транзакция POS (плюс)
        _db.Transactions.Add(new Transaction
        {
            userid = pos.Id,
            childid = child.Id,       // кто заплатил
            schoolid = school.Id,
            amount = (decimal)obj.Amount,
            kind = "PAYMENT",
            description = $"Income from {child.Username} ({school.code})",
            createdat = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new
        {
            status = "SUCCEEDED",
            child.PersonalCode,
            obj.OrgCode,
            obj.Amount,
            childBalanceAfter = child.Balance,
            posBalanceAfter = pos.Balance
        });
    }
}
