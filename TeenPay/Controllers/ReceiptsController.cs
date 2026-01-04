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
[Authorize] // Visi šī kontroliera endpointi pieejami tikai autorizētam lietotājam
public class ReceiptsController : ControllerBase
{
    // DB konteksts čeku izgūšanai un saistīto datu (lietotāji, skolas) ielādei
    private readonly AppDbContext _db;
    public ReceiptsController(AppDbContext db) => _db = db;

    // ==========================================================
    // Palīgmetode: nosaka pašreizējā lietotāja ID no JWT claimiem
    // ==========================================================
    private int CurrentUserId()
    {
        // Meklē lietotāja identifikatoru vairākos iespējamos claimos
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("id");

        // Ja ID nav atrodams, piekļuve nav korekta (nav derīga autorizācija)
        if (string.IsNullOrWhiteSpace(raw))
            throw new UnauthorizedAccessException("Trūkst lietotāja identifikatora pieprasījums.");

        // Pārvērš ID uz int (projekta līmenī lietotāju ID tiek lietots kā skaitlis)
        return int.Parse(raw);
    }

    // ==========================================================
    // GET /api/receipts
    // Funkcija: atgriež “manus čekus” (kur es esmu maksātājs vai saņēmējs)
    // ==========================================================
    [HttpGet]
    public async Task<IActionResult> My()
    {
        var meId = CurrentUserId();

        // 1) Atlasām čekus, kuros pašreizējais lietotājs ir payer vai payee
        // AsNoTracking => tikai lasīšanai (ātrāk, jo nav nepieciešams tracking)
        var rows = await _db.Receipts.AsNoTracking()
            .Where(r => r.PayerUserId == meId || r.PayeeUserId == meId)
            .OrderByDescending(r => r.CreatedAt)
            // Izvēlamies tikai laukus, kuri nepieciešami DTO izveidei
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

        // Ja čeku nav, atgriež tukšu sarakstu (nevis kļūdu)
        if (rows.Count == 0)
            return Ok(new List<ReceiptDto>());

        // 2) Ielādējam cilvēku vārdus (payer/payee) vienā piegājienā
        var userIds = rows.SelectMany(x => new[] { x.PayerUserId, x.PayeeUserId })
            .Distinct()
            .ToList();

        // Lietotājus pārvēršam vārdnīcā: userId -> FullName vai Username
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

        // 3) Ielādējam skolu nosaukumus (ja čekam ir piesaistīta skola)
        var schoolIds = rows.Where(x => x.SchoolId != null)
            .Select(x => (int)x.SchoolId!.Value)
            .Distinct()
            .ToList();

        var schools = await _db.Schools.AsNoTracking()
            .Where(s => schoolIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        // 4) Savācam rezultātu kā ReceiptDto sarakstu (ērti MAUI pusē)
        var result = rows.Select(r =>
        {
            // Payer/Payee vārdi, ja nav atrasts – fallback uz user#ID
            var fromName = users.TryGetValue(r.PayerUserId, out var fn) ? fn : $"user#{r.PayerUserId}";
            var toName = users.TryGetValue(r.PayeeUserId, out var tn) ? tn : $"user#{r.PayeeUserId}";

            // Skolas nosaukums, ja SchoolId ir norādīts
            string? schoolName = null;
            if (r.SchoolId != null && schools.TryGetValue((int)r.SchoolId.Value, out var sn))
                schoolName = sn;

            // “SignedAmount”: ja es esmu maksātājs, tad summa man ir ar mīnusu,
            // ja es esmu saņēmējs – ar plusu (ērti sarakstam/krāsošanai UI)
            var signed = (r.PayerUserId == meId) ? -r.Amount : r.Amount;

            // ✅ Virziens: IN (ienākums) vai OUT (izdevums)
            var direction = signed >= 0 ? "IN" : "OUT";

            return new ReceiptDto
            {
                Id = r.Id,
                ReceiptNo = r.ReceiptNo,
                Amount = r.Amount,
                SignedAmount = signed,     // svarīgi, lai UI var rādīt ar +/- zīmi
                Direction = direction,     // var izmantot filtrēšanai vai ikonai
                CreatedAt = r.CreatedAt,
                FromName = fromName,
                ToName = toName,
                SchoolName = schoolName
            };
        }).ToList();

        // Atgriež “manu čeku” sarakstu
        return Ok(result);
    }

    // ==========================================================
    // GET /api/receipts/{id}/pdf
    // Funkcija: ģenerē un atgriež PDF konkrētam čekam pēc tā ID
    // ==========================================================
    [HttpGet("{id:long}/pdf")]
    public async Task<IActionResult> Pdf(long id)
    {
        var meId = CurrentUserId();

        // Atrod čeku pēc ID
        var r = await _db.Receipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        // Ja čeks nav atrasts
        if (r == null) return NotFound();

        // Drošība: PDF var saņemt tikai čeka dalībnieki (payer vai payee)
        if (r.PayerUserId != meId && r.PayeeUserId != meId)
            return Forbid();

        // Ielādē payer/payee datus (vārds/uzvārds vai username)
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

        // Ja čekam ir skola, ielādē skolas nosaukumu
        string? schoolName = null;
        if (r.SchoolId != null)
        {
            schoolName = await _db.Schools.AsNoTracking()
                .Where(s => s.Id == (int)r.SchoolId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();
        }

        // Ģenerē PDF (kā byte[])
        var bytes = GenerateReceiptPdf(
            receiptNo: r.ReceiptNo,
            kind: r.Kind,
            amount: r.Amount,
            createdAt: r.CreatedAt,
            from: payerName,
            to: payeeName,
            school: schoolName
        );

        // Atgriež PDF failu lejupielādei/atvēršanai
        return File(bytes, "application/pdf", $"receipt_{r.ReceiptNo}.pdf");
    }

    // ==========================================================
    // GET /api/receipts/by-no/{receiptNo}/pdf
    // Funkcija: tas pats PDF, bet čeku meklē pēc ReceiptNo (numura)
    // ==========================================================
    [HttpGet("by-no/{receiptNo}/pdf")]
    public async Task<IActionResult> PdfByNo(string receiptNo)
    {
        var meId = CurrentUserId();

        // Atrod čeku pēc čeka numura (ReceiptNo)
        var r = await _db.Receipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ReceiptNo == receiptNo);

        if (r == null) return NotFound();

        // Drošība: PDF pieejams tikai čeka dalībniekiem
        if (r.PayerUserId != meId && r.PayeeUserId != meId)
            return Forbid();

        // Ielādē payer/payee vārdus
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

        // Ielādē skolas nosaukumu, ja ir
        string? schoolName = null;
        if (r.SchoolId != null)
        {
            schoolName = await _db.Schools.AsNoTracking()
                .Where(s => s.Id == (int)r.SchoolId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();
        }

        // Ģenerē PDF
        var bytes = GenerateReceiptPdf(
            receiptNo: r.ReceiptNo,
            kind: r.Kind,
            amount: r.Amount,
            createdAt: r.CreatedAt,
            from: payerName,
            to: payeeName,
            school: schoolName
        );

        // Atgriež PDF failu
        return File(bytes, "application/pdf", $"receipt_{r.ReceiptNo}.pdf");
    }

    // ==========================================================
    // PDF ģenerēšanas metode (QuestPDF)
    // Izveido vienkāršu čeka PDF ar pamata informāciju par darījumu
    // ==========================================================
    private static byte[] GenerateReceiptPdf(
        string receiptNo,
        string kind,
        decimal amount,
        DateTimeOffset createdAt,
        string from,
        string to,
        string? school)
    {
        // Rakstām PDF uz MemoryStream, pēc tam atgriežam kā byte[]
        using var ms = new MemoryStream();

        // QuestPDF dokumenta definīcija
        Document.Create(container =>
        {
            container.Page(page =>
            {
                // Lapas pamata iestatījumi
                page.Margin(30);
                page.Size(PageSizes.A4);

                // Saturs: kolonna ar tekstiem/atdalītāju
                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // Virsraksts
                    col.Item().Text("TeenPay atskaite par maksājumu.")
                        .FontSize(20).SemiBold();

                    // Pamatinformācija
                    col.Item().Text($"Kvīts #: {receiptNo}").FontSize(12);
                    col.Item().Text($"Datums: {createdAt:dd.MM.yyyy HH:mm}").FontSize(12);
                    col.Item().Text($"Tips: {kind}").FontSize(12);

                    // Atdalītājs
                    col.Item().LineHorizontal(1);

                    // Dalībnieki
                    col.Item().Text($"No: {from}").FontSize(12);
                    col.Item().Text($"Uz: {to}").FontSize(12);

                    // Skola (ja ir)
                    if (!string.IsNullOrWhiteSpace(school))
                        col.Item().Text($"Skola: {school}").FontSize(12);

                    // Summa ar izcēlumu
                    col.Item().PaddingTop(10).Text($"Summa: €{amount:0.00}")
                        .FontSize(16).SemiBold();

                    // Pateicības teksts (vizuāli pelēkāks)
                    col.Item().PaddingTop(20).Text("Paldies, ka izmantojat TeenPay!")
                        .FontSize(11).Italic().FontColor("#666666");
                });
            });
        }).GeneratePdf(ms);

        // Atgriež PDF kā masīvu
        return ms.ToArray();
    }
}
