using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Services;
using PdfSharp.Pdf.IO;

// Dữ liệu hoàn toàn giả, không đọc hay ghi database.
var contract = new ConsignmentContract
{
    ContractNumber = "HD-MAU-001",
    CustomerNameSnapshot = "Nguyễn Văn Mẫu",
    CustomerPhoneSnapshot = "0900000000",
    CustomerEmailSnapshot = "khachhang@example.com",
    CustomerIdentitySnapshot = "000000000000",
    CustomerAddressSnapshot = "123 Đường Mẫu, Quận Mẫu, TP. Hồ Chí Minh",
    BankCodeSnapshot = "NGAN-HANG-MAU",
    BankAccountNumberSnapshot = "0000000000",
    BankAccountHolderSnapshot = "NGUYEN VAN MAU",
    SignedAt = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(7)),
    EndsAt = new DateTimeOffset(2027, 2, 13, 9, 0, 0, TimeSpan.FromHours(7)),
    ExpiryHandling = ExpiryHandling.Return
};
var brand = new Brand { Name = "Thương hiệu mẫu", DefaultCommissionRate = 20m };
var shirt = new Product { Name = "Áo khoác denim", Barcode = "OCS-MAU-001", Size = "M", ConditionPercentage = 90, Description = "Đã qua sử dụng, tình trạng tốt." };
shirt.AssignConsignmentTerms(contract.Id, 350000m, brand, 18m);
var bag = new Product { Name = "Túi xách", Barcode = "OCS-MAU-002", Size = "Một kích cỡ", ConditionPercentage = 85, Description = "Có dấu hiệu sử dụng nhẹ." };
bag.AssignConsignmentTerms(contract.Id, 600000m, brand, 22m);
var products = new List<(Product Product, string Brand, string Category)>
{
    (shirt, brand.Name, "Áo khoác"),
    (bag, brand.Name, "Túi xách")
};
var terms = "1. [Điền nội dung điều khoản đã được phê duyệt tại đây.]\n2. [Nội dung mẫu chỉ để minh họa bố cục.]";
var signaturePath = Path.Combine("samples", "ContractPdfSample", "chu-ky-minh-hoa.png");
var signature = File.Exists(signaturePath) ? await File.ReadAllBytesAsync(signaturePath) : null;
var bytes = ContractPdfRenderer.Render(contract, "Trần Nhân Viên", "Nhân viên tiếp nhận", "0900000001", products, terms, signature);
var output = args.Length > 0 ? args[0] : Path.Combine("samples", "hop-dong-mau.pdf");
await File.WriteAllBytesAsync(output, bytes);
using var stream = new MemoryStream(bytes);
using var check = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
Console.WriteLine($"Đã tạo {Path.GetFullPath(output)} ({check.PageCount} trang). Không sử dụng database.");
