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
            .Include(p => p.ConsignmentContract)
            .FirstOrDefaultAsync(
                p => p.Barcode == identifier || (isGuid && p.Id == productGuid),
                cancellationToken);

        if (product is null)
        {
            return Result.Failure<PosProductLookupDto>(ApplicationError.NotFound("Pos.ProductNotFound", "Không tìm thấy sản phẩm với mã này."));
        }

        bool contractExpired = product.ConsignmentContract is { } contract && contract.EndsAt <= DateTimeOffset.UtcNow;
        bool canAddToCart = product.Status == ProductStatus.OnSale && !contractExpired;
        string? rejectionReason = contractExpired ? "Sản phẩm đã hết hạn ký gửi, không thể bán." : product.Status switch
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
        if (request is null)
        {
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Validation("POS.EMPTY_CART", "Giỏ hàng thanh toán không được để trống."));
        }

        if (request.CashGiven < 0)
        {
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Validation("POS.INVALID_CASH_GIVEN", "Số tiền khách đưa không hợp lệ."));
        }

        var validation = await ValidateCheckoutPreconditionsAsync(actorAccountId, request.ProductIds, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.Failure<PosCashPaymentResponse>(validation.Error!);
        }

        var employee = validation.Data!;
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var productsResult = await LoadAvailableCartProductsAsync(request.ProductIds, cancellationToken);
            if (!productsResult.IsSuccess)
            {
                return Result.Failure<PosCashPaymentResponse>(productsResult.Error!);
            }

            var products = productsResult.Data!;
            decimal totalAmount = products.Sum(p => p.ListedPrice);
            if (request.CashGiven < totalAmount)
            {
                return Result.Failure<PosCashPaymentResponse>(
                    ApplicationError.Validation(
                        "POS.INSUFFICIENT_CASH",
                        $"Số tiền khách đưa ({request.CashGiven:N0} đ) không đủ để thanh toán ({totalAmount:N0} đ)."));
            }

            var lineResolution = await ResolveExistingInvoiceLinesAsync(
                request.ProductIds,
                totalAmount,
                allowExactUnpaidReuse: false,
                cancellationToken);
            if (!lineResolution.IsSuccess)
            {
                return Result.Failure<PosCashPaymentResponse>(lineResolution.Error!);
            }

            decimal changeAmount = request.CashGiven - totalAmount;
            var now = DateTimeOffset.UtcNow;
            var invoice = BuildSaleInvoice(employee.Id, products, totalAmount, InvoicePaymentStatus.Paid, now);

            foreach (var product in products)
            {
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
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

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
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Conflict("POS.CONCURRENCY_CONFLICT", "Một hoặc nhiều sản phẩm vừa được thanh toán hoặc cập nhật ở nơi khác. Vui lòng thử lại."));
        }
        catch (DbUpdateException dbEx)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;
            Console.WriteLine($"[POS CHECKOUT DB ERROR] {innerMsg}");
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Conflict("POS.DB_UPDATE_ERROR", $"Lỗi cập nhật dữ liệu: {innerMsg}"));
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            var message = ex.InnerException?.Message ?? ex.Message;
            Console.WriteLine($"[POS CHECKOUT ERROR] {ex}");
            return Result.Failure<PosCashPaymentResponse>(
                ApplicationError.Conflict("POS.CHECKOUT_ERROR", $"Không thể hoàn tất thanh toán: {message}"));
        }
    }

    public async Task<Result<Guid>> PrepareVietQrInvoiceAsync(
        Guid actorAccountId,
        PosVietQrCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Result.Failure<Guid>(
                ApplicationError.Validation("POS.EMPTY_CART", "Giỏ hàng thanh toán không được để trống."));
        }

        var validation = await ValidateCheckoutPreconditionsAsync(actorAccountId, request.ProductIds, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.Failure<Guid>(validation.Error!);
        }

        var employee = validation.Data!;
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var productsResult = await LoadAvailableCartProductsAsync(request.ProductIds, cancellationToken);
            if (!productsResult.IsSuccess)
            {
                return Result.Failure<Guid>(productsResult.Error!);
            }

            var products = productsResult.Data!;
            decimal totalAmount = products.Sum(p => p.ListedPrice);
            if (totalAmount <= 0)
            {
                return Result.Failure<Guid>(
                    ApplicationError.Validation("POS.INVALID_INVOICE_AMOUNT", "Tổng tiền hóa đơn phải lớn hơn 0 để tạo mã VietQR."));
            }

            var lineResolution = await ResolveExistingInvoiceLinesAsync(
                request.ProductIds,
                totalAmount,
                allowExactUnpaidReuse: true,
                cancellationToken);
            if (!lineResolution.IsSuccess)
            {
                return Result.Failure<Guid>(lineResolution.Error!);
            }

            if (lineResolution.Data is Guid reusedInvoiceId)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return Result.Success(reusedInvoiceId);
            }

            var now = DateTimeOffset.UtcNow;
            var invoice = BuildSaleInvoice(employee.Id, products, totalAmount, InvoicePaymentStatus.Unpaid, now);
            dbContext.SaleInvoices.Add(invoice);

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result.Success(invoice.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return Result.Failure<Guid>(
                ApplicationError.Conflict("POS.CONCURRENCY_CONFLICT", "Một hoặc nhiều sản phẩm vừa được thanh toán hoặc cập nhật ở nơi khác. Vui lòng thử lại."));
        }
        catch (DbUpdateException dbEx)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;
            Console.WriteLine($"[POS VIETQR CHECKOUT DB ERROR] {innerMsg}");
            return Result.Failure<Guid>(
                ApplicationError.Conflict("POS.DB_UPDATE_ERROR", $"Lỗi cập nhật dữ liệu: {innerMsg}"));
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            var message = ex.InnerException?.Message ?? ex.Message;
            Console.WriteLine($"[POS VIETQR CHECKOUT ERROR] {ex}");
            return Result.Failure<Guid>(
                ApplicationError.Conflict("POS.CHECKOUT_ERROR", $"Không thể khởi tạo hóa đơn VietQR: {message}"));
        }
    }

    private async Task<Result<EmployeeProfile>> ValidateCheckoutPreconditionsAsync(
        Guid actorAccountId,
        List<Guid>? productIds,
        CancellationToken cancellationToken)
    {
        if (productIds is null || productIds.Count == 0)
        {
            return Result.Failure<EmployeeProfile>(
                ApplicationError.Validation("POS.EMPTY_CART", "Giỏ hàng thanh toán không được để trống."));
        }

        if (productIds.Distinct().Count() != productIds.Count)
        {
            return Result.Failure<EmployeeProfile>(
                ApplicationError.Validation("POS.DUPLICATE_ITEMS", "Danh sách sản phẩm thanh toán có sản phẩm bị trùng lặp."));
        }

        var employee = await dbContext.EmployeeProfiles
            .FirstOrDefaultAsync(e => e.Id == actorAccountId, cancellationToken);
        if (employee is null)
        {
            return Result.Failure<EmployeeProfile>(
                ApplicationError.Forbidden("POS.EMPLOYEE_NOT_FOUND", "Không tìm thấy hồ sơ nhân viên thực hiện thanh toán."));
        }

        return Result.Success(employee);
    }

    private async Task<Result<List<Product>>> LoadAvailableCartProductsAsync(
        List<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var products = await dbContext.Products
            .Include(p => p.ConsignmentContract)
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (products.Count != productIds.Count)
        {
            return Result.Failure<List<Product>>(
                ApplicationError.NotFound("POS.PRODUCT_NOT_FOUND", "Một số sản phẩm không tồn tại trong hệ thống."));
        }

        var now = DateTimeOffset.UtcNow;
        var invalidProducts = products.Where(p => p.Status != ProductStatus.OnSale ||
            p.ConsignmentContract is { } contract && contract.EndsAt <= now).ToList();
        if (invalidProducts.Count != 0)
        {
            var names = string.Join(", ", invalidProducts.Select(p => p.Name));
            return Result.Failure<List<Product>>(
                ApplicationError.Conflict("POS.PRODUCT_NOT_AVAILABLE", $"Sản phẩm không sẵn sàng để bán: {names}"));
        }

        return Result.Success(products);
    }

    /// <summary>
    /// Enforces the 1-to-1 Product-to-SaleInvoiceLine constraint cleanly:
    /// rejects items belonging to paid/settled invoices, optionally reuses an exact unpaid VietQR invoice,
    /// and otherwise detaches stale unpaid lines while expiring their pending VietQR transactions.
    /// </summary>
    private async Task<Result<Guid?>> ResolveExistingInvoiceLinesAsync(
        List<Guid> productIds,
        decimal expectedTotalAmount,
        bool allowExactUnpaidReuse,
        CancellationToken cancellationToken)
    {
        var existingLines = await dbContext.SaleInvoiceLines
            .Include(l => l.SaleInvoice)
                .ThenInclude(i => i.Lines)
            .Where(l => productIds.Contains(l.ProductId))
            .ToListAsync(cancellationToken);

        if (existingLines.Count == 0)
        {
            return Result.Success<Guid?>(null);
        }

        var affectedInvoiceIds = existingLines.Select(l => l.SaleInvoiceId).Distinct().ToList();
        var succeededInvoiceIds = await dbContext.FinancialTransactions
            .Where(t => t.SaleInvoiceId.HasValue
                        && affectedInvoiceIds.Contains(t.SaleInvoiceId.Value)
                        && t.Status == FinancialTransactionStatus.Success)
            .Select(t => t.SaleInvoiceId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var blockedLine = existingLines.FirstOrDefault(l =>
            l.SaleInvoice.PaymentStatus != InvoicePaymentStatus.Unpaid ||
            succeededInvoiceIds.Contains(l.SaleInvoiceId));
        if (blockedLine is not null)
        {
            return Result.Failure<Guid?>(
                ApplicationError.Conflict(
                    "POS.PRODUCT_ALREADY_INVOICED",
                    $"Sản phẩm đã được ghi nhận trong hóa đơn {blockedLine.SaleInvoice.InvoiceNumber}."));
        }

        if (allowExactUnpaidReuse && affectedInvoiceIds.Count == 1)
        {
            var candidateInvoice = existingLines[0].SaleInvoice;
            var candidateIds = candidateInvoice.Lines.Select(l => l.ProductId).ToHashSet();
            if (candidateIds.Count == productIds.Count
                && candidateIds.SetEquals(productIds)
                && candidateInvoice.TotalAmount == expectedTotalAmount)
            {
                return Result.Success<Guid?>(candidateInvoice.Id);
            }
        }

        foreach (var invoiceGroup in existingLines.GroupBy(l => l.SaleInvoice))
        {
            var staleInvoice = invoiceGroup.Key;
            foreach (var line in invoiceGroup)
            {
                staleInvoice.Lines.Remove(line);
                dbContext.SaleInvoiceLines.Remove(line);
            }

            staleInvoice.TotalAmount = staleInvoice.Lines.Sum(l => l.SalePrice);
        }

        var stalePendingTransactions = await dbContext.FinancialTransactions
            .Where(t => t.SaleInvoiceId.HasValue
                        && affectedInvoiceIds.Contains(t.SaleInvoiceId.Value)
                        && t.PaymentMethod == PaymentMethod.VietQr
                        && t.Status == FinancialTransactionStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var pendingTx in stalePendingTransactions)
        {
            pendingTx.Status = FinancialTransactionStatus.Failed;
        }

        return Result.Success<Guid?>(null);
    }

    private static SaleInvoice BuildSaleInvoice(
        Guid employeeId,
        IReadOnlyCollection<Product> products,
        decimal totalAmount,
        InvoicePaymentStatus paymentStatus,
        DateTimeOffset now)
    {
        var invoiceId = Guid.NewGuid();
        var invoice = new SaleInvoice
        {
            Id = invoiceId,
            InvoiceNumber = $"INV{now:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}",
            SoldByEmployeeId = employeeId,
            TotalAmount = totalAmount,
            PaymentStatus = paymentStatus,
            CreatedAt = now
        };

        foreach (var product in products)
        {
            invoice.Lines.Add(new SaleInvoiceLine
            {
                SaleInvoiceId = invoiceId,
                ProductId = product.Id,
                SalePrice = product.ListedPrice
            });
        }

        return invoice;
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
