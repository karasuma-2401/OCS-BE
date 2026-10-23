# Luồng dữ liệu nghiệp vụ

Tài liệu chuyển đổi các biểu mẫu, quy định, luồng D1–D6 và thuật toán trong Báo cáo SE100 (Final 3). Các hình DFD gốc là EMF; để tài liệu hiển thị nhất quán trên GitHub, nội dung của từng luồng được thể hiện bằng bảng thay cho ảnh định dạng EMF.

## 1. Đặt lịch hẹn ký gửi

### Biểu mẫu 1 — Phiếu đặt lịch hẹn ký gửi

| Trường thông tin | Trường thông tin |
| --- | --- |
| Mã lịch hẹn | Thời gian tạo |
| Họ tên khách hàng | Số điện thoại |
| Ngày hẹn | Khung giờ tiếp nhận |
| Trạng thái phiếu | Số lượng sản phẩm ước tính |

### Quy định

- **QĐ1.1:** Người ký gửi phải có tài khoản hợp lệ, định danh bằng số điện thoại đã xác thực OTP.
- **QĐ1.2:** Ngày hẹn không trước ngày hiện tại và phải thuộc khung giờ tiếp nhận hợp lệ.
- **QĐ1.3:** Lịch hẹn tạo thành công có trạng thái mặc định `Scheduled`.

### Luồng dữ liệu

| Luồng | Nội dung |
| --- | --- |
| D1 | Số điện thoại, họ tên, ngày hẹn, khung giờ và số lượng sản phẩm ước tính. |
| D2 | Không có. |
| D3 | Danh mục khung giờ tiếp nhận của cửa hàng. |
| D4 | D1 kèm mã lịch hẹn, thời gian tạo và trạng thái `Scheduled`. |
| D5 | Không có. |
| D6 | Thông báo đặt lịch thành công kèm mã lịch hẹn hoặc thông báo lỗi nhập liệu. |

### Thuật toán

1. Nhận D1 từ người dùng và kết nối cơ sở dữ liệu.
2. Kiểm tra các trường bắt buộc: số điện thoại, ngày hẹn, khung giờ.
3. Kiểm tra ngày hẹn không trước ngày hiện tại. Nếu dữ liệu không hợp lệ, trả D6 và kết thúc.
4. Sinh mã lịch hẹn, gán `Scheduled`, lưu D4.
5. Gửi D6 thành công, kích hoạt tác vụ nhắc lịch rồi kết thúc.

## 2. Lập hợp đồng ký gửi

### Biểu mẫu 2 — Hợp đồng ký gửi quần áo

| Trường thông tin | Trường thông tin |
| --- | --- |
| Mã hợp đồng | Ngày tạo |
| Khách ký gửi | Số điện thoại |
| Nhân viên kiểm định | Thời gian ký gửi |
| Hình thức xử lý khi hết hạn | Hoàn trả hoặc quyên góp |

Danh sách sản phẩm đạt chuẩn gồm: STT, GUID, tên sản phẩm, phần trăm mới, brand, phần trăm hoa hồng, dự trù nhận, giá bán và hình ảnh.

### Quy định

- **QĐ2.1:** Khách vãng lai chưa có tài khoản được kích hoạt theo số điện thoại tại quầy.
- **QĐ2.2:** Mỗi sản phẩm đạt chuẩn phải được phân hạng thương hiệu, áp tỷ lệ hoa hồng cấu hình và tính sẵn khoản dự trù thực nhận.
- **QĐ2.3:** Hợp đồng phải chốt phương án khi hết hạn: quyên góp hoặc hẹn nhận lại.
- **QĐ2.4:** Món được duyệt lên kệ phải có GUID, Barcode riêng, tem nhãn và trạng thái `OnSale`.

### Luồng dữ liệu

| Luồng | Nội dung |
| --- | --- |
| D1 | Số điện thoại khách, danh sách đồ tiếp nhận: phần trăm mới, hạng brand, giá niêm yết, vị trí kệ và hướng xử lý hết hạn. |
| D2 | Không có. |
| D3 | Tỷ lệ hoa hồng theo phân hạng và chu kỳ ký gửi, ví dụ 60 ngày. |
| D4 | D1 kèm mã hợp đồng, GUID, Barcode, trạng thái `OnSale`, ngày tiếp nhận và ngày hết hạn. |
| D5 | Dữ liệu in tem: GUID, Barcode, tên đồ, phân hạng, size, giá niêm yết và ngày hết hạn. |
| D6 | Thông báo lập hợp đồng thành công và bản hợp đồng ký gửi. |

