namespace TeenPay.Models;

// ==========================================================
// ParentChild — saistības (link) modelis starp Vecāku un Bērnu
// Funkcija: glabā attiecību "Vecāks ↔ Bērns" sistēmā, lai:
//  - vecāks varētu redzēt bērna informāciju (bilance, darījumi u. c.)
//  - vecāks varētu apstiprināt TopUp pieprasījumus un veikt pārskaitījumus
//  - sistēma varētu pārbaudīt, vai konkrēts bērns ir piesaistīts konkrētam vecākam
// ==========================================================
public class ParentChild
{
    public long Id { get; set; }

    public int ParentUserId { get; set; }

    public int ChildUserId { get; set; }
}

