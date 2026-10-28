# Use case chính

Tài liệu này chuyển các đặc tả use case trong Báo cáo SE100 (Final 3) sang Markdown. Các luồng mô tả mục tiêu nghiệp vụ; không phải toàn bộ use case dưới đây đã có controller hoặc API trong backend hiện tại.

## Sơ đồ use case tổng quan

Sơ đồ được lưu dưới dạng PlantUML tại [diagrams/use-case-overview.puml](diagrams/use-case-overview.puml). Repository không lưu ảnh nhị phân; tệp này là nguồn duy nhất để xem, chỉnh sửa hoặc sinh sơ đồ.

## UC-01 — Đặt lịch hẹn ký gửi

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Người ký gửi |
| Kích hoạt | Người ký gửi chọn **Đặt lịch hẹn mới** trên ứng dụng. |
| Điều kiện trước | Đã đăng nhập bằng tài khoản hợp lệ qua số điện thoại và OTP. |
| Kết quả | Thành công: tạo mã phiếu hẹn, lịch hiển thị cho nhân viên. Thất bại: không tạo lịch và báo lỗi. |

**Luồng chính**

1. Người ký gửi chọn chức năng đặt lịch.
2. Hệ thống hiển thị ngày tiếp nhận, khung giờ và số lượng quần áo dự kiến.
3. Người ký gửi nhập thông tin và xác nhận.
4. Hệ thống kiểm tra dữ liệu, ghi lịch vào cơ sở dữ liệu và cấp mã phiếu hẹn.
5. Hệ thống thông báo kết quả, đồng thời tạo tác vụ nhắc lịch.

**Luồng thay thế:** nếu khung giờ đã đủ số lượng tiếp nhận, hệ thống yêu cầu chọn ngày hoặc giờ khác. Nếu thiếu ngày hoặc khung giờ, hệ thống yêu cầu nhập lại.

## UC-02 — Tiếp nhận, kiểm định và lập hợp đồng ký gửi

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Nhân viên |
| Kích hoạt | Nhân viên tiếp nhận bằng mã lịch hẹn hoặc tạo tiếp nhận trực tiếp tại quầy. |
| Điều kiện trước | Quần áo đã được mang đến quầy; nhân viên đã đăng nhập. |
| Kết quả | Thành công: sản phẩm có GUID, Barcode, tem nhãn và chuyển sang `OnSale`. Hàng không đạt chuẩn được trả lại. |

**Luồng chính**

1. Nhân viên nhập số điện thoại hoặc quét mã lịch hẹn. Nếu chưa có hồ sơ, hệ thống tạo tài khoản theo số điện thoại và gửi thông tin kích hoạt.
2. Nhân viên kiểm tra độ mới, chất liệu, tem mác và khuyết tật của từng món.
3. Nhân viên chọn phân hạng thương hiệu, nhập giá niêm yết; hệ thống tính dự trù tiền người gửi nhận được.
4. Người ký gửi xác nhận phương án ký gửi và chọn hoàn trả hoặc quyên góp khi hết hạn.
5. Nhân viên lập hợp đồng. Hệ thống sinh GUID và Barcode độc nhất, gửi lệnh in tem với tên đồ, phân hạng, size, giá và ngày hết hạn.
6. Nhân viên dán tem, cập nhật vị trí lưu trữ và đưa sản phẩm lên bán.

**Luồng thay thế:** hàng ố mốc, rách hoặc nghi giả bị từ chối và trả tại quầy. Nếu khách muốn nhận tiền ngay, luồng chuyển sang UC-03.

> Báo cáo có đề cập gợi ý giá bằng AI ở bước kiểm định nhưng ghi rõ đang trong giai đoạn đề xuất; đây không được xem là chức năng đã cam kết trong use case.

## UC-03 — Thu mua đứt sản phẩm

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Nhân viên; Quản lý duyệt giá |
| Kích hoạt | Khách chọn **Bán đứt lấy tiền ngay** trong lúc thẩm định. |
| Điều kiện trước | Sản phẩm đã đạt tiêu chuẩn kiểm định. |
| Kết quả | Tạo phiếu thu mua, chi tiền; quyền sở hữu chuyển sang cửa hàng và sản phẩm được đưa lên bán. |

