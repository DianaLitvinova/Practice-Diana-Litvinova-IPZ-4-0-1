namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class InvoiceProductViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string Measure { get; set; }
        public decimal Amount { get; set; }
        public decimal? ActualAmount { get; set; }
        public decimal? Cost { get; set; }
    }
}