using System.ComponentModel.DataAnnotations.Schema;
namespace TeenPay.Models;

// =====================================================
// TopUpRequest — papildināšanas pieprasījuma (Top-up) entītija
// =====================================================
// Šī klase ir EF Core entītija, kas tiek glabāta datubāzes tabulā "teenpay.topup_requests".
// To izmanto TopUpRequestsController, lai:
// - bērns varētu izveidot naudas pieprasījumu (Create),
// - vecāks varētu redzēt ienākošos pieprasījumus (Inbox),
// - vecāks varētu apstiprināt pieprasījumu un pārskaitīt naudu bērnam (Approve).
[Table("topup_requests", Schema = "teenpay")]
public class TopUpRequest
{
   
    [Column("id")]
    public long Id { get; set; }

   
    [Column("child_id")]
    public long ChildId { get; set; }

   
    [Column("parent_id")]
    public long ParentId { get; set; }

    // Pieprasījuma statuss (piem., PENDING / APPROVED)
    // Pēc noklusējuma tiek izveidots kā "PENDING"
    [Column("status")]
    public string Status { get; set; } = "PENDING";

    
    [Column("requested_at")]
    public DateTimeOffset RequestedAt { get; set; }

    
    [Column("approved_at")]
    public DateTimeOffset? ApprovedAt { get; set; }

  
    [Column("note")]
    public string? Note { get; set; }
}
