using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TeenPay.Models
{
    // Transakcijas entītija: glabā naudas kustības ierakstu sistēmā (Top-up, maksājums u.c.)
    public class Transaction
    {
        public int id { get; set; }

        public int userid { get; set; }

        public decimal amount { get; set; }

        public string kind { get; set; } = "";

        public string? description { get; set; }

        public DateTime createdat { get; set; }

        public int? childid { get; set; }

        public int? schoolid { get; set; }
    }

}
