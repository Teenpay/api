using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeenPay.Data;
using TeenPay.Models;

[ApiController]
[Route("api/topup-requests")]
[Authorize]
public class TopUpRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    public TopUpRequestsController(AppDbContext db) => _db = db;

    private long CurrentUserId()
    {
        // иногда id лежит не в NameIdentifier, а в "sub" или "id"
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("id");

        if (string.IsNullOrWhiteSpace(raw))
            throw new UnauthorizedAccessException("Missing user id claim.");

        return long.Parse(raw);
    }

    // 1) РЕБЁНОК: создать запрос
    private static string NewReceiptNo()
        => Random.Shared.Next(0, 100_000_000).ToString("D8");

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTopUpRequestDto dto)
    {
        var childId = CurrentUserId();

        // можно вообще убрать dto.ChildId, но раз у тебя так — оставим проверку
        if (dto.ChildId != childId)
            return BadRequest(new { error = "child_mismatch" });

        var parentId = await _db.Set<ParentChild>()
            .Where(x => x.ChildUserId == childId)
            .Select(x => x.ParentUserId)
            .FirstOrDefaultAsync();

        if (parentId == 0)
            return BadRequest(new { error = "parent_not_linked" });

        var req = new TopUpRequest
        {
            ChildId = childId,
            ParentId = parentId,
            Status = "PENDING",
            RequestedAt = DateTimeOffset.UtcNow, // ✅ важно
            ApprovedAt = null,
            Note = dto.Note
        };

        _db.Add(req);
        await _db.SaveChangesAsync();

        return Ok(new { id = req.Id, status = req.Status, requestedAt = req.RequestedAt });
    }

    // 2) РОДИТЕЛЬ: посмотреть входящие запросы
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox()
    {
        var parentId = CurrentUserId();

        var list = await _db.Set<TopUpRequest>()
            .Where(r => r.ParentId == parentId && r.Status == "PENDING")
            .Join(_db.Users, r => r.ChildId, u => u.Id, (r, u) => new
            {
                id = r.Id,
                childId = r.ChildId,
                childUsername = u.Username,
                requestedAt = r.RequestedAt,
                note = r.Note
            })
            .OrderByDescending(x => x.requestedAt)
            .ToListAsync();

        return Ok(list);
    }

    // 3) РОДИТЕЛЬ: одобрить и перевести деньги ребёнку
    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, [FromBody] ApproveTopUpRequestDto dto)
    {
        var parentId = CurrentUserId();
        if (dto.Amount <= 0) return BadRequest(new { error = "amount_invalid" });

        await using var tx = await _db.Database.BeginTransactionAsync();

        var req = await _db.Set<TopUpRequest>().FirstOrDefaultAsync(r => r.Id == id);
        if (req == null) return NotFound();
        if (req.ParentId != parentId) return Forbid();
        if (req.Status != "PENDING") return BadRequest(new { error = "not_pending" });

        var parent = await _db.Users.FirstAsync(u => u.Id == parentId);
        var child = await _db.Users.FirstAsync(u => u.Id == req.ChildId);

        if (parent.Balance < dto.Amount)
            return BadRequest(new { error = "insufficient_funds" });

        parent.Balance -= dto.Amount;
        child.Balance += dto.Amount;

        req.Status = "APPROVED";
        req.ApprovedAt = DateTimeOffset.UtcNow;

        _db.Transactions.Add(new Transaction
        {
            userid = parent.Id,
            kind = "TOPUP",
            amount = -dto.Amount,
            description = $"Top up child @{child.Username} (request #{req.Id})",
            createdat = DateTime.UtcNow
        });

        _db.Transactions.Add(new Transaction
        {
            userid = child.Id,
            kind = "TOPUP",
            amount = dto.Amount,
            description = $"Received top up from parent @{parent.Username} (request #{req.Id})",
            createdat = DateTime.UtcNow
        });

        // ✅ 2 receipts: parent (out) + child (in)
        var receiptNoParent = NewReceiptNo();
        var receiptNoChild = NewReceiptNo();
        var now = DateTime.UtcNow;

        var rParent = new Receipt
        {
            ReceiptNo = receiptNoParent,
            Amount = dto.Amount,
            Kind = "TOPUP_OUT",
            PayerUserId = parent.Id,
            PayeeUserId = child.Id,
            SchoolId = null,
            CreatedAt = now
        };

        var rChild = new Receipt
        {
            ReceiptNo = receiptNoChild,
            Amount = dto.Amount,
            Kind = "TOPUP_IN",
            PayerUserId = parent.Id,
            PayeeUserId = child.Id,
            SchoolId = null,
            CreatedAt = now
        };

        _db.Receipts.AddRange(rParent, rChild);

        await _db.SaveChangesAsync();   // ✅ после этого появятся rParent.Id / rChild.Id
        await tx.CommitAsync();

        return Ok(new
        {
            ok = true,
            receiptParent = new { id = rParent.Id, no = rParent.ReceiptNo },
            receiptChild = new { id = rChild.Id, no = rChild.ReceiptNo }
        });
    }
}
