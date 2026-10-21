namespace OCS.Domain.Entities;

public sealed class SaleInvoiceLine
{
    public Guid SaleInvoiceId { get; set; }

    public Guid ProductId { get; set; }

    public decimal SalePrice { get; set; }

    public SaleInvoice SaleInvoice { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
