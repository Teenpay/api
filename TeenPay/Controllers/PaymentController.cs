using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly AppDbContext _db;
    public PaymentController(AppDbContext db) => _db = db;

    private static string NewReceiptNo()
        => Random.Shared.Next(0, 100_000_000).ToString("D8");

    public sealed class QrPaymentPayloadDto
    {
        public long UserId { get; set; }          // ребёнок
        public string? OrgCode { get; set; }      // школа
        public decimal? Amount { get; set; }      // POS добавляет
    }

    [HttpPost("pay")]
    [Authorize] // ✅ POS должен быть авторизован
    public async Task<IActionResult> PayByQr([FromQuery] string data)
    {
        // кто платит (POS)
        var mePosIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(mePosIdStr, out var mePosId) || mePosId <= 0)
            return Unauthorized(new { error = "unauthorized" });

        QrPaymentPayloadDto? obj;
        try
        {
            obj = JsonSerializer.Deserialize<QrPaymentPayloadDto>(data, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return BadRequest(new { error = "invalid_payload" });
        }

        if (obj == null)
            return BadRequest(new { error = "invalid_payload" });

        if (string.IsNullOrWhiteSpace(obj.OrgCode))
            return BadRequest(new { error = "schoolcode_required" });

        if (obj.Amount == null || obj.Amount <= 0)
            return BadRequest(new { error = "amount_required" });

        if (obj.UserId <= 0)
            return BadRequest(new { error = "child_required" });

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

        // ✅ если сканирует НЕ тот POS — mismatch
        if (school.PosUserId.Value != mePosId)
            return BadRequest(new { error = "school_mismatch" });

        var pos = await _db.Users.SingleOrDefaultAsync(u => u.Id == school.PosUserId.Value);
        if (pos == null)
            return BadRequest(new { error = "pos_user_not_found" });

        var amount = obj.Amount.Value;

        if (child.Balance < amount)
            return BadRequest(new { error = "insufficient_funds" });

        await using var tx = await _db.Database.BeginTransactionAsync();

        child.Balance -= amount;
        pos.Balance += amount;

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

        var receiptNoChild = NewReceiptNo();
        var receiptNoPos = NewReceiptNo();

        // чек ребёнку
        var rChild = new Receipt
        {
            ReceiptNo = receiptNoChild,
            Amount = amount,
            Kind = "PAYMENT",
            PayerUserId = child.Id,
            PayeeUserId = pos.Id,
            SchoolId = school.Id,
            CreatedAt = DateTime.UtcNow
        };

        // чек POS
        var rPos = new Receipt
        {
            ReceiptNo = receiptNoPos,
            Amount = amount,
            Kind = "INCOME",
            PayerUserId = child.Id,
            PayeeUserId = pos.Id,
            SchoolId = school.Id,
            CreatedAt = DateTime.UtcNow
        };


        _db.Receipts.Add(rChild);
        _db.Receipts.Add(rPos);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new
        {
            status = "SUCCEEDED",
            amount,
            childBalanceAfter = child.Balance,
            posBalanceAfter = pos.Balance,
            receiptChild = new { id = rChild.Id, no = rChild.ReceiptNo },
            receiptPos = new { id = rPos.Id, no = rPos.ReceiptNo }
        });
    }
}
