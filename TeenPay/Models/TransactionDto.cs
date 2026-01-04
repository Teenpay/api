namespace TeenPay.Models;

// DTO: bērns izveido naudas papildināšanas pieprasījumu (Top-up request)
public record CreateTopUpRequestDto(long ChildId, string? Note);

// DTO: vecāks apstiprina pieprasījumu un norāda summu
public record ApproveTopUpRequestDto(decimal Amount);

// DTO: tiešs pārskaitījums no vecāka bērnam (bērna ID + summa)
public record TopUpChildDto(int ChildId, decimal Amount);

// DTO: transakcijas attēlošanai lietotnē (sūtītājs/saņēmējs, tips, summa, datums)
public class TransactionDto
{
    public long Id { get; set; }
    public string? SenderUsername { get; set; }      // No
    public string? ReceiverUsername { get; set; }    // Kam
    public string? Description { get; set; }         // Paskaidrojums par darījumu
    public string Kind { get; set; } = "";           // Darījuma veids (piem., PAYMENT, TOPUP)
    public decimal Amount { get; set; }              // Darījuma summa
    public DateTime CreatedAt { get; set; }          // Izveides laiks
}
