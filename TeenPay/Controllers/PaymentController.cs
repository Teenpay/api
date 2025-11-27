using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // чтобы Swagger не требовал токен
    public class PaymentController : ControllerBase
    {
        private readonly AppDbContext _db;

        public PaymentController(AppDbContext db)
        {
            _db = db;
        }

        // POST: /api/Payment/pay
        [HttpPost("pay")]
        public async Task<IActionResult> Pay([FromBody] CreatePaymentDto dto)
        {
            if (dto.Amount <= 0)
                return BadRequest(new { error = "bad_amount" });

            // ищем ребёнка по Id
            var child = await _db.Users
                .SingleOrDefaultAsync(u => u.Id == dto.ChildUserId);

            if (child == null)
                return NotFound(new { error = "child_not_found" });

            // ищем продавца/столовую по Id
            var merchant = await _db.Users
                .SingleOrDefaultAsync(u => u.Id == dto.MerchantUserId);

            if (merchant == null)
                return NotFound(new { error = "merchant_not_found" });

            // по желанию: проверка, что это ребёнок
            // if (!string.Equals(child.Role, "C", StringComparison.OrdinalIgnoreCase))
            //     return BadRequest(new { error = "child_wrong_role" });

            if (child.Balance < dto.Amount)
            {
                return BadRequest(new
                {
                    error = "insufficient_funds",
                    message = "На балансе ребёнка недостаточно средств."
                });
            }

            child.Balance -= dto.Amount;
            merchant.Balance += dto.Amount;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                status = "SUCCEEDED",
                amount = dto.Amount,
                childUserId = child.Id,
                merchantUserId = merchant.Id,
                childBalanceAfter = child.Balance,
                merchantBalanceAfter = merchant.Balance
            });
        }
    }
}