**Luồng chính**

1. Nhân viên đề xuất giá thu mua theo tỷ lệ so với giá dự trù bán lẻ.
2. Quản lý xem và duyệt giá trên hệ thống.
3. Khách đồng ý; nhân viên lập phiếu và chi tiền mặt hoặc chuyển khoản.
4. Hệ thống chuyển quyền sở hữu cho cửa hàng, sinh GUID/Barcode, in tem và đặt trạng thái `OnSale`.

**Luồng thay thế:** nếu khách không đồng ý giá, khách có thể quay lại ký gửi hoặc nhận hàng về. Nếu quản lý không duyệt, giá được điều chỉnh hoặc yêu cầu thu mua bị từ chối.

## UC-04 — Bán hàng tại quầy POS

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Nhân viên/thu ngân |
| Kích hoạt | Khách mua mang sản phẩm đến quầy. |
| Điều kiện trước | Sản phẩm ở trạng thái `OnSale`, chưa được giao dịch khác bán. Quản lý / Thu ngân truy cập màn hình Web POS trực tuyến. |
| Kết quả | Lưu hóa đơn, dòng chi tiết và trạng thái `Sold` trên Cloud Database trong cùng một transaction. |

**Luồng chính**

1. Thu ngân quét Barcode/QR hoặc nhập GUID khi tem không đọc được.
2. POS nạp sản phẩm vào giỏ, giữ nguyên giá niêm yết đã chốt.
3. Thu ngân chọn tiền mặt hoặc VietQR động. Với tiền mặt, hệ thống tính tiền thừa; với VietQR, hệ thống chờ webhook xác nhận thanh toán.
4. Khi thanh toán hợp lệ, hệ thống ghi hóa đơn và chuyển sản phẩm sang `Sold`.
5. POS in hóa đơn, trong đó nêu rõ hàng ký gửi đã kiểm định và không áp dụng đổi trả.
6. Khi dữ liệu đã được ghi nhận trên Cloud, hệ thống thông báo cho người ký gửi.

**Luồng thay thế:** hủy mua chỉ xóa hàng khỏi giỏ, không đổi trạng thái. Nếu giao dịch thanh toán thất bại, toàn bộ giỏ hàng và trạng thái sản phẩm được hoàn nguyên (rollback) an toàn.

## UC-05 — Xử lý sản phẩm hết hạn đợt ký gửi

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Hệ thống; Nhân viên khi giao trả |
| Kích hoạt | Hangfire kiểm tra hạn ký gửi mỗi ngày lúc 00:00. |
| Điều kiện trước | Sản phẩm `OnSale` đã vượt chu kỳ ký gửi. |
| Kết quả | Sản phẩm bị khóa bán. Hàng được quyên góp thành `Donated` hoặc giao trả thành `Returned`. |

**Luồng chính**

1. Tác vụ nền chuyển hàng quá hạn từ `OnSale` sang `Expired` và vô hiệu hóa bán tại POS.
2. Hệ thống đọc thỏa thuận trong hợp đồng.
3. Nếu quyên góp, sản phẩm chuyển `Donated`, được gom vào danh sách thiện nguyện và người gửi nhận thông báo cảm ơn.
4. Nếu nhận lại, hệ thống nhắc người gửi đến nhận trong thời hạn quy định.
5. Nhân viên quét Barcode khi bàn giao; sản phẩm chuyển `Returned`.

**Luồng thay thế:** khi khách không đến nhận sau thời hạn quy định và không phản hồi, cửa hàng kích hoạt điều khoản từ bỏ quyền sở hữu, sau đó có thể chuyển hàng sang nhánh quyên góp.

## UC-06 — Yêu cầu và xử lý rút hàng sớm

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Người ký gửi, Nhân viên |
| Kích hoạt | Người ký gửi chọn **Yêu cầu rút hàng sớm** trong ứng dụng. |
| Điều kiện trước | Sản phẩm đang `OnSale` và đợt ký gửi chưa hết hạn. |
| Kết quả | Tạo biên nhận phí, giao lại sản phẩm và chuyển trạng thái sang `EarlyWithdrawn`. |

