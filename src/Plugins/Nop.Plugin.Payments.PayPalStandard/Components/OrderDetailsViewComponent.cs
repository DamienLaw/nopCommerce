using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Payments.PayPalStandard.Models;
using Nop.Plugin.Payments.PayPalStandard.Services;
using Nop.Services.Orders;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.PayPalStandard.Components
{
    public class OrderDetailsViewComponent : NopViewComponent
    {        
        private readonly IOrderService _orderService;
        private readonly PayPalStandardPaymentService _paymentService;

        public OrderDetailsViewComponent(
            IOrderService orderService,
            PayPalStandardPaymentService paymentService)
        {
            _orderService = orderService;
            _paymentService = paymentService;
        }

        public async Task<IViewComponentResult> InvokeAsync(int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (!await _paymentService.CanRePostProcessPaymentAsync(order))
            {
                return Content(string.Empty);
            }

            var model = new PaymentRePostModel
            {
                OrderId = orderId
            };

            return View("~/Plugins/Payments.PayPalStandard/Views/OrderDetails.cshtml", model);
        }
    }
}
