using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Visi šī kontroliera endpointi ir pieejami tikai autorizētiem lietotājiem
public class TransactionsController : ControllerBase
{
    // DB konteksts transakciju, lietotāju un skolu datu nolasīšanai
    private readonly AppDbContext _db;

    public TransactionsController(AppDbContext db)
    {
        _db = db;
    }

    // ==========================================================
    // GET /api/transactions
    // Funkcija: atgriež autorizētā lietotāja transakciju sarakstu (TransactionDto)
    // Papildu loģika: mēģina noteikt “No/Kam” (sender/receiver) pēc transakciju pāriem
    // ==========================================================
    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetMyTransactions()
    {
        // JWT tokenā ID var atrasties NameIdentifier vai "sub" claimā
        // (komentārs kodā: tev nav "id", tāpēc tiek izmantoti šie lauki)
        var userIdStr =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        // Ja ID nav vai to nevar pārvērst par skaitli — nav autorizācijas
        if (string.IsNullOrWhiteSpace(userIdStr) || !long.TryParse(userIdStr, out var meId))
            return Unauthorized();

        // Pašreizējā lietotāja lietotājvārds (fallback uz "me")
        var meUsername = User.FindFirstValue(ClaimTypes.Name) ?? "me";

        // ----------------------------------------------------------
        // 1) Ielādē transakcijas, kas pieder pašreizējam lietotājam
        // ----------------------------------------------------------
        var my = await _db.Transactions
            .Where(t => t.userid == meId)
            .OrderByDescending(t => t.createdat)
            // Atlasām tikai tos laukus, kas vajadzīgi DTO izveidei un papildloģikai
            .Select(t => new
            {
                t.id,
                t.userid,
                t.amount,
                t.kind,
                t.description,
                t.createdat,
                t.childid,
                t.schoolid
            })
            .ToListAsync();

        // Ja nav transakciju — atgriež tukšu sarakstu (nevis kļūdu)
        if (my.Count == 0)
            return Ok(new List<TransactionDto>());

        // ----------------------------------------------------------
        // 2) Savāc childIds un schoolIds, lai vēlāk ielādētu vārdus/nosaukumus
        // ----------------------------------------------------------
        var childIds = my.Where(x => x.childid != null).Select(x => x.childid!.Value).Distinct().ToList();
        var schoolIds = my.Where(x => x.schoolid != null).Select(x => x.schoolid!.Value).Distinct().ToList();

        // ----------------------------------------------------------
        // 3) Izveido vārdnīcas (lookup):
        //    child userId -> username, schoolId -> school name
        // ----------------------------------------------------------
        var childUsernames = await _db.Users
            .Where(u => childIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username);

        var schools = await _db.Schools
            .Where(s => schoolIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        // ----------------------------------------------------------
        // 4) “Pārošanas” loģika:
        //    TOPUP (+) bieži ir saistīts ar PAYMENT (-) citam lietotājam,
        //    tāpēc tiek meklēti kandidāti, lai noteiktu “sūtītāju”.
        //    (balstās uz: vienāds childid/schoolid, summa ar pretēju zīmi,
        //     un laiks +/- 10 sekundes)
        // ----------------------------------------------------------
        var pairCandidates = await _db.Transactions
            .Where(t =>
                t.kind == "PAYMENT" &&
                (
                    (t.childid != null && childIds.Contains(t.childid.Value)) ||
                    (t.schoolid != null && schoolIds.Contains(t.schoolid.Value))
                )
            )
            .Select(t => new { t.id, t.userid, t.amount, t.kind, t.createdat, t.childid, t.schoolid })
            .ToListAsync();

        // Izgūst iespējamos “sūtītāju” userId un sagatavo lookup userId -> username
        var senderIds = pairCandidates.Select(x => x.userid).Distinct().ToList();
        var senderUsernames = await _db.Users
            .Where(u => senderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username);

        // ----------------------------------------------------------
        // 5) Izveido TransactionDto sarakstu ar aprēķinātiem sender/receiver
        // ----------------------------------------------------------
        var result = new List<TransactionDto>(my.Count);

        foreach (var t in my)
        {
            // Noklusējuma vērtības, ja nevar noteikt virzienu
            string sender = "—";
            string receiver = "—";

            // ======================================================
            // A) Pārvedums bērnam (vecāks -> bērns)
            //    Shēma: vecākam PAYMENT(-), bērnam TOPUP(+)
            // ======================================================
            if (t.childid != null)
            {
                // Atrod bērna vārdu pēc childId
                var childName = childUsernames.TryGetValue(t.childid.Value, out var cn) ? cn : $"Bērns#{t.childid.Value}";

                if (t.kind == "PAYMENT" && t.amount < 0)
                {
                    // Vecāks redz savu PAYMENT(-): No = vecāks(me), Kam = bērns
                    sender = meUsername;
                    receiver = childName;
                }
                else if (t.kind == "TOPUP" && t.amount > 0)
                {
                    // Bērns redz TOPUP(+): No = vecāks (meklē parasto PAYMENT), Kam = bērns(me)
                    var from = pairCandidates
                        .Where(p =>
                            p.childid == t.childid &&
                            p.amount == -t.amount &&
                            p.createdat >= t.createdat.AddSeconds(-10) &&
                            p.createdat <= t.createdat.AddSeconds(10))
                        .OrderByDescending(p => p.createdat)
                        .FirstOrDefault();

                    // Ja atrasts pāris, nosaka sūtītāju pēc userid
                    if (from != null && senderUsernames.TryGetValue(from.userid, out var sn))
                        sender = sn;
                    else
                        sender = "-"; // fallback, ja nevar noteikt precīzi

                    receiver = meUsername; // bērns (pašreizējais lietotājs)
                }
            }

            // ======================================================
            // B) Apmaksa skolai (bērns -> skola) un POS ienākums
            //    Shēma: bērnam PAYMENT(-), POS/školai TOPUP(+)
            // ======================================================
            if (t.schoolid != null)
            {
                // Skolas nosaukums pēc schoolId (fallback uz school#ID)
                var schoolName = schools.TryGetValue(t.schoolid.Value, out var s)
                    ? s
                    : $"skola#{t.schoolid.Value}";

                if (t.kind == "PAYMENT" && t.amount < 0)
                {
                    // Bērns redz PAYMENT(-): No = bērns(me), Kam = skola
                    sender = meUsername;
                    receiver = schoolName;
                }
                else if (t.kind == "TOPUP" && t.amount > 0)
                {
                    // POS darbinieks redz TOPUP(+): No = bērns, Kam = skola
                    receiver = schoolName;

                    if (t.childid != null && childUsernames.TryGetValue(t.childid.Value, out var childName))
                        sender = childName;
                    else
                        sender = "skolēns"; // fallback, ja bērna vārdu nevar noteikt
                }
            }

            // Ja neviena no shēmām neatbilst, vismaz norāda sūtītāju kā pašreizējo lietotāju
            if (sender == "—" && receiver == "—")
                sender = meUsername;

            // Pievieno DTO rezultātu sarakstam
            result.Add(new TransactionDto
            {
                Id = t.id,
                SenderUsername = sender,
                ReceiverUsername = receiver,
                CreatedAt = t.createdat,
                Description = t.description,
                Amount = t.amount,
                Kind = t.kind
            });
        }

        // Atgriež rezultātu (jau sakārtotu, jo my bija OrderByDescending)
        return Ok(result);
    }

    // ==========================================================
    // GET /api/transactions/children
    // Funkcija: vecāks iegūst visu savu bērnu transakciju sarakstu
    // (izmanto ParentChildren saites un atgriež TransactionDto)
    // ==========================================================
    [HttpGet("children")]
    public async Task<ActionResult<List<TransactionDto>>> GetChildrenTransactions()
    {
        // Nosaka pašreizējo lietotāju (vecāku) no JWT claimiem
        var userIdStr =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userIdStr) || !long.TryParse(userIdStr, out var parentId))
            return Unauthorized();

