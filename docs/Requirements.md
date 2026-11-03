# Đặc tả yêu cầu phần mềm

Tài liệu này ghi lại yêu cầu của hệ thống quản lý cửa hàng ký gửi quần áo OCS theo Báo cáo SE100 (Final 3). Phần yêu cầu mô tả phạm vi nghiệp vụ mong muốn; mức độ đã hiện thực trong backend được theo dõi riêng trong [README.md](../README.md) và [DataModel.md](DataModel.md).

## Phạm vi vận hành

Hệ thống phục vụ một cửa hàng độc lập và một kho tại chỗ. Dữ liệu nghiệp vụ tập trung trên Cloud. Toàn bộ hệ thống vận hành tập trung trên Website duy nhất (React + Tailwind CSS + Vite) cho cả khách hàng, người ký gửi và nhân viên (quản lý, kho, thẩm định, thu ngân Web POS) kết nối trực tiếp với Backend Web API. Hệ thống không mở rộng sang chuỗi nhiều chi nhánh hoặc SaaS đa tổ chức trong phạm vi đề tài.

## Mục đích

- Số hóa việc đặt lịch, tiếp nhận, kiểm định và gắn mã định danh độc bản cho từng món đồ.
- Phân hạng thương hiệu, định giá và dự trù khoản tiền người ký gửi nhận được khi bán thành công.
- Hỗ trợ cả ký gửi và thu mua đứt; quản lý bán lẻ tại quầy bằng Barcode và VietQR.
- Theo dõi vòng đời sản phẩm, đối soát và giải ngân một lần khi kết thúc đợt ký gửi.
- Xử lý hàng tồn theo thỏa thuận: hoàn trả hoặc quyên góp.

## Vai trò trong hệ thống

| Vai trò | Trách nhiệm chính | Mã vai trò kỹ thuật |
| --- | --- | --- |
| Quản trị hệ thống | Quản lý tài khoản, quyền và tham số vận hành. | `Admin` |
| Quản lý cửa hàng | Giám sát doanh thu, duyệt thu mua đứt và duyệt giải ngân. | `Manager` |
| Nhân viên cửa hàng | Tiếp nhận, kiểm định, quản lý kho, bán hàng POS và bàn giao hàng tồn. | `Staff` |
| Người ký gửi / Khách hàng | Đặt lịch, theo dõi sản phẩm/đợt ký gửi và nhận thông báo quyết toán. | `Customer` |
| Người mua | Tra cứu sản phẩm đang bán và mua trực tiếp tại quầy. | Khách vãng lai / `Customer` |

## Yêu cầu chức năng

### Quản trị hệ thống, tài khoản và cấu hình

| Mã | Nghiệp vụ | Yêu cầu | Vai trò |
| --- | --- | --- | --- |
| FR-001 | Đăng ký và đăng nhập | Người dùng đăng ký tài khoản (họ tên, email, số điện thoại, mật khẩu), kích hoạt tài khoản bằng OTP gửi qua email. Đăng nhập bằng email và mật khẩu; phiên làm việc được duy trì qua cookie HttpOnly. Số điện thoại được lưu trong hồ sơ để liên hệ, chưa được chứng minh quyền sở hữu qua SMS. | Tất cả tác nhân |
| FR-002 | Phân quyền vai trò (RBAC) | Quản lý và gán quyền theo các mã vai trò chuẩn `Admin`, `Manager`, `Staff` và `Customer`. | Admin |
| FR-003 | Cấu hình tham số | Cấu hình thời hạn ký gửi, biên độ hoa hồng trần/sàn theo từng phân hạng. | Admin, Manager |
| FR-004 | Audit log | Ghi nhận các thao tác nhạy cảm như đổi tỷ lệ hoa hồng, duyệt mua đứt, điều chỉnh thanh toán. | Hệ thống |
| FR-005 | Tạo hồ sơ khách hàng tại quầy | Khởi tạo thông tin hồ sơ khách hàng theo số điện thoại khi tiếp nhận khách chưa có hồ sơ. Tài khoản trực tuyến (nếu cấp) yêu cầu xác thực email trước khi kích hoạt; không tự động chuyển quyền sở hữu hồ sơ chỉ dựa trên số điện thoại nhập vào. | Hệ thống, Staff |
| FR-006 | Tra cứu hồ sơ | Người ký gửi tra cứu hồ sơ, tài khoản nhận tiền và các đợt ký gửi của mình. | Customer |

### Tiếp nhận, thẩm định và mã định danh

