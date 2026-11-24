# API rút hàng sớm

## Luồng nghiệp vụ

1. Khách gửi yêu cầu cho 1–100 sản phẩm ký gửi của chính mình, đang `OnSale`, hợp đồng `Active` và còn hạn.
2. Hệ thống chốt giá niêm yết, tỷ lệ phí và số tiền từng sản phẩm tại thời điểm gửi yêu cầu.
3. Nhân viên duyệt. Hàng được giữ để trả khách, không còn xuất hiện trên danh mục công khai và không được bán qua POS/VietQR.
4. Nhân viên ghi nhận đã thu đủ phí, tạo giao dịch tài chính `Income / Success`.
5. Nhân viên xác nhận bàn giao kèm bằng chứng. Hệ thống đổi sản phẩm `OnSale → EarlyWithdrawn`, bỏ vị trí kho và ghi lịch sử trạng thái.

Trạng thái phiếu: `Pending → Approved → Completed`. Khách chỉ được `cancel` khi `Pending`. Nhân viên có thể `reject` phiếu `Pending` hoặc `Approved` chưa thu phí; thao tác này giải phóng hàng giữ. Sau khi thu phí, không cho từ chối/hủy trong API này vì cần quy trình hoàn phí riêng.

Khi còn `Pending`, hàng vẫn có thể bán hoặc hết hạn; lúc duyệt sẽ kiểm tra lại. Phiếu được duyệt trước hạn giữ hàng cho tới khi giao hoặc bị từ chối, kể cả khi hợp đồng hết hạn sau đó. Job hết hạn bỏ qua hàng đã giữ để tránh xử lý trả/quyên góp trùng. Nhân viên cần theo dõi phiếu `Approved` chưa hoàn tất.

## Phí theo hợp đồng

Thêm `earlyWithdrawalFeeRate` trong request tạo/xem trước hợp đồng, giá trị từ 0 đến 100, tối đa 2 chữ số thập phân. Giá trị được lưu trên hợp đồng và bổ sung vào nội dung điều khoản xem trước. Response tạo và chi tiết hợp đồng trả lại trường này. Đây là tỷ lệ phần trăm trên giá niêm yết, độc lập với tỷ lệ hoa hồng.

```json
{ "earlyWithdrawalFeeRate": 10 }
```

Các trường tạo hợp đồng hiện có vẫn cần được gửi đầy đủ. Phí từng sản phẩm:

```text
penaltyAmount = Round(listedPrice × earlyWithdrawalFeeRate / 100, 2, AwayFromZero)
totalPenaltyAmount = tổng penaltyAmount từng sản phẩm
```

Ví dụ giá 123456.75, tỷ lệ 10% → phí 12345.68. Không lấy phí từ request rút hàng. Thay đổi dữ liệu hợp đồng sau khi gửi không làm thay đổi mức phí đã chốt.

`null` nghĩa là chưa cấu hình, không đồng nghĩa miễn phí. Hợp đồng cũ giữ `null` và bị chặn rút sớm cho tới khi người phụ trách đối chiếu điều khoản đã ký và cập nhật dữ liệu đúng thỏa thuận. Không tự gán tỷ lệ mặc định cho hợp đồng cũ. Không có API sửa phí hợp đồng đã ký trong thay đổi này.

Tỷ lệ 0 là miễn phí rõ ràng: sau khi duyệt, `paymentStatus = Paid`, bỏ qua bước thu phí, không tạo giao dịch 0 đồng; vẫn phải xác nhận bàn giao. `paidAt` chỉ có khi thực sự ghi nhận thu phí.

## Xác thực và phân quyền

Mọi endpoint cần token đăng nhập theo cơ chế xác thực hiện có. API khách chỉ cho role `Customer`; ID khách lấy từ token, không nhận `customerId` từ body. Không cho xem hoặc thao tác yêu cầu của khách khác.

API nhân viên yêu cầu role `Admin,Manager,Staff` và quyền tương ứng:

| Quyền | Chức năng |
|---|---|
| `withdrawals.read` | Danh sách và chi tiết |
| `withdrawals.approve` | Duyệt, từ chối |
| `withdrawals.payment` | Ghi nhận thu phí |
| `withdrawals.handover` | Xác nhận bàn giao |

Migration cấp mặc định 4 quyền cho Manager và Staff. Admin cần được cấp quyền chức năng theo cơ chế quản lý quyền hiện có. Thao tác ghi kiểm tra thêm hồ sơ nhân viên đang làm việc và tài khoản nội bộ hoạt động.

## API cho khách

