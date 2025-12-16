using System.ComponentModel.DataAnnotations.Schema;
namespace TeenPay.Models;

[Table("topup_requests", Schema = "teenpay")]
public class TopUpRequest
{
    [Column("id")]
    public long Id { get; set; }

    [Column("child_id")]
    public long ChildId { get; set; }

    [Column("parent_id")]
    public long ParentId { get; set; }

    [Column("status")]
    public string Status { get; set; } = "PENDING";

    [Column("requested_at")]
    public DateTimeOffset RequestedAt { get; set; }

    [Column("approved_at")]
    public DateTimeOffset? ApprovedAt { get; set; }

    [Column("note")]
    public string? Note { get; set; }
}