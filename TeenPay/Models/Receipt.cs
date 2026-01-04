using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TeenPay.Models;

// =====================================================
// ReceiptItem — palīgmodelis (DTO / vienkāršots ieraksts)
// =====================================================
// Šī klase tiek izmantota kā vienkāršota čeka vienība,
// piemēram, atgriežot datus uz klienta aplikāciju vai
// izmantojot starp-apstrādi, kad nav vajadzīgi EF atribūti.
//
// Atšķirība no Receipt:
// - Receipt ir piesaistīts DB tabulai (Entity Framework modelis)
// - ReceiptItem var būt ērts kā "vienkāršs objekts" apmaiņai / attēlošanai
public class ReceiptItem
{
    public long Id { get; set; }

    public string ReceiptNo { get; set; } = "";      

    public decimal Amount { get; set; }              

    public string Kind { get; set; } = "";           

    public int PayerUserId { get; set; }

    public int PayeeUserId { get; set; }

    public long? SchoolId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

// =====================================================
// Receipt — DB entītija (EF Core), tabula teenpay.receipts
// =====================================================
// Šī klase ir Entity Framework modelis, kas atbilst tabulai "receipts".
// Glabā čeka informāciju par darījumiem:
// - maksājumi skolai (QR payment)
// - top-up pārskaitījumi starp vecāku un bērnu
// - citi darījumu veidi (ja tiek paplašināts)
[Table("receipts", Schema = "teenpay")]
public class Receipt
{
    // Primārā atslēga tabulā receipts
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("receipt_no")]
    public string ReceiptNo { get; set; } = ""; // 8 цифр

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("kind")]
    public string Kind { get; set; } = ""; // PAYMENT / TOPUP и т.д.


    [Column("payer_user_id")]
    public int PayerUserId { get; set; } // кто платит

    [Column("payee_user_id")]
    public int PayeeUserId { get; set; } // кто получает

    [Column("school_id")]
    public long? SchoolId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
