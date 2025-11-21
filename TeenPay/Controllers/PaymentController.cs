using Microsoft.AspNetCore.Mvc;

namespace TeenPay.Controllers
{
    public class PaymentController : Controller
    {
        [HttpGet("paymentrequest")] //metod kotorij ukazivajet summu i vitaskivajet jejo iz bazi
        public IActionResult Index()
        {
            
            return View();
        }
    }
}
