# TODO: Cải tiến nhập PDF và thiết kế mẫu hợp đồng

Trạng thái: **để thực hiện ở đợt sau**. Bản hiện tại vẫn cho quản lý tải PDF nền, điền vị trí theo tỷ lệ trang và xem thử; hệ thống nối phụ lục đầy đủ. Chưa thay thế hoặc chuyển đổi mẫu đã lưu trong đợt này.

## Mục tiêu

- [ ] Cho quản lý nhìn trực tiếp PDF nền từng trang, kéo/thả và thay kích thước các trường, có phóng to, căn chỉnh, đánh số trang và thao tác bằng bàn phím.
- [ ] Hỗ trợ danh sách sản phẩm có nhiều dòng, qua trang và lặp tiêu đề; xem trước tình huống tên dài, nhiều sản phẩm, chữ ký, tiếng Việt và nội dung trống.
- [ ] Kiểm tra đầu vào PDF, kích thước, PDF có mật khẩu và khả năng tương thích; thông báo trường tràn, trùng hoặc thiếu dữ liệu thay vì âm thầm che nội dung.
- [ ] Bảo toàn mẫu có phiên bản, xung đột chỉnh sửa, phân quyền quản lý và PDF đã phát hành; thống nhất một luồng tạo PDF tin cậy ở backend.
- [ ] Đánh giá bảo vệ dữ liệu khách hàng khi render, xử lý phông tiếng Việt, hiệu năng tệp lớn và kiểm thử trên màn hình nhỏ.

## Công cụ cần thử nghiệm

1. [pdfme](https://github.com/pdfme/pdfme): thư viện MIT cho React/TypeScript, có designer kéo thả, tạo PDF và schema cho bảng. Phù hợp để thử thiết kế mẫu trực quan; cần thử riêng [giới hạn bảng động với PDF nền có sẵn](https://pdfme.com/docs/tables), vì tài liệu nêu PDF nền tùy chỉnh giữ các trang cố định và không tự áp dụng ngắt trang động. Cần quyết định cách chia trách nhiệm giữa trình duyệt và backend .NET trước khi chọn.
2. [PDF.js](https://github.com/mozilla/pdf.js): dùng để render từng trang PDF trong trình duyệt, thuận tiện tạo lớp kéo/thả tọa độ mà vẫn giữ PDFsharp trên backend. Cần tự xây dựng phần chỉnh vùng, phân trang và kiểm tra tràn; PDF.js không tự giải quyết toàn bộ bài toán thiết kế mẫu.
3. Giữ PDFsharp hiện tại để tạo PDF trên server; khảo sát tính tương thích và giấy phép của bất kỳ công cụ bổ sung nào trước khi đưa vào dự án.

## Tiêu chí hoàn thành

- [ ] Có bản thử nghiệm so sánh ít nhất hai phương án trên PDF thực tế, gồm PDF nhiều trang và tài liệu chứa ảnh/phông tiếng Việt.
- [ ] Quản lý nhập, lưu, tải lại và xem trước mẫu trên desktop; nhân viên không được chỉnh mẫu.
- [ ] Dữ liệu thật trong PDF không bị thiếu khi sản phẩm hoặc điều khoản dài; có thông báo nếu bố cục không thể hiển thị đầy đủ.
- [ ] Migration hoặc chuyển đổi mẫu cũ được kiểm thử; các PDF hợp đồng đã tạo giữ nguyên.
