using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TeenPay.Models;


public class ReceiptItem
{
    public long Id { get; set; }
    public string ReceiptNo { get; set; } = "";      // 8 digits
    public decimal Amount { get; set; }              // always + in receipt
    public string Kind { get; set; } = "";           // SCHOOL_PAYMENT / PARENT_TOPUP

    public int PayerUserId { get; set; }
    public int PayeeUserId { get; set; }

    public long? SchoolId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[Table("receipts", Schema = "teenpay")]
public class Receipt
{
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

