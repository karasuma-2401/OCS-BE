# Barcode sản phẩm và in nhãn

Khi thêm sản phẩm vào hợp đồng, hệ thống tạo mã định danh và hiển thị barcode ngay trong form. Mã này cũng xuất hiện trong PDF xem trước và được giữ khi tạo hợp đồng. Mã chưa được dành riêng cho đến khi hợp đồng được lưu; nếu có xung đột, chọn **Tạo mã khác** ở sản phẩm liên quan và xem trước lại. Database kiểm tra tính duy nhất khi lưu, không phân biệt chữ hoa, chữ thường và khoảng trắng ở hai đầu.

Sau khi tạo hợp đồng, mở PDF hợp đồng và chọn **In barcode sản phẩm** để xem và in nhãn của các sản phẩm thuộc hợp đồng. Trong **Kho và sản phẩm**, có thể mở chi tiết một sản phẩm để in nhãn hoặc chọn **Chọn in barcode**, đánh dấu các sản phẩm cần in, rồi xem trước. Có thể chọn qua nhiều trang, tối đa 100 sản phẩm mỗi lần xem trước.

Quản lý và admin có thể mở **Mẫu in barcode** cạnh chức năng thêm vị trí kho. Mẫu chung cho cửa hàng cho phép điều chỉnh chiều rộng, chiều cao, số cột và cỡ chữ, với bản xem trước. Mỗi nhãn thể hiện tên, giá, vạch Code 128 và mã dạng chữ/số. Tên dài có thể bị rút gọn; mã cũ chứa ký tự ngoài ASCII không in được và cần được xử lý riêng. Nhân viên được in theo mẫu đã lưu nhưng không sửa mẫu.

Chọn khổ A4 và tỷ lệ 100% hoặc kích thước thực trong hộp thoại in, cho phép cửa sổ bật lên nếu trình duyệt yêu cầu. Tùy máy in, giấy và mã cũ dài, nên in thử và quét thử trước khi in hàng loạt. Thiết bị in và đầu đọc thực tế chưa được xác nhận bởi kiểm thử tự động.

Migration `AddBarcodeLabelTemplateAndBackfill` tạo bảng cấu hình và bổ sung mã cho sản phẩm chưa có mã, giữ nguyên mã hiện có; đồng thời tạo chỉ mục duy nhất cho barcode đã chuẩn hóa. Migration sẽ dừng nếu dữ liệu cũ có mã trùng sau chuẩn hóa. Có thể kiểm tra số sản phẩm thiếu mã và số nhóm trùng bằng lệnh sau từ thư mục backend, trong môi trường cấu hình đúng database:

```sh
dotnet run --project src/OCS.Api/OCS.Api.csproj -- --check-barcodes
```
