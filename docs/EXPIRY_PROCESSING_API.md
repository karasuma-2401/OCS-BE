# Xử lý hàng ký gửi hết hạn

Mốc hết hạn là `ConsignmentContract.EndsAt` (UTC). Hangfire chạy mỗi phút để chuyển sản phẩm ký gửi `OnSale` đã hết hạn sang `Expired` và tạo một phiếu cho các sản phẩm cùng hợp đồng. Job có thể chạy lại mà không tạo thêm phiếu hoặc lịch sử trạng thái. POS cũng kiểm tra trực tiếp `EndsAt` khi tra cứu, tạo hóa đơn và nhận thanh toán; thanh toán VietQR đến sau khi sản phẩm không còn bán được trả `409 POS.PAYMENT_PRODUCT_CONFLICT` để đối soát thủ công.

Quyên góp chỉ hoàn tất khi nhân viên xác nhận đã bàn giao thực tế. Một phiếu có thể được bàn giao từng món; mỗi dòng lưu người xử lý, thời điểm, tổ chức nhận và bằng chứng. Trạng thái của phiếu là `Pending`, `OverdueDonated` (đã duyệt hàng không nhận, chờ bàn giao) hoặc `Completed`.

## Cấu hình

- `ExpiryProcessing__CollectionDays`: số ngày khách được đến nhận (1–365), tính từ `EndsAt`. Không cấu hình hoặc giá trị không hợp lệ thì `CollectionDeadline = null` và không thể duyệt hàng không nhận. Nếu cấu hình sau khi phiếu đã tạo, job sẽ điền hạn cho các phiếu đang chờ.
- `ExpiryProcessing__AllowUnclaimedDonation`: mặc định `false`. Chỉ bật khi cửa hàng đã xác nhận điều khoản và chính sách pháp lý cho phép chuyển hàng không nhận sang quyên góp. API còn yêu cầu Manager/Admin nhập căn cứ cụ thể.

Không tự đặt thời hạn N ngày hoặc tự chuyển hàng không nhận sang quyên góp.

## Migration trước khi chạy API

Khởi động API bình thường **không tự áp dụng migration**. Chạy `--check-migrations` để xem các migration còn thiếu; sau khi xác nhận đúng database và có bản sao/điểm khôi phục theo runbook, dùng `--apply-migrations` để áp dụng. Các migration của luồng này là `20261123123556_AddExpiryProcessingWorkflow`, `20261123124015_AddExpiryProcessingItemEvidence` và `20261123131001_AddDonationReceipts`. Thiếu migration đầu tiên sẽ gây lỗi PostgreSQL `42703: column e.Evidence does not exist` khi lấy danh sách phiếu. Script xem trước của ba migration nằm ở [expiry-migrations-review.sql](expiry-migrations-review.sql). Với Neon dùng chung, tạo branch từ trạng thái hiện tại và phối hợp thời điểm cập nhật với nhóm trước khi chạy lệnh áp dụng.

## API

Các API dùng cookie đăng nhập và CSRF như phần còn lại của hệ thống; lỗi dùng `application/problem+json`.

| Endpoint | Permission | Nội dung |
| --- | --- | --- |
| `GET /api/expiry-processings` | `expiry.read` | `page`, `pageSize`, `q`, `customerId`, `customerQuery`, `status`, `type`, `deadlineFrom`, `deadlineTo`; trả `PagedResponse<ExpiryDto>`. Các bộ lọc chạy trước phân trang. `customerQuery` tìm tên/số điện thoại snapshot trên hợp đồng, gồm khách vãng lai. |
| `GET /api/expiry-processings/{id}` | `expiry.read` | Chi tiết phiếu và từng sản phẩm, gồm bằng chứng bàn giao từng dòng. |
| `POST /api/expiry-processings/{id}/confirm-return` | `expiry.return` | `{ "barcodes": ["BC-1"] }`; chỉ món `Expired` thuộc phiếu `Return` được chuyển `Returned`. |
| `POST /api/expiry-processings/{id}/confirm-donation` | `expiry.donate` | `{ "barcodes": ["BC-1"], "recipientOrganization": "...", "evidence": "...", "note": "..." }`; chỉ món `Expired` thuộc phiếu `Donate` được chuyển `Donated`. |
| `POST /api/expiry-processings/{id}/mark-unclaimed` | `expiry.mark-unclaimed`, Manager/Admin | `{ "basis": "..." }`; chỉ phiếu `Return` còn chờ, quá `CollectionDeadline` và đã bật chính sách mới chuyển sang `OverdueDonated`. |
| `GET /api/expiry-processings/{id}/donation-receipts` | `expiry.read` | Các biên nhận đã xác nhận của phiếu. |
| `GET /api/expiry-processings/{id}/donation-receipts/{receiptId}` | `expiry.read` | Một biên nhận lưu bền vững, gồm snapshot các món bàn giao của đúng lần xác nhận. |

`400` là dữ liệu đầu vào sai; `404` là phiếu không tồn tại; `409` là phiếu/sản phẩm không còn ở trạng thái cho phép hoặc chưa đủ điều kiện duyệt. Không gọi API này để tự chuyển `Donated` tại thời điểm hợp đồng hết hạn.

Mỗi lần `confirm-donation` thành công tạo một `DonationReceipt` trong cùng transaction, có số `QG-<UUID>` duy nhất, tổ chức nhận, bằng chứng dạng chuỗi, người/thời gian xử lý và snapshot sản phẩm. Gửi lại barcode đã xử lý trả `409`, không tạo biên nhận khác. Frontend dùng JSON để dựng bản in; hiện chưa có PDF hay upload file. Các lần bàn giao trước migration biên nhận không được backfill.

API khách `/api/customers/me/consignments`, `/search` và chi tiết sản phẩm trả thêm `expiryProcessingType`, `expiryProcessingStatus`, `collectionDeadline`; mỗi sản phẩm trong danh sách và chi tiết có `handledAt`. Giá trị chưa phát sinh là `null`. Controller khách chỉ trả hợp đồng thuộc tài khoản đăng nhập, không trả căn cứ duyệt, bằng chứng hoặc thông tin nhân viên.