| Mã | Nghiệp vụ | Yêu cầu | Vai trò |
| --- | --- | --- | --- |
| FR-007 | Đặt lịch ký gửi | Chọn ngày, khung giờ tiếp nhận và số lượng quần áo ước tính. | Customer |
| FR-008 | Điều phối lịch hẹn | Hiển thị lịch hẹn trong ngày và gửi thông báo nhắc lịch. | Staff, Hệ thống |
| FR-009 | Đánh giá chất lượng | Kiểm tra độ mới, chất liệu, khuyết tật nhỏ và độ nguyên bản của tem mác. | Staff |
| FR-010 | Phân hạng thương hiệu | Gắn một trong bốn hạng: No Brand, Local Brand, Commercial hoặc Luxury. | Staff |
| FR-011 | Định giá và dự trù | Nhập giá niêm yết; hệ thống tính khoản dự trù người gửi nhận được nếu bán thành công. | Staff, Hệ thống |
| FR-012 | Thu mua đứt | Tạo phiếu thu mua, chi tiền ngay và chuyển quyền sở hữu cho cửa hàng. | Customer, Staff, Manager |
| FR-013 | Sinh GUID và Barcode | Sinh GUID và Barcode/QR duy nhất cho sản phẩm được duyệt lên kệ. | Hệ thống |
| FR-014 | In tem nhãn | In Barcode, GUID, tên đồ, phân hạng, size, giá niêm yết và ngày hết hạn. | Staff |
| FR-015 | Gán vị trí trưng bày | Cập nhật vị trí kho/quầy kệ thực tế của món hàng. | Staff |
| FR-016 | Quản lý trạng thái | Chỉ cho phép chuyển đổi tuần tự giữa các trạng thái hợp lệ của sản phẩm. | Hệ thống, Staff |

### Bán hàng tại quầy POS

| Mã | Nghiệp vụ | Yêu cầu | Vai trò |
| --- | --- | --- | --- |
| FR-017 | Quét Barcode | Quét Barcode hoặc QR trên tag để nạp sản phẩm vào giỏ. | Staff |
| FR-018 | Nhập GUID thủ công | Nhập GUID khi tem bị mờ hoặc rách. | Staff |
| FR-020 | Giữ nguyên giá niêm yết | Giá bán giữ theo mức đã chốt trong hợp đồng suốt đợt ký gửi. | Hệ thống |
| FR-022 | Thanh toán tiền mặt | Nhập tiền khách trả và tính tiền thừa. | Staff |
| FR-023 | VietQR động | Chỉ hoàn tất khi có Internet và nhận xác nhận thanh toán hợp lệ. | Hệ thống |
| FR-024 | Chuyển sang `Sold` | Lưu hóa đơn, chi tiết và cập nhật trạng thái `Sold` trong một transaction trực tuyến. | Hệ thống |
| FR-025 | In hóa đơn | In hóa đơn có thông tin món đồ và quy định hàng ký gửi không đổi trả. | Staff |
| FR-026 | Web POS trực tuyến | Giao diện Web POS xử lý giỏ hàng, thanh toán tiền mặt/VietQR trực tiếp qua Backend API với transaction toàn vẹn. | Hệ thống, Staff |

> Báo cáo gốc không có FR-019 và FR-021; các mã được giữ nguyên để không làm thay đổi tham chiếu nghiệp vụ của nhóm.

### Quản lý đợt hàng và tồn kho

| Mã | Nghiệp vụ | Yêu cầu | Vai trò |
| --- | --- | --- | --- |
| FR-027 | Theo dõi đợt ký gửi | Theo dõi chu kỳ 60/70 ngày và danh mục đã bán/chưa bán theo thời gian thực. | Customer, Staff |
| FR-028 | Khóa bán khi hết hạn | Hangfire quét hằng ngày, chuyển món chưa bán sang `Expired`. | Hệ thống |
| FR-029 | Quyên góp | Chuyển hàng hết hạn sang `Donated` theo điều khoản hợp đồng. | Hệ thống, Staff |
| FR-030 | Trả hàng tồn | Quét Barcode để giao trả và đổi trạng thái sang `Returned`. | Staff |
| FR-031 | Yêu cầu rút sớm | Người ký gửi yêu cầu lấy sản phẩm trước khi đợt kết thúc. | Customer |
| FR-032 | Tính phí rút sớm | Tính phí quản lý/lưu kho theo tỷ lệ đã thỏa thuận trong hợp đồng. | Hệ thống |
| FR-033 | Bồi thường sự cố | Lập biên bản, duyệt bồi thường và chuyển sản phẩm sang `Compensated`. | Staff, Manager |

### Đối soát, giải ngân và báo cáo

