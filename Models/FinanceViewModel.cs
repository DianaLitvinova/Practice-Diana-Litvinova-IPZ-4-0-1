namespace Diana_Litvinova_IPZ_4_0_1.Models
{
    public class FinanceViewModel
    {
        // Общая статистика
        public decimal TotalBalance { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal ParentPayments { get; set; }
        public decimal ExpectedPayments { get; set; }
        public decimal TotalDebt { get; set; }
        public decimal PurchaseExpenses { get; set; }
        public int ApprovedInvoices { get; set; }
        public int PendingInvoices { get; set; }

        // Списки для таблиц
        public List<FamilyPaymentViewModel> FamilyPayments { get; set; } = new List<FamilyPaymentViewModel>();
        public List<DebtViewModel> Debts { get; set; } = new List<DebtViewModel>();
        public List<FamilySelectViewModel> Families { get; set; } = new List<FamilySelectViewModel>();
    }

    public class FamilyPaymentViewModel
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string FamilyName { get; set; }
        public decimal Amount { get; set; }
        public string HeadTeacherName { get; set; }
    }

    public class FamilySelectViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}