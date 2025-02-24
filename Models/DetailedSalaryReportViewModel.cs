namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class DetailedSalaryReportViewModel
    {
        public string EmployeeName { get; set; }
        public string Position { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal Bonuses { get; set; }
        public decimal Fines { get; set; }
        public decimal TotalSalary { get; set; }
        public string Period { get; set; }
    }
}
