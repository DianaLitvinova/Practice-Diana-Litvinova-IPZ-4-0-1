using Microsoft.AspNetCore.Mvc;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class InvoiceExpenseReportViewModel
    {
        public int InvoiceId { get; set; }
        public DateTime Date { get; set; }
        public string CookName { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public string ProductsList { get; set; }  // Добавлено новое поле
    }
}
