using System.ComponentModel.DataAnnotations;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class ProductViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Назва продукту обов'язкова")]
        [Display(Name = "Назва продукту")]
        [StringLength(32, ErrorMessage = "Назва продукту не може перевищувати 32 символи")]
        public string Name { get; set; }
    }
}