        // ----------------------------------------------------------
        // 1) Atrod visus bērnu ID, kas piesaistīti šim vecākam
        // ----------------------------------------------------------
        var childIds = await _db.ParentChildren
            .Where(pc => pc.ParentUserId == (int)parentId)
            .Select(pc => pc.ChildUserId)
            .Distinct()
            .ToListAsync();

        // Ja bērnu nav — atgriež tukšu sarakstu
        if (childIds.Count == 0)
            return Ok(new List<TransactionDto>());

        // ----------------------------------------------------------
        // 2) Ielādē visu bērnu transakcijas
        //    (userid šeit ir bērna lietotāja ID)
        // ----------------------------------------------------------
        var childTx = await _db.Transactions
            .Where(t => childIds.Contains(t.userid))
            .OrderByDescending(t => t.createdat)
            .Select(t => new
            {
                t.id,
                t.userid,
                t.amount,
                t.kind,
                t.description,
                t.createdat,
                t.childid,
                t.schoolid
            })
            .ToListAsync();

        if (childTx.Count == 0)
            return Ok(new List<TransactionDto>());

        // ----------------------------------------------------------
        // 3) Sagatavo lookup vārdnīcas lietotājiem un skolām
        // ----------------------------------------------------------
        var involvedUserIds = childTx
            .Select(x => x.userid)
            .Concat(childTx.Where(x => x.childid != null).Select(x => x.childid!.Value))
            .Distinct()
            .ToList();

