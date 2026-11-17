namespace OCS.Api.Models.Pos;

public record PosInvoiceDetailDto(
    Guid Id,
    string InvoiceNumber,
    decimal TotalAmount,
    string PaymentStatus,
    DateTimeOffset CreatedAt,
    string? CashierName,
    string? PaymentMethod,
    decimal? CashGiven,
    decimal? ChangeAmount,
    List<PosInvoiceItemDetailDto> Items
);

public record PosInvoiceItemDetailDto(
    Guid ProductId,
    string Barcode,
    string Name,
    string? Size,
    string? BrandName,
    string? CategoryName,
    decimal Price
);
