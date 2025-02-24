﻿// Models/CreateFamilyViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class CreateFamilyViewModel
    {
        [Required(ErrorMessage = "Введіть ПІБ відповідальної особи")]
        [Display(Name = "ПІБ відповідальної особи")]
        public string ResponsibleName { get; set; }

        [Required(ErrorMessage = "Введіть номер телефону")]
        [RegularExpression(@"\+[0-9]{6,15}", ErrorMessage = "Невірний формат телефону")]
        [Display(Name = "Телефон")]
        public string PhoneResponsible { get; set; }

        [Required(ErrorMessage = "Введіть адресу проживання")]
        [Display(Name = "Адреса проживання")]
        public string PlaceOfResidence { get; set; }
    }
}