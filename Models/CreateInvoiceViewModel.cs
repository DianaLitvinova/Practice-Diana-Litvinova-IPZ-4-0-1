using System.ComponentModel.DataAnnotations;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class CreateInvoiceViewModel
    {
        [Required(ErrorMessage = "Виберіть кухаря")]
        [Display(Name = "Кухар")]
        public int CookId { get; set; }

        [Required(ErrorMessage = "Додайте хоча б один товар")]
        [Display(Name = "Товари")]
        public List<InvoiceItemViewModel> Items { get; set; } = new List<InvoiceItemViewModel>();
    }

    public class InvoiceItemViewModel
    {
        [Required(ErrorMessage = "Виберіть продукт")]
        [Display(Name = "Продукт")]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Вкажіть кількість")]
        [Display(Name = "Кількість")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Кількість повинна бути більше 0")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Виберіть одиницю виміру")]
        [Display(Name = "Одиниця виміру")]
        public string Measure { get; set; }
    }
}