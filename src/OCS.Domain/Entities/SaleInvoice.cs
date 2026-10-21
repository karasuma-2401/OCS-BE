using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class SaleInvoice : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid SoldByEmployeeId { get; set; }

    public decimal TotalAmount { get; set; }

    public InvoicePaymentStatus PaymentStatus { get; set; } = InvoicePaymentStatus.Unpaid;

    public EmployeeProfile SoldByEmployee { get; set; } = null!;

    public ICollection<SaleInvoiceLine> Lines { get; set; } = new List<SaleInvoiceLine>();
}