### Thuật toán

1. Nhận dữ liệu kiểm định D1, đọc cấu hình hoa hồng D3.
2. Tìm khách theo số điện thoại; tạo tài khoản nếu chưa có.
3. Với từng sản phẩm: kiểm tra chất lượng, áp tỷ lệ hoa hồng, tính `dự trù thực nhận = giá niêm yết × (100% − % hoa hồng)`, sinh GUID/Barcode và gán `OnSale`.
4. Lưu hợp đồng, danh mục sản phẩm và ngày hết hạn vào D4.
5. Gửi D5 tới máy in tem, trả D6 rồi kết thúc.

## 3. Thu mua đứt sản phẩm

### Biểu mẫu 3 — Phiếu thu mua đứt

| Trường thông tin | Trường thông tin |
| --- | --- |
| Mã phiếu thu mua | Ngày tạo |
| Khách thanh lý | Số điện thoại |
| Nhân viên | Hình thức trả và tổng chi trả |

### Quy định

- **QĐ3.1:** Chỉ áp dụng cho sản phẩm đã qua kiểm định và khách muốn nhận tiền ngay.
- **QĐ3.2:** Giá thu mua đứt phải được Quản lý duyệt trước khi xuất quỹ.
- **QĐ3.3:** Sau khi chi tiền, quyền sở hữu thuộc cửa hàng; sản phẩm có Barcode của shop và chuyển `OnSale`.

### Luồng dữ liệu

| Luồng | Nội dung |
| --- | --- |
| D1 | Thông tin khách, sản phẩm đã thẩm định và giá thu mua đề xuất. |
| D2 | Xác nhận duyệt giá mua đứt từ Quản lý. |
| D3 | Hạn mức ngân sách thu mua của cửa hàng. |
| D4 | D1 + D2 + mã phiếu thu mua + Barcode shop + trạng thái `OnSale`, nguồn sở hữu cửa hàng. |
| D5 | Biên nhận chi tiền mua đứt. |
| D6 | Thông báo kết quả duyệt và hoàn tất giao dịch. |

### Thuật toán

1. Nhân viên gửi sản phẩm và giá đề xuất; hệ thống lấy xác nhận của Quản lý.
2. Nếu không duyệt, trả D6 từ chối và kết thúc.
3. Nếu duyệt, nhân viên chi tiền mặt hoặc chuyển khoản.
4. Sinh GUID/Barcode, đặt nguồn sở hữu cửa hàng và trạng thái `OnSale`.
5. Lưu D4, ghi audit log, in D5, trả D6.

## 4. Lập hóa đơn bán hàng tại quầy

### Biểu mẫu 4 — Hóa đơn bán hàng tại quầy

| Trường thông tin | Trường thông tin |
| --- | --- |
| Số hóa đơn | Ngày tạo hóa đơn |
| Nhân viên | Số điện thoại người mua |
| Phương thức thanh toán | Tổng tiền hóa đơn |
| Ghi chú | Danh sách sản phẩm |

Danh sách sản phẩm gồm: STT, GUID, tên sản phẩm, brand, đơn giá và thành tiền.

### Quy định

- **QĐ4.1:** Sản phẩm được nạp vào giỏ khi quét Barcode hoặc nhập GUID.
- **QĐ4.2:** Giá bán giữ theo giá niêm yết đã chốt trong hợp đồng.
- **QĐ4.3:** Thanh toán thành công chuyển sản phẩm ngay sang `Sold`.
- **QĐ4.4:** Offline chỉ cho phép tiền mặt, dữ liệu sản phẩm phải tải trước và một máy POS được quyền bán offline ở phiên bản đầu.
- **QĐ4.5:** Mobile/Website có thể chậm hiển thị trạng thái và doanh thu cho tới khi POS đồng bộ; phải đối soát trước khi giải ngân.

