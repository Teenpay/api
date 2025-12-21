using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;


namespace TeenPay_App.Models;

public class QrUserInfo
{
    public string FullName { get; set; } = "";
    public string Code { get; set; } = "";
    public string personal_code { get; set; } = "";
}

public class QrPaymentPayload
{
    public int UserId { get; set; }

    // ДОЛЖНО БЫТЬ nullable, потому что сначала null в QR
    public decimal? Amount { get; set; }

    // тоже nullable, потому что сначала null в QR
    public string? OrgCode { get; set; }

    public QrUserInfo User { get; set; } = new();
}