using Microsoft.AspNetCore.Mvc;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class ParentPaymentReportViewModel
    {
        public string FamilyName { get; set; }
        public decimal ExpectedAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal Debt  { get; set; }
    }
}
