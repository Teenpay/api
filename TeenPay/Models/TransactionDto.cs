namespace TeenPay.Models;

public record CreateTopUpRequestDto(long ChildId, string? Note);
public record ApproveTopUpRequestDto(decimal Amount);
public record TopUpChildDto(int ChildId, decimal Amount);
public class TransactionDto
{
    public long Id { get; set; }
    public string? SenderUsername { get; set; }      // No
    public string? ReceiverUsername { get; set; }    // Kam
    public string? Description { get; set; }
    public string Kind { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}


