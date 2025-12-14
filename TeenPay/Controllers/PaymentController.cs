using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly AppDbContext _db;
    public PaymentController(AppDbContext db) => _db = db;

    // POST /api/Payment/pay?pk=240509-21368&schoolcode=RIGA_25&summ=3.50
    [HttpPost("pay")]
    [AllowAnonymous]
    public async Task<IActionResult> PayByQr(
        [FromQuery] string pk,
        [FromQuery] string schoolcode,
        [FromQuery] decimal summ)
    {
        if (string.IsNullOrWhiteSpace(pk))
            return BadRequest(new { error = "pk_required" });

        if (string.IsNullOrWhiteSpace(schoolcode))
            return BadRequest(new { error = "schoolcode_required" });

        if (summ <= 0)
            return BadRequest(new { error = "bad_amount" });

        // 1) ищем пользователя по personal_code
        var child = await _db.Users.SingleOrDefaultAsync(u => u.PersonalCode == pk);
        if (child == null)
            return NotFound(new { error = "person_not_found" });

        // 2) ищем школу по code (ВАЖНО: колонка "code")
        var school = await _db.Schools.SingleOrDefaultAsync(s => s.code == schoolcode);
        if (school == null)
            return NotFound(new { error = "school_not_found" });

        // 3) проверяем привязку user<->school
        var linked = await _db.StudentSchools
            .AnyAsync(ss => ss.UserId == child.Id && ss.SchoolId == school.Id);

        if (!linked)
            return BadRequest(new { error = "not_linked_to_school" });

        // 4) проверяем баланс
        if (child.Balance < summ)
            return BadRequest(new { error = "insufficient_funds" });

        await using var tx = await _db.Database.BeginTransactionAsync();

        // 5) списываем
        child.Balance -= summ;

        // 6) создаём транзакцию (amount минусом)
        var tr = new Transaction
        {
            userid = child.Id,
            childid = child.Id,
            schoolid = school.Id,
            amount = -summ,
            kind = "PAYMENT",
            description = $"Payment to {school.Name} ({school.code})",
            createdat = DateTime.UtcNow
        };

        _db.Transactions.Add(tr);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new
        {
            status = "SUCCEEDED",
            pk,
            schoolcode,
            summ,
            balanceAfter = child.Balance,
            transactionId = tr.id
        });
    }
}
