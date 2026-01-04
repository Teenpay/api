using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TeenPay.Models
{
    // =====================================================
    // TeenpayUserT — vienkāršots lietotāja modelis (testiem / demo / pagaidu datiem)
    // =====================================================
    // Šī klase izskatās kā "vieglā" lietotāja versija, ko var izmantot:
    // - vienkāršiem piemēriem,
    // - testēšanai,
    // - datu ievadei bez pilnas TeenpayUser struktūras.
    // (Reālajā sistēmā galvenais modelis ir TeenpayUser, kas ir piesaistīts DB tabulai.)
    public class TeenpayUserT
    {
       
        public int Id { get; set; }

     
        public string? Name { get; set; }

      
        public string? Surname { get; set; }

        public int? Age { get; set; }

     
        public string? Child { get; set; }

    
        public float? Balance { get; set; }
    }

    // =====================================================
    // TeenpayID — vienkāršs DTO identifikatora nodošanai/procedūrām
    // =====================================================
    // Šo objektu var izmantot situācijās, kad jānosūta tikai ID:
    // - DB procedūrai,
    // - API atbildē,
    // - vienkāršiem testiem.
    public class TeenpayID
    {
        public int? ID { get; set; }
    }

    // =====================================================
    // Auth DTO (datu pārvades objekti autentifikācijas modulim)
    // =====================================================
    // Šie ieraksti (record) definē API pieprasījumu/atbilžu struktūras.
    // Tos izmanto AuthController darbībās: reģistrācija, ielogošanās, refresh, paroles atjaunošana u.c.

    // Lietotāja reģistrācijas dati (lietotājvārds/parole obligāti, pārējais — pēc izvēles)
    public record RegisterDto(string Username, string Password, string? Email, string? FirstName, string? LastName);

    // Ielogošanās dati (DeviceId izmanto refresh-token piesaistei konkrētai ierīcei)
    public record LoginDto(string Username, string Password, string? DeviceId);

    // Refresh token atjaunošanas dati (ar DeviceId, lai pārbaudītu "Device mismatch")
    public record RefreshDto(string RefreshToken, string? DeviceId);

    // Minimālie profila dati, ko atgriezt klientam (piem., /api/user/me)
    public record UserDto(int Id, string Username, string? Email, string? FirstName, string? LastName);

    // Dati paroles atjaunošanas pieprasījumam (piemēram, pārbaude pēc lomas un telefona)
    public record ForgotPasswordDto(string Username, string Role, string Phone);

    // Dev/test endpoints: paroles uzstādīšana tieši (izmantojams izstrādei)
    public record DevSetPasswordDto(string Username, string NewPassword);

    // =====================================================
    // Payment DTO (datu pārvades objekti maksājumu scenārijiem)
    // =====================================================
    // Šis DTO apraksta maksājuma izveidi (alternatīvs ceļš maksājumiem),
    // kur:
    // - ChildUserId — bērns (maksātājs),
    // - MerchantUserId — saņēmējs (tirgotājs/POS),
    // - Amount — maksājuma summa.
    public record CreatePaymentDto(
        int ChildUserId,
        int MerchantUserId,
        decimal Amount
    );

    // =====================================================
    // RejectDto — atteikuma iemesla nodošana
    // =====================================================
    // Lietojams, ja nepieciešams noraidīt pieprasījumu ar iemeslu
    // (piem., top-up pieprasījuma noraidīšana vai cits biznesa process).
    public record RejectDto(string Reason);
}