| Mã | Nghiệp vụ | Yêu cầu | Vai trò |
| --- | --- | --- | --- |
| FR-034 | Tạo bảng quyết toán | Tổng hợp doanh thu khi đợt kết thúc hoặc toàn bộ sản phẩm đã xử lý. | Hệ thống |
| FR-035 | Tính hoa hồng | Tính tỷ lệ hoa hồng theo phân hạng đã chốt cho từng món. | Hệ thống |
| FR-036 | Tính tiền thực nhận | Tổng giá bán món `Sold` trừ hoa hồng, phí dịch vụ và cộng tiền bồi thường (nếu có). | Hệ thống |
| FR-037 | Duyệt lệnh chi | Quản lý kiểm tra bảng quyết toán và duyệt chi một lần cho người ký gửi. | Manager |
| FR-038 | Lưu chứng từ | Tải ảnh biên lai/ủy nhiệm chi của giao dịch giải ngân. | Manager |
| FR-039 | Xác nhận tất toán | Người gửi xác nhận đã nhận tiền; hết thời gian cấu hình thì tự xác nhận nếu không có khiếu nại. | Customer |
| FR-040 | Báo cáo doanh thu POS | Thống kê theo ngày, tháng và phương thức thanh toán. | Manager |
| FR-041 | Báo cáo hoa hồng | Thống kê hoa hồng thực tế từ các đợt ký gửi hoàn tất. | Manager |
| FR-042 | Báo cáo thu mua | Thống kê ngân sách thu mua đứt và lợi nhuận khi bán lại. | Manager |

## Yêu cầu phi chức năng

### Hiệu năng và tải trọng

- Phản hồi khi quét mã và tải thông tin sản phẩm tại POS không quá 2 giây.
- Tạo đơn ký gửi và tải ảnh hoàn thành trong 3 giây.
- Hệ thống phục vụ ổn định 100 người dùng đồng thời và tối thiểu 20 giao dịch POS cùng lúc.
- Có thể quản lý hàng chục nghìn sản phẩm mà vẫn bảo đảm tốc độ tìm kiếm.

### Độ tin cậy và khả năng chịu lỗi

- Mục tiêu thời gian sẵn sàng của máy chủ: từ 99%; sao lưu cơ sở dữ liệu mỗi đêm; khắc phục sự cố không quá 4 giờ.
- Toàn bộ giao dịch bán lẻ tại quầy POS được xử lý trực tuyến qua Backend API; hỗ trợ cả tiền mặt và VietQR.

### An toàn và bảo mật

- Phải đăng nhập trước khi xem thông tin cá nhân hoặc lịch sử giao dịch; mật khẩu được băm bằng thuật toán an toàn (BCrypt) trước khi lưu.
- Chính sách mật khẩu: tối thiểu 8 ký tự, tối đa 70 ký tự; bao gồm ít nhất 1 chữ hoa, 1 chữ thường và 1 số hoặc ký tự đặc biệt.
- Khóa tạm thời tài khoản trong 15 phút sau 5 lần nhập sai mật khẩu liên tiếp.
- Xác thực đăng ký qua mã OTP email có thời hạn 5 phút, giới hạn 5 lần thử sai/challenge, thời gian chờ gửi lại 60 giây và rate limit 5 lần gửi/email/giờ, 20 lần/IP/giờ.
- Phiên đăng nhập được quản lý bằng cookie HttpOnly, bảo vệ chống CSRF bằng Anti-Forgery token (`X-XSRF-TOKEN`).
- Dữ liệu truyền giữa client và server dùng HTTPS; quyền truy cập dữ liệu giới hạn nghiêm ngặt theo vai trò (`Admin`, `Manager`, `Staff`, `Customer`).
- Ghi audit log cho các thao tác quan trọng như sửa giá, đổi hoa hồng hoặc xóa sản phẩm.

### Tính chính xác và toàn vẹn dữ liệu

- Tính chính xác tiền thừa, hoa hồng và tiền thực trả cho người ký gửi.
- Mỗi món đồ được duyệt lên kệ có GUID duy nhất.
- Hóa đơn và dữ liệu giải ngân được lưu ít nhất 5 năm để phục vụ đối soát.

### Độ tiện dụng và khả năng bảo trì

- POS ưu tiên bàn phím, máy quét mã vạch và máy in hóa đơn; biểu mẫu phải báo lỗi rõ ràng tại thời điểm nhập.
- Người ký gửi có thể hoàn tất đăng ký thông tin trực tuyến trong vòng 5 phút.
- Giao diện Web xây dựng trên React 19/Vite/TypeScript; backend theo monolithic N-tier tinh gọn, Dependency Injection và kiểm thử tự động.
- Các tham số như hoa hồng, thời gian giữ hàng và mốc hạ giá được điều chỉnh từ màn hình quản lý, không sửa trực tiếp mã nguồn.
