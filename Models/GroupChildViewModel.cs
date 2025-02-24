namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class GroupChildViewModel
    {
        public string FullName { get; set; }
        public List<string> Contraindications { get; set; } = new List<string>();
        public List<string> ForbiddenProducts { get; set; } = new List<string>();
    }
}
