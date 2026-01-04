using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TeenPay.Models
{
    // =====================================================
    // School — skolas entītija (biznesa modelis / EF entītija)
    // =====================================================
    // Šī klase apraksta skolu sistēmā:
    // - skolas nosaukums un adrese,
    // - skolas identifikācijas kods (OrgCode), ko izmanto QR maksājumos,
    // - piesaistītais POS darbinieks (lietotājs), kurš drīkst pieņemt maksājumus šai skolai.
    public class School
    {
        // Skolas unikālais identifikators
        public int Id { get; set; }

        // Skolas nosaukums (piem., "Rīgas 1. vidusskola")
        public string Name { get; set; } = "";

        // Pilsēta (nav obligāts lauks)
        public string? City { get; set; }

        // Adrese (nav obligāts lauks)
        public string? Address { get; set; }

        // Skolas kods (OrgCode), ko izmanto, lai noteiktu skolu QR maksājumā
        public string? code { get; set; }

        // POS darbinieka lietotāja ID, kas ir piesaistīts šai skolai
        // (izmanto, lai pārbaudītu "school_mismatch" maksājuma laikā)
        public int? PosUserId { get; set; }

        // Navigācijas īpašība uz POS lietotāju (ja sistēmā definētas attiecības)
        public TeenpayUser? PosUser { get; set; }
    }


    // =====================================================
    // School_codes — alternatīvs skolas apraksts ar papildus kodiem
    // =====================================================
    // Šī klase izmanto EF anotācijas un ir piesaistīta DB tabulai "schools".
    // Tajā ir papildus lauki:
    // - Code (skolas kods),
    // - PaymentCode (maksājuma kods vai papildus identifikators maksājumiem),
    // - PosUserId (piesaistītais POS darbinieks).
    //
    // Piezīme: šobrīd ir gan School, gan School_codes, un abas norāda uz "schools" tabulu.
    // Dokumentācijā vari pieminēt, ka viens modelis tiek izmantots vienkāršākai loģikai,
    // bet otrs — darbībām, kur nepieciešami papildus kodi.
    [Table("schools", Schema = "teenpay")]
    public class School_codes
    {
        // Primārā atslēga tabulā "schools"
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // Skolas kods (OrgCode)
        [Column("code")]
        public string Code { get; set; } = null!;

        // Skolas nosaukums
        [Column("name")]
        public string Name { get; set; } = null!;

        // Papildus maksājuma kods (var izmantot maksājumu identifikācijai vai integrācijai)
        [Column("payment_code")]
        public string PaymentCode { get; set; } = null!;

        // Piesaistītais POS darbinieks
        [Column("pos_user_id")]
        public int? PosUserId { get; set; } = null!;
    }

    // =====================================================
    // StudentSchool — saite "bērns ↔ skola" (many-to-many / sasaistes tabula)
    // =====================================================
    // Šī tabula glabā informāciju par to, kurš lietotājs (parasti bērns) ir piesaistīts kurai skolai.
    // Maksājuma laikā (PayByQr) tiek pārbaudīts, vai bērns ir piesaistīts skolai:
    // - ja nav, sistēma atgriež kļūdu "not_linked_to_school".
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
