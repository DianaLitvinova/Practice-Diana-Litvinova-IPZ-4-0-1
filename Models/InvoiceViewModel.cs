namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class InvoiceViewModel
    {
        public int Id { get; set; }
        public string CookName { get; set; }
        public string HeadTeacherName { get; set; }
        public string ReceiverName { get; set; }
        public DateTime DateCreate { get; set; }
        public DateTime? DateAccept { get; set; }
        public DateTime? DateReceipt { get; set; }
        public string Status { get; set; }
        public List<InvoiceProductViewModel> Products { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
