using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TeenPay.Models
{
    public class Transaction
    {
        public int id { get; set; }
        public int userid { get; set; }

        public decimal amount { get; set; }      // + пополнение, - списание
        public string kind { get; set; } = "";   // TOPUP / PAYMENT и т.п.
        public string? description { get; set; }

        public DateTime createdat { get; set; }
        public int? childid { get; set; }

        public int? schoolid { get; set; }
        // если есть в БД
    }
}
