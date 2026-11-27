using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Contracts;
using OCS.Api.Models.Products;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OCS.Api.Services;

public sealed class ContractService(AppDbContext db) : IContractService
{
    public async Task<Result<ContractResponse>> CreateAsync(CreateContractRequest request, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await PrepareAsync(request, employeeId, false, false, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Data!.Contract) : Result.Failure<ContractResponse>(result.Error!);
    }

    public async Task<Result<byte[]>> PreviewAsync(CreateContractRequest request, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await PrepareAsync(request, employeeId, true, true, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Data!.Pdf) : Result.Failure<byte[]>(result.Error!);
    }

    public async Task<Result<byte[]>> PreviewWordAsync(CreateContractRequest request, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await PrepareAsync(request, employeeId, true, true, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Data!.Word) : Result.Failure<byte[]>(result.Error!);
    }

    public async Task<Result<string>> PreviewWebAsync(CreateContractRequest request, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await PrepareAsync(request, employeeId, true, false, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Data!.Terms ?? "") : Result.Failure<string>(result.Error!);
    }

    private sealed record PreparedContract(ContractResponse Contract, byte[] Pdf, byte[] Word, string? Terms);

    private async Task<Result<PreparedContract>> PrepareAsync(CreateContractRequest request, Guid employeeId, bool preview, bool renderLegacy, CancellationToken cancellationToken)
    {
        if (request.EarlyWithdrawalFeeRate is decimal fee && (fee < 0 || fee > 100 || decimal.Round(fee, 2) != fee))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.FEE_RATE", "Tỷ lệ phí rút sớm phải từ 0 đến 100, tối đa 2 chữ số thập phân."));
        if (request.SignedAt is not DateTimeOffset signedAt || request.EndsAt is not DateTimeOffset endsAt ||
            endsAt <= signedAt || (request.ExpiryHandling != "Return" && request.ExpiryHandling != "Donate"))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_DATA", "Vui lòng kiểm tra ngày ký, ngày kết thúc và cách xử lý khi hết hạn."));

        var employee = await db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result.Failure<PreparedContract>(ApplicationError.Forbidden("CONTRACT.EMPLOYEE_REQUIRED", "Tài khoản chưa có hồ sơ nhân viên."));
        var employeeAccount = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        var template = await db.ContractTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new ContractTemplate();
        var templateVersion = template.Version;
        if (request.TemplateVersion.HasValue && request.TemplateVersion.Value != templateVersion)
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT_TEMPLATE.CHANGED", "Mẫu hợp đồng đã được cập nhật. Hãy tải lại mẫu và kiểm tra điều khoản trước khi tiếp tục."));
        if (!string.IsNullOrWhiteSpace(request.Terms))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_TERMS", "Điều khoản được quản lý trong mẫu hợp đồng và không được sửa khi lập hợp đồng."));

        Guid? customerId;
        Guid numberId;
        ContractGuestRequest? guest = request.Guest;
        Guid? appointmentId = request.AppointmentId;
        if (appointmentId.HasValue && appointmentId != Guid.Empty && !request.CustomerId.HasValue && guest is null)
        {
            var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == appointmentId, cancellationToken);
            if (appointment is null)
                return Result.Failure<PreparedContract>(ApplicationError.NotFound("APPOINTMENT.NOT_FOUND", "Không tìm thấy lịch hẹn."));
            if (appointment.Status != AppointmentStatus.CheckedIn)
                return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.INVALID_APPOINTMENT", "Lịch hẹn cần được tiếp nhận trước khi lập hợp đồng."));
            if (await db.ConsignmentContracts.AnyAsync(x => x.AppointmentId == appointmentId, cancellationToken))
                return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.ALREADY_EXISTS", "Lịch hẹn này đã có hợp đồng."));
            customerId = appointment.CustomerId;
            numberId = appointment.Id;
        }
        else if (!appointmentId.HasValue && guest is null && request.CustomerId is Guid directCustomerId && directCustomerId != Guid.Empty &&
                 request.RequestId is Guid requestId && requestId != Guid.Empty)
        {
            var exists = await db.CustomerProfiles.AnyAsync(x => x.Id == directCustomerId && x.Status == CustomerStatus.Active &&
                x.Account.AccountType == AccountType.Customer && x.Account.Status != AccountStatus.Deactivated, cancellationToken);
            if (!exists)
                return Result.Failure<PreparedContract>(ApplicationError.NotFound("CUSTOMER.NOT_FOUND", "Không tìm thấy hồ sơ khách hợp lệ."));
            customerId = directCustomerId;
            numberId = requestId;
        }
        else if (!appointmentId.HasValue && !request.CustomerId.HasValue && guest is not null &&
                 request.RequestId is Guid guestRequestId && guestRequestId != Guid.Empty)
        {
            if (string.IsNullOrWhiteSpace(guest.FullName) || guest.PhoneNumber is null || guest.PhoneNumber.Length != 10 || guest.PhoneNumber[0] != '0' || guest.PhoneNumber.Any(c => c < '0' || c > '9'))
                return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_DATA", "Vui lòng nhập họ tên và số điện thoại hợp lệ."));
            var bankParts = new[] { guest.BankCode, guest.BankAccountNumber, guest.BankAccountHolderName };
            if (bankParts.Any(x => !string.IsNullOrWhiteSpace(x)) && bankParts.Any(string.IsNullOrWhiteSpace))
                return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_BANK", "Vui lòng nhập đầy đủ mã ngân hàng, số tài khoản và tên chủ tài khoản."));
            if (await db.Accounts.AnyAsync(x => x.AccountType == AccountType.Customer && x.PhoneNumber == guest.PhoneNumber, cancellationToken))
                return Result.Failure<PreparedContract>(ApplicationError.Conflict("CUSTOMER.PHONE_EXISTS", "Số điện thoại đã có hồ sơ khách. Hãy tra cứu và chọn hồ sơ đó."));
            customerId = null;
            numberId = guestRequestId;
        }
        else
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_CUSTOMER", "Chọn một lịch hẹn hoặc một hồ sơ khách đến trực tiếp."));

        await using var draftTransaction = preview ? null : await ContractDraftLock.AcquireAsync(db, numberId, cancellationToken);
        var termsRequest = await db.ContractTermsRequests.SingleOrDefaultAsync(x => x.DraftKey == numberId &&
            (x.Status == "Pending" || x.Status == "Approved"), cancellationToken);
        if (termsRequest?.Status == "Pending")
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.TERMS_PENDING", "Yêu cầu điều chỉnh điều khoản đang chờ quản lý xử lý."));
        if (termsRequest?.Status == "Approved" && (request.TermsRequestId != termsRequest.Id || termsRequest.RequestedBy != employeeId) ||
            request.TermsRequestId.HasValue && (termsRequest is null || request.TermsRequestId != termsRequest.Id))
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.TERMS_CHANGED", "Hãy tải lại yêu cầu điều khoản đã được quản lý xử lý."));
        if (termsRequest?.Status == "Approved" && termsRequest.ApprovedTemplate is null)
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.TERMS_INVALID", "Mẫu điều khoản riêng không hợp lệ. Hãy liên hệ quản lý."));

