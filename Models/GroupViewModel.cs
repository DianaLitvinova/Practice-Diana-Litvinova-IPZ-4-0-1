namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class GroupViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string TeacherName { get; set; }
        public int TeacherId { get; set; }
        public int Capacity { get; set; }
        public int CurrentCount { get; set; }
        public decimal CurrentPrice { get; set; }
        public List<ChildInGroupViewModel> Children { get; set; }
        public decimal AttendancePercentage { get; set; }
        public bool IsTeacherGroup { get; set; }
    }

    public class ChildInGroupViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string FamilyName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> Contraindications { get; set; }
        public string AttendanceStatus { get; set; }
        public decimal AttendancePercentage { get; set; }
    }
}
