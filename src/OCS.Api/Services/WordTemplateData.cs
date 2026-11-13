using System.Globalization;
using OCS.Api.Consts;
using OCS.Api.Entities;

namespace OCS.Api.Services;

/// <summary>Ánh xạ dữ liệu nghiệp vụ thành các ô của mẫu Word.</summary>
public static class WordTemplateData
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static string Money(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", Vietnamese) + " đ";
    private static string Date(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static IReadOnlyDictionary<string, string?> Contract(ConsignmentContract contract, ContractTemplate template,
        string employeeName, string employeeTitle, string employeePhone, string? terms,
        IReadOnlyList<Product> products, bool preview, string? storeAddress = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(products);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["contract.number"] = contract.ContractNumber,
            ["contract.signed_at"] = Date(contract.SignedAt),
            ["contract.signed_day"] = contract.SignedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd", CultureInfo.InvariantCulture),
            ["contract.signed_month"] = contract.SignedAt.ToOffset(TimeSpan.FromHours(7)).ToString("MM", CultureInfo.InvariantCulture),
            ["contract.signed_year"] = contract.SignedAt.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy", CultureInfo.InvariantCulture),
            ["contract.ends_at"] = Date(contract.EndsAt),
            ["contract.term_days"] = (contract.EndsAt.ToOffset(TimeSpan.FromHours(7)).Date - contract.SignedAt.ToOffset(TimeSpan.FromHours(7)).Date).Days.ToString(CultureInfo.InvariantCulture),
            ["contract.expiry"] = contract.ExpiryHandling switch { ExpiryHandling.Return => "Nhận lại sản phẩm", ExpiryHandling.Donate => "Quyên góp sản phẩm", _ => "Không xác định" },
            ["contract.product_count"] = products.Count.ToString(CultureInfo.InvariantCulture),
            ["contract.total_price"] = Money(products.Sum(product => product.ListedPrice)),
            ["contract.organization"] = template.OrganizationName,
            ["contract.store_address"] = storeAddress,
            ["contract.title"] = template.Title,
            ["contract.header"] = template.HeaderText,
            ["contract.footer"] = template.FooterText,
            ["contract.preview_notice"] = preview ? "BẢN XEM TRƯỚC - CHƯA LƯU HỢP ĐỒNG" : "",
            ["contract.terms"] = terms,
            ["employee.name"] = employeeName,
            ["employee.title"] = employeeTitle,
            ["employee.phone"] = employeePhone,
            ["customer.name"] = contract.CustomerNameSnapshot,
            ["customer.phone"] = contract.CustomerPhoneSnapshot,
            ["customer.email"] = contract.CustomerEmailSnapshot,
            ["customer.identity"] = contract.CustomerIdentitySnapshot,
            ["customer.address"] = contract.CustomerAddressSnapshot,
            ["bank.code"] = contract.BankCodeSnapshot,
            ["bank.number"] = contract.BankAccountNumberSnapshot,
            ["bank.holder"] = contract.BankAccountHolderSnapshot
        };
    }

    public static IReadOnlyDictionary<string, string?> Product(Product product, string brand, string category, string? location, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["product.index"] = index?.ToString(CultureInfo.InvariantCulture),
            ["product.name"] = product.Name,
            ["product.barcode"] = product.Barcode,
            ["product.brand"] = brand,
            ["product.category"] = category,
            ["product.size"] = product.Size,
            ["product.condition"] = product.ConditionPercentage.ToString(CultureInfo.InvariantCulture) + "%",
            ["product.price"] = Money(product.ListedPrice),
            ["product.commission"] = product.AgreedCommissionRate?.ToString("0.##", Vietnamese) + (product.AgreedCommissionRate.HasValue ? "%" : ""),
            ["product.description"] = product.Description,
            ["product.location"] = location,
            ["product.source"] = product.SourceType == ProductSourceType.Consignment ? "Ký gửi" : "Thu mua",
            ["product.status"] = product.Status switch
            {
                ProductStatus.PendingProcessing => "Chờ xử lý",
                ProductStatus.OnSale => "Đang bán",
                ProductStatus.Sold => "Đã bán",
                ProductStatus.Expired => "Hết hạn",
                ProductStatus.Donated => "Đã quyên góp",
                ProductStatus.Returned => "Đã trả",
                ProductStatus.EarlyWithdrawn => "Đã rút trước hạn",
                ProductStatus.Compensated => "Đã bồi thường",
                ProductStatus.Cancelled => "Đã hủy",
                _ => "Không xác định"
            }
        };
    }
}