        if (guest is null && request.SaveCustomer)
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_DATA", "Chỉ lưu hồ sơ mới cho khách vãng lai."));

        if (customerId.HasValue)
        {
            var existing = await GetCustomerAsync(customerId.Value, cancellationToken);
            if (!existing.IsSuccess) return Result.Failure<PreparedContract>(existing.Error!);
            var customer = existing.Data!;
            guest = new ContractGuestRequest
            {
                FullName = customer.FullName,
                PhoneNumber = customer.PhoneNumber,
                Email = customer.Email,
                IdentityNumber = customer.IdentityNumber,
                Address = customer.Address,
                BankCode = customer.BankCode,
                BankAccountNumber = customer.BankAccountNumber,
                BankAccountHolderName = customer.BankAccountHolderName
            };
            if (request.CustomerDetails is { } details)
                guest = guest with
                {
                    Email = details.Email,
                    IdentityNumber = details.IdentityNumber,
                    Address = details.Address,
                    BankCode = details.BankCode,
                    BankAccountNumber = details.BankAccountNumber,
                    BankAccountHolderName = details.BankAccountHolderName
                };
        }
        else if (request.CustomerDetails is not null)
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_DATA", "Thông tin bổ sung chỉ dùng cho khách đã có hồ sơ."));

        if (termsRequest is not null && termsRequest.CustomerPhone != guest!.PhoneNumber?.Trim())
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.TERMS_CUSTOMER_CHANGED", "Khách hàng đã thay đổi. Hãy hủy yêu cầu điều khoản cũ và gửi yêu cầu mới."));

        var bankFields = new[] { guest!.BankCode, guest.BankAccountNumber, guest.BankAccountHolderName };
        if (guest.Email?.Length > 254 || guest.IdentityNumber?.Length > 20 || guest.Address?.Length > 500 ||
            guest.BankCode?.Length > 50 || guest.BankAccountNumber?.Length > 50 || guest.BankAccountHolderName?.Length > 200 ||
            (!string.IsNullOrWhiteSpace(guest.Email) && EmailIdentity.Normalize(guest.Email) is null) ||
            (bankFields.Any(x => !string.IsNullOrWhiteSpace(x)) && bankFields.Any(string.IsNullOrWhiteSpace)))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_DATA", "Vui lòng kiểm tra thông tin khách và nhập đủ thông tin tài khoản nhận tiền."));

        byte[]? signatureBytes = null;
        if (request.Signature is { } signature)
        {
            if (signature.ContentType is not ("image/png" or "image/jpeg") || signature.Data is null || signature.Data.Length > 700000)
                return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_SIGNATURE", "Chữ ký phải là ảnh PNG hoặc JPEG không quá 512 KB."));
            try { signatureBytes = Convert.FromBase64String(signature.Data); }
            catch (FormatException) { return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_SIGNATURE", "Không thể đọc ảnh chữ ký.")); }
            if (signatureBytes.Length > 512 * 1024 || !ContractPdfRenderer.IsValidSignature(signatureBytes, signature.ContentType))
                return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_SIGNATURE", "Ảnh chữ ký không hợp lệ hoặc vượt quá giới hạn."));
        }
        if (!preview && signatureBytes is null)
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.SIGNATURE_REQUIRED", "Khách cần ký xác nhận trước khi lưu hợp đồng."));

        if (request.Products is null || request.Products.Count > 100 || request.Products.Any(x => x is null))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_PRODUCTS", "Danh sách sản phẩm không hợp lệ."));
        var brandIds = request.Products.Select(x => x.BrandId).Distinct().ToArray();
        var categoryIds = request.Products.Select(x => x.CategoryId).Distinct().ToArray();
        var locationIds = request.Products.Where(x => x.StorageLocationId.HasValue).Select(x => x.StorageLocationId!.Value).Distinct().ToArray();
        var brands = await db.Brands.Where(x => x.IsActive && brandIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var categories = await db.Categories.Where(x => x.IsActive && categoryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var locations = await db.StorageLocations.Where(x => x.IsActive && locationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (brands.Count != brandIds.Length || categories.Count != categoryIds.Length || locations.Count != locationIds.Length || request.Products.Any(x =>
            string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 200 || string.IsNullOrWhiteSpace(x.Size) || x.Size.Length > 30 ||
            x.ConditionPercentage is < 0 or > 100 || x.ListedPrice < 0 || x.ListedPrice > 9999999999999999m || decimal.Round(x.ListedPrice, 2) != x.ListedPrice ||
            x.AgreedCommissionRate is null or < 0 or > 100 || decimal.Round(x.AgreedCommissionRate.Value, 2) != x.AgreedCommissionRate.Value ||
            x.Description?.Length > 2000 || (x.Barcode is not null && !Regex.IsMatch(x.Barcode, "^OCS-[0-9A-F]{16}$", RegexOptions.CultureInvariant))))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_PRODUCTS", "Thông tin sản phẩm hoặc danh mục không hợp lệ."));
        // Legacy callers without a barcode receive a stable value for the same draft.
        var barcodes = request.Products.Select((item, index) => item.Barcode ??
            "OCS-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{numberId:N}:{index}")))[..16]).ToArray();
#pragma warning disable CA1304, CA1311 // EF Core dich ToUpper sang SQL.
        if (barcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != barcodes.Length ||
            await db.Products.AnyAsync(product => product.Barcode != null && barcodes.Contains(product.Barcode.Trim().ToUpper()), cancellationToken))
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("PRODUCT.DUPLICATE_BARCODE", "Mã barcode đã tồn tại. Hãy tạo lại mã cho sản phẩm trước khi tiếp tục."));
#pragma warning restore CA1304, CA1311
        var decodedImages = new List<(string ContentType, byte[] Content)[]>();
        long totalImageBytes = 0;
        foreach (var item in request.Products)
        {
            if (item.Images is null || item.Images.Count > 5 || item.Images.Any(x => x is null))
                return Result.Failure<PreparedContract>(ApplicationError.Validation("PRODUCT.INVALID_IMAGES", "Mỗi sản phẩm được tải tối đa 5 ảnh."));
            var files = new List<(string ContentType, byte[] Content)>();
            foreach (var img in item.Images)
            {
                if (img.ContentType is not ("image/jpeg" or "image/png") || img.Data is null || img.Data.Length > 2800000)
                    return Result.Failure<PreparedContract>(ApplicationError.Validation("PRODUCT.INVALID_IMAGE", "Ảnh phải là JPEG hoặc PNG và không quá 2 MB."));
                byte[] bytes;
                try { bytes = Convert.FromBase64String(img.Data); }
                catch (FormatException) { return Result.Failure<PreparedContract>(ApplicationError.Validation("PRODUCT.INVALID_IMAGE", "Không thể đọc dữ liệu ảnh.")); }
                var isPng = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                var isJpeg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
                if (bytes.Length > 2 * 1024 * 1024 || !(img.ContentType == "image/png" ? isPng : isJpeg))
                    return Result.Failure<PreparedContract>(ApplicationError.Validation("PRODUCT.INVALID_IMAGE", "Nội dung ảnh không hợp lệ hoặc vượt quá 2 MB."));
                totalImageBytes += bytes.Length;
                if (totalImageBytes > 8 * 1024 * 1024)
                    return Result.Failure<PreparedContract>(ApplicationError.Validation("PRODUCT.IMAGES_TOO_LARGE", "Tổng dung lượng ảnh không được vượt quá 8 MB."));
                files.Add((img.ContentType, bytes));
            }
            decodedImages.Add(files.ToArray());
        }