### Luồng dữ liệu

| Luồng | Nội dung |
| --- | --- |
| D1 | Barcode/GUID, phương thức thanh toán và số tiền nhận. |
| D2 | Webhook xác nhận thanh toán VietQR. |
| D3 | Online: dữ liệu hàng và kho trên Cloud. Offline: bản sao danh mục cục bộ; kiểm tra Barcode, trạng thái có thể bán, giá và phiên bản. |
| D4 | UUID giao dịch, hóa đơn, chi tiết hóa đơn và `Sold` cục bộ; offline phải ghi trong một transaction. |
| D5 | Hóa đơn bán lẻ in cho người mua. |
| D6 | Kết quả POS: Online, chờ đồng bộ, đã đồng bộ hoặc cần đối soát. Thông báo người gửi chỉ có sau khi Cloud ghi nhận thành công. |

### Thuật toán

1. Thu ngân quét Barcode hoặc nhập GUID, đọc D3. Nếu không phải `OnSale`, trả lỗi D6.
2. Nạp hàng vào giỏ với đúng giá niêm yết.
3. Online: nhận tiền mặt hoặc tạo VietQR và chờ D2. Offline: chỉ nhận tiền mặt.
4. Online cập nhật giao dịch theo quy tắc nghiệp vụ. Offline kiểm tra lại bản sao cục bộ, rồi ghi hóa đơn, chi tiết, `Sold` và Outbox trong một transaction; thất bại phải rollback.
5. In D5. Khi có mạng, Sync Service gửi sự kiện `Pending`/`Retry` cùng `EventId` không đổi. Nếu trả về `Conflict`, giữ giao dịch cục bộ để đối soát và không gửi thông báo hay giải ngân dựa trên giao dịch đó.

## 5. Xử lý sản phẩm hết hạn ký gửi

### Biểu mẫu 5 — Phiếu xử lý sản phẩm hết hạn

| Trường thông tin | Trường thông tin |
| --- | --- |
| Mã phiếu | Thời gian tạo |
| Nhân viên xử lý | Hình thức xử lý |
| Khách ký gửi | Số điện thoại |
| Tổ chức nhận từ thiện (nếu có) | Danh sách sản phẩm xử lý |

Danh sách gồm: STT, GUID, tên sản phẩm, brand, hình ảnh, giá bán và trạng thái.

### Quy định

- **QĐ5.1:** Hangfire chuyển hàng `OnSale` quá 60 ngày sang `Expired` và khóa bán tại POS.
- **QĐ5.2:** Hợp đồng quyên góp chuyển hàng sang `Donated`; hợp đồng hẹn nhận lại gửi thông báo trong N ngày, sau đó nhân viên quét mã để chuyển `Returned`.
- **QĐ5.3:** Quá N ngày không nhận lại và không phản hồi thì kích hoạt điều khoản từ bỏ quyền sở hữu, chuyển hàng sang `Donated`.

### Luồng dữ liệu

| Luồng | Nội dung |
| --- | --- |
| D1 | Barcode nhân viên quét khi giao trả tại quầy. |
| D2 | Không có. |
| D3 | Sản phẩm hết hạn và điều khoản trong hợp đồng. |
| D4 | Phiếu xử lý hết hạn và trạng thái `Expired` → `Donated` hoặc `Returned`. |
| D5 | Biên nhận trả hàng tồn cho người ký gửi. |
| D6 | Thông báo nhắc nhận hàng hoặc thư cảm ơn quyên góp. |

### Thuật toán

1. Lúc 00:00, Hangfire quét sản phẩm `OnSale` đã quá 60 ngày và chuyển sang `Expired`.
2. Đọc thỏa thuận hợp đồng. Nhánh quyên góp chuyển `Donated` và gửi thư cảm ơn; nhánh hoàn trả gửi thông báo hẹn nhận.
3. Khi khách đến, nhân viên quét D1 và cập nhật sản phẩm `Returned`.
4. Lưu D4, in D5 và gửi D6.

## 6. Rút hàng trước hạn

### Biểu mẫu 6 — Phiếu yêu cầu rút hàng trước hạn

