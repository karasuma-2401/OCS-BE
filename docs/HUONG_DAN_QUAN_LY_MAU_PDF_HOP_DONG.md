# Hướng dẫn quản lý mẫu PDF hợp đồng

## Ai được sử dụng

Quản lý và admin mở **Hợp Đồng → Quản lý mẫu PDF và điều khoản**. Nhân viên tiếp nhận vẫn lập hợp đồng, ký và xem PDF theo quyền được cấp, nhưng không xem danh sách quản lý hoặc chỉnh mẫu. Mẫu mới áp dụng cho các hợp đồng tạo sau khi lưu; PDF của hợp đồng đã tạo không bị thay đổi.

## Dùng mẫu tham khảo

1. Chọn **Dùng mẫu tham khảo** để nạp nền PDF và các vùng thông tin gợi ý.
2. Nhập tên cửa hàng, phần đầu/cuối và điều khoản mặc định. Phần này xuất hiện trong phụ lục chi tiết; điều khoản cũng được đưa vào form lập hợp đồng.
3. Chọn **Xem thử PDF**, đối chiếu các ô với chữ trên nền, sau đó chọn **Lưu mẫu**. Nhân viên đã mở form từ trước cần tải lại mẫu nếu thấy thông báo mẫu đã thay đổi.

Chọn **Tải PDF mẫu** để tải riêng tệp PDF nền tham khảo về máy.

Mẫu tham khảo có quốc hiệu, tiêu ngữ và cách trình bày lấy cảm hứng từ văn bản hành chính. Các hướng dẫn về khổ giấy và lề tham khảo [Nghị định 30/2020/NĐ-CP và Phụ lục I](https://vanban.chinhphu.vn/?docid=199378&pageid=27160). Hợp đồng cửa hàng không tự trở thành văn bản hành chính; mẫu tham khảo dùng phông Noto Sans hỗ trợ tiếng Việt, vì vậy không khẳng định đáp ứng đầy đủ mọi yêu cầu về phông và thể thức của nghị định. Cửa hàng tự kiểm tra nội dung hợp đồng phù hợp trước khi sử dụng.

## Tự thay PDF và quyết định vị trí điền

1. Soạn mẫu bằng công cụ quen thuộc (ví dụ Word hoặc LibreOffice), chừa khoảng trống cho các thông tin sẽ tự điền, rồi xuất PDF. Nên dùng A4 dọc, lề trái khoảng 30–35 mm, lề trên/dưới 20–25 mm và lề phải 15–20 mm. Chỉ tải tệp tin cậy, không khóa mật khẩu, tối đa 2 MB và 20 trang.
2. Chọn tệp tại **Tải PDF nền**. Khi thay tệp, các vùng cũ được xóa để tránh in nhầm vị trí.
3. Chọn **Thêm vùng**, chọn thông tin cần điền, nhập trang và vị trí. Tọa độ tính theo phần trăm kích thước từng trang: `Trái` và `Trên` tính từ góc trên trái; `Rộng` và `Cao` tính theo kích thước trang. Ví dụ trang 1, trái 35, trên 42, rộng 50, cao 4 tạo một ô từ 35% chiều ngang và 42% chiều dọc. Tọa độ cộng kích thước không vượt 100%. Tối đa 40 vùng; một thông tin có thể xuất hiện nhiều lần.
4. Sơ đồ minh họa vị trí tương đối chứ không phải ảnh PDF nền. Nhấn **Xem thử PDF** và điều chỉnh cho khớp nội dung thực tế. Với sản phẩm và điều khoản, chừa vùng đủ lớn; nội dung dài hơn vùng sẽ được rút gọn bằng dấu ba chấm trên nền.
5. Để chèn chữ ký ảnh, thêm vùng **Ảnh chữ ký khách hàng**, chọn trang và kích thước. Khi nhân viên ký hoặc tải ảnh, ảnh được thu vừa vùng đó. Ảnh chữ ký không phải chữ ký số.
6. Lưu mẫu sau khi kiểm tra. Nếu người khác vừa lưu phiên bản mới, tải lại mẫu trước khi chỉnh tiếp để tránh ghi đè.

Với PDF nền tự chọn, hệ thống **luôn nối phụ lục chi tiết** gồm khách hàng, nhân viên, sản phẩm, điều khoản và chữ ký vào cuối PDF. Phụ lục bảo toàn thông tin khi ô đặt trên nền không đủ chỗ hoặc chưa được cấu hình. Bản xem trước dùng dữ liệu minh họa và chưa tạo hợp đồng; hợp đồng mới vẫn được đánh dấu dự thảo trong PDF.

Để chỉnh bố cục chữ hoặc đường kẻ, sửa tệp nguồn bằng công cụ soạn thảo của cửa hàng rồi xuất và tải lại PDF; trang quản lý không phải trình biên tập nội dung PDF trực tiếp. Việc thay PDF xóa danh sách vùng cũ, cần thiết lập lại vị trí và xem thử.

## Cập nhật và kiểm tra sau triển khai

Backend cần migration `AddContractPdfBackground` để bổ sung `PdfBackground` và `FieldMappingsJson` vào bảng mẫu hiện có. Từ thư mục `OCS-BE`, kiểm tra các migration đang chờ bằng cấu hình Development của backend:

```powershell
dotnet run --project src/OCS.Api --configuration Release -- --check-migrations
```

Lệnh `dotnet ef` dùng `AppDbContextFactory`, chỉ đọc biến môi trường `ConnectionStrings__DefaultConnection` và không tự đọc `.env`. Nếu không thiết lập biến, nó mặc định kết nối PostgreSQL cục bộ, có thể khác cơ sở dữ liệu backend đang dùng. Không áp dụng migration trước khi xác nhận đúng môi trường, cơ sở dữ liệu đích và đã có bản sao lưu. Sau khi kiểm tra, lệnh backend `dotnet run --project src/OCS.Api --configuration Release -- --apply-migrations` sẽ áp dụng **tất cả** migration đang chờ, không chỉ migration PDF. SQL cho riêng migration PDF được sinh tại [add-contract-pdf-background-review.sql](./add-contract-pdf-background-review.sql) để xem trước; chỉ dùng khi migration trước đó đã được áp dụng và sau khi xác nhận chính xác cơ sở dữ liệu đích.

Khởi động lại backend và frontend sau khi cập nhật. Kiểm tra bằng tài khoản quản lý: tải mẫu, xem thử, lưu, lập hợp đồng và kiểm tra PDF cùng phụ lục; đăng nhập nhân viên để xác nhận không thấy tab quản lý nhưng vẫn tiếp nhận và xem được PDF hợp đồng vừa lập. Nếu PDF không mở hoặc mẫu bị từ chối, thử xuất lại PDF thường không đặt mật khẩu và kiểm tra giới hạn tệp.
