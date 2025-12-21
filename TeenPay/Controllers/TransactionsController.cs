using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeenPay.Data;
using TeenPay.Models;

namespace TeenPay.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TransactionsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetMyTransactions()
    {
        // В твоём JWT нет "id". Поэтому берём NameIdentifier (или Sub).
        var userIdStr =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userIdStr) || !long.TryParse(userIdStr, out var meId))
            return Unauthorized();

        var meUsername = User.FindFirstValue(ClaimTypes.Name) ?? "me";

        // 1) Берём транзакции текущего юзера
        var my = await _db.Transactions
            .Where(t => t.userid == meId)
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

        if (my.Count == 0)
            return Ok(new List<TransactionDto>());

        // 2) Собираем нужные childIds / schoolIds
        var childIds = my.Where(x => x.childid != null).Select(x => x.childid!.Value).Distinct().ToList();
        var schoolIds = my.Where(x => x.schoolid != null).Select(x => x.schoolid!.Value).Distinct().ToList();

        // 3) Справочники: userId->username (для детей) и schoolId->name
        var childUsernames = await _db.Users
            .Where(u => childIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username);

        var schools = await _db.Schools
            .Where(s => schoolIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        // 4) Для TOPUP ребёнка и TOPUP школы ищем “парные” PAYMENT (чтобы узнать отправителя)
        //    (работает, если ты пишешь парой: PAYMENT(-X) и TOPUP(+X) почти в одно время)
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

        // справочник отправителей (userId) -> username (для тех, кто встречается в pairCandidates)
        var senderIds = pairCandidates.Select(x => x.userid).Distinct().ToList();
        var senderUsernames = await _db.Users
            .Where(u => senderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username);

        // 5) Собираем результат
        var result = new List<TransactionDto>(my.Count);

        foreach (var t in my)
        {
            string sender = "—";
            string receiver = "—";

            // A) Перевод ребёнку (в твоей схеме: у родителя PAYMENT -, у ребёнка TOPUP +)
            if (t.childid != null)
            {
                var childName = childUsernames.TryGetValue(t.childid.Value, out var cn) ? cn : $"child#{t.childid.Value}";

                if (t.kind == "PAYMENT" && t.amount < 0)
                {
                    // Родитель смотрит свою строку PAYMENT: No = родитель(me), Kam = ребёнок
                    sender = meUsername;
                    receiver = childName;
                }
                else if (t.kind == "TOPUP" && t.amount > 0)
                {
                    // Ребёнок смотрит TOPUP: No = родитель(ищем по парной PAYMENT), Kam = ребёнок(me)
                    // ищем PAYMENT: same child_id, amount = -topupAmount, created_at +/- 10 сек
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
                        sender = "parent";

                    receiver = meUsername; // ребёнок
                }
            }

            // B) Оплата в POS/школу (PAYMENT - у ребёнка, TOPUP + у школы/POS)
            if (t.schoolid != null)
            {
                var schoolName = schools.TryGetValue(t.schoolid.Value, out var s) ? s : $"school#{t.schoolid.Value}";

                if (t.kind == "PAYMENT" && t.amount < 0)
                {
                    // Ребёнок платит: No=ребёнок(me), Kam=школа
                    sender = meUsername;
                    receiver = schoolName;
                }
                else if (t.kind == "TOPUP" && t.amount > 0)
                {
                    // POS/школа получает: No=кто заплатил (ищем парный PAYMENT), Kam=школа
                    var from = pairCandidates
                        .Where(p =>
                            p.schoolid == t.schoolid &&
                            p.amount == -t.amount &&
                            p.createdat >= t.createdat.AddSeconds(-10) &&
                            p.createdat <= t.createdat.AddSeconds(10))
                        .OrderByDescending(p => p.createdat)
                        .FirstOrDefault();

                    if (from != null && senderUsernames.TryGetValue(from.userid, out var sn))
                        sender = sn;
                    else
                        sender = "student";

                    receiver = schoolName;
                }
            }

            // если ничего не подошло — хотя бы No=me
            if (sender == "—" && receiver == "—")
                sender = meUsername;

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

        return Ok(result);
    }

    [HttpGet("children")]
    public async Task<ActionResult<List<TransactionDto>>> GetChildrenTransactions()
    {
        // кто залогинен
        var userIdStr =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userIdStr) || !long.TryParse(userIdStr, out var parentId))
            return Unauthorized();

        // ✅ 1) получить ids детей этого родителя
        var childIds = await _db.ParentChildren
            .Where(pc => pc.ParentUserId == (int)parentId)
            .Select(pc => pc.ChildUserId)
            .Distinct()
            .ToListAsync();

        if (childIds.Count == 0)
            return Ok(new List<TransactionDto>());

        // ✅ 2) получить транзакции детей
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

        // ✅ 3) справочники username/schools как у тебя
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

        // ✅ 4) как и у тебя: ищем парные PAYMENT для TOPUP (чтобы отобразить sender)
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

        // ✅ 5) собираем DTO
        var result = new List<TransactionDto>(childTx.Count);

        foreach (var t in childTx)
        {
            var meUsername = userNames.TryGetValue(t.userid, out var meU) ? meU : $"user#{t.userid}";

            string sender = "—";
            string receiver = "—";

            // A) Родитель -> ребёнок: у ребёнка TOPUP(+)
            if (t.kind == "TOPUP" && t.amount > 0)
            {
                // получатель: ребёнок (t.userid)
                receiver = meUsername;

                // отправитель: ищем парный PAYMENT(-X) по childid
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
                        sender = "parent";
                }
                else
                {
                    sender = "parent";
                }
            }

            // B) Ребёнок платит школе: PAYMENT(-)
            if (t.kind == "PAYMENT" && t.amount < 0 && t.schoolid != null)
            {
                sender = meUsername;
                receiver = schools.TryGetValue(t.schoolid.Value, out var s) ? s : $"school#{t.schoolid.Value}";
            }

            // C) Остальные случаи — хотя бы покажем, кто сделал запись
            if (sender == "—" && receiver == "—")
            {
                var tt = schools.Where(s => s.Key == t.schoolid).FirstOrDefault();
                
                sender = meUsername;
                receiver = tt.Value;
            }

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

        // общий порядок
        result = result.OrderByDescending(x => x.CreatedAt).ToList();

        return Ok(result);
    }
}
