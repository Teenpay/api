using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace TeenPay_App.Models;

// ==========================================================
// QR modeļi (mobilā lietotne)
// Šīs klases apraksta datus, kas tiek kodēti QR kodā un/vai
// tiek pārsūtīti starp MAUI lietotni un serveri maksājuma laikā.
// ==========================================================


// ==========================================================
// QrUserInfo — lietotāja identifikācijas informācija QR kontekstā
// Funkcija: satur minimālos profila datus, kurus var parādīt UI
// (piem., POS ekrānā vai bērna profilā) pēc QR nolasīšanas.
// ==========================================================
public class QrUserInfo
{

    public string FullName { get; set; } = "";

    public string Code { get; set; } = "";

    public string personal_code { get; set; } = "";
}


// ==========================================================
// QrPaymentPayload — maksājuma dati QR kodā
// Funkcija: objekts, kas tiek serializēts QR kodā un pēc tam
// nolasīts POS pusē, lai izpildītu maksājumu (PayByQr).
// ==========================================================
public class QrPaymentPayload
{
    // Bērna (maksātāja) lietotāja ID sistēmā
    public int UserId { get; set; }

    public decimal? Amount { get; set; }

    public string? OrgCode { get; set; }

    public QrUserInfo User { get; set; } = new();
}
