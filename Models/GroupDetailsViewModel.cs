namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class GroupDetailsViewModel
    {
        public string GroupName { get; set; }
        public List<GroupChildViewModel> Children { get; set; } = new List<GroupChildViewModel>();
    }
}
