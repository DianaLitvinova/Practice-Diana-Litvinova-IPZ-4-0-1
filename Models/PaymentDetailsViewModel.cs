using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class PaymentDetailsViewModel
    {
        public DateTime Date { get; set; }
        public string FamilyName { get; set; }
        public decimal Amount { get; set; }
        public string HeadTeacherName { get; set; }
        public string ChildrenNames { get; set; }
    }

}
