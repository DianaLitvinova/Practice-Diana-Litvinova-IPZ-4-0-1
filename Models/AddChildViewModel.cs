using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class AddChildViewModel
    {
        public int FamilyId { get; set; }

        [Required(ErrorMessage = "Введіть ПІБ дитини")]
        [Display(Name = "ПІБ дитини")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Оберіть групу")]
        [Display(Name = "Група")]
        public int GroupId { get; set; }

        [Required(ErrorMessage = "Оберіть дату початку")]
        [Display(Name = "Дата початку")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = DateTime.Today;

        public SelectList Groups { get; set; } = new SelectList(Enumerable.Empty<SelectListItem>());

        [Display(Name = "Протипоказання")]
        public List<int> SelectedContraindications { get; set; } = new List<int>();

        public MultiSelectList AvailableContraindications { get; set; } = new MultiSelectList(Enumerable.Empty<SelectListItem>());
    }
}