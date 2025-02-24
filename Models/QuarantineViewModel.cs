namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class QuarantineViewModel
    {
        public List<GroupQuarantineInfo> Groups { get; set; }
        public decimal OverallAttendancePercentage { get; set; }
        public int TotalAbsentChildren { get; set; }
        public List<DailyAttendanceStat> AttendanceStats { get; set; }
    }

    public class GroupQuarantineInfo
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; }
        public string TeacherName { get; set; }
        public int TotalChildren { get; set; }
        public int AbsentChildren { get; set; }
        public int SickChildren { get; set; }
        public decimal AttendancePercentage { get; set; }
        public bool RequiresQuarantine => AttendancePercentage < 60;
        public List<AbsentChildInfo> AbsentChildrenDetails { get; set; }
    }

    public class AbsentChildInfo
    {
        public string ChildName { get; set; }
        public string Status { get; set; }
        public int ConsecutiveAbsentDays { get; set; }
    }

    public class DailyAttendanceStat
    {
        public DateTime Date { get; set; }
        public int TotalChildren { get; set; }
        public int PresentChildren { get; set; }
        public int SickChildren { get; set; }
        public decimal AttendancePercentage { get; set; }
        public decimal SickPercentage { get; set; }
    }
}