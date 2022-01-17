using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.PayPalStandard.Components
{
    public class PaymentInfoViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.PayPalStandard/Views/PaymentInfo.cshtml");
        }
    }
}