| Method | URL | Body |
|---|---|---|
| POST | `/api/customers/me/early-withdrawals` | `productIds`, `reason` |
| GET | `/api/customers/me/early-withdrawals/eligible-products` | Không |
| GET | `/api/customers/me/early-withdrawals?page=1&pageSize=20` | Không |
| GET | `/api/customers/me/early-withdrawals/{id}` | Không |
| POST | `/api/customers/me/early-withdrawals/{id}/cancel` | Không |

Gửi yêu cầu:

```json
{
  "productIds": ["11111111-1111-1111-1111-111111111111"],
  "reason": "Tôi cần lấy lại sản phẩm"
}
```

`reason` bắt buộc, không chỉ khoảng trắng, tối đa 1000 ký tự. Không nhận GUID rỗng, sản phẩm trùng trong body, hàng mua đứt, đã bán, hết hạn, sai chủ hoặc đã có yêu cầu `Pending/Approved`.

`eligible-products` trả mảng món đủ điều kiện thuộc khách hiện tại; mỗi món có `productId`, `name`, `barcode`, `contractId`, `contractNumber`, `endsAt`, `listedPrice`, `penaltyRate`, `penaltyAmount`. Phí được server tính và làm tròn theo cùng quy tắc lúc tạo yêu cầu. Các món đang có phiếu Pending/Approved hoặc hợp đồng chưa cấu hình phí không xuất hiện. Màn hình có thể cộng các phí item để hiển thị tổng dự kiến, nhưng mức phí được chốt lại khi gửi yêu cầu.

## API cho nhân viên

| Method | URL | Body |
|---|---|---|
| GET | `/api/early-withdrawals?page=1&pageSize=20` | Không |
| GET | `/api/early-withdrawals/{id}` | Không |
| POST | `/api/early-withdrawals/{id}/approve` | Không |
| POST | `/api/early-withdrawals/{id}/reject` | `{"note":"Khách đổi ý"}` |
| POST | `/api/early-withdrawals/{id}/payment` | Ví dụ bên dưới |
| POST | `/api/early-withdrawals/{id}/handover` | `{"evidence":"Khách đã nhận đủ hàng, biên bản BG-001"}` |

Ghi nhận thu phí:

```json
{
  "amount": 12345.68,
  "paymentMethod": "BankTransfer",
  "reference": "BANK-001"
}
```

`amount` phải đúng toàn bộ phí. Hỗ trợ `Cash` hoặc `BankTransfer`. Chuyển khoản bắt buộc `reference`, tối đa 64 ký tự; tiền mặt có thể bỏ trường này. Đây là nhân viên xác nhận tiền đã thu, không phải cổng thanh toán hoặc tạo QR. Không hỗ trợ thu một phần; thu lần hai bị trả 409 và không tạo thêm giao dịch.

`note` và `evidence` bắt buộc, tối đa 1000 ký tự. Bằng chứng bàn giao là nội dung hoặc mã tham chiếu biên bản, không phải endpoint upload file. Giao một lần toàn bộ sản phẩm trong phiếu; không hỗ trợ giao từng phần.

Nếu sản phẩm liên kết hóa đơn đã thu tiền hoặc phiên thanh toán còn hiệu lực, API chặn duyệt. Có thể duyệt sau khi phiên thanh toán chưa thu tiền hết hạn; hóa đơn và giao dịch cũ được giữ nguyên. POS/VietQR kiểm tra trạng thái giữ hàng trước khi hoàn tất bán để chặn thanh toán muộn cho hàng đã được duyệt rút.

## Response và lỗi

Thành công trả HTTP 200, body trực tiếp là phiếu; danh sách trả mảng, sắp xếp thời điểm gửi mới nhất. `page` từ 1–100000, `pageSize` từ 1–100. Không có tổng số dòng trong response danh sách.

Phiếu gồm `id`, `customerId`, `status`, `reason`, `totalPenaltyAmount`, `paymentStatus`, `requestedAt`, người/thời điểm duyệt (`processedByEmployeeId`, `approvedAt`), `decisionNote`, người/thời điểm thu phí (`paidByEmployeeId`, `paidAt`), người/thời điểm giao (`handedOverByEmployeeId`, `handedOverAt`), `handoverEvidence` và `items`.

Mỗi item gồm `productId`, `name`, `contractId`, `listedPrice`, `penaltyRate`, `penaltyAmount`. UI hiển thị tiền đã được server tính, không tự thay đổi mức phí.

Response phiếu bổ sung `customerName`, `customerPhone`; item bổ sung `contractNumber` để nhân viên đối chiếu. Các trường này có thể null khi thiếu dữ liệu.

