using Microsoft.AspNetCore.Mvc;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class SalaryReportViewModel
    {
        public string EmployeeName { get; set; }
        public string Position { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal BonusesAndFines { get; set; }
        public decimal TotalSalary { get; set; }
    }
}
