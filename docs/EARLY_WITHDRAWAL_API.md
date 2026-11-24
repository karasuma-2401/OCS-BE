# API rút hàng sớm

## Luồng nghiệp vụ

1. Khách gửi yêu cầu cho 1–100 sản phẩm ký gửi của chính mình, đang `OnSale`, hợp đồng `Active` và còn hạn.
2. Hệ thống chốt giá niêm yết, tỷ lệ phí và số tiền từng sản phẩm tại thời điểm gửi yêu cầu.
3. Nhân viên duyệt. Hàng được giữ để trả khách, không còn xuất hiện trên danh mục công khai và không được bán qua POS/VietQR.
4. Nhân viên ghi nhận đã thu đủ phí, tạo giao dịch tài chính `Income / Success`.
5. Nhân viên xác nhận bàn giao kèm bằng chứng. Hệ thống đổi sản phẩm `OnSale → EarlyWithdrawn`, bỏ vị trí kho và ghi lịch sử trạng thái.

Trạng thái phiếu: `Pending → Approved → Completed`. Khách chỉ được `cancel` khi `Pending`. Nhân viên có thể `reject` phiếu `Pending` hoặc `Approved` chưa thu phí; thao tác này giải phóng hàng giữ. Sau khi thu phí, không cho từ chối/hủy trong API này vì cần quy trình hoàn phí riêng.

Khi còn `Pending`, hàng vẫn có thể bán hoặc hết hạn; lúc duyệt sẽ kiểm tra lại. Phiếu được duyệt trước hạn giữ hàng cho tới khi giao hoặc bị từ chối, kể cả khi hợp đồng hết hạn sau đó. Job hết hạn bỏ qua hàng đã giữ để tránh xử lý trả/quyên góp trùng. Nhân viên cần theo dõi phiếu `Approved` chưa hoàn tất.

## Phí theo từng sản phẩm trên hợp đồng

Phí rút sớm dùng chính tỷ lệ hoa hồng đã thỏa thuận của từng sản phẩm (`Product.AgreedCommissionRate`). Không dùng tỷ lệ hiện tại của thương hiệu hoặc một mức phí riêng cho toàn bộ hợp đồng.

```text
penaltyRate = product.agreedCommissionRate
penaltyAmount = Round(product.listedPrice × penaltyRate / 100, 2, AwayFromZero)
totalPenaltyAmount = tổng penaltyAmount từng sản phẩm
```

Ví dụ giá 123456.75, tỷ lệ hoa hồng 10% → phí 12345.68. Hai món trong cùng hợp đồng có thể có tỷ lệ và phí khác nhau. API dự kiến và API tạo yêu cầu dùng cùng công thức, không nhận phí từ trình duyệt. Yêu cầu đã gửi giữ nguyên giá/tỷ lệ/số tiền đã chốt, kể cả phiếu tạo theo quy tắc cũ.

`AgreedCommissionRate = null` nghĩa là thiếu dữ liệu thỏa thuận và bị chặn rút sớm. Trường `earlyWithdrawalFeeRate` trên hợp đồng được giữ để tương thích API/dữ liệu cũ, không còn dùng để tính phí hay xét điều kiện rút. Hợp đồng có trường này bằng null vẫn rút được nếu sản phẩm có tỷ lệ hoa hồng hợp lệ. Màn hình lập hợp đồng bỏ ô nhập phí riêng; hợp đồng mới thông thường ghi rõ công thức theo hoa hồng từng sản phẩm. Điều khoản riêng đã được quản lý phê duyệt giữ nguyên nội dung. Thay đổi này không cần migration mới.

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
| GET | `/api/customers/me/early-withdrawals/search` | Query: `page`, `pageSize`, `q`, `status`, `paymentStatus`, `sort` |
| GET | `/api/customers/me/early-withdrawals/summary` | Không |
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

`eligible-products` trả mảng món đủ điều kiện thuộc khách hiện tại; mỗi món có `productId`, `name`, `barcode`, `contractId`, `contractNumber`, `endsAt`, `listedPrice`, `penaltyRate`, `penaltyAmount`. Phí được server tính và làm tròn theo cùng quy tắc lúc tạo yêu cầu. Các món đang có phiếu Pending/Approved hoặc chưa có tỷ lệ hoa hồng thỏa thuận không xuất hiện. Màn hình có thể cộng các phí item để hiển thị tổng dự kiến, nhưng mức phí được chốt lại khi gửi yêu cầu.

