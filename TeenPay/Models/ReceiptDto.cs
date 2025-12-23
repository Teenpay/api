namespace TeenPay.Models
{
    public class ReceiptDto
    {
        public long Id { get; set; }
        public string ReceiptNo { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public decimal Amount { get; set; }        // всегда +
        public decimal SignedAmount { get; set; }  // для UI (+/-)
        public string Direction { get; set; } = ""; // IN/OUT

        public string FromName { get; set; } = "";
        public string ToName { get; set; } = "";
        public string? SchoolName { get; set; }
    }
}
