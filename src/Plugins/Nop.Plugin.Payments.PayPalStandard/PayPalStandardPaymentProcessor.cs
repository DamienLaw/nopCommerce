using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.PayPalStandard.Components;
using Nop.Plugin.Payments.PayPalStandard.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;

namespace Nop.Plugin.Payments.PayPalStandard
{
    /// <summary>
    /// PayPalStandard payment processor
    /// </summary>
    public class PayPalStandardPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields
                       
        private readonly ILocalizationService _localizationService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly PayPalStandardHttpClient _payPalStandardHttpClient;
        private readonly PayPalStandardPaymentSettings _payPalStandardPaymentSettings;
        private readonly PayPalStandardPaymentService _paymentService;
        private readonly IOrderService _orderService;

        #endregion

        #region Ctor

        public PayPalStandardPaymentProcessor(
            ILocalizationService localizationService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService,
            IWebHelper webHelper,
            PayPalStandardHttpClient payPalStandardHttpClient,
            PayPalStandardPaymentSettings payPalStandardPaymentSettings,
            PayPalStandardPaymentService paymentService,
            IOrderService orderService)
        {            
            _localizationService = localizationService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _settingService = settingService;
            _webHelper = webHelper;
            _payPalStandardHttpClient = payPalStandardHttpClient;
            _payPalStandardPaymentSettings = payPalStandardPaymentSettings;
            _paymentService = paymentService;
            _orderService = orderService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Verifies IPN
        /// </summary>
        /// <param name="formString">Form string</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result, Values
        /// </returns>
        public async Task<(bool result, Dictionary<string, string> values)> VerifyIpnAsync(string formString)
        {
            var response = WebUtility.UrlDecode(await _payPalStandardHttpClient.VerifyIpnAsync(formString));
            var success = response.Trim().Equals("VERIFIED", StringComparison.OrdinalIgnoreCase);

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in formString.Split('&'))
            {
                var line = l.Trim();
                var equalPox = line.IndexOf('=');
                if (equalPox >= 0)
                    values.Add(line[0..equalPox], line[(equalPox + 1)..]);
            }

            return (success, values);
        }

        /// <summary>
        /// Gets PDT details
        /// </summary>
        /// <param name="tx">TX</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result, Values, Response
        /// </returns>
        public async Task<(bool result, Dictionary<string, string> values, string response)> GetPdtDetailsAsync(string tx)
        {
            var response = WebUtility.UrlDecode(await _payPalStandardHttpClient.GetPdtDetailsAsync(tx));

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool firstLine = true, success = false;
            foreach (var l in response.Split('\n'))
            {
                var line = l.Trim();
                if (firstLine)
                {
                    success = line.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);
                    firstLine = false;
                }
                else
                {
                    var equalPox = line.IndexOf('=');
                    if (equalPox >= 0)
                        values.Add(line[0..equalPox], line[(equalPox + 1)..]);
                }
            }

