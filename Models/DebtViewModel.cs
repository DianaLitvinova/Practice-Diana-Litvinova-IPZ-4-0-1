using Microsoft.AspNetCore.Mvc;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class DebtViewModel
    {
        public int FamilyId { get; set; }
        public string FamilyName { get; set; }
        public decimal Amount { get; set; }
        public DateTime? LastPaymentDate { get; set; }
    }
}
