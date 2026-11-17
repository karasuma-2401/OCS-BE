using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;

namespace OCS.Api.Services;

public class PosService(AppDbContext dbContext) : IPosService
{
    public async Task<Result<PosProductLookupDto>> LookupPosProductAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Result.Failure<PosProductLookupDto>(ApplicationError.Validation("Pos.InvalidIdentifier", "Mã tra cứu không được để trống."));
        }

        identifier = identifier.Trim();
        bool isGuid = Guid.TryParse(identifier, out Guid productGuid);

        var product = await dbContext.Products
            .AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(
                p => p.Barcode == identifier || (isGuid && p.Id == productGuid),
                cancellationToken);

        if (product is null)
        {
            return Result.Failure<PosProductLookupDto>(ApplicationError.NotFound("Pos.ProductNotFound", "Không tìm thấy sản phẩm với mã này."));
        }

        bool canAddToCart = product.Status == ProductStatus.OnSale;
        string? rejectionReason = product.Status switch
        {
            ProductStatus.OnSale => null,
            ProductStatus.Sold => "Sản phẩm này đã được bán.",
            ProductStatus.Expired => "Sản phẩm đã hết hạn ký gửi, không thể bán.",
            ProductStatus.Returned => "Sản phẩm đã được hoàn trả lại cho khách ký gửi.",
            ProductStatus.EarlyWithdrawn => "Sản phẩm đã bị rút khỏi kệ trước hạn.",
            ProductStatus.PendingProcessing => "Sản phẩm đang chờ xử lý/kiểm định, chưa sẵn sàng để bán.",
            _ => $"Sản phẩm không khả dụng để bán (Trạng thái: {product.Status})."
        };

        var coverImage = product.Images.FirstOrDefault(i => i.ImageType == ProductImageType.Cover)?.Url
                         ?? product.Images.FirstOrDefault()?.Url;

        var response = new PosProductLookupDto(
            Id: product.Id,
            Barcode: product.Barcode,
            Name: product.Name,
            Size: product.Size,
            ListedPrice: product.ListedPrice,
            Status: product.Status.ToString(),
            CanAddToCart: canAddToCart,
            RejectionReason: rejectionReason,
            CategoryName: product.Category?.Name,
            BrandName: product.Brand?.Name,
            ImageUrl: coverImage
        );

        return Result<PosProductLookupDto>.Success(response);
    }

    public async Task<Result<PosCashPaymentResponse>> CheckoutCashAsync(
        Guid actorAccountId,
        PosCashPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.ProductIds is null || request.ProductIds.Count == 0)
        {
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Validation("POS.EMPTY_CART", "Giỏ hàng thanh toán không được để trống."));
        }

        if (request.ProductIds.Distinct().Count() != request.ProductIds.Count)
        {
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Validation("POS.DUPLICATE_ITEMS", "Danh sách sản phẩm thanh toán có sản phẩm bị trùng lặp."));
        }

        if (request.CashGiven < 0)
        {
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Validation("POS.INVALID_CASH_GIVEN", "Số tiền khách đưa không hợp lệ."));
        }

        // Kiểm tra tài khoản nhân viên hợp lệ
        var employee = await dbContext.EmployeeProfiles
            .FirstOrDefaultAsync(e => e.Id == actorAccountId, cancellationToken);
        if (employee is null)
        {
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Forbidden("POS.EMPLOYEE_NOT_FOUND", "Không tìm thấy hồ sơ nhân viên thực hiện thanh toán."));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var products = await dbContext.Products
                .Where(p => request.ProductIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            if (products.Count != request.ProductIds.Count)
            {
                return Result.Failure<PosCashPaymentResponse>(
                    ApplicationError.NotFound("POS.PRODUCT_NOT_FOUND", "Một số sản phẩm không tồn tại trong hệ thống."));
            }

            var invalidProducts = products.Where(p => p.Status != ProductStatus.OnSale).ToList();
            if (invalidProducts.Count != 0)
            {
                var names = string.Join(", ", invalidProducts.Select(p => p.Name));
                return Result.Failure<PosCashPaymentResponse>(
                    ApplicationError.Conflict("POS.PRODUCT_NOT_AVAILABLE", $"Sản phẩm không sẵn sàng để bán: {names}"));
            }

            decimal totalAmount = products.Sum(p => p.ListedPrice);
            if (request.CashGiven < totalAmount)
            {
                return Result.Failure<PosCashPaymentResponse>(
                    ApplicationError.Validation(
                        "POS.INSUFFICIENT_CASH",
                        $"Số tiền khách đưa ({request.CashGiven:N0} đ) không đủ để thanh toán ({totalAmount:N0} đ)."));
            }

            decimal changeAmount = request.CashGiven - totalAmount;
            var now = DateTimeOffset.UtcNow;

            var invoiceNumber = $"INV{now:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}";

            var invoice = new SaleInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = invoiceNumber,
                SoldByEmployeeId = employee.Id,
                TotalAmount = totalAmount,
                PaymentStatus = InvoicePaymentStatus.Paid,
                CreatedAt = now
            };

            foreach (var product in products)
            {
                invoice.Lines.Add(new SaleInvoiceLine
                {
                    SaleInvoiceId = invoice.Id,
                    ProductId = product.Id,
                    SalePrice = product.ListedPrice
                });

                var previousStatus = product.Status;
                product.MarkSold();
                product.InventoryVersion = Guid.NewGuid();

                dbContext.ProductStatusHistories.Add(new ProductStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    PreviousStatus = previousStatus,
                    NewStatus = product.Status,
                    Reason = $"Thanh toán tiền mặt tại POS cho hóa đơn {invoice.InvoiceNumber}",
                    ChangedByAccountId = employee.Id,
                    OccurredAt = now
                });
            }

            dbContext.SaleInvoices.Add(invoice);

            var financialTx = new FinancialTransaction
            {
                Id = Guid.NewGuid(),
                Type = FinancialTransactionType.Income,
                SaleInvoiceId = invoice.Id,
                Amount = totalAmount,
                PaymentMethod = PaymentMethod.Cash,
                Status = FinancialTransactionStatus.Success,
                OccurredAt = now,
                PerformedByEmployeeId = employee.Id,
                CreatedAt = now
            };
            dbContext.FinancialTransactions.Add(financialTx);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = new PosCashPaymentResponse(
                InvoiceId: invoice.Id,
                InvoiceNumber: invoice.InvoiceNumber,
                TotalAmount: totalAmount,
                CashGiven: request.CashGiven,
                ChangeAmount: changeAmount,
                CreatedAt: now,
                Items: products.Select(p => new PosInvoiceItemDto(p.Id, p.Barcode, p.Name, p.ListedPrice)).ToList()
            );

            return Result.Success(response);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Conflict("POS.CONCURRENCY_CONFLICT", "Một hoặc nhiều sản phẩm vừa được thanh toán hoặc cập nhật ở nơi khác. Vui lòng thử lại."));
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Result<PosInvoiceDetailDto>> GetInvoiceDetailAsync(
        string invoiceIdOrNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceIdOrNumber))
        {
            return Result.Failure<PosInvoiceDetailDto>(
                ApplicationError.Validation("POS.INVALID_INVOICE_IDENTIFIER", "Mã hóa đơn không được để trống."));
        }

        invoiceIdOrNumber = invoiceIdOrNumber.Trim();
        bool isGuid = Guid.TryParse(invoiceIdOrNumber, out var invoiceId);

        var invoice = await dbContext.SaleInvoices
            .AsNoTracking()
            .Include(i => i.SoldByEmployee)
                .ThenInclude(e => e.Account)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Product)
                    .ThenInclude(p => p.Brand)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Product)
                    .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(i =>
                (isGuid && i.Id == invoiceId) || i.InvoiceNumber == invoiceIdOrNumber,
                cancellationToken);

        if (invoice is null)
        {
            return Result.Failure<PosInvoiceDetailDto>(
                ApplicationError.NotFound("POS.INVOICE_NOT_FOUND", "Không tìm thấy hóa đơn bán lẻ."));
        }

        // Lấy thông tin giao dịch tài chính liên quan (nếu có)
        var transactions = await dbContext.FinancialTransactions
            .AsNoTracking()
            .Where(t => t.SaleInvoiceId == invoice.Id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var primaryTx = transactions.FirstOrDefault(t => t.Status == FinancialTransactionStatus.Success)
                     ?? transactions.FirstOrDefault();

        string? paymentMethod = primaryTx?.PaymentMethod switch
        {
            PaymentMethod.Cash => "Tiền mặt",
            PaymentMethod.VietQr => "VietQR chuyển khoản",
            PaymentMethod.BankTransfer => "Chuyển khoản",
            _ => primaryTx != null ? primaryTx.PaymentMethod.ToString() : null
        };

        decimal? cashGiven = null;
        decimal? changeAmount = null;

        if (primaryTx?.PaymentMethod == PaymentMethod.Cash)
        {
            cashGiven = primaryTx.Amount;
            changeAmount = 0m;
        }

        var detail = new PosInvoiceDetailDto(
            Id: invoice.Id,
            InvoiceNumber: invoice.InvoiceNumber,
            TotalAmount: invoice.TotalAmount,
            PaymentStatus: invoice.PaymentStatus.ToString(),
            CreatedAt: invoice.CreatedAt,
            CashierName: invoice.SoldByEmployee?.Account?.FullName,
            PaymentMethod: paymentMethod,
            CashGiven: cashGiven,
            ChangeAmount: changeAmount,
            Items: invoice.Lines.Select(l => new PosInvoiceItemDetailDto(
                ProductId: l.ProductId,
                Barcode: l.Product?.Barcode ?? string.Empty,
                Name: l.Product?.Name ?? string.Empty,
                Size: l.Product?.Size,
                BrandName: l.Product?.Brand?.Name,
                CategoryName: l.Product?.Category?.Name,
                Price: l.SalePrice
            )).ToList()
        );

        return Result.Success(detail);
    }

    public async Task<Result<byte[]>> ExportInvoicePdfAsync(
        string invoiceIdOrNumber,
        CancellationToken cancellationToken = default)
    {
        var detailResult = await GetInvoiceDetailAsync(invoiceIdOrNumber, cancellationToken);
        if (!detailResult.IsSuccess || detailResult.Data is null)
        {
            return Result.Failure<byte[]>(detailResult.Error ?? ApplicationError.NotFound("POS.INVOICE_NOT_FOUND", "Không tìm thấy hóa đơn."));
        }

        try
        {
            var pdfBytes = PosInvoicePdfRenderer.Render(detailResult.Data);
            return Result.Success(pdfBytes);
        }
        catch (Exception ex)
        {
            return Result.Failure<byte[]>(
                ApplicationError.Conflict("POS.PDF_EXPORT_FAILED", $"Lỗi khi xuất PDF hóa đơn: {ex.Message}"));
        }
    }
}