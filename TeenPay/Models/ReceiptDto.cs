namespace TeenPay.Models
{
    // =====================================================
    // ReceiptDto — datu pārraides objekts (DTO) čeku attēlošanai
    // =====================================================
    // Šī klase tiek izmantota, lai nosūtītu čeka informāciju uz klienta pusi (piem., MAUI lietotni).
    // Atšķirībā no Receipt (DB entītijas), ReceiptDto ir pielāgots UI vajadzībām:
    // - satur papildus laukus (SignedAmount, Direction, FromName/ToName, SchoolName),
    // - ļauj viegli attēlot čeku sarakstu bez papildus aprēķiniem klienta pusē.
    public class ReceiptDto
    {
   
        public long Id { get; set; }

       
        public string ReceiptNo { get; set; } = "";

        // Darījuma veids (piem.: PAYMENT, INCOME, TOPUP_IN, TOPUP_OUT u.c.)
     
        public string Kind { get; set; } = "";
  
        public DateTime CreatedAt { get; set; }
 
        public decimal Amount { get; set; }   
 
        // + ja ienākošs darījums lietotājam, - ja izejošs
        public decimal SignedAmount { get; set; } 
   
        public string Direction { get; set; } = ""; 

        
        public string FromName { get; set; } = "";

      
        public string ToName { get; set; } = "";

        public string? SchoolName { get; set; }
    }
}