        var approvedTemplate = termsRequest?.ApprovedTemplate;
        var terms = approvedTemplate is null ? (string.IsNullOrWhiteSpace(template.DefaultTerms) ? null : template.DefaultTerms) : ExtractTermsFromWord(approvedTemplate);
        if (approvedTemplate is not null && string.IsNullOrWhiteSpace(terms))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.TERMS_INVALID", "Không thể đọc điều khoản đã duyệt."));

        // Số cố định theo lịch hẹn hoặc mã yêu cầu, kết hợp unique index để chặn yêu cầu lặp.
        var contractNumber = $"HD-{numberId:N}";
        if (await db.ConsignmentContracts.AnyAsync(x => x.ContractNumber == contractNumber, cancellationToken))
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.ALREADY_EXISTS", "Yêu cầu này đã tạo hợp đồng. Vui lòng kiểm tra lại trước khi thử tiếp."));
        var contract = new ConsignmentContract
        {
            ContractNumber = contractNumber,
            AppointmentId = appointmentId,
            CustomerId = customerId,
            CreatedByEmployeeId = employeeId,
            SignedAt = signedAt.ToUniversalTime(),
            EndsAt = endsAt.ToUniversalTime(),
            ExpiryHandling = request.ExpiryHandling == "Return" ? ExpiryHandling.Return : ExpiryHandling.Donate,
            Status = ConsignmentContractStatus.Active
        };
        contract.CustomerNameSnapshot = guest!.FullName.Trim();
        contract.CustomerPhoneSnapshot = guest.PhoneNumber.Trim();
        contract.CustomerEmailSnapshot = guest.Email?.Trim();
        contract.CustomerIdentitySnapshot = guest.IdentityNumber?.Trim();
        contract.CustomerAddressSnapshot = guest.Address?.Trim();
        contract.BankCodeSnapshot = guest.BankCode?.Trim();
        contract.BankAccountNumberSnapshot = guest.BankAccountNumber?.Trim();
        contract.BankAccountHolderSnapshot = guest.BankAccountHolderName?.Trim();

        var systemConfig = await SystemConfigService.ReadEffectiveAsync(db, cancellationToken);
        if (request.EarlyWithdrawalFeeRate.HasValue &&
            decimal.Round(request.EarlyWithdrawalFeeRate.Value, 2, MidpointRounding.AwayFromZero) != systemConfig.EarlyWithdrawalFeeRate)
        {
            return Result.Failure<PreparedContract>(ApplicationError.Conflict(
                "CONTRACT.SYSTEM_CONFIG_CHANGED",
                "Cấu hình tỷ lệ phí rút hàng sớm vừa thay đổi. Vui lòng xem lại hợp đồng trước khi lưu."));
        }
        var earlyWithdrawalFeeRate = systemConfig.EarlyWithdrawalFeeRate;
        contract.EarlyWithdrawalFeeRate = earlyWithdrawalFeeRate;
        var termsResult = ContractTermsFormatter.BuildEffectiveTerms(terms, earlyWithdrawalFeeRate, approvedTemplate is not null);
        if (!termsResult.IsSuccess)
            return Result.Failure<PreparedContract>(termsResult.Error!);
        terms = termsResult.Data!;
        contract.TermsSnapshot = terms;
        contract.TemplateTitleSnapshot = template.Title;
        contract.OrganizationNameSnapshot = template.OrganizationName;
        contract.HeaderTextSnapshot = template.HeaderText;
        contract.FooterTextSnapshot = template.FooterText;
        contract.EmployeeNameSnapshot = employeeAccount?.FullName;
        contract.EmployeeTitleSnapshot = employee.JobTitle;
        contract.EmployeePhoneSnapshot = employeeAccount?.PhoneNumber;