        var userNames = await _db.Users
            .Where(u => involvedUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username);

        var schoolIds = childTx
            .Where(x => x.schoolid != null)
            .Select(x => x.schoolid!.Value)
            .Distinct()
            .ToList();

        var schools = await _db.Schools
            .Where(s => schoolIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        // ----------------------------------------------------------
        // 4) Meklē parastos PAYMENT kandidātus, lai TOPUP gadījumā varētu noteikt sūtītāju
        // ----------------------------------------------------------
        var pairCandidates = await _db.Transactions
            .Where(t =>
                t.kind == "PAYMENT" &&
                (
                    (t.childid != null && childIds.Contains(t.childid.Value)) ||
                    (t.schoolid != null && schoolIds.Contains(t.schoolid.Value))
                )
            )
            .Select(t => new { t.userid, t.amount, t.createdat, t.childid, t.schoolid })
            .ToListAsync();

        var senderIds = pairCandidates.Select(x => x.userid).Distinct().ToList();

        var senderUsernames = await _db.Users
            .Where(u => senderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username);

        // ----------------------------------------------------------
        // 5) Izveido TransactionDto sarakstu bērnu transakcijām
        // ----------------------------------------------------------
        var result = new List<TransactionDto>(childTx.Count);

        foreach (var t in childTx)
        {
            // Bērna username, kam pieder šī transakcija (t.userid)
            var meUsername = userNames.TryGetValue(t.userid, out var meU) ? meU : $"skolēns#{t.userid}";

            string sender = "—";
            string receiver = "—";

            // ======================================================
            // A) Vecāks -> bērns: bērnam TOPUP(+) (ienākošs pārvedums)
            // ======================================================
            if (t.kind == "TOPUP" && t.amount > 0)
            {
                // Saņēmējs ir bērns (t.userid)
                receiver = meUsername;

                // Sūtītāju mēģina noteikt pēc “parastā” PAYMENT(-X) pāra
                if (t.childid != null)
                {
                    var from = pairCandidates
                        .Where(p =>
                            p.childid == t.childid &&
                            p.amount == -t.amount &&
                            p.createdat >= t.createdat.AddSeconds(-10) &&
                            p.createdat <= t.createdat.AddSeconds(10))
                        .OrderByDescending(p => p.createdat)
                        .FirstOrDefault();

                    if (from != null && senderUsernames.TryGetValue(from.userid, out var sn))
                        sender = sn;
                    else
                        sender = "vecāks";
                }
                else
                {
                    sender = "vecāks";
                }
            }

            // ======================================================
            // B) Bērns maksā skolai: PAYMENT(-)
            // ======================================================
            if (t.kind == "PAYMENT" && t.amount < 0 && t.schoolid != null)
            {
                sender = meUsername;
                receiver = schools.TryGetValue(t.schoolid.Value, out var s)
                    ? s
                    : $"skola#{t.schoolid.Value}";
            }

            // Pievieno DTO (pat ja sender/receiver nav noteikts ideāli, tiek saglabāts ieraksts)
            result.Add(new TransactionDto
            {
                Id = t.id,
                SenderUsername = sender,
                ReceiverUsername = receiver,
                CreatedAt = t.createdat,
                Description = t.description,
                Amount = t.amount,
                Kind = t.kind
            });

        }

        // Sakārto rezultātu pēc datuma (drošībai, jo veidošana var mainīt secību)
        result = result.OrderByDescending(x => x.CreatedAt).ToList();

        return Ok(result);
    }
}
