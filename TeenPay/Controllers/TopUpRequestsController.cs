using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeenPay.Data;
using TeenPay.Models;

[ApiController]
[Route("api/topup-requests")]
[Authorize] // Visi šī kontroliera endpointi pieejami tikai autorizētam lietotājam
public class TopUpRequestsController : ControllerBase
{
    // DB konteksts top-up pieprasījumu, lietotāju un transakciju apstrādei
    private readonly AppDbContext _db;
    public TopUpRequestsController(AppDbContext db) => _db = db;

    // ==========================================================
    // Palīgmetode: atgriež pašreizējā lietotāja ID no JWT claimiem
    // ==========================================================
    private long CurrentUserId()
    {
        // Dažkārt ID tiek glabāts nevis NameIdentifier, bet "sub" vai "id" claimā
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("id");

        // Ja ID nav pieejams, lietotājs nav korekti autentificēts
        if (string.IsNullOrWhiteSpace(raw))
            throw new UnauthorizedAccessException("Trūkstošs lietotāja identifikators.");

        return long.Parse(raw);
    }

    // ==========================================================
    // Palīgmetode: ģenerē čeka numuru (8 cipari)
    // ==========================================================
    // 1) РЕБЁНОК: создать запрос
    private static string NewReceiptNo()
        => Random.Shared.Next(0, 100_000_000).ToString("D8");

    // ==========================================================
    // POST /api/topup-requests
    // Funkcija: bērns izveido top-up (naudas pieprasījuma) ierakstu vecākam
    // ==========================================================
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTopUpRequestDto dto)
    {
        // Pašreizējais lietotājs = bērns, kas izveido pieprasījumu
        var childId = CurrentUserId();

        // Papildu drošība: pieprasījuma DTO ChildId jāsakrīt ar tokenā esošo childId
        // (komentārs: varētu arī vispār neizmantot dto.ChildId, bet šeit tas tiek pārbaudīts)
        if (dto.ChildId != childId)
            return BadRequest(new { error = "bērna_neatbilstība" });

        // Atrod bērna piesaistīto vecāku (ParentChild tabula)
        var parentId = await _db.Set<ParentChild>()
            .Where(x => x.ChildUserId == childId)
            .Select(x => x.ParentUserId)
            .FirstOrDefaultAsync();

        // Ja vecāks nav piesaistīts, pieprasījumu nevar izveidot
        if (parentId == 0)
            return BadRequest(new { error = "vecāks_nav_saistīts" });

        // Izveido jaunu top-up pieprasījumu ar statusu PENDING
        var req = new TopUpRequest
        {
            ChildId = childId,
            ParentId = parentId,
            Status = "PENDING",
            RequestedAt = DateTimeOffset.UtcNow, // ✅ svarīgi: pieprasījuma izveides laiks
            ApprovedAt = null,
            Note = dto.Note
        };

        // Saglabā pieprasījumu DB
        _db.Add(req);
        await _db.SaveChangesAsync();

        // Atgriež pieprasījuma ID, statusu un izveides datumu
        return Ok(new { id = req.Id, status = req.Status, requestedAt = req.RequestedAt });
    }

    // ==========================================================
    // GET /api/topup-requests/inbox
    // Funkcija: vecāks redz ienākošos (PENDING) top-up pieprasījumus no bērna
    // ==========================================================
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox()
    {
        // Pašreizējais lietotājs = vecāks, kam adresēti pieprasījumi
        var parentId = CurrentUserId();

        // Atrod visus PENDING pieprasījumus, kas adresēti šim vecākam,
        // un pievieno bērna lietotājvārdu (JOIN ar Users)
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

        // Atgriež vecāka “inbox” sarakstu
        return Ok(list);
    }

    // ==========================================================
    // POST /api/topup-requests/{id}/approve
    // Funkcija: vecāks apstiprina top-up pieprasījumu un veic naudas pārskaitījumu bērnam
    // (vienlaikus atjauno bilances, transakcijas un izveido čekus)
    // ==========================================================
    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, [FromBody] ApproveTopUpRequestDto dto)
    {
        // Pašreizējais lietotājs = vecāks, kas apstiprina
        var parentId = CurrentUserId();

        // Validācija: summa nedrīkst būt 0 vai negatīva
        if (dto.Amount <= 0) return BadRequest(new { error = "summa_nav_derīga" });

        // DB transakcija, lai visas izmaiņas tiktu saglabātas atomāri
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Atrod pieprasījumu pēc ID
        var req = await _db.Set<TopUpRequest>().FirstOrDefaultAsync(r => r.Id == id);
        if (req == null) return NotFound();

        // Drošība: pieprasījumu drīkst apstiprināt tikai tas vecāks, kam tas pieder
        if (req.ParentId != parentId) return Forbid();

        // Biznesa noteikums: pieprasījumam jābūt PENDING
        if (req.Status != "PENDING") return BadRequest(new { error = "not_pending" });

        // Ielādē vecāka un bērna lietotājus
        var parent = await _db.Users.FirstAsync(u => u.Id == parentId);
        var child = await _db.Users.FirstAsync(u => u.Id == req.ChildId);

        // Pārbauda, vai vecākam pietiek līdzekļu
        if (parent.Balance < dto.Amount)
            return BadRequest(new { error = "nepietiekami_līdzekļi" });

        // Atjauno bilances: vecākam mīnus, bērnam plus
        parent.Balance -= dto.Amount;
        child.Balance += dto.Amount;

        // Atjauno pieprasījuma statusu un apstiprināšanas laiku
        req.Status = "APPROVED";
        req.ApprovedAt = DateTimeOffset.UtcNow;

        // Izveido transakciju ierakstu vecākam (OUT)
        _db.Transactions.Add(new Transaction
        {
            userid = parent.Id,
            kind = "TOPUP",
            amount = -dto.Amount,
            description = $"Papildināts bērnam @{child.Username} (request #{req.Id})",
            createdat = DateTime.UtcNow
        });

        // Izveido transakciju ierakstu bērnam (IN)
        _db.Transactions.Add(new Transaction
        {
            userid = child.Id,
            kind = "TOPUP",
            amount = dto.Amount,
            description = $"Saņemts papildinājums no vecākiem @{parent.Username} (request #{req.Id})",
            createdat = DateTime.UtcNow
        });

        // ✅ Izveido 2 čekus: vecākam (izdevums) un bērnam (ienākums)
        var receiptNoParent = NewReceiptNo();
        var receiptNoChild = NewReceiptNo();
        var now = DateTime.UtcNow;

        // Vecāka čeks (OUT)
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

        // Bērna čeks (IN)
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

        // Saglabā čekus DB
        _db.Receipts.AddRange(rParent, rChild);

        // Saglabā visas izmaiņas DB (pēc šī izsaukuma parādās rParent.Id / rChild.Id)
        await _db.SaveChangesAsync();

        // Apstiprina DB transakciju (visas izmaiņas stājas spēkā kopā)
        await tx.CommitAsync();

        // Atgriež veiksmīgu rezultātu + abu čeku identifikatorus
        return Ok(new
        {
            ok = true,
            receiptParent = new { id = rParent.Id, no = rParent.ReceiptNo },
            receiptChild = new { id = rChild.Id, no = rChild.ReceiptNo }
        });
    }
}
