# Mẫu PDF hợp đồng

[Mở PDF mẫu](./hop-dong-mau.pdf). Toàn bộ họ tên, số điện thoại, tài khoản, sản phẩm và chữ ký trong mẫu là dữ liệu minh họa. Điều khoản là chỗ trống để điền nội dung được duyệt.

## Chỉnh nội dung và bố cục

Mẫu trong ứng dụng được tạo bằng mã C# tại [ContractPdfRenderer.cs](../src/OCS.Api/Services/ContractPdfRenderer.cs). Trong hàm `Render`:

- Các lệnh `Line(...)` xác định nội dung và thứ tự hiển thị. Ví dụ thay `Line("HỢP ĐỒNG KÝ GỬI", title)` để sửa tiêu đề; di chuyển các lệnh để đổi thứ tự các phần.
- Các biến `regular`, `bold`, `title` quyết định cỡ và kiểu chữ. Phần `NewPage`, `Line` quyết định lề, khoảng cách dòng, xuống dòng và sang trang.
- Những biểu thức như `contract.CustomerNameSnapshot`, `p.ListedPrice` lấy dữ liệu thật khi tạo hợp đồng. Muốn thêm một trường, cần đảm bảo dữ liệu đó được gửi, lưu và truyền vào hàm `Render`.
- Khối `CHỮ KÝ KHÁCH HÀNG` vẽ ảnh chữ ký được truyền vào. Giới hạn chiều rộng và cao của ảnh được đặt trong phép tính `scale`.
- Điều khoản trên hợp đồng thật đến từ ô nhập trên màn hình lập hợp đồng; không tự sửa thành điều khoản có hiệu lực khi chưa có nội dung được duyệt.

Phông chữ tiếng Việt được đóng gói trong [Assets/Fonts](../src/OCS.Api/Assets/Fonts). Nếu đổi phông, cần bảo đảm phông hỗ trợ tiếng Việt và kiểm tra giấy phép sử dụng.

## Tạo lại file mẫu

Mở terminal tại thư mục `OCS-BE`, sau đó chạy:

```powershell
dotnet run --project samples/ContractPdfSample/ContractPdfSample.csproj -c Release
```

Chương trình [Program.cs](./ContractPdfSample/Program.cs) tạo dữ liệu giả và dùng chính bộ tạo PDF của ứng dụng; không kết nối hay thay đổi database. Đổi dữ liệu giả trong file này để xem các trường hợp khác. Có thể thay [ảnh chữ ký minh họa](./ContractPdfSample/chu-ky-minh-hoa.png) bằng ảnh PNG khác để thử.

File mẫu sẽ được ghi lại tại `samples/hop-dong-mau.pdf`. Các hợp đồng đã tạo trước đó lưu PDF riêng trong database và không tự cập nhật khi sửa bộ tạo PDF; sửa mã chỉ ảnh hưởng các PDF được tạo sau khi ứng dụng được build và khởi động lại.
