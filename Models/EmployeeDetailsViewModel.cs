namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class EmployeeDetailsViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string CurrentPost { get; set; }
        public decimal CurrentSalary { get; set; }
        public DateTime? DateStart { get; set; }
        public DateTime? DateFinal { get; set; }
        public List<EmployeePostHistory> PostHistory { get; set; }
        public List<BonusAndFineRecord> BonusAndFines { get; set; }
    }
}