## Giao diện frontend

- Khách truy cập `/my-withdrawals` từ menu hoặc nút Rút hàng sớm trong Theo dõi ký gửi. Chọn tối đa 100 món, xem phí từng món và tổng dự kiến, nhập lý do rồi gửi. Có thể hủy phiếu đang chờ duyệt.
- Nhân viên truy cập `/shop/withdrawals` qua menu Rút hàng sớm, cần `withdrawals.read`. Chi tiết chỉ hiện các nút được cấp đúng quyền chức năng; duyệt, thu phí và bàn giao đều có bước xác nhận.
- Chuyển khoản cần mã tham chiếu; từ chối cần lý do hiển thị cho khách; bàn giao cần bằng chứng hoặc mã biên bản. Hàng miễn phí bỏ qua bước thu phí.
- Danh sách yêu cầu tự cập nhật mỗi 30 giây khi trang đang hiển thị và có nút làm mới. Danh sách món đủ điều kiện được cập nhật bằng nút Cập nhật và sau các thao tác; server vẫn kiểm tra lại trước khi ghi dữ liệu.
- Màn hình lập hợp đồng có trường tỷ lệ phí rút sớm; thay đổi phí làm mất hiệu lực chữ ký hiện tại và cần ký lại. Để trống giữ trạng thái chưa cấu hình, nhập 0 là miễn phí.
- Dùng component chung, token màu/font/spacing của dự án; có trạng thái tải, trống, lỗi, và xử lý 409 bằng tải lại phiếu.

| HTTP | Ý nghĩa |
|---|---|
| 400 | Body, phân trang, số tiền, phương thức hoặc bằng chứng không hợp lệ |
| 401 | Chưa xác thực hoặc token thiếu ID hợp lệ |
| 403 | Thiếu role/quyền, tài khoản/hồ sơ không hoạt động |
| 404 | Không tìm thấy phiếu/sản phẩm trong phạm vi được truy cập |
| 409 | Sai bước, trùng yêu cầu, hợp đồng thiếu phí, hàng không còn hợp lệ hoặc dữ liệu thay đổi đồng thời |

Lỗi nghiệp vụ dùng `ProblemDetails` hiện có và mã `WITHDRAWAL.*`. Khi nhận 409, tải lại dữ liệu và kiểm tra kết quả trước khi thao tác lại. Các thao tác dùng transaction Serializable trên PostgreSQL cùng concurrency token của phiếu/sản phẩm để tránh duyệt, thu phí hoặc bàn giao lặp khi xử lý đồng thời.

## Triển khai và kiểm thử

Đã thêm migration `AddEarlyWithdrawalWorkflow`, cập nhật model snapshot và quyền. Migration đã được áp dụng vào database Neon đang cấu hình của API ngày 24/11/2026; đã xác nhận không còn migration chờ và truy vấn sản phẩm, phiếu rút hàng, hợp đồng thành công. Các môi trường khác cần chạy từ thư mục `OCS-BE` khi triển khai:

```powershell
dotnet ef database update --project src/OCS.Api
```

Quyền rút hàng sớm dùng ID ổn định theo số 1001–1004 trong `PermissionCatalog`, tránh trùng ID 36–39 mà database hiện tại đã dùng cho sự cố và quyết toán. Migration giữ nguyên các quyền có sẵn đó.

Bản ghi rút sớm tồn tại trước migration được đánh dấu `Legacy`, giữ số tiền/trạng thái thanh toán cũ và không cho đi vào các thao tác mới. `requestedAt` lấy từ `CreatedAt`; tỷ lệ phí item cũ là 0 do chưa từng có dữ liệu tỷ lệ, không được dùng làm căn cứ tính lại phí. Hợp đồng cũ không được tự bổ sung tỷ lệ.

Server đang chạy cần được build lại và khởi động lại sau khi áp dụng migration. Nếu thư mục build bị khóa bởi API đang chạy, có thể kiểm thử bằng cấu hình riêng:

```powershell
dotnet test tests/OCS.Api.IntegrationTests/OCS.Api.IntegrationTests.csproj --no-restore -c WithdrawalVerify -p:UseAppHost=false --filter EarlyWithdrawalTests
```

Kiểm thử bao gồm phí được chốt, làm tròn, quyền sở hữu, gửi trùng, hủy/gửi lại, duyệt, chặn POS, sai tiền, thu trùng, bàn giao trước khi thu, bàn giao trùng, miễn phí và giải phóng hàng khi từ chối. Các kiểm thử nghiệp vụ dùng EF InMemory; không thay thế kiểm thử đồng thời trên PostgreSQL thật.
