using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Orders;
using Nop.Services.Tax;

namespace Nop.Plugin.Payments.PayPalStandard.Services
{
    public partial class PayPalStandardPaymentService
    {
        #region Fields

        private readonly PaymentSettings _paymentSettings;
        private readonly CurrencySettings _currencySettings;
        private readonly IAddressService _addressService;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOrderService _orderService;
        private readonly IProductService _productService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly PayPalStandardPaymentSettings _payPalStandardPaymentSettings;

        #endregion

        #region Ctor

        public PayPalStandardPaymentService(
            PaymentSettings paymentSettings,
            CurrencySettings currencySettings,
            IAddressService addressService,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICountryService countryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            IOrderService orderService,
            IProductService productService,
            IStateProvinceService stateProvinceService,
            ITaxService taxService,
            IWebHelper webHelper,
            PayPalStandardPaymentSettings payPalStandardPaymentSettings)
        {
            _paymentSettings = paymentSettings;
            _currencySettings = currencySettings;
            _addressService = addressService;
            _checkoutAttributeParser = checkoutAttributeParser;
            _countryService = countryService;
            _currencyService = currencyService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _httpContextAccessor = httpContextAccessor;
            _orderService = orderService;
            _productService = productService;
            _stateProvinceService = stateProvinceService;
            _taxService = taxService;
            _webHelper = webHelper;
            _payPalStandardPaymentSettings = payPalStandardPaymentSettings;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public virtual Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            if (!_paymentSettings.AllowRePostingPayments)
            {
                return Task.FromResult(false);
            }

            if (order.Deleted)
            {
                return Task.FromResult(false);  //do not allow for deleted orders
            }

            if (order.OrderStatus == OrderStatus.Cancelled)
            {
                return Task.FromResult(false);  //do not allow for cancelled orders
            }

            if (order.PaymentStatus != PaymentStatus.Pending)
            {
                return Task.FromResult(false);  //payment status should be Pending
            }

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual async Task PostProcessPaymentAsync(Order order)
        {
            //already paid or order.OrderTotal == decimal.Zero
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                return;
            }

            var baseUrl = _payPalStandardPaymentSettings.UseSandbox ?
                "https://www.sandbox.paypal.com/us/cgi-bin/webscr" :
                "https://www.paypal.com/us/cgi-bin/webscr";

            //create common query parameters for the request
            var queryParameters = await CreateQueryParametersAsync(order);

            //whether to include order items in a transaction
            if (_payPalStandardPaymentSettings.PassProductNamesAndTotals)
            {
                //add order items query parameters to the request
                var parameters = new Dictionary<string, string>(queryParameters);
                await AddItemsParametersAsync(parameters, order);

                //remove null values from parameters
                parameters = parameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                    .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

                //ensure redirect URL doesn't exceed 2K chars to avoid "too long URL" exception
                var redirectUrl = QueryHelpers.AddQueryString(baseUrl, parameters);
                if (redirectUrl.Length <= 2048)
                {
                    _httpContextAccessor.HttpContext.Response.Redirect(redirectUrl);
                    return;
                }
            }

            //or add only an order total query parameters to the request
            await AddOrderTotalParametersAsync(queryParameters, order);

            //remove null values from parameters
            queryParameters = queryParameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

            var url = QueryHelpers.AddQueryString(baseUrl, queryParameters);
            _httpContextAccessor.HttpContext.Response.Redirect(url);
        }
        
        #endregion

        #region Utilities

        /// <summary>
        /// Create common query parameters for the request
        /// </summary>
        /// <param name="Order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the created query parameters
        /// </returns>
        private async Task<IDictionary<string, string>> CreateQueryParametersAsync(Order order)
        {
            //get store location
            var storeLocation = _webHelper.GetStoreLocation();

            //choosing correct order address
            var orderAddress = await _addressService.GetAddressByIdAsync(
                (order.PickupInStore ? order.PickupAddressId : order.ShippingAddressId) ?? 0);

            //create query parameters
            return new Dictionary<string, string>
            {
                //PayPal ID or an email address associated with your PayPal account
                ["business"] = _payPalStandardPaymentSettings.BusinessEmail,

                //the character set and character encoding
                ["charset"] = "utf-8",

                //set return method to "2" (the customer redirected to the return URL by using the POST method, and all payment variables are included)
                ["rm"] = "2",

                ["bn"] = PayPalHelper.NopCommercePartnerCode,
                ["currency_code"] = (await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId))?.CurrencyCode,

                //order identifier
                ["invoice"] = order.CustomOrderNumber,
                ["custom"] = order.OrderGuid.ToString(),

                //PDT, IPN and cancel URL
                ["return"] = $"{storeLocation}Plugins/PaymentPayPalStandard/PDTHandler",
                ["notify_url"] = $"{storeLocation}Plugins/PaymentPayPalStandard/IPNHandler",
                ["cancel_return"] = $"{storeLocation}Plugins/PaymentPayPalStandard/CancelOrder",

                //shipping address, if exists
                ["no_shipping"] = order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "1" : "2",
                ["address_override"] = order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "0" : "1",
                ["first_name"] = orderAddress?.FirstName,
                ["last_name"] = orderAddress?.LastName,
                ["address1"] = orderAddress?.Address1,
                ["address2"] = orderAddress?.Address2,
                ["city"] = orderAddress?.City,
                ["state"] = (await _stateProvinceService.GetStateProvinceByAddressAsync(orderAddress))?.Abbreviation,
                ["country"] = (await _countryService.GetCountryByAddressAsync(orderAddress))?.TwoLetterIsoCode,
                ["zip"] = orderAddress?.ZipPostalCode,
                ["email"] = orderAddress?.Email
            };
        }