| Trường thông tin | Trường thông tin |
| --- | --- |
| Mã phiếu | Thời gian lập |
| Người ký gửi | Số điện thoại |
| Chiết khấu/phí rút sớm | Thành tiền cần trả |
| Hình thức đóng phí | Danh sách sản phẩm yêu cầu rút |

Danh sách gồm: STT, GUID, tên sản phẩm, hình ảnh và giá bán.

### Quy định

- **QĐ6.1:** Chỉ được rút sớm khi sản phẩm `OnSale` và đợt ký gửi chưa hết hạn.
- **QĐ6.2:** Khách phải nộp phí rút sớm/phí lưu kho theo tỷ lệ trong hợp đồng.
- **QĐ6.3:** Sau khi thu đủ phí và quét mã bàn giao, sản phẩm chuyển `EarlyWithdrawn`.

### Luồng dữ liệu và thuật toán

| Luồng | Nội dung |
| --- | --- |
| D1 | GUID của sản phẩm xin rút và xác nhận thu phí từ nhân viên. |
| D2 | Không có. |
| D3 | Trạng thái `OnSale` và tỷ lệ phí rút sớm. |
| D4 | Phiếu rút hàng sớm và trạng thái `EarlyWithdrawn`. |
| D5 | Biên lai thu tiền phí. |
| D6 | Thông báo kết quả trên ứng dụng người ký gửi. |

1. Nhận D1, đọc D3. Nếu sản phẩm không `OnSale`, trả D6 và dừng.
2. Tính `phí rút sớm = giá niêm yết × % phí quy định`.
3. Khách nộp phí; nhân viên xác nhận thu đủ và quét Barcode bàn giao.
4. Đổi trạng thái sang `EarlyWithdrawn`, lưu D4, in D5 và trả D6.

## 7. Quyết toán và giải ngân cuối đợt

### Biểu mẫu 7 — Bảng kê quyết toán đợt ký gửi

| Trường thông tin | Trường thông tin |
| --- | --- |
| Mã phiếu | Thời gian tạo |
| Mã hợp đồng | Người ký gửi thụ hưởng và số điện thoại |
| Hình thức quyết toán | Minh chứng chuyển khoản (nếu có) |
| Tổng doanh thu món đã bán | Tổng hoa hồng cửa hàng |
| Tổng bồi thường | Phí dịch vụ phát sinh |
| Tổng thực chi cho khách |  |

### Quy định

- **QĐ7.1:** Chỉ lập bảng chính thức khi đợt kết thúc và đã nhận đủ giao dịch POS offline. Khi còn đồng bộ chờ hoặc xung đột, chỉ được xem bảng tạm tính.
- **QĐ7.2:** Quản lý phải đối soát và tải chứng từ chuyển khoản trước khi thông báo giải ngân.
- **QĐ7.3:** Đợt chỉ hoàn tất `Completed` khi người ký gửi xác nhận đã nhận tiền; nếu không, hệ thống tự xác nhận sau thời gian cấu hình.

### Luồng dữ liệu

| Luồng | Nội dung |
| --- | --- |
| D1 | Lệnh duyệt chi và ảnh chứng từ chuyển khoản của Quản lý. |
| D2 | Xác nhận **Đã nhận tiền** từ người ký gửi. |
| D3 | Sản phẩm trong đợt: món `Sold`, tỷ lệ hoa hồng và bồi thường sự cố. |
| D4 | Phiếu giải ngân và cập nhật đợt sang `Completed`. |
| D5 | Không có. |
| D6 | Thông báo giải ngân kèm ảnh ủy nhiệm chi. |

### Thuật toán

1. Kiểm tra đợt đã hết hạn hoặc không còn `OnSale`, mọi giao dịch POS đã đồng bộ và không có xung đột. Nếu chưa đạt, chuyển chờ đối soát.
2. Đọc D3, tính `tiền thực chi = tổng giá bán món Sold − tổng hoa hồng + bồi thường − phí dịch vụ`.
3. Xuất bảng kê để Quản lý kiểm tra. Quản lý chuyển khoản và tải D1.
4. Gửi D6, chờ D2, sau đó cập nhật phiếu giải ngân và đợt ký gửi `Completed`, lưu D4.
