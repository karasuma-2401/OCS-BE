namespace OCS.Api.Models.Pos;

public record PosCashPaymentRequest(
    List<Guid> ProductIds,
    decimal CashGiven
);

public record PosInvoiceItemDto(
    Guid ProductId,
    string Barcode,
    string Name,
    decimal Price
);

public record PosCashPaymentResponse(
    Guid InvoiceId,
    string InvoiceNumber,
    decimal TotalAmount,
    decimal CashGiven,
    decimal ChangeAmount,
    DateTimeOffset CreatedAt,
    List<PosInvoiceItemDto> Items
);