        /// <summary>
        /// Add order items to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="order">Order</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        private async Task AddItemsParametersAsync(IDictionary<string, string> parameters, Order order)
        {
            //upload order items
            parameters.Add("cmd", "_cart");
            parameters.Add("upload", "1");

            var cartTotal = decimal.Zero;
            var roundedCartTotal = decimal.Zero;
            var itemCount = 1;

            //add shopping cart items
            foreach (var item in await _orderService.GetOrderItemsAsync(order.Id))
            {
                var roundedItemPrice = Math.Round(item.UnitPriceExclTax, 2);

                var product = await _productService.GetProductByIdAsync(item.ProductId);

                //add query parameters
                parameters.Add($"item_name_{itemCount}", product.Name);
                parameters.Add($"amount_{itemCount}", roundedItemPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", item.Quantity.ToString());

                cartTotal += item.PriceExclTax;
                roundedCartTotal += roundedItemPrice * item.Quantity;
                itemCount++;
            }

            //add checkout attributes as order items
            var checkoutAttributeValues = _checkoutAttributeParser.ParseCheckoutAttributeValues(order.CheckoutAttributesXml);
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

            await foreach (var (attribute, values) in checkoutAttributeValues)
            {
                await foreach (var attributeValue in values)
                {
                    var (attributePrice, _) = await _taxService.GetCheckoutAttributePriceAsync(attribute, attributeValue, false, customer);
                    var roundedAttributePrice = Math.Round(attributePrice, 2);

                    //add query parameters
                    if (attribute == null)
                        continue;

                    parameters.Add($"item_name_{itemCount}", attribute.Name);
                    parameters.Add($"amount_{itemCount}", roundedAttributePrice.ToString("0.00", CultureInfo.InvariantCulture));
                    parameters.Add($"quantity_{itemCount}", "1");

                    cartTotal += attributePrice;
                    roundedCartTotal += roundedAttributePrice;
                    itemCount++;
                }
            }

            //add shipping fee as a separate order item, if it has price
            var roundedShippingPrice = Math.Round(order.OrderShippingExclTax, 2);
            if (roundedShippingPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Shipping fee");
                parameters.Add($"amount_{itemCount}", roundedShippingPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += order.OrderShippingExclTax;
                roundedCartTotal += roundedShippingPrice;
                itemCount++;
            }

            //add payment method additional fee as a separate order item, if it has price
            var roundedPaymentMethodPrice = Math.Round(order.PaymentMethodAdditionalFeeExclTax, 2);
            if (roundedPaymentMethodPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Payment method fee");
                parameters.Add($"amount_{itemCount}", roundedPaymentMethodPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += order.PaymentMethodAdditionalFeeExclTax;
                roundedCartTotal += roundedPaymentMethodPrice;
                itemCount++;
            }

            //add tax as a separate order item, if it has positive amount
            var roundedTaxAmount = Math.Round(order.OrderTax, 2);
            if (roundedTaxAmount > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Tax amount");
                parameters.Add($"amount_{itemCount}", roundedTaxAmount.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += order.OrderTax;
                roundedCartTotal += roundedTaxAmount;
            }

            if (cartTotal > order.OrderTotal)
            {
                //get the difference between what the order total is and what it should be and use that as the "discount"
                var discountTotal = Math.Round(cartTotal - order.OrderTotal, 2);
                roundedCartTotal -= discountTotal;

                //gift card or rewarded point amount applied to cart in nopCommerce - shows in PayPal as "discount"
                parameters.Add("discount_amount_cart", discountTotal.ToString("0.00", CultureInfo.InvariantCulture));
            }

            //save order total that actually sent to PayPal (used for PDT order total validation)
            await _genericAttributeService.SaveAttributeAsync(order, PayPalHelper.OrderTotalSentToPayPal, roundedCartTotal);
        }

        /// <summary>
        /// Add order total to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="order">Order</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        private async Task AddOrderTotalParametersAsync(IDictionary<string, string> parameters, Order order)
        {
            //round order total
            var roundedOrderTotal = Math.Round(order.OrderTotal, 2);

            parameters.Add("cmd", "_xclick");
            parameters.Add("item_name", $"Order Number {order.CustomOrderNumber}");
            parameters.Add("amount", roundedOrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

            //save order total that actually sent to PayPal (used for PDT order total validation)
            await _genericAttributeService.SaveAttributeAsync(order, PayPalHelper.OrderTotalSentToPayPal, roundedOrderTotal);
        }

        #endregion
    }
}