**Luồng chính**

1. Người ký gửi chọn sản phẩm và tạo yêu cầu.
2. Hệ thống tính phí quản lý/hủy hợp đồng theo tỷ lệ đã thỏa thuận.
3. Hệ thống gửi yêu cầu đến phần mềm của nhân viên tại quầy.
4. Khách đến cửa hàng đóng phí; nhân viên thu tiền và quét Barcode xác nhận giao trả.
5. Hệ thống cập nhật trạng thái sản phẩm sang `EarlyWithdrawn`.

## UC-07 — Quyết toán và giải ngân cuối đợt

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Quản lý, Người ký gửi |
| Kích hoạt | Đợt ký gửi đã hết hạn hoặc mọi sản phẩm trong đợt đã được xử lý. |
| Điều kiện trước | Không còn sản phẩm `OnSale` hay sự cố chưa xử lý; POS đã đồng bộ các giao dịch liên quan. |
| Kết quả | Phiếu giải ngân hoàn tất và hợp đồng/đợt ký gửi chuyển `Completed`. |

**Luồng chính**

1. Hệ thống tổng hợp doanh thu và tạo bảng quyết toán.
2. Hệ thống tính hoa hồng theo từng món đã bán.
3. Tiền thực nhận = tổng giá bán món `Sold` − tổng hoa hồng − phí dịch vụ (nếu có) + tiền bồi thường (nếu có).
4. Quản lý kiểm tra, duyệt lệnh chi, chuyển khoản và tải chứng từ.
5. Hệ thống gửi thông báo kèm biên lai cho người ký gửi.
6. Người ký gửi xác nhận đã nhận tiền. Nếu không có phản hồi sau thời gian cấu hình, hệ thống tự xác nhận.
7. Hệ thống chốt sổ, chuyển đợt ký gửi sang `Completed`.

**Luồng thay thế:** nếu có giao dịch POS `Pending`, `Failed`, `Conflict` hoặc chưa xác nhận đồng bộ, không được duyệt giải ngân. Xung đột liên quan hàng rút sớm/hết hạn phải được quản lý kiểm tra; không tự bỏ qua hóa đơn tiền mặt.

## UC-08 — Ghi nhận và bồi thường sự cố

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Nhân viên, Quản lý |
| Kích hoạt | Phát hiện sản phẩm bị hư hỏng nặng hoặc thất lạc trong kho/trưng bày. |
| Điều kiện trước | Sản phẩm đang được cửa hàng quản lý. |
| Kết quả | Sản phẩm chuyển `Compensated`; khoản bồi thường được đưa vào bảng quyết toán của người ký gửi. |

**Luồng chính:** nhân viên tìm sản phẩm bằng Barcode/GUID, lập biên bản có mô tả hư hại/mất mát. Quản lý duyệt mức bồi thường theo giá đã thỏa thuận. Hệ thống bỏ sản phẩm khỏi kệ, chuyển sang `Compensated` và cộng khoản bồi thường vào quyết toán.

**Luồng thay thế:** quản lý có thể từ chối khi xác định lỗi đã có lúc tiếp nhận hoặc cần nhân viên kiểm tra lại.

## UC-09 — Cấu hình tham số hệ thống

| Thuộc tính | Mô tả |
| --- | --- |
| Tác nhân | Admin, Quản lý |
| Kích hoạt | Người có quyền mở mục **Cài đặt / Cấu hình hệ thống**. |
| Điều kiện trước | Tài khoản có vai trò Admin hoặc Manager. |
| Kết quả | Tham số hợp lệ được áp dụng và có audit log về người thực hiện, thời điểm, giá trị cũ/mới. |

**Luồng chính:** người dùng điều chỉnh chu kỳ ký gửi, biên độ hoa hồng theo phân hạng và các tham số liên quan; hệ thống kiểm tra giá trị, lưu cấu hình rồi ghi audit log.

**Luồng thay thế:** nếu tỷ lệ hoa hồng âm hoặc lớn hơn 100%, hệ thống hiển thị lỗi và không lưu.
