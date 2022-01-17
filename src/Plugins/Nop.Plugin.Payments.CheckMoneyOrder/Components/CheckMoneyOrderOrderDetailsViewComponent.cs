using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.CheckMoneyOrder.Models;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.CheckMoneyOrder.Components
{
    public class CheckMoneyOrderOrderDetailsViewComponent : NopViewComponent
    {
        private readonly IOrderService _orderService;
        private readonly CheckMoneyOrderPaymentSettings _checkMoneyOrderPaymentSettings;
        private readonly ILocalizationService _localizationService;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;

        public CheckMoneyOrderOrderDetailsViewComponent(
            IOrderService orderService,
            CheckMoneyOrderPaymentSettings checkMoneyOrderPaymentSettings,
            ILocalizationService localizationService,
            IStoreContext storeContext,
            IWorkContext workContext)
        {
            _orderService = orderService;
            _checkMoneyOrderPaymentSettings = checkMoneyOrderPaymentSettings;
            _localizationService = localizationService;
            _storeContext = storeContext;
            _workContext = workContext;
        }

        public async Task<IViewComponentResult> InvokeAsync(int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);

            if (order.OrderStatus != OrderStatus.Pending)
            {
                return Content(string.Empty);
            }

            var store = await _storeContext.GetCurrentStoreAsync();

            var model = new OrderDetailsModel
            {
                OrderId = orderId,
                CustomOrderNumber = order.CustomOrderNumber,
                DescriptionText = await _localizationService.GetLocalizedSettingAsync(_checkMoneyOrderPaymentSettings,
                    x => x.DescriptionText, (await _workContext.GetWorkingLanguageAsync()).Id, store.Id)
            };

            return View("~/Plugins/Payments.CheckMoneyOrder/Views/OrderDetails.cshtml", model);
        }
    }
}
