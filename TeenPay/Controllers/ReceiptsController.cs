using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReceiptsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReceiptsController(AppDbContext db) => _db = db;

    private int CurrentUserId()
    {
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("id");

        if (string.IsNullOrWhiteSpace(raw))
            throw new UnauthorizedAccessException("Missing user id claim.");

        return int.Parse(raw);
    }

    [HttpGet]
    public async Task<IActionResult> My()
    {
        var meId = CurrentUserId();

        // 1) берём чеки, где я payer или payee
        var rows = await _db.Receipts.AsNoTracking()
            .Where(r => r.PayerUserId == meId || r.PayeeUserId == meId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.ReceiptNo,
                r.Amount,
                r.Kind,
                r.CreatedAt,
                r.PayerUserId,
                r.PayeeUserId,
                r.SchoolId
            })
            .ToListAsync();

        if (rows.Count == 0)
            return Ok(new List<ReceiptDto>());

        // 2) подтягиваем имена людей
        var userIds = rows.SelectMany(x => new[] { x.PayerUserId, x.PayeeUserId })
            .Distinct()
            .ToList();

        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.Username,
                FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()
            })
            .ToDictionaryAsync(
                x => x.Id,
                x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName
            );

        // 3) подтягиваем школы
        var schoolIds = rows.Where(x => x.SchoolId != null)
            .Select(x => (int)x.SchoolId!.Value)
            .Distinct()
            .ToList();

        var schools = await _db.Schools.AsNoTracking()
            .Where(s => schoolIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        // 4) собираем DTO
        var result = rows.Select(r =>
        {
            var fromName = users.TryGetValue(r.PayerUserId, out var fn) ? fn : $"user#{r.PayerUserId}";
            var toName = users.TryGetValue(r.PayeeUserId, out var tn) ? tn : $"user#{r.PayeeUserId}";

            string? schoolName = null;
            if (r.SchoolId != null && schools.TryGetValue((int)r.SchoolId.Value, out var sn))
                schoolName = sn;

            return new ReceiptDto
            {
                Id = r.Id,
                ReceiptNo = r.ReceiptNo,
                Amount = r.Amount,
                CreatedAt = r.CreatedAt,
                FromName = fromName,
                ToName = toName,
                SchoolName = schoolName
            };
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{id:long}/pdf")]
    public async Task<IActionResult> Pdf(long id)
    {
        var meId = CurrentUserId();

        var r = await _db.Receipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (r == null) return NotFound();

        // доступ только участникам
        if (r.PayerUserId != meId && r.PayeeUserId != meId)
            return Forbid();

        var userIds = new[] { r.PayerUserId, r.PayeeUserId }.Distinct().ToList();

        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.Username,
                FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()
            })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName);

        string payerName = users.TryGetValue(r.PayerUserId, out var p) ? p : $"user#{r.PayerUserId}";
        string payeeName = users.TryGetValue(r.PayeeUserId, out var q) ? q : $"user#{r.PayeeUserId}";

        string? schoolName = null;
        if (r.SchoolId != null)
        {
            schoolName = await _db.Schools.AsNoTracking()
                .Where(s => s.Id == (int)r.SchoolId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();
        }

        var bytes = GenerateReceiptPdf(
            receiptNo: r.ReceiptNo,
            kind: r.Kind,
            amount: r.Amount,
            createdAt: r.CreatedAt,
            from: payerName,
            to: payeeName,
            school: schoolName
        );

        return File(bytes, "application/pdf", $"receipt_{r.ReceiptNo}.pdf");
    }

    // ✅ NEW: PDF по номеру чека (ReceiptNo), например 03394084
    [HttpGet("by-no/{receiptNo}/pdf")]
    public async Task<IActionResult> PdfByNo(string receiptNo)
    {
        var meId = CurrentUserId();

        var r = await _db.Receipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ReceiptNo == receiptNo);

        if (r == null) return NotFound();

        // доступ только участникам
        if (r.PayerUserId != meId && r.PayeeUserId != meId)
            return Forbid();

        var userIds = new[] { r.PayerUserId, r.PayeeUserId }.Distinct().ToList();

        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.Username,
                FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()
            })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName);

        string payerName = users.TryGetValue(r.PayerUserId, out var p) ? p : $"user#{r.PayerUserId}";
        string payeeName = users.TryGetValue(r.PayeeUserId, out var q) ? q : $"user#{r.PayeeUserId}";

        string? schoolName = null;
        if (r.SchoolId != null)
        {
            schoolName = await _db.Schools.AsNoTracking()
                .Where(s => s.Id == (int)r.SchoolId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();
        }

        var bytes = GenerateReceiptPdf(
            receiptNo: r.ReceiptNo,
            kind: r.Kind,
            amount: r.Amount,
            createdAt: r.CreatedAt,
            from: payerName,
            to: payeeName,
            school: schoolName
        );

        return File(bytes, "application/pdf", $"receipt_{r.ReceiptNo}.pdf");
    }

    private static byte[] GenerateReceiptPdf(
        string receiptNo,
        string kind,
        decimal amount,
        DateTimeOffset createdAt,
        string from,
        string to,
        string? school)
    {
        using var ms = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text("TeenPay Receipt")
                        .FontSize(20).SemiBold();

                    col.Item().Text($"Receipt #: {receiptNo}").FontSize(12);
                    col.Item().Text($"Date: {createdAt:dd.MM.yyyy HH:mm}").FontSize(12);
                    col.Item().Text($"Type: {kind}").FontSize(12);

                    col.Item().LineHorizontal(1);

                    col.Item().Text($"From: {from}").FontSize(12);
                    col.Item().Text($"To: {to}").FontSize(12);

                    if (!string.IsNullOrWhiteSpace(school))
                        col.Item().Text($"School: {school}").FontSize(12);

                    col.Item().PaddingTop(10).Text($"Amount: €{amount:0.00}")
                        .FontSize(16).SemiBold();

                    col.Item().PaddingTop(20).Text("Thank you for using TeenPay.")
                        .FontSize(11).Italic().FontColor("#666666");
                });
            });
        }).GeneratePdf(ms);

        return ms.ToArray();
    }
}
