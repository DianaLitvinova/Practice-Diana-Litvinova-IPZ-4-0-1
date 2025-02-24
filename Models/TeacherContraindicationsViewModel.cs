namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class TeacherContraindicationsViewModel
    {
        public List<ContraindicationGroupViewModel> Groups { get; set; } = new List<ContraindicationGroupViewModel>();
        public List<ContraindicatedProductViewModel> AllProducts { get; set; } = new List<ContraindicatedProductViewModel>();
    }

    public class ContraindicationGroupViewModel
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; }
        public List<ChildContraindicationViewModel> Children { get; set; } = new List<ChildContraindicationViewModel>();
    }

    public class ChildContraindicationViewModel
    {
        public int ChildId { get; set; }
        public string ChildName { get; set; }
        public List<ContraindicationViewModel> Contraindications { get; set; } = new List<ContraindicationViewModel>();
        public List<ContraindicatedProductViewModel> ForbiddenProducts { get; set; } = new List<ContraindicatedProductViewModel>();
    }

    public class ContraindicationViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}