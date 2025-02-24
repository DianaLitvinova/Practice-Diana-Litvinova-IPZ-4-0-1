using System.ComponentModel.DataAnnotations;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    // Основная модель для семьи
    public class FamilyViewModel
    {
        public int Id { get; set; }
        public string ResponsibleName { get; set; }
        public string PhoneResponsible { get; set; }
        public string PlaceOfResidence { get; set; }
        public List<ResponsibleViewModel> AdditionalResponsibles { get; set; }
        public List<ChildViewModel> Children { get; set; }
    }

    // Модель для представления ответственных лиц
    public class ResponsibleViewModel
    {
        public int Id { get; set; }
        public string Fullname { get; set; }
        public string Phone { get; set; }
    }

    // Модель для поиска и отображения списка
    public class FamilySearchViewModel
    {
        public string SearchTerm { get; set; }
        public List<GroupViewModel> Groups { get; set; }
        public int? SelectedGroupId { get; set; }
        public List<ChildWithFamilyViewModel> Children { get; set; }
    }

    // Модель для отображения ребенка со связанной информацией
    public class ChildWithFamilyViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string GroupName { get; set; }
        public string TeacherName { get; set; }
        public string MainResponsibleName { get; set; }
        public string MainResponsiblePhone { get; set; }
        public DateTime StartDate { get; set; }
        public List<string> Contraindications { get; set; }
        public int GroupId { get; set; }
    }

    // Базовая модель для ребенка
    public class ChildViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string GroupName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> Contraindications { get; set; }
    }

    public class TransferChildModel
    {
        public int ChildId { get; set; }
        public int NewGroupId { get; set; }
    }
}