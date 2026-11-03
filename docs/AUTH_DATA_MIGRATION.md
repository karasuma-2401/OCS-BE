# CP1: chuyển dữ liệu email đăng nhập

CP1 chỉ bổ sung persistence. Login/register HTTP vẫn dùng contract cũ cho đến CP3–CP5; chưa có gửi OTP, xác thực mã hay cookie session runtime. Không triển khai code đang chuyển tiếp này riêng lên Production như thể luồng auth mới đã hoàn tất.

## Schema và thứ tự triển khai

1. `20261103101428_AddAuthenticationIdentityState`: thêm các cột nullable/default cho tài khoản và bảng `OTP_CHALLENGE`, `AUTH_SESSION`; giữ các index cũ.
2. `20261103101757_NormalizeAccountEmail`: khóa ghi bảng tài khoản trong transaction, tính email chuẩn hóa, dừng nếu có xung đột, backfill, thay unique email theo AccountType bằng unique `NormalizedEmail` toàn hệ thống và thêm check constraint nhất quán.

Không tự chạy migration vào database vận hành. Sao lưu, chạy preflight và rehearsal trên bản sao được quản lý trước; dùng maintenance window vì migration thứ hai giữ lock đến commit. Khi triển khai thật, áp đủ hai migration trước khi chạy code CP1; không cho phiên bản cũ tiếp tục ghi email thiếu NormalizedEmail sau khi check constraint được bật.

## Chính sách email v1

- Email ASCII, local-part dạng dot-atom tối đa 64 ký tự, domain có ít nhất hai nhãn; mỗi nhãn tối đa 63 ký tự. Địa chỉ sau trim dài tối đa 254 ký tự. Không nhận display name/quoted local-part/địa chỉ Unicode trực tiếp; domain quốc tế dùng IDNA ASCII.
- Chỉ trim các ký tự ASCII space, tab, CR/LF, vertical tab và form feed ở hai đầu. Giữ nguyên Email gốc để hiển thị, uppercase invariant cho lookup; giữ dấu chấm và `+tag`.
- `EmailIdentity` dùng cùng regex với SQL migration, với collation `C` để tránh phụ thuộc locale database. Test đối chiếu kết quả C# và PostgreSQL, bao gồm biên ký tự trắng và Unicode không hỗ trợ.
- Email thiếu/sai/ngoài chính sách vẫn giữ nguyên, NormalizedEmail null và không đủ điều kiện lookup email. Nhiều giá trị null được cho phép. Không gộp các hồ sơ chưa xác minh dựa trên SĐT.
- Ghi qua `AppDbContext.SaveChanges/SaveChangesAsync` tự cập nhật NormalizedEmail. Check constraint chặn SQL trực tiếp ghi email và giá trị chuẩn hóa không khớp. `ExecuteUpdate`, bulk loader và SQL ngoài EF không chạy SaveChanges: phải tự tuân theo cùng chính sách, không bỏ constraint để ghi dữ liệu.
- Nếu thay đổi sang email khác sau chuẩn hóa, SaveChanges xóa EmailVerifiedAt. Thay đổi casing/trim giữ cùng identity thì không xóa proof. PhoneVerifiedAt không bị thay đổi.

## Preflight và xử lý dữ liệu cũ

Chạy `docs/sql/auth-email-preflight.sql` bằng tài khoản được phép đọc database cần đánh giá. Script read-only và chỉ trả số lượng tổng hợp; không in email, password hash, OTP hoặc thông tin hồ sơ ra log/terminal dùng chung.

