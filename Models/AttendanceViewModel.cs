using System;
using System.Collections.Generic;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class AttendanceViewModel
    {
        public List<GroupViewModel> TeacherGroups { get; set; }
        public int? SelectedGroupId { get; set; }
        public string Period { get; set; } = "day"; // day, week, month
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool ShowPercentage { get; set; } = false;
        public AttendanceStatsData StatsData { get; set; }
    }

    public class DailyAttendanceStats
    {
        public DateTime Date { get; set; }
        public int TotalChildren { get; set; }
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public int SickCount { get; set; }
        public decimal AttendancePercentage => TotalChildren > 0
            ? Math.Round((decimal)PresentCount / TotalChildren * 100, 2)
            : 0;
    }

    public class MonthlyAttendanceStats
    {
        public string Month { get; set; }
        public int Year { get; set; }
        public decimal AverageAttendance { get; set; }
        public int MaxAttendance { get; set; }
        public int MinAttendance { get; set; }
    }

    public class AttendanceStatsData
    {
        public List<string> Labels { get; set; } = new List<string>();
        public List<int> Present { get; set; } = new List<int>();
        public List<int> Absent { get; set; } = new List<int>();
        public List<int> Sick { get; set; } = new List<int>();
        public List<int> Weekend { get; set; } = new List<int>();
        public int MaxValue { get; set; }
    }
}