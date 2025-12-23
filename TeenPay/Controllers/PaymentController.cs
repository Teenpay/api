using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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

    private static string NewReceiptNo()
        => Random.Shared.Next(0, 100_000_000).ToString("D8");

    [HttpPost("pay")]
    [AllowAnonymous]
    public async Task<IActionResult> PayByQr([FromQuery] string data)
    {
        var obj = JsonSerializer.Deserialize<QrPaymentPayload>(data);

        if (obj == null)
            return BadRequest(new { error = "invalid_payload" });

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

        var linked = await _db.StudentSchools
            .AnyAsync(ss => ss.UserId == child.Id && ss.SchoolId == school.Id);
        if (!linked)
            return BadRequest(new { error = "not_linked_to_school" });

        if (school.PosUserId == null)
            return BadRequest(new { error = "school_has_no_pos" });

        var pos = await _db.Users.SingleOrDefaultAsync(u => u.Id == school.PosUserId.Value);
        if (pos == null)
            return BadRequest(new { error = "pos_user_not_found" });

        var amount = (decimal)obj.Amount;

        if (child.Balance < amount)
            return BadRequest(new { error = "insufficient_funds" });

        await using var tx = await _db.Database.BeginTransactionAsync();

        // 1) перевод баланса
        child.Balance -= amount;
        pos.Balance += amount;

        // 2) транзакции
        _db.Transactions.Add(new Transaction
        {
            userid = child.Id,
            childid = child.Id,
            schoolid = school.Id,
            amount = -amount,
            kind = "PAYMENT",
            description = $"Payment to {school.Name} ({school.code})",
            createdat = DateTime.UtcNow
        });

        _db.Transactions.Add(new Transaction
        {
            userid = pos.Id,
            childid = child.Id,
            schoolid = school.Id,
            amount = amount,
            kind = "TOPUP",
            description = $"Income from {child.Username} ({school.code})",
            createdat = DateTime.UtcNow
        });

        // 3) чеки (2 штуки)
        var receiptNo = NewReceiptNo();

        var rChild = new Receipt
        {
            ReceiptNo = receiptNo,       // можно общий номер
            Amount = amount,
            Kind = "PAYMENT",
            PayerUserId = child.Id,
            PayeeUserId = pos.Id,
            SchoolId = school.Id,
            CreatedAt = DateTime.UtcNow
        };

        _db.Receipts.Add(rChild);

        // 4) сохраняем, чтобы появились Id
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new
        {
            status = "SUCCEEDED",
            amount,
            childBalanceAfter = child.Balance,
            posBalanceAfter = pos.Balance,

            receiptChild = new { id = rChild.Id, no = rChild.ReceiptNo },
        });
    }
}