| Nhóm | Hành động |
| --- | --- |
| Email hợp lệ, không trùng | Backfill NormalizedEmail. Giữ nguyên Id, Status, password hash, ActivatedAt, PhoneVerifiedAt, profile, role và lịch sử. Không tự đặt EmailVerifiedAt. |
| Email thiếu/sai | Giữ email gốc, NormalizedEmail null. Người vận hành liên hệ và xác minh danh tính qua quy trình được quản lý trước khi cập nhật. Không tự sinh email/username/password. |
| Email trùng sau chuẩn hóa giữa bất kỳ loại tài khoản nào | Migration dừng và rollback bước đang chạy; chỉ báo số nhóm xung đột. Review từng tài khoản trên công cụ quản lý dữ liệu được cấp quyền; xác minh chủ sở hữu, lập mapping AccountId -> email riêng được xác nhận; không xóa/gộp tài khoản hoặc chuyển lịch sử. Chạy preflight rồi migration lại. |
| Tài khoản Active cũ chưa EmailVerifiedAt | Không đổi Status/lịch sử trong migration. CP3–CP5 phải kiểm tra proof email trước khi cấp phiên; login đúng password mở đường email OTP, không coi Active cũ là đã xác thực email. |
| Tài khoản tại quầy thiếu password/email | Không nhận quyền từ SĐT người dùng tự khai. Hoàn thiện email/password bằng quy trình cấp quyền được quản lý; chưa cung cấp public recovery trong CP1. |
| Locked/Deactivated | Giữ nguyên khóa quản trị. Không tự mở khóa khi backfill/OTP hoặc khi khóa tạm hết thời gian. |

Không lưu mapping email/tài khoản vào repo. Preflight không sửa dữ liệu. Nếu migration thứ hai bị chặn, kiểm tra `__EFMigrationsHistory` để biết migration đầu đã commit hay cùng rollback; xử lý xung đột rồi chạy lại, không sửa lịch sử migration thủ công.

## OTP/session và concurrency

- OTP chỉ lưu CodeHash và ContinuationTokenHash, email đã chuẩn hóa, purpose, số lần sai, TTL, thời điểm resend, consumed/invalidated và trạng thái gửi Pending/Sent/Failed. Không có cột mã OTP hay continuation token plaintext.
- Session lưu Id làm claim `sid`, AccountId, thời hạn tuyệt đối và RevokedAt; không lưu JWT/refresh token plaintext. Cookie/JWT, gửi SMTP và thuật toán HMAC triển khai ở CP2–CP5.
- Account, OtpChallenge, AuthSession dùng PostgreSQL `xmin` làm concurrency token (`uint` + `IsRowVersion`). DDL Npgsql không tạo/xóa cột hệ thống xmin dù scaffold có khai báo column operation. Xem [Npgsql concurrency mapping](https://www.npgsql.org/efcore/release-notes/7.0.html).
- CP2–CP5 phải xử lý `DbUpdateConcurrencyException` bằng retry có giới hạn/đọc lại trạng thái, và verify + consume + activate cùng transaction. Mapping concurrency ở CP1 không tự hoàn tất toàn bộ nghiệp vụ chống replay.
- Automatic audit không thu thập password hash, normalized email, OTP/session hashes hoặc token. Chỉ audit metadata được allowlist; sự kiện bảo mật chuyên biệt bổ sung sau, không ghi bí mật vào Changes.

## Rollback

Ưu tiên forward fix và khôi phục từ bản sao đã kiểm tra. Down của migration thứ hai phục hồi unique `(AccountType, Email)` cũ và giữ các cột mới; dữ liệu email không hợp lệ trùng raw được tạo sau rollout có thể làm index cũ không tạo được, cần preflight trước rollback. Down migration đầu xóa bảng OTP/session và trạng thái xác thực mới, do đó mất dữ liệu auth mới: không dùng như thao tác rollback không mất dữ liệu trên Production. Id/hồ sơ/lịch sử nghiệp vụ vẫn được giữ.

## Kiểm thử

`dotnet build OCS.sln` và `dotnet test OCS.sln`. Bộ test PostgreSQL yêu cầu biến `OCS_TEST_POSTGRES` trỏ tới server test riêng có quyền CREATE DATABASE; nếu thiếu thì các test đó được đánh dấu skip, không phải đã được xác minh trên PostgreSQL.

Mỗi test tạo database tên `ocs_auth_test_<guid>`, migrate và xóa đúng database do nó tạo; không đọc `.env` hay connection string dự án. Không trỏ biến này vào server vận hành. Bộ test kiểm tra backfill trên dữ liệu cũ, xung đột toàn hệ thống, SQL/C# parity, unique race, invalid legacy lookup, clear email proof, constraint và concurrency/audit secrets.
