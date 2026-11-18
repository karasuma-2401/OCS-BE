# FCM và thông báo in-app

Token thiết bị được lưu trong `FcmDeviceTokens` của PostgreSQL. Mỗi token chỉ thuộc
một tài khoản; đăng nhập bằng tài khoản khác sẽ chuyển quyền sở hữu khi trình duyệt
đăng ký lại. Trong luồng FCM, cache chỉ còn dùng cho OAuth access token của Google.

## Áp dụng thay đổi

Áp dụng migration `20261118125224_PersistFcmDeviceTokens` theo quy trình migration
của môi trường trước khi khởi động backend mới. Migration tạo một bảng mới, không
sửa dữ liệu nghiệp vụ hiện có. Có thể xuất SQL để review mà không kết nối database:

```powershell
dotnet ef migrations script AddBuyoutAutoApproval PersistFcmDeviceTokens --project src/OCS.Api
```

Token trong cache của phiên bản cũ không được nhập vào bảng mới vì có thể đang
thuộc nhiều tài khoản. Thiết bị cần mở lại website một lần để tự đăng ký; không cần
xin lại quyền thông báo đã được cấp. Token được đồng bộ khi mở trang, focus,
kết nối mạng lại và định kỳ khi trang đang hiển thị.

## Kiểm tra trên thiết bị thật

1. Dùng HTTPS hoặc localhost, đăng nhập tài khoản thử nghiệm và bật quyền thông báo.
2. Kiểm tra `GET /api/notifications/fcm/status`: `configured` phải là `true`, số
   thiết bị phải lớn hơn 0. Đây là trạng thái cấu hình/đăng ký, chưa chứng minh giao tin.
3. Gọi `POST /api/notifications/fcm/test` bằng phiên đăng nhập của chính tài khoản
   thử nghiệm. `pushDelivery.successCount > 0` nghĩa là Google đã chấp nhận gửi;
   vẫn cần kiểm tra thiết bị thực sự hiển thị thông báo.
4. Thử khi tab đang mở, ở nền và đã đóng. Mỗi lần gửi chỉ hiện một thông báo hệ
   thống; danh sách in-app của tab đang mở cập nhật qua service worker.
5. Restart backend, mở lại trang và thử tiếp. Đăng nhập B sau A trên cùng trình
   duyệt rồi gửi thử tới A từ thiết bị thử nghiệm khác: trình duyệt của B không
   được nhận push của A sau khi B đăng ký thành công.
6. Với lịch sử có thông báo chưa đọc nằm ngoài 100 mục mới nhất, chọn “Đọc tất cả”
   và xác nhận `unreadCount` về 0. Tạo/duyệt/từ chối phiếu mua đứt để kiểm tra cả hai kênh.

Lỗi FCM không làm mất thông báo in-app hoặc đảo ngược phiếu mua đứt đã lưu.
Backend chỉ xóa token khi FCM xác nhận `UNREGISTERED`, giữ token khi lỗi nằm ở payload.

## Test tự động

`FcmNotificationTests` kiểm tra vòng đời token, phân loại lỗi gửi và thao tác đọc.
Test upsert PostgreSQL cần biến `OCS_TEST_POSTGRES` trỏ tới server thử nghiệm riêng
có quyền tạo database; test tự tạo/xóa database riêng, không dùng `.env` nghiệp vụ.
Test frontend nằm ở `tests/notifications.test.mjs` trong repo frontend.
