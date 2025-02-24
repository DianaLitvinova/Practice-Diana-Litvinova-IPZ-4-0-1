namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class EmployeeViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Post { get; set; }
        public decimal Salary { get; set; }
        public DateTime? DateStart { get; set; }
        public DateTime? DateFinal { get; set; }

        public string Username { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
}