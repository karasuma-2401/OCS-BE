# Public Product API — Kế hoạch và nghiệm thu

Tài liệu này là nơi phối hợp chính cho task. Các phase đã hoàn tất được xóa
khỏi file; phần còn lại là contract và kết quả review cuối cùng.

## Trạng thái

**ĐÃ HOÀN THÀNH** sau khi review code, chuẩn hóa route/contract và chạy lại bộ test
Release thành công.

## Phạm vi đã chốt

API danh sách sản phẩm cho Landing Page:

- `GET /api/products`, không yêu cầu đăng nhập.
- Phân trang bằng `page`, `pageSize` (mặc định 1/20, tối đa 100).
- Tìm kiếm bằng `q` theo tên sản phẩm, brand hoặc category.
- Lọc bằng `categoryId`, `brandId`, `size`, `minPrice`, `maxPrice`.
- Chỉ trả sản phẩm có `Product.Status == OnSale`.
- Kết quả sắp xếp ổn định theo `CreatedAt` giảm dần, sau đó `Id` giảm dần.
- Public DTO chỉ gồm thông tin cần cho card sản phẩm: id, name, description,
  listedPrice, size, conditionPercentage, category, brand và coverImageUrl.

Các route nội bộ cùng resource:

- `GET /api/products/{id}` — Admin/Manager/Staff.
- `GET /api/products/barcode/{barcode}` — Admin/Manager/Staff.
- `POST /api/products` — Admin/Manager/Staff.

Các route khác trong project giữ tiền tố hiện hành, không thêm `v1`: auth dùng
`/api/auth/*`, customer dùng `/api/customers/*`, brand dùng `/api/brands/*`,
appointment dùng `/api/appointments/*`.

## Bảo mật và riêng tư

Public projection không trả hoặc điều hướng tới barcode, status nội bộ, source,
contract/buyout/storage IDs, purchase price, commission, hoặc bất kỳ
customer/account/profile/bank/phone/email nào của người ký gửi.

Điều kiện hiển thị chỉ dựa trên `Product.Status`. Không dùng trạng thái active
của category/brand hoặc trạng thái/hạn hợp đồng để mở rộng quy tắc trong task này.
Việc này làm rõ một nguồn sự thật duy nhất và tránh phụ thuộc nhiều aggregate.

## Ngoài phạm vi

Không thực hiện trong task này:

- API chi tiết sản phẩm hoặc danh sách ảnh detail.
- Giá giảm, giá gốc, phần trăm giảm, mã khuyến mãi hoặc sale badge.
- Tự động kiểm tra hạn hợp đồng ký gửi theo thời gian thực.
- Cursor/keyset pagination, full-text search, Redis/cache hoặc migration tối ưu
  hiệu năng khi chưa có số liệu tải thực tế.
- Bộ lọc màu, chất liệu, giới tính/độ tuổi vì schema hiện chưa có trường tương ứng.

## Dữ liệu development

Development seeder có dữ liệu catalog và sản phẩm để kiểm tra thủ công: ít nhất
25 sản phẩm OnSale, nhiều category/brand/size/khoảng giá, ảnh cover và các mẫu
trạng thái không phải OnSale. Test tự động tạo fixture riêng, không phụ thuộc
seed development.

## Acceptance cuối cùng

- [x] `GET /api/products` anonymous trả `PagedResponse` đúng contract.
- [x] Pagination, search, category, brand, size và price range hoạt động độc lập
      và khi kết hợp.
- [x] Chỉ sản phẩm `OnSale` xuất hiện; các status khác bị loại.
- [x] Anonymous không truy cập được các route nội bộ theo id/barcode/create.
- [x] Response không lộ dữ liệu người ký gửi hoặc trường nội bộ.
- [x] Query invalid trả `400 application/problem+json` với
      `VALIDATION_FAILED`.
- [x] Test regression và test Product public pass.
- [x] Không còn route Product/Customer/Brand nào dùng `api/v1`.

## Kết quả review

Kết quả test sau chuẩn hóa route/điều kiện lọc: **141 passed, 28 skipped, 0 failed**
trên cấu hình Release. Các test bị skip là nhóm phụ thuộc dịch vụ ngoài môi trường
local. Build vẫn có analyzer warning hiện hữu và warning liên quan cách tìm kiếm
không phân biệt hoa thường; warning không làm fail acceptance của feature.

## Quyết định

| Ngày | Quyết định |
| --- | --- |
| 2026-11-05 | Không version hóa API bằng `v1` trong project này. |
| 2026-11-05 | Public Product dùng `GET /api/products`; read theo id/barcode và create vẫn nội bộ. |
| 2026-11-05 | Điều kiện public duy nhất là `Product.Status == OnSale`. |
| 2026-11-05 | Không hỗ trợ discount/giá khuyến mãi. |
