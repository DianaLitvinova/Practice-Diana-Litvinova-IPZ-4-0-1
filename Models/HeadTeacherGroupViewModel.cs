using Microsoft.AspNetCore.Mvc.Rendering;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class HeadTeacherGroupViewModel
    {
        public List<GroupViewModel> Groups { get; set; } = new List<GroupViewModel>();
        public AttendanceStatsData StatsData { get; set; }
        public int? SelectedGroupId { get; set; }
        public string Period { get; set; } = "day";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<SelectListItem> Teachers { get; set; } = new List<SelectListItem>();
    }
}