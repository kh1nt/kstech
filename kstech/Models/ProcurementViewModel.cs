namespace kstech.Models
{
    public class ProcurementViewModel
    {
        public int? PurchaseOrderId { get; set; }
        public string Id { get; set; } = string.Empty; // Purchase order number for display.
        public string SupplierName { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public decimal TotalProcurementCost { get; set; }
        public decimal ReservedAmount { get; set; }
        public decimal ActualExpenseAmount { get; set; }
        public decimal RemainingReservationAmount { get; set; }
        public string Status { get; set; } = "Draft";
        public int? BudgetId { get; set; }
        public string BudgetLabel { get; set; } = "No linked budget";
        public bool CanApprove { get; set; }
        public bool CanReceive { get; set; }

        public List<ProcurementItemViewModel> Items { get; set; } = new();
    }

    public class ProcurementItemViewModel
    {
        public int? PurchaseOrderLineId { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty; // For display
        public int QuantityOrdered { get; set; }
        public int QuantityReceived { get; set; }
        public int QuantityRemaining => Math.Max(0, QuantityOrdered - QuantityReceived);
        public decimal CostPerItem { get; set; }
        public decimal OrderedLineTotal => Math.Round(QuantityOrdered * CostPerItem, 2, MidpointRounding.AwayFromZero);
        public decimal ReceivedLineTotal => Math.Round(QuantityReceived * CostPerItem, 2, MidpointRounding.AwayFromZero);
    }

    public class ProcurementActionResult
    {
        public bool Succeeded { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? PurchaseOrderId { get; set; }
    }

    public class ProcurementReceiveLineInput
    {
        public int PurchaseOrderLineId { get; set; }
        public int QuantityToReceive { get; set; }
    }
}
