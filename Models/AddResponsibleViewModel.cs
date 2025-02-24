using System.ComponentModel.DataAnnotations;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class AddResponsibleViewModel
    {
        public int FamilyId { get; set; }

        [Required(ErrorMessage = "Введіть ПІБ відповідальної особи")]
        [Display(Name = "ПІБ відповідальної особи")]
        public string Fullname { get; set; }

        [Required(ErrorMessage = "Введіть номер телефону")]
        [RegularExpression(@"\+[0-9]{6,15}", ErrorMessage = "Невірний формат телефону")]
        [Display(Name = "Телефон")]
        public string Phone { get; set; }
    }
}