## API cho nhân viên

| Method | URL | Body |
|---|---|---|
| GET | `/api/early-withdrawals?page=1&pageSize=20` | Không |
| GET | `/api/early-withdrawals/search` | Query: `page`, `pageSize`, `q`, `status`, `paymentStatus`, `sort` |
| GET | `/api/early-withdrawals/summary` | Không |
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

Riêng `/search` trả `{ items, page, pageSize, totalCount, totalPages }`, lọc trên server trước khi phân trang. `q` tối đa 200 ký tự, tìm theo tiền tố ID yêu cầu, tên/số điện thoại khách, tên/barcode sản phẩm hoặc số hợp đồng. `status`: Pending, Approved, Completed, Rejected, Cancelled, Legacy; `paymentStatus`: Unpaid, Partial, Paid; `sort`: newest (mặc định) hoặc oldest. Bỏ trường bộ lọc để lấy tất cả. `/summary` trả `totalCount`, `pendingCount`, `approvedCount`, `completedCount`, `rejectedCount` cho toàn bộ phạm vi được phép, độc lập với bộ lọc. Endpoint khách luôn chỉ tính và tìm yêu cầu của chính tài khoản đăng nhập.

Phiếu gồm `id`, `customerId`, `status`, `reason`, `totalPenaltyAmount`, `paymentStatus`, `requestedAt`, người/thời điểm duyệt (`processedByEmployeeId`, `approvedAt`), `decisionNote`, người/thời điểm thu phí (`paidByEmployeeId`, `paidAt`), người/thời điểm giao (`handedOverByEmployeeId`, `handedOverAt`), `handoverEvidence` và `items`.

Mỗi item gồm `productId`, `name`, `contractId`, `listedPrice`, `penaltyRate`, `penaltyAmount`. UI hiển thị tiền đã được server tính, không tự thay đổi mức phí.

Response phiếu bổ sung `customerName`, `customerPhone`; item bổ sung `contractNumber` để nhân viên đối chiếu. Các trường này có thể null khi thiếu dữ liệu.

## Giao diện frontend

- Khách truy cập `/my-withdrawals` từ menu hoặc nút Rút hàng sớm trong **chi tiết sản phẩm** của Theo dõi ký gửi. Header ký gửi chỉ còn nút Làm mới.
- Hai tab **Rút hàng** (`?tab=withdraw`) và **Theo dõi yêu cầu muốn rút** (`?tab=requests`) chỉ hiển thị nội dung tab đang chọn. Tab rút cho chọn tối đa 100 món, xem phí từng món/tổng, nhập lý do và gửi. Gửi thành công chuyển sang tab theo dõi và mở phiếu vừa tạo. Khách có thể hủy phiếu đang chờ duyệt.
- Liên kết từ chi tiết sản phẩm có thêm `contractId` và `productId`. Trang đích tải danh sách của khách đăng nhập, chọn sẵn đúng món và lọc theo barcode. Có thể bỏ lọc để chọn thêm; món không còn đủ điều kiện sẽ có thông báo và không tự được chọn/gửi.
- Nút Theo dõi yêu cầu trong chi tiết sản phẩm dẫn đến `?tab=requests&requestId={id}`, mở đúng phiếu sau khi API xác thực quyền sở hữu.
- Nhân viên truy cập `/shop/withdrawals` qua menu Rút hàng sớm, cần `withdrawals.read`. Chi tiết chỉ hiện các nút được cấp đúng quyền chức năng; duyệt, thu phí và bàn giao đều có bước xác nhận.
- Chuyển khoản cần mã tham chiếu; từ chối cần lý do hiển thị cho khách; bàn giao cần bằng chứng hoặc mã biên bản. Hàng miễn phí bỏ qua bước thu phí.
- Danh sách yêu cầu tự cập nhật mỗi 30 giây khi trang đang hiển thị và có nút làm mới. Danh sách món đủ điều kiện được cập nhật bằng nút Cập nhật và sau các thao tác; server vẫn kiểm tra lại trước khi ghi dữ liệu.
- Màn hình lập hợp đồng bỏ mức phí riêng; phí dùng tỷ lệ hoa hồng đã chốt từng món.
- Dùng component chung, token màu/font/spacing của dự án; có trạng thái tải, trống, lỗi, và xử lý 409 bằng tải lại phiếu.
- Trang rút hàng dùng cùng thẻ thống kê và bố cục tìm kiếm/bộ lọc với Quản lý kho. Có lọc nhanh bằng thẻ, chip điều kiện, xóa từng điều kiện hoặc tất cả, và tổng số dòng/phân trang từ server. Bấm dòng chỉ đánh dấu bằng vạch dọc; nút Chi tiết mới mở popup. Các endpoint search/summary không cần migration database mới.

