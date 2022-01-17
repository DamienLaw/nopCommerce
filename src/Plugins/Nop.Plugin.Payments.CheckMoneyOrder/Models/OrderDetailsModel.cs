namespace Nop.Plugin.Payments.CheckMoneyOrder.Models
{
    public class OrderDetailsModel
    {
        public int OrderId { get; set; }

        public string CustomOrderNumber { get; set; }

        public string DescriptionText { get; set; }
    }
}
