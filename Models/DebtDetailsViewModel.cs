using Microsoft.AspNetCore.Mvc;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class DebtDetailsViewModel
    {
        public List<ChildDebtInfo> Children { get; set; }
        public List<PaymentInfo> RecentPayments { get; set; }
        public decimal TotalDebt { get; set; }
    }

    public class ChildDebtInfo
    {
        public string Name { get; set; }
        public string GroupName { get; set; }
        public decimal MonthlyFee { get; set; }
    }

    public class PaymentInfo
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
    }
}
