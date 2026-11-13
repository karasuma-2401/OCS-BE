using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Consignments;
using OCS.Api.Models.Notifications;
using OCS.Api.Storage;

namespace OCS.Api.Services;

public sealed class ConsignmentService(
    AppDbContext dbContext,
    INotificationService notificationService,
    IDistributedCache? cache = null,
    IOptions<R2Options>? r2Options = null,
    IHttpContextAccessor? httpContextAccessor = null) : IConsignmentService
{
    private readonly R2Options? _r2Options = r2Options?.Value;
    private readonly IHttpContextAccessor? _httpContextAccessor = httpContextAccessor;
    public async Task<Result<ReceptionContractResponse>> CreateReceptionAsync(
        Guid staffId,
        CreateReceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (staffId == Guid.Empty)
        {
            return Result.Failure<ReceptionContractResponse>(
                ApplicationError.Unauthorized(ApiErrorCodes.Unauthorized, "Không xác định được nhân viên thực hiện."));
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Result.Failure<ReceptionContractResponse>(
                ApplicationError.Validation("CONSIGNMENT.ITEMS_REQUIRED", "Phiếu tiếp nhận cần ít nhất 1 sản phẩm."));
        }

        var isBuyout = string.Equals(request.IntakeType?.Trim(), "Buyout", StringComparison.OrdinalIgnoreCase);

        if (!Enum.TryParse<ExpiryHandling>(request.ExpiryHandling, true, out var expiryHandling) ||
            !Enum.IsDefined(expiryHandling))
        {
            expiryHandling = ExpiryHandling.Return;
        }

        var employeeExists = await dbContext.EmployeeProfiles
            .AnyAsync(e => e.Id == staffId, cancellationToken);
        if (!employeeExists)
        {
            var staffAccount = await dbContext.Accounts
                .Include(a => a.AccountRoles)
                    .ThenInclude(ar => ar.Role)
                .FirstOrDefaultAsync(a => a.Id == staffId, cancellationToken);
            if (staffAccount is null ||
                (staffAccount.AccountType != AccountType.Internal &&
                 !staffAccount.AccountRoles.Any(ar => ar.Role.Code == "Admin" || ar.Role.Code == "Manager" || ar.Role.Code == "Staff")))
            {
                return Result.Failure<ReceptionContractResponse>(
                    ApplicationError.Forbidden(ApiErrorCodes.Forbidden, "Tài khoản không có hồ sơ nhân viên hợp lệ."));
            }

            dbContext.EmployeeProfiles.Add(new EmployeeProfile { Id = staffId });
        }

        var requestedCategoryIds = request.Items.Select(i => i.CategoryId).Distinct().ToList();
        var categories = await dbContext.Categories
            .Where(c => requestedCategoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var requestedBrandIds = request.Items.Select(i => i.BrandId).Distinct().ToList();
        var brands = await dbContext.Brands
            .Where(b => requestedBrandIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        Category? fallbackCategory = null;
        Brand? fallbackBrand = null;

        foreach (var item in request.Items)
        {
            if (!categories.ContainsKey(item.CategoryId))
            {
                fallbackCategory ??= await dbContext.Categories.FirstOrDefaultAsync(cancellationToken);
                if (fallbackCategory is not null)
                {
                    item.CategoryId = fallbackCategory.Id;
                    categories[fallbackCategory.Id] = fallbackCategory;
                }
                else
                {
                    return Result.Failure<ReceptionContractResponse>(
                        ApplicationError.Validation("CONSIGNMENT.INVALID_CATEGORY", $"Danh mục {item.CategoryId} không tồn tại trong hệ thống."));
                }
            }

            if (!brands.ContainsKey(item.BrandId))
            {
                var existingBrand = await dbContext.Brands.FirstOrDefaultAsync(b => b.Id == item.BrandId, cancellationToken);
                if (existingBrand is null && (item.BrandId == Guid.Parse("40000000-0000-0000-0000-000000000000") || item.BrandId == Guid.Parse("40000000-0000-0000-0000-000000000099") || item.BrandId == Guid.Empty))
                {
                    existingBrand = await dbContext.Brands.FirstOrDefaultAsync(b => b.Name == "Khác" && b.IsActive, cancellationToken)
                        ?? await dbContext.Brands.FirstOrDefaultAsync(b => b.Tier == BrandTier.NoBrand && b.IsActive, cancellationToken);

                    if (existingBrand is null)
                    {
                        existingBrand = new Brand
                        {
                            Id = Guid.Parse("40000000-0000-0000-0000-000000000099"),
                            Name = "Khác",
                            Tier = BrandTier.NoBrand,
                            DefaultCommissionRate = 10m,
                            IsActive = true
                        };
                        dbContext.Brands.Add(existingBrand);
                    }
                }

                if (existingBrand is not null)
                {
                    item.BrandId = existingBrand.Id;
                    brands[existingBrand.Id] = existingBrand;
                }
                else
                {
                    fallbackBrand ??= await dbContext.Brands.FirstOrDefaultAsync(cancellationToken);
                    if (fallbackBrand is not null)
                    {
                        item.BrandId = fallbackBrand.Id;
                        brands[fallbackBrand.Id] = fallbackBrand;
                    }
                    else
                    {
                        return Result.Failure<ReceptionContractResponse>(
                            ApplicationError.Validation("CONSIGNMENT.INVALID_BRAND", $"Thương hiệu {item.BrandId} không tồn tại trong hệ thống."));
                    }
                }
            }
        }

        var seenUrlsInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allImageUrls = new List<string>();

        foreach (var item in request.Items)
        {
            var itemUrls = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.CoverImageUrl))
            {
                itemUrls.Add(item.CoverImageUrl.Trim());
            }

            if (item.ImageUrls is not null)
            {
                foreach (var u in item.ImageUrls)
                {
                    if (!string.IsNullOrWhiteSpace(u))
                    {
                        itemUrls.Add(u.Trim());
                    }
                }
            }

            foreach (var url in itemUrls)
            {
                if (!IsValidStorageImageUrl(url, out var urlError))
                {
                    return Result.Failure<ReceptionContractResponse>(
                        ApplicationError.Validation("CONSIGNMENT.INVALID_IMAGE_URL", urlError ?? "URL hình ảnh không hợp lệ."));
                }

                if (!seenUrlsInBatch.Add(url))
                {
                    return Result.Failure<ReceptionContractResponse>(
                        ApplicationError.Validation("CONSIGNMENT.DUPLICATE_IMAGE_URL",
                            $"Hình ảnh '{url}' bị gán trùng lặp cho nhiều sản phẩm trong cùng phiếu tiếp nhận."));
                }

                allImageUrls.Add(url);
            }
        }

        if (allImageUrls.Count > 0)
        {
            var alreadyUsedUrl = await dbContext.ProductImages
                .Where(pi => allImageUrls.Contains(pi.Url))
                .Select(pi => pi.Url)
                .FirstOrDefaultAsync(cancellationToken);

            if (alreadyUsedUrl is not null)
            {
                return Result.Failure<ReceptionContractResponse>(
                    ApplicationError.Validation("CONSIGNMENT.DUPLICATE_IMAGE_URL",
                        $"Hình ảnh '{alreadyUsedUrl}' đã được liên kết với một sản phẩm khác trong hệ thống."));
            }
        }

        await using IDbContextTransaction? transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            Appointment? linkedAppointment = null;
            Guid customerId;
            string customerName;
            string customerPhone;

            if (request.AppointmentId.HasValue && request.AppointmentId.Value != Guid.Empty)
            {
                linkedAppointment = await dbContext.Appointments
                    .Include(a => a.Customer)
                        .ThenInclude(c => c.Account)
                    .FirstOrDefaultAsync(a => a.Id == request.AppointmentId.Value, cancellationToken);

                if (linkedAppointment is null)
                {
                    if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                    return Result.Failure<ReceptionContractResponse>(
                        ApplicationError.NotFound("APPOINTMENT_NOT_FOUND", "Không tìm thấy lịch hẹn được chọn."));
                }

                if (linkedAppointment.Status != AppointmentStatus.Scheduled &&
                    linkedAppointment.Status != AppointmentStatus.CheckedIn)
                {
                    if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                    return Result.Failure<ReceptionContractResponse>(
                        ApplicationError.Conflict("APPOINTMENT_INVALID_STATUS",
                            $"Lịch hẹn {linkedAppointment.TrackingNumber} đang ở trạng thái '{linkedAppointment.Status}', không thể tiếp nhận."));
                }

                customerId = linkedAppointment.CustomerId;
                customerName = !string.IsNullOrWhiteSpace(request.CustomerInfo.FullName)
                    ? request.CustomerInfo.FullName.Trim()
                    : linkedAppointment.Customer.Account.FullName;
                customerPhone = !string.IsNullOrWhiteSpace(request.CustomerInfo.PhoneNumber)
                    ? request.CustomerInfo.PhoneNumber.Trim()
                    : linkedAppointment.Customer.Account.PhoneNumber;
            }
            else
            {
                var cleanPhone = request.CustomerInfo.PhoneNumber.Trim();
                var cleanName = request.CustomerInfo.FullName.Trim();
                var cleanEmail = string.IsNullOrWhiteSpace(request.CustomerInfo.Email)
                    ? null
                    : request.CustomerInfo.Email.Trim();
                var normalizedEmail = EmailIdentity.Normalize(cleanEmail);

                Account? existingCustomerAccount = null;

                if (!string.IsNullOrWhiteSpace(normalizedEmail))
                {
                    existingCustomerAccount = await dbContext.Accounts
                        .Include(a => a.CustomerProfile)
                        .FirstOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail, cancellationToken);
                }

                if (existingCustomerAccount is null)
                {
                    existingCustomerAccount = await dbContext.Accounts
                        .Include(a => a.CustomerProfile)
                        .FirstOrDefaultAsync(
                            a => a.AccountType == AccountType.Customer && a.PhoneNumber == cleanPhone,
                            cancellationToken);
                }

                if (existingCustomerAccount is not null)
                {
                    if (existingCustomerAccount.CustomerProfile is null)
                    {
                        dbContext.CustomerProfiles.Add(new CustomerProfile
                        {
                            Id = existingCustomerAccount.Id,
                            Status = CustomerStatus.Active
                        });
                    }

                    customerId = existingCustomerAccount.Id;
                    customerName = existingCustomerAccount.FullName;
                    customerPhone = existingCustomerAccount.PhoneNumber;
                }
                else
                {
                    var newCustomerId = Guid.NewGuid();
                    var newAccount = new Account
                    {
                        Id = newCustomerId,
                        AccountType = AccountType.Customer,
                        FullName = cleanName,
                        PhoneNumber = cleanPhone,
                        Email = cleanEmail,
                        Status = AccountStatus.Active,
                        CreatedByAccountId = staffId
                    };

                    var newProfile = new CustomerProfile
                    {
                        Id = newCustomerId,
                        Status = CustomerStatus.Active,
                        Account = newAccount
                    };

                    var customerRole = new AccountRole
                    {
                        AccountId = newCustomerId,
                        RoleId = Role.CustomerRoleId
                    };

                    dbContext.Accounts.Add(newAccount);
                    dbContext.CustomerProfiles.Add(newProfile);
                    dbContext.AccountRoles.Add(customerRole);

                    customerId = newCustomerId;
                    customerName = cleanName;
                    customerPhone = cleanPhone;
                }
            }

            var now = DateTimeOffset.UtcNow;
            var vietnamToday = DateOnly.FromDateTime(now.UtcDateTime.AddHours(7));
            var dateStamp = vietnamToday.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var shortDateStamp = vietnamToday.ToString("yyMMdd", CultureInfo.InvariantCulture);
            var termDays = Math.Clamp(request.TermDays > 0 ? request.TermDays : 60, 1, 60);
            var endsAt = now.AddDays(termDays);

            Guid contractId;
            string contractNumber;
            ConsignmentContract? contract = null;
            Buyout? buyout = null;

            if (!isBuyout)
            {
                var todayCount = await dbContext.ConsignmentContracts
                    .CountAsync(c => c.ContractNumber.StartsWith($"HD-{dateStamp}-"), cancellationToken);
                contractNumber = await GenerateUniqueContractNumberAsync(dateStamp, todayCount + 1, cancellationToken);
                contractId = Guid.NewGuid();

                contract = new ConsignmentContract
                {
                    Id = contractId,
                    ContractNumber = contractNumber,
                    CustomerId = customerId,
                    AppointmentId = linkedAppointment?.Id,
                    CreatedByEmployeeId = staffId,
                    SignedAt = now,
                    EndsAt = endsAt,
                    ExpiryHandling = expiryHandling,
                    Status = ConsignmentContractStatus.Active
                };
                dbContext.ConsignmentContracts.Add(contract);
            }
            else
            {
                var todayCount = await dbContext.Buyouts
                    .CountAsync(b => b.BuyoutNumber.StartsWith($"BO-{dateStamp}-"), cancellationToken);
                contractNumber = await GenerateUniqueBuyoutNumberAsync(dateStamp, todayCount + 1, cancellationToken);
                contractId = Guid.NewGuid();

                var totalPurchase = request.Items.Sum(i => i.PurchasePrice ?? decimal.Round(i.ListedPrice * 0.5m, 0, MidpointRounding.AwayFromZero));

                buyout = new Buyout
                {
                    Id = contractId,
                    BuyoutNumber = contractNumber,
                    CustomerId = customerId,
                    CreatedByEmployeeId = staffId,
                    ApprovedByEmployeeId = staffId,
                    TotalPurchasePrice = totalPurchase,
                    Status = BuyoutStatus.Approved
                };
                dbContext.Buyouts.Add(buyout);
            }

            var todayProductCount = await dbContext.Products
                .CountAsync(p => p.Barcode.StartsWith($"OCS-{shortDateStamp}-") || p.Barcode.StartsWith($"REJ-{shortDateStamp}-"), cancellationToken);

            var createdProductsDto = new List<ReceptionCreatedProductDto>();
            decimal totalEstimatedValue = 0m;
            decimal totalCustomerPayout = 0m;
            int approvedCount = 0;
            int rejectedCount = 0;

            for (var index = 0; index < request.Items.Count; index++)
            {
                var item = request.Items[index];
                var brand = brands[item.BrandId];
                var isRejected = string.Equals(item.ApprovalStatus?.Trim(), "Rejected", StringComparison.OrdinalIgnoreCase);

                var prefix = isRejected ? "REJ" : "OCS";
                var barcode = await GenerateUniqueBarcodeAsync(prefix, shortDateStamp, todayProductCount + index + 1, cancellationToken);
                var productId = Guid.NewGuid();

                var descBuilder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(item.Material))
                    descBuilder.Append($"Chất liệu: {item.Material.Trim()}. ");
                descBuilder.Append(item.HasLabel ? "Tem mác: Còn nguyên. " : "Tem mác: Đã mất/cắt. ");
                if (!string.IsNullOrWhiteSpace(item.DefectNotes))
                    descBuilder.Append($"Ghi chú lỗi: {item.DefectNotes.Trim()}. ");
                if (isRejected && !string.IsNullOrWhiteSpace(item.RejectionReason))
                    descBuilder.Append($"Lý do từ chối: {item.RejectionReason.Trim()}.");

                var product = new Product
                {
                    Id = productId,
                    Barcode = barcode,
                    Name = item.Name.Trim(),
                    Size = string.IsNullOrWhiteSpace(item.Size) ? "FreeSize" : item.Size.Trim(),
                    Description = descBuilder.ToString().Trim(),
                    ConditionPercentage = Math.Clamp(item.ConditionPercentage, 0, 100),
                    BrandId = brand.Id,
                    CategoryId = item.CategoryId,
                    ApprovalStatus = isRejected ? ProductApprovalStatus.Rejected : ProductApprovalStatus.Approved,
                    Status = isRejected ? ProductStatus.Cancelled : ProductStatus.PendingProcessing
                };

                decimal brandConfiguredRate = brand.DefaultCommissionRate > 0
                    ? brand.DefaultCommissionRate
                    : GetDefaultTierCommissionRate(brand.Tier);
                decimal effectiveRate = brandConfiguredRate;
                decimal customerPayout;

                if (!isBuyout)
                {
                    product.AssignConsignmentTerms(contractId, item.ListedPrice, brand, effectiveRate);
                    if (!isRejected)
                    {
                        var evidence = $"Phiếu tiếp nhận {contractNumber} - Thẩm định đạt tại quầy";
                        product.RecordVerifiedAgreement(item.ListedPrice, evidence);
                    }
                    else
                    {
                        product.AgreementEvidence = !string.IsNullOrWhiteSpace(item.RejectionReason)
                            ? $"Từ chối: {item.RejectionReason.Trim()}"
                            : "Không đạt tiêu chuẩn ký gửi";
                    }

                    customerPayout = isRejected ? 0m : product.CalculateEstimatedConsignorPayout();
                }
                else
                {
                    var purchasePrice = item.PurchasePrice ?? decimal.Round(item.ListedPrice * 0.5m, 0, MidpointRounding.AwayFromZero);
                    product.AssignBuyoutTerms(contractId, item.ListedPrice, purchasePrice);
                    effectiveRate = 0m;
                    customerPayout = isRejected ? 0m : purchasePrice;
                }

                dbContext.Products.Add(product);

                dbContext.ProductStatusHistories.Add(new ProductStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    PreviousStatus = null,
                    NewStatus = product.Status,
                    ChangedByAccountId = staffId,
                    Reason = $"Tiếp nhận theo phiếu {contractNumber}",
                    OccurredAt = now
                });

                var rawUrls = item.ImageUrls ?? new List<string>();
                var coverUrl = item.CoverImageUrl?.Trim();
                var orderedUrls = new List<string>();

                if (!string.IsNullOrWhiteSpace(coverUrl))
                {
                    orderedUrls.Add(coverUrl);
                    foreach (var u in rawUrls)
                    {
                        var clean = u?.Trim();
                        if (!string.IsNullOrWhiteSpace(clean) && !string.Equals(clean, coverUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            orderedUrls.Add(clean);
                        }
                    }
                }
                else
                {
                    orderedUrls = rawUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();
                }

                for (var imgIdx = 0; imgIdx < orderedUrls.Count; imgIdx++)
                {
                    var url = orderedUrls[imgIdx];
                    dbContext.ProductImages.Add(new ProductImage
                    {
                        Id = Guid.NewGuid(),
                        ProductId = productId,
                        Url = url,
                        ImageType = isRejected
                            ? ProductImageType.Incident
                            : (imgIdx == 0 ? ProductImageType.Cover : ProductImageType.Detail),
                        SortOrder = imgIdx
                    });
                }

                if (isRejected)
                {
                    rejectedCount++;
                }
                else
                {
                    approvedCount++;
                    totalEstimatedValue += item.ListedPrice;
                    totalCustomerPayout += customerPayout;
                }

                createdProductsDto.Add(new ReceptionCreatedProductDto
                {
                    Id = product.Id,
                    Barcode = product.Barcode,
                    Name = product.Name,
                    ApprovalStatus = product.ApprovalStatus.ToString(),
                    ListedPrice = product.ListedPrice,
                    CommissionRate = effectiveRate,
                    CustomerPayout = customerPayout
                });
            }

            if (linkedAppointment is not null)
            {
                linkedAppointment.Status = AppointmentStatus.Completed;
                linkedAppointment.UpdatedAt = now;
                if (!string.IsNullOrWhiteSpace(request.Note))
                {
                    linkedAppointment.Note = request.Note.Trim();
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            try
            {
                await notificationService.CreateAsync(new CreateNotificationDto
                {
                    RecipientAccountId = customerId,
                    SenderAccountId = staffId,
                    Title = isBuyout ? "Hoàn tất thu mua sản phẩm" : "Tiếp nhận ký gửi thành công",
                    Content = $"Phiếu {contractNumber} đã tiếp nhận {approvedCount} sản phẩm đạt chuẩn (Tổng niêm yết: {totalEstimatedValue:N0}đ).",
                    Type = NotificationType.System
                }, cancellationToken);
            }
            catch
            {
            }

            return Result.Success(new ReceptionContractResponse
            {
                ContractId = contractId,
                ContractNumber = contractNumber,
                IntakeType = isBuyout ? "Buyout" : "Consignment",
                SignedAt = now,
                EndsAt = endsAt,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                TotalReceivedItems = request.ActualItemCount > 0 ? request.ActualItemCount : request.Items.Count,
                TotalApprovedItems = approvedCount,
                TotalRejectedItems = rejectedCount,
                TotalEstimatedValue = totalEstimatedValue,
                TotalCustomerPayout = totalCustomerPayout,
                ExpiryHandling = expiryHandling.ToString(),
                CreatedProducts = createdProductsDto
            });
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return Result.Failure<ReceptionContractResponse>(
                ApplicationError.Conflict("CONSIGNMENT.RECEPTION_FAILED", $"Không thể lưu phiếu tiếp nhận: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<ConsignmentContractSummaryResponse>>> GetContractsAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await dbContext.ConsignmentContracts
            .AsNoTracking()
            .Include(c => c.Customer)
                .ThenInclude(cu => cu.Account)
            .Include(c => c.Products)
            .OrderByDescending(c => c.SignedAt)
            .Take(100)
            .Select(c => new ConsignmentContractSummaryResponse
            {
                Id = c.Id,
                ContractNumber = c.ContractNumber,
                CustomerId = c.CustomerId,
                CustomerName = c.Customer.Account.FullName,
                CustomerPhone = c.Customer.Account.PhoneNumber,
                AppointmentId = c.AppointmentId,
                SignedAt = c.SignedAt,
                EndsAt = c.EndsAt,
                ExpiryHandling = c.ExpiryHandling.ToString(),
                Status = c.Status.ToString(),
                ItemCount = c.Products.Count,
                TotalListedValue = c.Products.Where(p => p.ApprovalStatus == ProductApprovalStatus.Approved).Sum(p => p.ListedPrice)
            })
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ConsignmentContractSummaryResponse>>(list);
    }

    public async Task<Result<int>> GetCustomerItemHistoryCountAsync(
        Guid? customerId,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        Guid resolvedCustomerId = customerId ?? Guid.Empty;
        if (resolvedCustomerId == Guid.Empty && !string.IsNullOrWhiteSpace(phoneNumber))
        {
            var cleanPhone = phoneNumber.Trim();
            resolvedCustomerId = await dbContext.Accounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Customer && a.PhoneNumber == cleanPhone)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (resolvedCustomerId == Guid.Empty)
        {
            return Result.Success(0);
        }

        var count = await dbContext.Products
            .AsNoTracking()
            .CountAsync(p => p.ConsignmentContract != null && p.ConsignmentContract.CustomerId == resolvedCustomerId, cancellationToken);

        return Result.Success(count);
    }

    private async Task<string> GenerateUniqueContractNumberAsync(string dateStamp, int startSeq, CancellationToken ct)
    {
        var seq = startSeq;
        while (true)
        {
            var candidate = $"HD-{dateStamp}-{seq:000}";
            var exists = await dbContext.ConsignmentContracts.AnyAsync(c => c.ContractNumber == candidate, ct);
            if (!exists) return candidate;
            seq++;
        }
    }

    public async Task<Result<ReceptionDraftResponse>> SaveDraftAsync(
        Guid staffId,
        SaveReceptionDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var draftId = request.DraftId ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var response = new ReceptionDraftResponse
        {
            DraftId = draftId,
            StaffId = staffId,
            AppointmentId = request.AppointmentId,
            IntakeType = request.IntakeType ?? "Consignment",
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
            CustomerEmail = request.CustomerEmail,
            TermDays = request.TermDays,
            CommissionShop = request.CommissionShop,
            Note = request.Note,
            Items = request.Items ?? new List<CreateReceptionItemDto>(),
            SavedAt = now
        };

        if (cache is not null)
        {
            var json = JsonSerializer.Serialize(response);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(14)
            };
            await cache.SetStringAsync($"draft:reception:{draftId}", json, cacheOptions, cancellationToken);
            if (request.AppointmentId.HasValue)
            {
                await cache.SetStringAsync($"draft:reception:apt:{request.AppointmentId.Value}", draftId.ToString(), cacheOptions, cancellationToken);
            }
            else
            {
                await cache.SetStringAsync($"draft:reception:walkin:{staffId}", draftId.ToString(), cacheOptions, cancellationToken);
            }
        }

        return Result.Success(response);
    }

    public async Task<Result<ReceptionDraftResponse?>> GetDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        if (cache is null) return Result.Success<ReceptionDraftResponse?>(null);
        var json = await cache.GetStringAsync($"draft:reception:{draftId}", cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return Result.Success<ReceptionDraftResponse?>(null);
        var draft = JsonSerializer.Deserialize<ReceptionDraftResponse>(json);
        return Result.Success(draft);
    }

    public async Task<Result<ReceptionDraftResponse?>> GetDraftByAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (cache is null) return Result.Success<ReceptionDraftResponse?>(null);
        var draftIdStr = await cache.GetStringAsync($"draft:reception:apt:{appointmentId}", cancellationToken);
        if (Guid.TryParse(draftIdStr, out var draftId))
        {
            return await GetDraftAsync(draftId, cancellationToken);
        }
        return Result.Success<ReceptionDraftResponse?>(null);
    }

    public async Task<Result<ReceptionDraftResponse?>> GetLatestWalkInDraftAsync(
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        if (cache is null) return Result.Success<ReceptionDraftResponse?>(null);
        var draftIdStr = await cache.GetStringAsync($"draft:reception:walkin:{staffId}", cancellationToken);
        if (Guid.TryParse(draftIdStr, out var draftId))
        {
            return await GetDraftAsync(draftId, cancellationToken);
        }
        return Result.Success<ReceptionDraftResponse?>(null);
    }

    public async Task<Result<bool>> DeleteDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        if (cache is not null)
        {
            var draftRes = await GetDraftAsync(draftId, cancellationToken);
            if (draftRes.IsSuccess && draftRes.Data is not null)
            {
                if (draftRes.Data.AppointmentId is not null)
                {
                    await cache.RemoveAsync($"draft:reception:apt:{draftRes.Data.AppointmentId.Value}", cancellationToken);
                }
                if (draftRes.Data.StaffId is not null)
                {
                    await cache.RemoveAsync($"draft:reception:walkin:{draftRes.Data.StaffId.Value}", cancellationToken);
                }
            }
            await cache.RemoveAsync($"draft:reception:{draftId}", cancellationToken);
        }
        return Result.Success(true);
    }

    public async Task<Result<bool>> DeleteDraftByAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (cache is not null)
        {
            var draftIdStr = await cache.GetStringAsync($"draft:reception:apt:{appointmentId}", cancellationToken);
            if (Guid.TryParse(draftIdStr, out var draftId))
            {
                await DeleteDraftAsync(draftId, cancellationToken);
            }
            await cache.RemoveAsync($"draft:reception:apt:{appointmentId}", cancellationToken);
        }
        return Result.Success(true);
    }

    public async Task<Result<bool>> DeleteWalkInDraftAsync(
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        if (cache is not null)
        {
            var draftIdStr = await cache.GetStringAsync($"draft:reception:walkin:{staffId}", cancellationToken);
            if (Guid.TryParse(draftIdStr, out var draftId))
            {
                await DeleteDraftAsync(draftId, cancellationToken);
            }
            await cache.RemoveAsync($"draft:reception:walkin:{staffId}", cancellationToken);
        }
        return Result.Success(true);
    }

    private async Task<string> GenerateUniqueBuyoutNumberAsync(string dateStamp, int startSeq, CancellationToken ct)
    {
        var seq = startSeq;
        while (true)
        {
            var candidate = $"BO-{dateStamp}-{seq:000}";
            var exists = await dbContext.Buyouts.AnyAsync(b => b.BuyoutNumber == candidate, ct);
            if (!exists) return candidate;
            seq++;
        }
    }

    private async Task<string> GenerateUniqueBarcodeAsync(string prefix, string shortDateStamp, int startSeq, CancellationToken ct)
    {
        var seq = startSeq;
        while (true)
        {
            var candidate = $"{prefix}-{shortDateStamp}-{seq:0000}";
            var exists = await dbContext.Products.AnyAsync(p => p.Barcode == candidate, ct);
            if (!exists) return candidate;
            seq++;
        }
    }

    private bool IsValidStorageImageUrl(string? url, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        var clean = url.Trim();
        var isAbsolute = clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (isAbsolute)
        {
            if (!Uri.TryCreate(clean, UriKind.Absolute, out var uri))
            {
                error = $"URL hình ảnh '{url}' không phải là URL hợp lệ.";
                return false;
            }

            var isMatchedHost = false;
            var isPublicCdn = false;
            string? publicPathPrefix = null;

            if (!string.IsNullOrWhiteSpace(_r2Options?.PublicBaseUrl) &&
                Uri.TryCreate(_r2Options.PublicBaseUrl, UriKind.Absolute, out var publicUri))
            {
                if (string.Equals(uri.Host, publicUri.Host, StringComparison.OrdinalIgnoreCase) &&
                    (publicUri.IsDefaultPort || uri.Port == publicUri.Port))
                {
                    isMatchedHost = true;
                    isPublicCdn = true;
                    publicPathPrefix = Uri.UnescapeDataString(publicUri.AbsolutePath).Trim('/');
                }
            }

            var request = _httpContextAccessor?.HttpContext?.Request;
            if (request is not null)
            {
                var requestHost = request.Host.Host;
                var requestPort = request.Host.Port ?? (request.Scheme == "https" ? 443 : 80);
                if (string.Equals(uri.Host, requestHost, StringComparison.OrdinalIgnoreCase) &&
                    uri.Port == requestPort)
                {
                    isMatchedHost = true;
                }
            }

            if (uri.IsLoopback ||
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                isMatchedHost = true;
            }

            if (!isMatchedHost)
            {
                error = $"URL hình ảnh '{url}' không thuộc máy chủ API hoặc CDN R2 đã cấu hình của hệ thống.";
                return false;
            }

            clean = uri.AbsolutePath;

            if (isPublicCdn && !string.IsNullOrEmpty(publicPathPrefix))
            {
                var unescapedPath = Uri.UnescapeDataString(clean).Trim('/');
                if (unescapedPath.StartsWith(publicPathPrefix + "/", StringComparison.OrdinalIgnoreCase))
                {
                    clean = unescapedPath[(publicPathPrefix.Length + 1)..];
                }
                else if (string.Equals(unescapedPath, publicPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    clean = string.Empty;
                }
            }
        }

        clean = Uri.UnescapeDataString(clean);

        if (clean.StartsWith("/api/storage/", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["/api/storage/".Length..];
        }
        else if (clean.StartsWith("api/storage/", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["api/storage/".Length..];
        }

        clean = clean.Replace('\\', '/').Trim('/');

        if (clean.Contains("..", StringComparison.Ordinal) ||
            clean.Contains(':', StringComparison.Ordinal) ||
            clean.Contains('\0', StringComparison.Ordinal) ||
            clean.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = $"Đường dẫn hình ảnh '{url}' chứa ký tự không an toàn.";
            return false;
        }

        var isAllowedPrefix = clean.StartsWith("products/", StringComparison.OrdinalIgnoreCase) ||
                              clean.StartsWith("inspections/", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedPrefix)
        {
            error = $"Hình ảnh '{url}' không thuộc thư mục lưu trữ hợp lệ của hệ thống (phải thuộc 'products' hoặc 'inspections').";
            return false;
        }

        var ext = Path.GetExtension(clean).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            error = $"Định dạng tệp ảnh '{url}' không được hỗ trợ (chỉ chấp nhận: .jpg, .jpeg, .png, .webp).";
            return false;
        }

        return true;
    }

    private static decimal GetDefaultTierCommissionRate(BrandTier tier) => tier switch
    {
        BrandTier.Luxury => 20m,
        BrandTier.Commercial => 15m,
        BrandTier.LocalBrand => 12m,
        BrandTier.NoBrand => 10m,
        _ => 15m
    };
}
