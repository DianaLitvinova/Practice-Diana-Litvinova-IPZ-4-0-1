// Models/ChildContraindicationsViewModel.cs
namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class ChildContraindicationsViewModel
    {
        public string ChildName { get; set; }
        public string GroupName { get; set; }
        public List<string> Contraindications { get; set; }
        public List<string> ForbiddenProducts { get; set; }
    }
}