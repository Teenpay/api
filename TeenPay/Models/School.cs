using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TeenPay.Models
{
    public class School
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? City { get; set; }
        public string? Address { get; set; }
        public string? code { get; set; }
    }


    [Table("schools", Schema = "teenpay")]
    public class School_codes
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("code")]
        public string Code { get; set; } = null!;

        [Column("name")]
        public string Name { get; set; } = null!;

        [Column("payment_code")]
        public string PaymentCode { get; set; } = null!;
    }

    // связь ребёнок-школа
    [Table("student_schools", Schema = "teenpay")]
    public class StudentSchool
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("school_id")]
        public int SchoolId { get; set; }
    }
}
