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
        var result = await PrepareAsync(request, employeeId, false, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Data!.Contract) : Result.Failure<ContractResponse>(result.Error!);
    }

    public async Task<Result<byte[]>> PreviewAsync(CreateContractRequest request, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await PrepareAsync(request, employeeId, true, cancellationToken);
        return result.IsSuccess ? Result.Success(result.Data!.Pdf) : Result.Failure<byte[]>(result.Error!);
    }

    private sealed record PreparedContract(ContractResponse Contract, byte[] Pdf);

    private async Task<Result<PreparedContract>> PrepareAsync(CreateContractRequest request, Guid employeeId, bool preview, CancellationToken cancellationToken)
    {
        if (request.SignedAt is not DateTimeOffset signedAt || request.EndsAt is not DateTimeOffset endsAt ||
            endsAt <= signedAt || (request.ExpiryHandling != "Return" && request.ExpiryHandling != "Donate"))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_DATA", "Vui lòng kiểm tra ngày ký, ngày kết thúc và cách xử lý khi hết hạn."));

        var employee = await db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result.Failure<PreparedContract>(ApplicationError.Forbidden("CONTRACT.EMPLOYEE_REQUIRED", "Tài khoản chưa có hồ sơ nhân viên."));
        var employeeAccount = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        var template = await db.ContractTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new ContractTemplate();
        if (request.TemplateVersion.HasValue && request.TemplateVersion.Value != template.Version)
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("CONTRACT_TEMPLATE.CHANGED", "Mẫu hợp đồng đã được cập nhật. Hãy tải lại mẫu và kiểm tra điều khoản trước khi tiếp tục."));
        if (request.Terms?.Length > 6000)
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_TERMS", "Điều khoản không được vượt quá 6000 ký tự."));

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

        if (guest is null && request.SaveCustomer)
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_DATA", "Chỉ lưu hồ sơ mới cho khách vãng lai."));

        if (customerId.HasValue)
        {
            var existing = await GetCustomerAsync(customerId.Value, cancellationToken);
            if (!existing.IsSuccess) return Result.Failure<PreparedContract>(existing.Error!);
            var customer = existing.Data!;
            guest = new ContractGuestRequest { FullName = customer.FullName, PhoneNumber = customer.PhoneNumber, Email = customer.Email,
                IdentityNumber = customer.IdentityNumber, Address = customer.Address, BankCode = customer.BankCode,
                BankAccountNumber = customer.BankAccountNumber, BankAccountHolderName = customer.BankAccountHolderName };
            if (request.CustomerDetails is { } details)
                guest = guest with { Email = details.Email, IdentityNumber = details.IdentityNumber, Address = details.Address,
                    BankCode = details.BankCode, BankAccountNumber = details.BankAccountNumber, BankAccountHolderName = details.BankAccountHolderName };
        }
        else if (request.CustomerDetails is not null)
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_DATA", "Thông tin bổ sung chỉ dùng cho khách đã có hồ sơ."));

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

        if (request.Products is null || request.Products.Count > 100 || request.Products.Any(x => x is null))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_PRODUCTS", "Danh sách sản phẩm không hợp lệ."));
        var brandIds = request.Products.Select(x => x.BrandId).Distinct().ToArray();
        var categoryIds = request.Products.Select(x => x.CategoryId).Distinct().ToArray();
        var locationIds = request.Products.Where(x => x.StorageLocationId.HasValue).Select(x => x.StorageLocationId!.Value).Distinct().ToArray();
        var brands = await db.Brands.Where(x => x.IsActive && brandIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var categories = await db.Categories.Where(x => x.IsActive && categoryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var locations = await db.StorageLocations.CountAsync(x => x.IsActive && locationIds.Contains(x.Id), cancellationToken);
        if (brands.Count != brandIds.Length || categories.Count != categoryIds.Length || locations != locationIds.Length || request.Products.Any(x =>
            string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 200 || string.IsNullOrWhiteSpace(x.Size) || x.Size.Length > 30 ||
            x.ConditionPercentage is < 0 or > 100 || x.ListedPrice < 0 || x.ListedPrice > 9999999999999999m || decimal.Round(x.ListedPrice, 2) != x.ListedPrice ||
            x.AgreedCommissionRate is null or < 0 or > 100 || decimal.Round(x.AgreedCommissionRate.Value, 2) != x.AgreedCommissionRate.Value ||
            x.Description?.Length > 2000 || (x.Barcode is not null && !Regex.IsMatch(x.Barcode, "^OCS-[0-9A-F]{16}$", RegexOptions.CultureInvariant))))
            return Result.Failure<PreparedContract>(ApplicationError.Validation("CONTRACT.INVALID_PRODUCTS", "Thông tin sản phẩm hoặc danh mục không hợp lệ."));
        // Legacy callers without a barcode receive a stable value for the same draft.
        var barcodes = request.Products.Select((item, index) => item.Barcode ??
            "OCS-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{numberId:N}:{index}")))[..16]).ToArray();
        if (barcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != barcodes.Length ||
            await db.Products.AnyAsync(product => product.Barcode != null && barcodes.Contains(product.Barcode.Trim().ToUpper()), cancellationToken))
            return Result.Failure<PreparedContract>(ApplicationError.Conflict("PRODUCT.DUPLICATE_BARCODE", "Mã barcode đã tồn tại. Hãy tạo lại mã cho sản phẩm trước khi tiếp tục."));
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
        if (request.SaveCustomer)
        {
            var email = string.IsNullOrWhiteSpace(guest.Email) ? null : guest.Email.Trim();
            var identity = string.IsNullOrWhiteSpace(guest.IdentityNumber) ? null : guest.IdentityNumber.Trim();
            if (email is not null && EmailIdentity.Normalize(email) is null)
                return Result.Failure<PreparedContract>(ApplicationError.Validation("CUSTOMER.INVALID_EMAIL", "Email không đúng định dạng được hỗ trợ."));
            if ((email is not null && await db.Accounts.AnyAsync(x => x.NormalizedEmail == EmailIdentity.Normalize(email), cancellationToken)) ||
                (identity is not null && await db.Accounts.AnyAsync(x => x.AccountType == AccountType.Customer && x.IdentityNumber == identity, cancellationToken)))
                return Result.Failure<PreparedContract>(ApplicationError.Conflict("CUSTOMER.ALREADY_EXISTS", "Email hoặc giấy tờ đã có hồ sơ khách."));
            var account = new Account { AccountType = AccountType.Customer, Status = AccountStatus.PendingActivation,
                FullName = guest.FullName.Trim(), PhoneNumber = guest.PhoneNumber.Trim(), Email = email, IdentityNumber = identity, CreatedByAccountId = employeeId };
            var profile = new CustomerProfile { Id = account.Id, Account = account, Address = guest.Address?.Trim(), BankCode = guest.BankCode?.Trim(),
                BankAccountNumber = guest.BankAccountNumber?.Trim(), BankAccountHolderName = guest.BankAccountHolderName?.Trim() };
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
            var product = new Product { Name = item.Name.Trim(), Size = item.Size.Trim(), Description = item.Description?.Trim(),
                ConditionPercentage = item.ConditionPercentage, CategoryId = item.CategoryId, StorageLocationId = item.StorageLocationId };
            product.Barcode = barcodes[productIndex];
            product.AssignConsignmentTerms(contract.Id, item.ListedPrice, brands[item.BrandId], item.AgreedCommissionRate);
            pdfProducts.Add((product, brands[item.BrandId].Name, categories[item.CategoryId].Name));
            for (var imageIndex = 0; imageIndex < decodedImages[productIndex].Length; imageIndex++)
            {
                var file = decodedImages[productIndex][imageIndex];
                var image = new ProductImage { ProductId = product.Id, Content = file.Content, ContentType = file.ContentType,
                    ImageType = imageIndex == 0 ? ProductImageType.Cover : ProductImageType.Detail, SortOrder = imageIndex };
                image.Url = $"/api/product-images/{image.Id:D}";
                product.Images.Add(image);
            }
            if (!preview) db.Products.Add(product);
        }
        var terms = (request.Terms ?? template.DefaultTerms).Trim();
        var pdf = ContractPdfRenderer.Render(contract, employeeAccount?.FullName ?? "", employee.JobTitle, employeeAccount?.PhoneNumber ?? "", pdfProducts, terms, signatureBytes, preview, template);
        if (preview) return Result.Success(new PreparedContract(Map(contract), pdf));
        db.ConsignmentContracts.Add(contract);
        db.ContractDocuments.Add(new ContractDocument { ContractId = contract.Id, Content = pdf, Terms = terms, TemplateVersion = template.Version.ToString("N"), SignatureContent = signatureBytes, SignatureContentType = request.Signature?.ContentType });
        try { await db.SaveChangesAsync(cancellationToken); }
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
        return Result.Success(new PreparedContract(Map(contract), pdf));
    }

    public async Task<string> GenerateBarcodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var barcode = "OCS-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
            if (!await db.Products.AnyAsync(product => product.Barcode != null && product.Barcode.Trim().ToUpper() == barcode, cancellationToken)) return barcode;
        }
    }

    public async Task<IReadOnlyList<BarcodeLabelProduct>?> GetBarcodeProductsAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.ConsignmentContracts.AsNoTracking().AnyAsync(contract => contract.Id == id, cancellationToken)) return null;
        return await db.Products.AsNoTracking().Where(product => product.ConsignmentContractId == id).OrderBy(product => product.Name)
            .Select(product => new BarcodeLabelProduct(product.Id, product.Name, product.Barcode, product.ListedPrice)).ToListAsync(cancellationToken);
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
        var brands = await db.Brands.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ContractOption(x.Id, x.Name, x.DefaultCommissionRate)).ToListAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ContractOption(x.Id, x.Name, null)).ToListAsync(cancellationToken);
        var locations = await db.StorageLocations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ContractOption(x.Id, x.Name, null)).ToListAsync(cancellationToken);
        return new ContractOptionsResponse(brands, categories, locations);
    }

    public async Task<Result<ContractResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await db.ConsignmentContracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return contract is null
            ? Result.Failure<ContractResponse>(ApplicationError.NotFound("CONTRACT.NOT_FOUND", "Không tìm thấy hợp đồng."))
            : Result.Success(Map(contract));
    }

    public async Task<byte[]?> GetPdfAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ContractDocuments.AsNoTracking().Where(x => x.ContractId == id).Select(x => x.Content).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ContractListItem>> ListAsync(int page, CancellationToken cancellationToken) =>
        await db.ConsignmentContracts.AsNoTracking().OrderByDescending(x => x.SignedAt).ThenBy(x => x.Id)
            .Skip((Math.Clamp(page, 1, 10000) - 1) * 20).Take(20)
            .Select(x => new ContractListItem(x.Id, x.ContractNumber, x.CustomerNameSnapshot, x.SignedAt,
                db.ContractDocuments.Any(d => d.ContractId == x.Id))).ToListAsync(cancellationToken);

    private static ContractResponse Map(ConsignmentContract contract) => new(
        contract.Id, contract.ContractNumber, contract.CustomerId, contract.AppointmentId,
        contract.CreatedByEmployeeId, contract.SignedAt, contract.EndsAt,
        contract.ExpiryHandling.ToString(), contract.Status.ToString());
}