            return (success, values, response);
        }


        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var order = await _orderService.GetOrderByGuidAsync(processPaymentRequest.OrderGuid);

            await _paymentService.PostProcessPaymentAsync(order);

            // won't get here anyway because PostProcessPaymentAsync() would've redirected to PayPal website.
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the rue - hide; false - display.
        /// </returns>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the additional handling fee
        /// </returns>
        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
                _payPalStandardPaymentSettings.AdditionalFee, _payPalStandardPaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the capture payment result
        /// </returns>
        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            return Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }       

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of validating errors
        /// </returns>
        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the payment info holder
        /// </returns>
        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentPayPalStandard/Configure";
        }

        /// <summary>
        /// Gets the <see cref="Type"/> of the <see cref="ViewComponent"/> for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>The <see cref="Type"/> of the <see cref="ViewComponent"/>.</returns>
        public Type GetPaymentInfoViewComponentType() => typeof(PaymentInfoViewComponent);

        /// <summary>
        /// Gets the <see cref="Type"/> of the <see cref="ViewComponent"/> for displaying plugin in public store (during checkout completed)
        /// </summary>
        /// <returns>The <see cref="Type"/> of the <see cref="ViewComponent"/>.</returns>
        public Type GetCheckoutCompletedViewComponentType() => null;

        /// <summary>
        /// Gets the <see cref="Type"/> of the <see cref="ViewComponent"/> for displaying plugin in public store (in order details page)
        /// </summary>
        /// <returns>The <see cref="Type"/> of the <see cref="ViewComponent"/>.</returns>
        public Type GetOrderDetailsViewComponentType() => typeof(OrderDetailsViewComponent);

        /// <summary>
        /// Gets the <see cref="Type"/> of the <see cref="ViewComponent"/> for displaying plugin in admin backoffice (in order details edit page)
        /// </summary>
        /// <returns>The <see cref="Type"/> of the <see cref="ViewComponent"/>.</returns>
        public Type GetAdminOrderDetailsViewComponentType() => null;

        /// <summary>
        /// Install the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task InstallAsync()
        {
            //settings
            await _settingService.SaveSettingAsync(new PayPalStandardPaymentSettings
            {
                UseSandbox = true
            });

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.PayPalStandard.Fields.AdditionalFee"] = "Additional fee",
                ["Plugins.Payments.PayPalStandard.Fields.AdditionalFee.Hint"] = "Enter additional fee to charge your customers.",
                ["Plugins.Payments.PayPalStandard.Fields.AdditionalFeePercentage"] = "Additional fee. Use percentage",
                ["Plugins.Payments.PayPalStandard.Fields.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
                ["Plugins.Payments.PayPalStandard.Fields.BusinessEmail"] = "Business Email",
                ["Plugins.Payments.PayPalStandard.Fields.BusinessEmail.Hint"] = "Specify your PayPal business email.",
                ["Plugins.Payments.PayPalStandard.Fields.PassProductNamesAndTotals"] = "Pass product names and order totals to PayPal",
                ["Plugins.Payments.PayPalStandard.Fields.PassProductNamesAndTotals.Hint"] = "Check if product names and order totals should be passed to PayPal.",
                ["Plugins.Payments.PayPalStandard.Fields.PDTToken"] = "PDT Identity Token",
                ["Plugins.Payments.PayPalStandard.Fields.PDTToken.Hint"] = "Specify PDT identity token",
                ["Plugins.Payments.PayPalStandard.Fields.RedirectionTip"] = "You will be redirected to PayPal site to complete the order.",
                ["Plugins.Payments.PayPalStandard.Fields.UseSandbox"] = "Use Sandbox",
                ["Plugins.Payments.PayPalStandard.Fields.UseSandbox.Hint"] = "Check to enable Sandbox (testing environment).",
                ["Plugins.Payments.PayPalStandard.Instructions"] = @"
                    <p>
                        <b>If you're using this gateway ensure that your primary store currency is supported by PayPal.</b>
                        <br />
                        <br />To use PDT, you must activate PDT and Auto Return in your PayPal account profile. You must also acquire a PDT identity token, which is used in all PDT communication you send to PayPal. Follow these steps to configure your account for PDT:<br />
                        <br />1. Log in to your PayPal account (click <a href=""https://www.paypal.com/us/webapps/mpp/referral/paypal-business-account2?partner_id=9JJPJNNPQ7PZ8"" target=""_blank"">here</a> to create your account).
                        <br />2. Click on the Profile button.
                        <br />3. Click on the <b>Account Settings</b> link.
                        <br />4. Select the <b>Website payments</b> item on left panel.
                        <br />5. Find <b>Website Preferences</b> and click on the <b>Update</b> link.
                        <br />6. Under <b>Auto Return</b> for <b>Website payments preferences</b>, select the <b>On</b> radio button.
                        <br />7. For the <b>Return URL</b>, enter and save the URL on your site that will receive the transaction ID posted by PayPal after a customer payment (<em>{0}</em>).
                        <br />8. Under <b>Payment Data Transfer</b>, select the <b>On</b> radio button and get your <b>Identity token</b>.
                        <br />9. Enter <b>Identity token</b> in the field below on the plugin configuration page.
                        <br />10. Click <b>Save</b> button on this page.
                        <br />
                    </p>",
                ["Plugins.Payments.PayPalStandard.PaymentMethodDescription"] = "You will be redirected to PayPal site to complete the payment",
                ["Plugins.Payments.PayPalStandard.RoundingWarning"] = "It looks like you have \"ShoppingCartSettings.RoundPricesDuringCalculation\" setting disabled. Keep in mind that this can lead to a discrepancy of the order total amount, as PayPal only rounds to two decimals.",
                ["Plugins.Payments.PayPalStandard.Order.RetryPayment"] = "Retry payment",
                ["Plugins.Payments.PayPalStandard.Order.RetryPayment.Hint"] = "This order is not yet paid for. To pay now, click the \"Retry payment\" button."

            });

            await base.InstallAsync();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<PayPalStandardPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.PayPalStandard");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.PayPalStandard.PaymentMethodDescription");
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => false;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => false;

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => false;

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => false;

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => false;

        #endregion
    }
}