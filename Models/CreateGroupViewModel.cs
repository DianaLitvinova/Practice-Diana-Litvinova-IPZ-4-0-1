using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace Diana_Litvinova_IPZ_4_0_1.Models
{

        public class CreateGroupViewModel
        {
            [Required(ErrorMessage = "Введіть назву групи")]
            public string Name { get; set; }

            [Required(ErrorMessage = "Виберіть вихователя")]
            public int TeacherId { get; set; }

            [Required(ErrorMessage = "Введіть місткість групи")]
            [Range(1, Int16.MaxValue, ErrorMessage = "Місткість групи повинна бути більше 0")]
            public int Capacity { get; set; }

            [Required(ErrorMessage = "Введіть вартість")]
            [Range(0.01, double.MaxValue, ErrorMessage = "Вартість повинна бути більше 0")]
            public decimal Price { get; set; }

            public List<TeacherSelectListItem> Teachers { get; set; } = new List<TeacherSelectListItem>();
        }

        public class TeacherSelectListItem
        {
            public short Value { get; set; }  // Изменено с int на short
            public string Text { get; set; }
        }
   
}
