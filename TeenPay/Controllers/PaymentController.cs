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
    // DB konteksts maksājumu, lietotāju, skolu, saistību un čeku apstrādei
    private readonly AppDbContext _db;
    public PaymentController(AppDbContext db) => _db = db;

    // Ģenerē vienkāršu čeka numuru (8 cipari) katram darījumam/čekam
    private static string NewReceiptNo()
        => Random.Shared.Next(0, 100_000_000).ToString("D8");

    // DTO objekts QR maksājuma datiem:
    // QR parasti satur bērna ID + skolas kodu, bet summu pievieno POS
    public sealed class QrPaymentPayloadDto
    {
        public long UserId { get; set; }         
        public string? OrgCode { get; set; }     
        public decimal? Amount { get; set; }     
    }

    // ==========================================================
    // MAKSĀJUMS PĒC QR (PayByQr)
    // ==========================================================
    [HttpPost("pay")]
    [Authorize] //  POS jābūt autorizētam (jābūt derīgam access token)
    public async Task<IActionResult> PayByQr([FromQuery] string data)
    {
        // ===== 1) Identificē, kas veic maksājumu (POS lietotājs) =====
        // POS lietotāja ID tiek ņemts no JWT claimiem (NameIdentifier)
        var mePosIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(mePosIdStr, out var mePosId) || mePosId <= 0)
            return Unauthorized(new { error = "neautorizēts" });

        // ===== 2) Nolasa QR “payload” (data) un pārveido par DTO =====
        // data tiek padots kā query parametrs, piemēram: ?data={...json...}
        QrPaymentPayloadDto? obj;
        try
        {
            // Deserializē JSON uz QrPaymentPayloadDto (case-insensitive)
            obj = JsonSerializer.Deserialize<QrPaymentPayloadDto>(data, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            // Ja JSON nav pareizs vai neizdodas deserializācija
            return BadRequest(new { error = "nepareiza_satura_dati" });
        }

        // Papildu drošība: obj nedrīkst būt null
        if (obj == null)
            return BadRequest(new { error = "nepareiza_satura_dati" });

        // ===== 3) Ievaddatu validācija (obligātie lauki) =====
        if (string.IsNullOrWhiteSpace(obj.OrgCode))
            return BadRequest(new { error = "skolas_kods_nepieciešams" });

        if (obj.Amount == null || obj.Amount <= 0)
            return BadRequest(new { error = "nepieciešamais_apjoms" });

        if (obj.UserId <= 0)
            return BadRequest(new { error = "bērns_nepieciešams" });

        // ===== 4) Datu ielāde no DB: bērns un skola =====
        // Atrod bērnu pēc ID
        var child = await _db.Users.SingleOrDefaultAsync(u => u.Id == obj.UserId);
        if (child == null)
            return NotFound(new { error = "persona_nav_atrasta" });

        // Atrod skolu pēc koda
        var school = await _db.Schools.SingleOrDefaultAsync(s => s.code == obj.OrgCode);
        if (school == null)
            return NotFound(new { error = "skola_nav_atrasta" });

        // ===== 5) Biznesa pārbaudes pirms darījuma =====

        // Bērnam jābūt piesaistītam konkrētajai skolai (StudentSchools saite)
        var linked = await _db.StudentSchools
            .AnyAsync(ss => ss.UserId == child.Id && ss.SchoolId == school.Id);
        if (!linked)
            return BadRequest(new { error = "nav_saistīts_ar_skolu" });

        // Skolai jābūt definētam POS lietotājam
        if (school.PosUserId == null)
            return BadRequest(new { error = "skolai_nav_pārdevēja" });

        // Drošības pārbaude: maksājumu drīkst apstrādāt tikai konkrētās skolas POS
        // Ja skenē cits POS → atgriež kļūdu
        if (school.PosUserId.Value != mePosId)
            return BadRequest(new { error = "skolas_neatbilstība" });

        // Atrod POS lietotāju DB pēc school.PosUserId
        var pos = await _db.Users.SingleOrDefaultAsync(u => u.Id == school.PosUserId.Value);
        if (pos == null)
            return BadRequest(new { error = "pos_lietotājs_nav_atrasts" });

        // ===== 6) Līdzekļu pārbaude (bērna bilance) =====
        var amount = obj.Amount.Value;

        // Ja bērnam nepietiek līdzekļu → darījumu neveic
        if (child.Balance < amount)
            return BadRequest(new { error = "nepietiekami_līdzekļi" });

        // ===== 7) Darījuma izpilde transakcijā =====
        // DB transakcija nodrošina, ka bilances, transakcijas un čeki saglabājas kopā
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Atjauno bilances: bērnam mīnus, POS plus
        child.Balance -= amount;
        pos.Balance += amount;

        // Izveido 1. transakcijas ierakstu bērnam (negatīva summa, PAYMENT)
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

        // Izveido 2. transakcijas ierakstu POS (pozitīva summa, TOPUP/ienākums)
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

        // Ģenerē divus čeka numurus (atsevišķi bērnam un POS)
        var receiptNoChild = NewReceiptNo();
        var receiptNoPos = NewReceiptNo();

        // ===== 8) Čeku izveide =====

        // Čeks bērnam (maksājums)
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

        // Čeks POS (ienākums)
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

        // Pievieno čekus DB kontekstam
        _db.Receipts.Add(rChild);
        _db.Receipts.Add(rPos);

        // Saglabā visas izmaiņas un apstiprina transakciju
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // ===== 9) Atbilde klientam/UI =====
        // Atgriež statusu, bilances pēc darījuma un abu čeku identifikatorus
        return Ok(new
        {
            status = "SUCCESSED",
            amount,
            childBalanceAfter = child.Balance,
            posBalanceAfter = pos.Balance,
            receiptChild = new { id = rChild.Id, no = rChild.ReceiptNo },
            receiptPos = new { id = rPos.Id, no = rPos.ReceiptNo }
        });
    }
}