        if (request.SaveCustomer)
        {
            var email = string.IsNullOrWhiteSpace(guest.Email) ? null : guest.Email.Trim();
            var identity = string.IsNullOrWhiteSpace(guest.IdentityNumber) ? null : guest.IdentityNumber.Trim();
            if (email is not null && EmailIdentity.Normalize(email) is null)
                return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_EMAIL", "Email không đúng định dạng được hỗ trợ."));
            if ((email is not null && await db.Accounts.AnyAsync(x => x.NormalizedEmail == EmailIdentity.Normalize(email), cancellationToken)) ||
                (identity is not null && await db.Accounts.AnyAsync(x => x.AccountType == AccountType.Customer && x.IdentityNumber == identity, cancellationToken)))
                return Result.Failure<PreparedContract>(ApplicationError.Conflict("CUSTOMER.ALREADY_EXISTS", "Email hoặc giấy tờ đã có hồ sơ khách."));
            var account = new Account
            {
                AccountType = AccountType.Customer,
                Status = AccountStatus.PendingActivation,
                FullName = guest.FullName.Trim(),
                PhoneNumber = guest.PhoneNumber.Trim(),
                Email = email,
                IdentityNumber = identity,
                CreatedByAccountId = employeeId
            };
            var profile = new CustomerProfile
            {
                Id = account.Id,
                Account = account,
                Address = guest.Address?.Trim(),
                BankCode = guest.BankCode?.Trim(),
                BankAccountNumber = guest.BankAccountNumber?.Trim(),
                BankAccountHolderName = guest.BankAccountHolderName?.Trim()
            };
            if (!preview)
            {
                db.CustomerProfiles.Add(profile);
                db.AccountRoles.Add(new AccountRole { AccountId = account.Id, RoleId = Role.CustomerRoleId });
            }
            contract.CustomerId = account.Id;
        }
        var pdfProducts = new List<(Product Product, string Brand, string Category)>();
        for (var productIndex = 0; productIndex < request.Products.Count; productIndex++)
        {
            var item = request.Products[productIndex];
            var product = new Product
            {
                Name = item.Name.Trim(),
                Size = item.Size.Trim(),
                Description = item.Description?.Trim(),
                ConditionPercentage = item.ConditionPercentage,
                CategoryId = item.CategoryId,
                StorageLocationId = item.StorageLocationId
            };
            product.Barcode = barcodes[productIndex];
            var brand = brands[item.BrandId];
            var effectiveRate = item.AgreedCommissionRate
                ?? (brand.DefaultCommissionRate > 0
                    ? brand.DefaultCommissionRate
                    : SystemConfigService.GetTierCommissionRate(systemConfig.CommissionTierRates, brand.Tier));
            product.AssignConsignmentTerms(contract.Id, item.ListedPrice, brand, effectiveRate);
            pdfProducts.Add((product, brand.Name, categories[item.CategoryId].Name));
            for (var imageIndex = 0; imageIndex < decodedImages[productIndex].Length; imageIndex++)
            {
                var file = decodedImages[productIndex][imageIndex];
                var image = new ProductImage
                {
                    ProductId = product.Id,
                    Content = file.Content,
                    ContentType = file.ContentType,
                    ImageType = imageIndex == 0 ? ProductImageType.Cover : ProductImageType.Detail,
                    SortOrder = imageIndex
                };
                image.Url = $"/api/product-images/{image.Id:D}";
                product.Images.Add(image);
            }
            if (!preview) db.Products.Add(product);
        }
        byte[] word = [], pdf = [];
        if (renderLegacy)
        {
            var wordTemplate = await db.WordDocumentTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == WordTemplateSamples.Contract, cancellationToken);
            var templateBytes = wordTemplate?.Content ?? WordTemplateSamples.Create(WordTemplateSamples.Contract);
            var productRows = pdfProducts.Select((item, index) => WordTemplateData.Product(item.Product, item.Brand, item.Category,
                item.Product.StorageLocationId is Guid locationId && locations.TryGetValue(locationId, out var location) ? location.Name : null, index + 1)).ToArray();
            var storeAddress = await db.SystemParameters.AsNoTracking().Where(x => x.Key == "store.address").Select(x => x.Value).SingleOrDefaultAsync(cancellationToken);
            var values = WordTemplateData.Contract(contract, template, employeeAccount?.FullName ?? "", employee.JobTitle,
                employeeAccount?.PhoneNumber ?? "", terms, pdfProducts.Select(item => item.Product).ToArray(), preview, storeAddress);
            var images = signatureBytes is null ? null : new Dictionary<string, byte[]> { ["customer.signature"] = signatureBytes };
            var effectiveTemplate = approvedTemplate is not null && approvedTemplate.AsSpan().StartsWith("PK"u8) ? approvedTemplate : templateBytes;
            word = WordTemplateFiller.Fill(effectiveTemplate, WordTemplateSamples.Contract, values, productRows, images);
            pdf = ContractPdfRenderer.Render(contract, employeeAccount?.FullName ?? "", employee.JobTitle,
                employeeAccount?.PhoneNumber ?? "", pdfProducts, terms, signatureBytes, true, template);
        }
        if (preview) return Result.Success(new PreparedContract(Map(contract), pdf, word, terms));
        db.ConsignmentContracts.Add(contract);
        db.ContractDocuments.Add(new ContractDocument
        {
            ContractId = contract.Id,
            Content = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                template.Title,
                template.OrganizationName,
                template.HeaderText,
                template.FooterText,
                CustomerName = contract.CustomerNameSnapshot,
                CustomerPhone = contract.CustomerPhoneSnapshot,
                Products = pdfProducts.Select(item => new
                {
                    item.Product.Id,
                    item.Product.Name,
                    item.Product.Barcode,
                    item.Product.Size,
                    item.Brand,
                    item.Category,
                    item.Product.Material,
                    item.Product.Description,
                    item.Product.ConditionPercentage,
                    ListedPrice = item.Product.AgreedListedPrice ?? item.Product.ListedPrice,
                    CommissionRate = item.Product.AgreedCommissionRate ?? 0,
                    Images = item.Product.Images.OrderBy(i => i.SortOrder).Select(i => i.Content != null && i.ContentType != null ? $"data:{i.ContentType};base64,{Convert.ToBase64String(i.Content)}" : i.Url).ToArray()
                }).ToArray()
            }, System.Text.Json.JsonSerializerOptions.Web),
            Terms = terms,
            TemplateVersion = termsRequest is null ? templateVersion.ToString("N") : "terms-" + termsRequest.Id.ToString("N"),
            SignatureContent = signatureBytes,
            SignatureContentType = request.Signature?.ContentType
        });
        if (termsRequest is not null) { termsRequest.Status = "Used"; termsRequest.Version = Guid.NewGuid(); }
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.TERMS_CHANGED", "Yêu cầu điều khoản đã thay đổi. Hãy tải lại.")); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ConsignmentContracts_ContractNumber" })
        {
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.ALREADY_EXISTS", "Yêu cầu này đã tạo hợp đồng. Vui lòng kiểm tra lại trước khi thử tiếp."));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Products_Barcode" or "IX_Products_Barcode_Normalized" })
        {
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("PRODUCT.DUPLICATE_BARCODE", "Mã sản phẩm đã được sử dụng. Hãy chọn ‘Tạo mã khác’ cho sản phẩm bị trùng và xem trước lại hợp đồng."));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT.DUPLICATE_DATA", "Mã sản phẩm hoặc thông tin hồ sơ đã tồn tại. Vui lòng kiểm tra lại."));
        }
        if (draftTransaction is not null) await draftTransaction.CommitAsync(cancellationToken);
        return Result.Success(new PreparedContract(Map(contract), pdf, word, terms));
    }

    private static string? ExtractTermsFromWord(byte[] docxBytes)
    {
        if (docxBytes.AsSpan().StartsWith("OCS-TEXT:\n"u8)) return Encoding.UTF8.GetString(docxBytes.AsSpan(10));
        try
        {
            using var stream = new MemoryStream(docxBytes);
            using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return null;
            var paragraphs = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();
            var startIndex = paragraphs.FindIndex(p => p.InnerText.Contains("ĐIỀU KHOẢN", StringComparison.OrdinalIgnoreCase));
            if (startIndex == -1) return null;
            var lines = new List<string>();
            for (var i = startIndex + 1; i < paragraphs.Count; i++)
            {
                var p = paragraphs[i];
                var text = p.InnerText.Trim();
                if (p.InnerText.Contains("CHỮ KÝ KHÁCH HÀNG", StringComparison.OrdinalIgnoreCase) ||
                    p.InnerText.Contains("[Chữ ký khách hàng]", StringComparison.OrdinalIgnoreCase))
                    break;
                if (!string.IsNullOrEmpty(text) && !text.StartsWith('[') &&
                    !text.Contains(WordTemplateSamples.TermsInstruction, StringComparison.Ordinal))
                    lines.Add(text);
            }
            var result = string.Join("\n", lines);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GenerateBarcodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var barcode = "OCS-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
#pragma warning disable CA1304, CA1311, CA1862 // EF Core dich ToUpper sang SQL.
            if (!await db.Products.AnyAsync(product => product.Barcode != null && product.Barcode.Trim().ToUpper() == barcode, cancellationToken)) return barcode;
#pragma warning restore CA1304, CA1311, CA1862
        }
    }

    public async Task<IReadOnlyList<BarcodeLabelProduct>?> GetBarcodeProductsAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.ConsignmentContracts.AsNoTracking().AnyAsync(contract => contract.Id == id, cancellationToken)) return null;
        return await db.Products.AsNoTracking().Where(product => product.ConsignmentContractId == id).OrderBy(product => product.Name)
            .Select(product => new BarcodeLabelProduct(
                product.Id,
                product.Name,
                product.Barcode,
                product.ListedPrice,
                product.Size,
                product.Brand.Name,
                product.ConditionPercentage > 0 ? $"{product.ConditionPercentage}%" : null
            )).ToListAsync(cancellationToken);
    }

    public async Task<ContractImageContent?> GetImageAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ProductImages.AsNoTracking().Where(x => x.Id == id && x.Content != null && x.ContentType != null && x.ImageType != ProductImageType.Incident)
            .Select(x => new ContractImageContent(x.Content!, x.ContentType!, x.Product.Status == ProductStatus.OnSale && x.Product.Brand.IsActive && x.Product.Category.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Result<ContractCustomerResponse>> FindCustomerByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length != 10 || phone[0] != '0' || phone.Any(c => c < '0' || c > '9'))
            return Result.Failure<ContractCustomerResponse>(ApplicationError.Validation("CUSTOMER.INVALID_PHONE", "Vui lòng nhập số điện thoại gồm 10 chữ số bắt đầu bằng 0."));
        var customer = await db.CustomerProfiles.AsNoTracking()
            .Where(x => x.Status == CustomerStatus.Active && x.Account.AccountType == AccountType.Customer &&
                x.Account.Status != AccountStatus.Deactivated && x.Account.PhoneNumber == phone)
            .Select(x => new ContractCustomerResponse(x.Id, x.Account.FullName, x.Account.PhoneNumber, x.Account.Email, x.Account.IdentityNumber,
                x.Address, x.BankCode, x.BankAccountNumber, x.BankAccountHolderName, x.Account.Version, x.Version))
            .SingleOrDefaultAsync(cancellationToken);
        return customer is null
            ? Result.Failure<ContractCustomerResponse>(ApplicationError.NotFound("CUSTOMER.NOT_FOUND", "Không tìm thấy hồ sơ khách phù hợp."))
            : Result.Success(customer);
    }

    public async Task<Result<ContractCustomerResponse>> GetCustomerAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.CustomerProfiles.AsNoTracking()
            .Where(x => x.Id == id && x.Status == CustomerStatus.Active && x.Account.AccountType == AccountType.Customer && x.Account.Status != AccountStatus.Deactivated)
            .Select(x => new ContractCustomerResponse(x.Id, x.Account.FullName, x.Account.PhoneNumber, x.Account.Email, x.Account.IdentityNumber,
                x.Address, x.BankCode, x.BankAccountNumber, x.BankAccountHolderName, x.Account.Version, x.Version)).SingleOrDefaultAsync(cancellationToken);
        return customer is null ? Result.Failure<ContractCustomerResponse>(ApplicationError.NotFound("CUSTOMER.NOT_FOUND", "Không tìm thấy hồ sơ khách hợp lệ.")) : Result.Success(customer);
    }

    public async Task<Result<ContractEmployeeResponse>> GetEmployeeAsync(Guid id, CancellationToken cancellationToken)
    {
        var employee = await db.EmployeeProfiles.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ContractEmployeeResponse(x.Account.FullName, x.Account.PhoneNumber, x.JobTitle)).SingleOrDefaultAsync(cancellationToken);
        return employee is null ? Result.Failure<ContractEmployeeResponse>(ApplicationError.NotFound("EMPLOYEE.NOT_FOUND", "Không tìm thấy hồ sơ nhân viên.")) : Result.Success(employee);
    }

    public async Task<Result<ContractCustomerResponse>> UpdateCustomerAsync(Guid id, UpdateContractCustomerRequest request, CancellationToken cancellationToken)
    {
        var profile = await db.CustomerProfiles.Include(x => x.Account).SingleOrDefaultAsync(x => x.Id == id && x.Status == CustomerStatus.Active && x.Account.AccountType == AccountType.Customer && x.Account.Status != AccountStatus.Deactivated, cancellationToken);
        if (profile is null) return Result.Failure<ContractCustomerResponse>(ApplicationError.NotFound("CUSTOMER.NOT_FOUND", "Không tìm thấy hồ sơ khách hợp lệ."));
        var account = profile.Account;
        if (account.Version != request.Version || profile.Version != request.ProfileVersion) return Result.Failure<ContractCustomerResponse>(ApplicationError.Conflict("CUSTOMER.CONCURRENT_UPDATE", "Hồ sơ đã thay đổi. Vui lòng tải lại."));
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var identity = string.IsNullOrWhiteSpace(request.IdentityNumber) ? null : request.IdentityNumber.Trim();
        var normalized = email is null ? null : EmailIdentity.Normalize(email);
        if (email is not null && normalized is null) return Result.Failure<ContractCustomerResponse>(ApplicationError.Validation("CUSTOMER.INVALID_EMAIL", "Email không hợp lệ."));
        if ((normalized is not null && await db.Accounts.AnyAsync(x => x.Id != id && x.NormalizedEmail == normalized, cancellationToken)) ||
            (identity is not null && await db.Accounts.AnyAsync(x => x.Id != id && x.AccountType == AccountType.Customer && x.IdentityNumber == identity, cancellationToken)))
            return Result.Failure<ContractCustomerResponse>(ApplicationError.Conflict("CUSTOMER.ALREADY_EXISTS", "Email hoặc giấy tờ đã được sử dụng."));
        var bankCode = string.IsNullOrWhiteSpace(request.BankCode) ? null : request.BankCode.Trim().ToUpperInvariant();
        var bankNumber = string.IsNullOrWhiteSpace(request.BankAccountNumber) ? null : request.BankAccountNumber.Trim();
        var bankHolder = string.IsNullOrWhiteSpace(request.BankAccountHolderName) ? null : request.BankAccountHolderName.Trim();
        var bankParts = new[] { bankCode, bankNumber, bankHolder };
        if (bankParts.Any(x => x is not null) && bankParts.Any(x => x is null)) return Result.Failure<ContractCustomerResponse>(ApplicationError.Validation("CUSTOMER.INVALID_BANK", "Vui lòng nhập đầy đủ thông tin tài khoản ngân hàng."));
        var bankChanged = profile.BankCode != bankCode || profile.BankAccountNumber != bankNumber || profile.BankAccountHolderName != bankHolder;
        if (bankChanged && await db.Payouts.AnyAsync(x => x.ConsignmentContract.CustomerId == id && x.Status == PayoutStatus.Pending, cancellationToken))
            return Result.Failure<ContractCustomerResponse>(ApplicationError.Conflict("CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT", "Không thể đổi thông tin ngân hàng khi có giải ngân chờ xử lý."));
        var emailChanged = EmailIdentity.Normalize(account.Email) != normalized;
        if (emailChanged && account.Status == AccountStatus.Locked)
            return Result.Failure<ContractCustomerResponse>(ApplicationError.Conflict("CUSTOMER.ACCOUNT_LOCKED", "Không thể đổi email khi tài khoản đang bị khóa."));
        account.Email = email;
        account.IdentityNumber = identity;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        if (emailChanged)
        {
            account.Status = AccountStatus.PendingActivation;
            account.EmailVerifiedAt = null;
            var sessions = await db.AuthSessions.Where(x => x.AccountId == id && x.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var session in sessions) session.RevokedAt = DateTimeOffset.UtcNow;
        }
        profile.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        profile.BankCode = bankCode;
        profile.BankAccountNumber = bankNumber;
        profile.BankAccountHolderName = bankHolder;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result.Failure<ContractCustomerResponse>(ApplicationError.Conflict("CUSTOMER.CONCURRENT_UPDATE", "Hồ sơ đã thay đổi. Vui lòng tải lại.")); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Result.Failure<ContractCustomerResponse>(ApplicationError.Conflict("CUSTOMER.ALREADY_EXISTS", "Email hoặc giấy tờ đã được sử dụng.")); }
        return Result.Success(new ContractCustomerResponse(id, account.FullName, account.PhoneNumber, account.Email, account.IdentityNumber,
            profile.Address, profile.BankCode, profile.BankAccountNumber, profile.BankAccountHolderName, account.Version, profile.Version));
    }

    public async Task<ContractOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var systemConfig = await SystemConfigService.ReadEffectiveAsync(db, cancellationToken);
        var brands = (await db.Brands.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.DefaultCommissionRate, x.Tier })
            .ToListAsync(cancellationToken))
            .Select(x => new ContractOption(
                x.Id,
                x.Name,
                x.DefaultCommissionRate > 0
                    ? x.DefaultCommissionRate
                    : SystemConfigService.GetTierCommissionRate(systemConfig.CommissionTierRates, x.Tier)))
            .ToList();
        var categories = await db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ContractOption(x.Id, x.Name, null)).ToListAsync(cancellationToken);
        var locations = await db.StorageLocations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ContractOption(x.Id, x.Name, null)).ToListAsync(cancellationToken);
        return new ContractOptionsResponse(brands, categories, locations, systemConfig.ConsignmentDurationDays, systemConfig.EarlyWithdrawalFeeRate);
    }

    public async Task<Result<ContractResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await db.ConsignmentContracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return contract is null
            ? Result.Failure<ContractResponse>(ApplicationError.NotFound("CONTRACT.NOT_FOUND", "Không tìm thấy hợp đồng."))
            : Result.Success(Map(contract));
    }

    public Task<Result<ContractDetailDto>> GetDetailAsync(Guid id, CancellationToken cancellationToken) =>
        GetDetailAsync(id.ToString(), cancellationToken);

    public async Task<Result<ContractDetailDto>> GetDetailAsync(string idOrNumber, CancellationToken cancellationToken)
    {
        var parsedGuid = Guid.TryParse(idOrNumber, out var parsed) ? parsed : Guid.Empty;
        var contract = await db.ConsignmentContracts
            .Include(x => x.Customer).ThenInclude(x => x!.Account)
            .Include(x => x.Appointment).ThenInclude(x => x!.Customer).ThenInclude(x => x!.Account)
            .Include(x => x.Products).ThenInclude(x => x.Brand)
            .Include(x => x.Products).ThenInclude(x => x.Category)
            .Include(x => x.CreatedByEmployee).ThenInclude(x => x.Account)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == parsedGuid || x.ContractNumber == idOrNumber, cancellationToken);

        if (contract is null)
            return Result.Failure<ContractDetailDto>(ApplicationError.NotFound("CONTRACT.NOT_FOUND", "Không tìm thấy hợp đồng."));

        Account? directCustomerAccount = null;
        CustomerProfile? directCustomerProfile = null;
        if (contract.CustomerId.HasValue)
        {
            directCustomerAccount = contract.Customer?.Account ?? await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == contract.CustomerId.Value, cancellationToken);
            directCustomerProfile = contract.Customer ?? await db.CustomerProfiles.AsNoTracking().SingleOrDefaultAsync(cp => cp.Id == contract.CustomerId.Value, cancellationToken);
        }

        Account? directEmployeeAccount = null;
        EmployeeProfile? directEmployeeProfile = null;
        if (contract.CreatedByEmployeeId != Guid.Empty)
        {
            directEmployeeAccount = contract.CreatedByEmployee?.Account ?? await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == contract.CreatedByEmployeeId, cancellationToken);
            directEmployeeProfile = contract.CreatedByEmployee ?? await db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(ep => ep.Id == contract.CreatedByEmployeeId, cancellationToken);
        }

        var document = await db.ContractDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == contract.Id, cancellationToken);
        var template = await db.ContractTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new ContractTemplate();

        string? sigDataUrl = null;
        if (document?.SignatureContent is { Length: > 0 } sigBytes)
        {
            var sigType = string.IsNullOrWhiteSpace(document.SignatureContentType) ? "image/png" : document.SignatureContentType;
            sigDataUrl = $"data:{sigType};base64,{Convert.ToBase64String(sigBytes)}";
        }

        string? title = null;
        string? organizationName = null;
        string? headerText = null;
        string? footerText = null;
        string? snapCustName = null;
        string? snapCustPhone = null;

        if (document?.Content is { Length: > 0 } content && content[0] == (byte)'{')
        {
            try
            {
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;
                if (root.TryGetProperty("title", out var p1) || root.TryGetProperty("Title", out p1))
                    title = p1.GetString();
                if (root.TryGetProperty("organizationName", out var p2) || root.TryGetProperty("OrganizationName", out p2))
                    organizationName = p2.GetString();
                if (root.TryGetProperty("headerText", out var p3) || root.TryGetProperty("HeaderText", out p3))
                    headerText = p3.GetString();
                if (root.TryGetProperty("footerText", out var p4) || root.TryGetProperty("FooterText", out p4))
                    footerText = p4.GetString();
                if (root.TryGetProperty("customerName", out var p5) || root.TryGetProperty("CustomerName", out p5))
                    snapCustName = p5.GetString();
                if (root.TryGetProperty("customerPhone", out var p6) || root.TryGetProperty("CustomerPhone", out p6))
                    snapCustPhone = p6.GetString();
            }
            catch (System.Text.Json.JsonException) { }
        }

        title = !string.IsNullOrWhiteSpace(contract.TemplateTitleSnapshot)
            ? contract.TemplateTitleSnapshot
            : (string.IsNullOrWhiteSpace(title) ? template.Title : title);

        organizationName = !string.IsNullOrWhiteSpace(contract.OrganizationNameSnapshot)
            ? contract.OrganizationNameSnapshot
            : (string.IsNullOrWhiteSpace(organizationName) ? template.OrganizationName : organizationName);

        headerText = !string.IsNullOrWhiteSpace(contract.HeaderTextSnapshot)
            ? contract.HeaderTextSnapshot
            : (string.IsNullOrWhiteSpace(headerText) ? template.HeaderText : headerText);

        footerText = !string.IsNullOrWhiteSpace(contract.FooterTextSnapshot)
            ? contract.FooterTextSnapshot
            : (string.IsNullOrWhiteSpace(footerText) ? template.FooterText : footerText);

        var terms = !string.IsNullOrWhiteSpace(contract.TermsSnapshot)
            ? contract.TermsSnapshot
            : (document?.Terms ?? template.DefaultTerms);
        var products = contract.Products
            .Where(x => x.ApprovalStatus != Consts.ProductApprovalStatus.Rejected)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ContractDetailProductDto(
                x.Id,
                x.Name,
                x.Barcode,
                x.Size,
                x.ConditionPercentage,
                x.ListedPrice,
                x.AgreedCommissionRate,
                x.Brand?.Name ?? "",
                x.Category?.Name ?? ""
            )).ToList();

        if (products.Count == 0 && document?.Content is { Length: > 0 } cnt && cnt[0] == (byte)'{')
        {
            try
            {
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(cnt);
                var root = jsonDoc.RootElement;
                if ((root.TryGetProperty("products", out var pArr) || root.TryGetProperty("Products", out pArr)) &&
                    pArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in pArr.EnumerateArray())
                    {
                        var pId = (item.TryGetProperty("id", out var idElem) || item.TryGetProperty("Id", out idElem)) && Guid.TryParse(idElem.GetString(), out var gid) ? gid : Guid.Empty;
                        var pName = (item.TryGetProperty("name", out var nElem) || item.TryGetProperty("Name", out nElem)) ? nElem.GetString() ?? "" : "";
                        var pBarcode = (item.TryGetProperty("barcode", out var bElem) || item.TryGetProperty("Barcode", out bElem)) ? bElem.GetString() ?? "" : "";
                        var pSize = (item.TryGetProperty("size", out var sElem) || item.TryGetProperty("Size", out sElem)) ? sElem.GetString() ?? "" : "";
                        var pCond = (item.TryGetProperty("conditionPercentage", out var cpElem) || item.TryGetProperty("ConditionPercentage", out cpElem)) && cpElem.TryGetInt32(out var cp) ? cp : 100;
                        var pPrice = (item.TryGetProperty("listedPrice", out var lpElem) || item.TryGetProperty("ListedPrice", out lpElem)) && lpElem.TryGetDecimal(out var lp) ? lp : 0m;
                        decimal? pComm = (item.TryGetProperty("commissionRate", out var crElem) || item.TryGetProperty("CommissionRate", out crElem)) && crElem.TryGetDecimal(out var cr) ? cr : null;
                        var pBrand = (item.TryGetProperty("brand", out var brElem) || item.TryGetProperty("Brand", out brElem) || item.TryGetProperty("brandName", out brElem)) ? brElem.GetString() ?? "" : "";
                        var pCat = (item.TryGetProperty("category", out var ctElem) || item.TryGetProperty("Category", out ctElem) || item.TryGetProperty("categoryName", out ctElem)) ? ctElem.GetString() ?? "" : "";

                        products.Add(new ContractDetailProductDto(
                            pId, pName, pBarcode, pSize, pCond, pPrice, pComm, pBrand, pCat
                        ));
                    }
                }
            }
            catch (System.Text.Json.JsonException) { }
        }

        var customerName = !string.IsNullOrWhiteSpace(contract.CustomerNameSnapshot)
            ? contract.CustomerNameSnapshot
            : (!string.IsNullOrWhiteSpace(directCustomerAccount?.FullName)
                ? directCustomerAccount!.FullName
                : (!string.IsNullOrWhiteSpace(contract.Customer?.Account?.FullName)
                    ? contract.Customer!.Account.FullName
                    : (!string.IsNullOrWhiteSpace(contract.Appointment?.Customer?.Account?.FullName)
                        ? contract.Appointment!.Customer!.Account.FullName
                        : (!string.IsNullOrWhiteSpace(snapCustName) ? snapCustName : "Khách hàng"))));

        var customerPhone = !string.IsNullOrWhiteSpace(contract.CustomerPhoneSnapshot)
            ? contract.CustomerPhoneSnapshot
            : (!string.IsNullOrWhiteSpace(directCustomerAccount?.PhoneNumber)
                ? directCustomerAccount!.PhoneNumber
                : (!string.IsNullOrWhiteSpace(contract.Customer?.Account?.PhoneNumber)
                    ? contract.Customer!.Account.PhoneNumber
                    : (!string.IsNullOrWhiteSpace(contract.Appointment?.Customer?.Account?.PhoneNumber)
                        ? contract.Appointment!.Customer!.Account.PhoneNumber
                        : (!string.IsNullOrWhiteSpace(snapCustPhone) ? snapCustPhone : ""))));

        var customerEmail = !string.IsNullOrWhiteSpace(contract.CustomerEmailSnapshot)
            ? contract.CustomerEmailSnapshot
            : (directCustomerAccount?.Email ?? contract.Customer?.Account?.Email ?? contract.Appointment?.Customer?.Account?.Email);

        var customerIdentity = !string.IsNullOrWhiteSpace(contract.CustomerIdentitySnapshot)
            ? contract.CustomerIdentitySnapshot
            : (directCustomerAccount?.IdentityNumber ?? contract.Customer?.Account?.IdentityNumber ?? contract.Appointment?.Customer?.Account?.IdentityNumber);

        var customerAddress = !string.IsNullOrWhiteSpace(contract.CustomerAddressSnapshot)
            ? contract.CustomerAddressSnapshot
            : (directCustomerProfile?.Address ?? contract.Customer?.Address ?? contract.Appointment?.Customer?.Address);

        var bankCode = !string.IsNullOrWhiteSpace(contract.BankCodeSnapshot)
            ? contract.BankCodeSnapshot
            : (directCustomerProfile?.BankCode ?? contract.Customer?.BankCode ?? contract.Appointment?.Customer?.BankCode);

        var bankAccountNumber = !string.IsNullOrWhiteSpace(contract.BankAccountNumberSnapshot)
            ? contract.BankAccountNumberSnapshot
            : (directCustomerProfile?.BankAccountNumber ?? contract.Customer?.BankAccountNumber ?? contract.Appointment?.Customer?.BankAccountNumber);

        var bankAccountHolderName = !string.IsNullOrWhiteSpace(contract.BankAccountHolderSnapshot)
            ? contract.BankAccountHolderSnapshot
            : (directCustomerProfile?.BankAccountHolderName ?? contract.Customer?.BankAccountHolderName ?? contract.Appointment?.Customer?.BankAccountHolderName);

        var employeeName = !string.IsNullOrWhiteSpace(contract.EmployeeNameSnapshot)
            ? contract.EmployeeNameSnapshot
            : (!string.IsNullOrWhiteSpace(directEmployeeAccount?.FullName)
                ? directEmployeeAccount!.FullName
                : (!string.IsNullOrWhiteSpace(contract.CreatedByEmployee?.Account?.FullName)
                    ? contract.CreatedByEmployee.Account.FullName
                    : "Nhân viên tiếp nhận"));

        var employeeTitle = !string.IsNullOrWhiteSpace(contract.EmployeeTitleSnapshot)
            ? contract.EmployeeTitleSnapshot
            : (directEmployeeProfile?.JobTitle ?? contract.CreatedByEmployee?.JobTitle);

        var employeePhone = !string.IsNullOrWhiteSpace(contract.EmployeePhoneSnapshot)
            ? contract.EmployeePhoneSnapshot
            : (directEmployeeAccount?.PhoneNumber ?? contract.CreatedByEmployee?.Account?.PhoneNumber);

        var detail = new ContractDetailDto(
            contract.Id,
            contract.ContractNumber,
            contract.SignedAt,
            contract.EndsAt,
            contract.ExpiryHandling.ToString(),
            contract.Status.ToString(),
            customerName,
            customerPhone,
            customerEmail,
            customerIdentity,
            customerAddress,
            bankCode,
            bankAccountNumber,
            bankAccountHolderName,
            employeeName,
            employeeTitle,
            employeePhone,
            terms,
            sigDataUrl,
            products,
            title,
            organizationName,
            headerText,
            footerText,
            contract.EarlyWithdrawalFeeRate
        );

        return Result.Success(detail);
    }

    public Task<byte[]?> GetPdfAsync(Guid id, CancellationToken cancellationToken) => RenderPdfAsync(id, true, cancellationToken);

    public Task<byte[]?> GetCustomerPreviewPdfAsync(Guid id, CancellationToken cancellationToken) => RenderPdfAsync(id, false, cancellationToken);

    private async Task<byte[]?> RenderPdfAsync(Guid id, bool persist, CancellationToken cancellationToken)
    {
        var existing = await db.ContractDocuments.SingleOrDefaultAsync(x => x.ContractId == id, cancellationToken);
        if (existing?.Content is { Length: > 0 } bytes && (bytes.AsSpan().StartsWith("%PDF"u8) || bytes.AsSpan().StartsWith("PK"u8)))
            return bytes;

        var contract = await db.ConsignmentContracts
            .Include(x => x.Customer).ThenInclude(x => x!.Account)
            .Include(x => x.Products).ThenInclude(x => x.Brand)
            .Include(x => x.Products).ThenInclude(x => x.Category)
            .Include(x => x.CreatedByEmployee).ThenInclude(x => x.Account)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (contract is null) return null;

        var template = await db.ContractTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new ContractTemplate();
        var pdfProducts = contract.Products
            .Where(x => x.ApprovalStatus != Consts.ProductApprovalStatus.Rejected)
            .OrderBy(x => x.CreatedAt)
            .Select(x => (x, x.Brand?.Name ?? "", x.Category?.Name ?? "")).ToList();

        var employeeAccount = contract.CreatedByEmployee?.Account;
        var terms = existing?.Terms ?? (string.IsNullOrWhiteSpace(template.DefaultTerms) ? null : template.DefaultTerms);
        var signatureBytes = existing?.SignatureContent;

        var pdf = ContractPdfRenderer.Render(
            contract,
            employeeAccount?.FullName ?? "",
            contract.CreatedByEmployee?.JobTitle ?? "",
            employeeAccount?.PhoneNumber ?? "",
            pdfProducts,
            terms,
            signatureBytes,
            preview: !persist,
            template: template);

        if (!persist) return pdf;

        if (existing is not null)
        {
            existing.Content = pdf;
        }
        else
        {
            db.ContractDocuments.Add(new ContractDocument
            {
                ContractId = contract.Id,
                Content = pdf,
                Terms = terms,
                TemplateVersion = template.Version.ToString("N"),
                SignatureContent = signatureBytes,
                SignatureContentType = "image/png"
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return pdf;
    }

    public async Task<IReadOnlyList<ContractListItem>> ListAsync(
        int page,
        string? query = null,
        string? status = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var q = db.ConsignmentContracts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var trimmed = query.Trim().ToUpperInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // EF Core dich ToUpper sang SQL.
            q = q.Where(x => x.ContractNumber.ToUpper().Contains(trimmed) ||
                             (x.CustomerNameSnapshot != null && x.CustomerNameSnapshot.ToUpper().Contains(trimmed)) ||
                             (x.CustomerPhoneSnapshot != null && x.CustomerPhoneSnapshot.Contains(trimmed)) ||
                             (x.Customer != null && x.Customer.Account != null && x.Customer.Account.FullName.ToUpper().Contains(trimmed)) ||
                             (x.Customer != null && x.Customer.Account != null && x.Customer.Account.PhoneNumber.Contains(trimmed)) ||
                             (x.Appointment != null && x.Appointment.Customer != null && x.Appointment.Customer.Account != null && x.Appointment.Customer.Account.FullName.ToUpper().Contains(trimmed)) ||
                             (x.Appointment != null && x.Appointment.Customer != null && x.Appointment.Customer.Account != null && x.Appointment.Customer.Account.PhoneNumber.Contains(trimmed)));
#pragma warning restore CA1304, CA1311, CA1862
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ConsignmentContractStatus>(status, true, out var parsedStatus))
        {
            q = q.Where(x => x.Status == parsedStatus);
        }
        if (fromDate.HasValue)
        {
            q = q.Where(x => x.SignedAt >= fromDate.Value);
        }
        if (toDate.HasValue)
        {
            q = q.Where(x => x.SignedAt <= toDate.Value);
        }

        var items = await q.OrderByDescending(x => x.SignedAt).ThenBy(x => x.Id)
            .Skip((Math.Clamp(page, 1, 10000) - 1) * 20).Take(20)
            .Select(x => new
            {
                x.Id,
                x.ContractNumber,
                x.CustomerNameSnapshot,
                x.CustomerId,
                x.SignedAt,
                Status = x.Status.ToString()
            })
            .ToListAsync(cancellationToken);

        var missingCustomerIds = items
            .Where(x => string.IsNullOrWhiteSpace(x.CustomerNameSnapshot) && x.CustomerId.HasValue)
            .Select(x => x.CustomerId!.Value)
            .Distinct()
            .ToList();

        var customerNameMap = missingCustomerIds.Count > 0
            ? await db.Accounts.AsNoTracking()
                .Where(a => missingCustomerIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.FullName, cancellationToken)
            : new Dictionary<Guid, string>();

        return items.Select(x => new ContractListItem(
            x.Id,
            x.ContractNumber,
            !string.IsNullOrWhiteSpace(x.CustomerNameSnapshot)
                ? x.CustomerNameSnapshot
                : (x.CustomerId.HasValue && customerNameMap.TryGetValue(x.CustomerId.Value, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : "Khách hàng"),
            x.SignedAt,
            true,
            x.Status
        )).ToList();
    }

    private static ContractResponse Map(ConsignmentContract contract) => new(
        contract.Id, contract.ContractNumber, contract.CustomerId, contract.AppointmentId,
        contract.CreatedByEmployeeId, contract.SignedAt, contract.EndsAt,
        contract.ExpiryHandling.ToString(), contract.Status.ToString(), contract.EarlyWithdrawalFeeRate);
}