API ký gửi danh sách/tìm kiếm/chi tiết sản phẩm bổ sung `canWithdrawEarly`, `earlyWithdrawalFee` và `withdrawal` (yêu cầu gần nhất: `id`, `status`, `paymentStatus`, `decisionNote`, `requestedAt`). Bảng Theo dõi ký gửi tổng hợp trạng thái các món, chi tiết hiển thị từng món kèm tiến trình rút: chờ duyệt, chờ thu phí, chờ bàn giao, từ chối và lý do, đã hủy, đã bàn giao. Trạng thái sản phẩm vẫn là `OnSale` trong lúc chờ/giữ hàng, đổi sang `EarlyWithdrawn` sau bàn giao. Trang ký gửi tải lại khi quay lại/focus và mỗi 30 giây.

| HTTP | Ý nghĩa |
|---|---|
| 400 | Body, phân trang, số tiền, phương thức hoặc bằng chứng không hợp lệ |
| 401 | Chưa xác thực hoặc token thiếu ID hợp lệ |
| 403 | Thiếu role/quyền, tài khoản/hồ sơ không hoạt động |
| 404 | Không tìm thấy phiếu/sản phẩm trong phạm vi được truy cập |
| 409 | Sai bước, trùng yêu cầu, món thiếu tỷ lệ hoa hồng, hàng không còn hợp lệ hoặc dữ liệu thay đổi đồng thời |

Lỗi nghiệp vụ dùng `ProblemDetails` hiện có và mã `WITHDRAWAL.*`. Khi nhận 409, tải lại dữ liệu và kiểm tra kết quả trước khi thao tác lại. Các thao tác dùng transaction Serializable trên PostgreSQL cùng concurrency token của phiếu/sản phẩm để tránh duyệt, thu phí hoặc bàn giao lặp khi xử lý đồng thời.

## Triển khai và kiểm thử

Đã thêm migration `AddEarlyWithdrawalWorkflow`, cập nhật model snapshot và quyền. Migration đã được áp dụng vào database Neon đang cấu hình của API ngày 24/11/2026; đã xác nhận không còn migration chờ và truy vấn sản phẩm, phiếu rút hàng, hợp đồng thành công. Các môi trường khác cần chạy từ thư mục `OCS-BE` khi triển khai:

```powershell
dotnet ef database update --project src/OCS.Api
```

Quyền rút hàng sớm dùng ID ổn định theo số 1001–1004 trong `PermissionCatalog`, tránh trùng ID 36–39 mà database hiện tại đã dùng cho sự cố và quyết toán. Migration giữ nguyên các quyền có sẵn đó.

Bản ghi rút sớm tồn tại trước migration được đánh dấu `Legacy`, giữ số tiền/trạng thái thanh toán cũ và không cho đi vào các thao tác mới. `requestedAt` lấy từ `CreatedAt`; tỷ lệ phí item cũ là 0 do chưa từng có dữ liệu tỷ lệ, không được dùng làm căn cứ tính lại phí. Không tự tính lại số tiền các yêu cầu đã gửi.

Server đang chạy cần được build lại và khởi động lại sau khi áp dụng migration. Nếu thư mục build bị khóa bởi API đang chạy, có thể kiểm thử bằng cấu hình riêng:

```powershell
dotnet test tests/OCS.Api.IntegrationTests/OCS.Api.IntegrationTests.csproj --no-restore -c WithdrawalVerify -p:UseAppHost=false --filter EarlyWithdrawalTests
```

Kiểm thử bao gồm phí được chốt, làm tròn, quyền sở hữu, gửi trùng, hủy/gửi lại, duyệt, chặn POS, sai tiền, thu trùng, bàn giao trước khi thu, bàn giao trùng, miễn phí và giải phóng hàng khi từ chối. Các kiểm thử nghiệp vụ dùng EF InMemory; không thay thế kiểm thử đồng thời trên PostgreSQL thật.
