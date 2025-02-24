using System.ComponentModel.DataAnnotations;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class FamilyManagementViewModel
    {
        public List<FamilyViewModel> Families { get; set; } = new List<FamilyViewModel>();
        public FamilyViewModel NewFamily { get; set; }
        public string SearchTerm { get; set; }
    }
}