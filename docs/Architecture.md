# Kiến trúc backend

Tài liệu này mô tả kiến trúc đang có trong mã nguồn `backend`, không phải kiến trúc dự kiến. OCS tổ chức theo Clean Architecture nhẹ để tách quy tắc nghiệp vụ khỏi HTTP và hạ tầng, đồng thời vẫn giữ cấu trúc đủ đơn giản cho một đồ án nhóm.

## Phạm vi

- Nền tảng: .NET 10, ASP.NET Core Web API và Entity Framework Core.
- Kiểu kiến trúc: monolith phân lớp; chưa dùng microservice, event bus, CQRS hay MediatR.
- Composition root: `src/OCS.Api/Program.cs` và `Extensions/ServiceCollectionExtensions.cs`.

## Các layer

| Project | Vai trò | Được phép phụ thuộc |
| --- | --- | --- |
| `OCS.Domain` | Entity, enum, `BaseEntity` và quy tắc nghiệp vụ thuần. | Không phụ thuộc project hay framework khác. |
| `OCS.Application` | Abstraction, `Result` và application service/use case khi từng nghiệp vụ được bổ sung. | `OCS.Domain`. |
| `OCS.Infrastructure` | EF Core, migration, PostgreSQL, audit interceptor, Redis, Hangfire, Cloudflare R2 và health check. | `OCS.Application`, `OCS.Domain`. |
| `OCS.Api` | Điểm vào HTTP: controller, xác thực JWT, CORS, Problem Details, OpenAPI/Scalar và health endpoint. | `OCS.Application`, `OCS.Infrastructure`. |

Chiều phụ thuộc tại compile time là `Api → Application`, `Api → Infrastructure`, `Infrastructure → Application → Domain`. `OCS.Api` ghép implementation hạ tầng vào abstraction ở `AddOcsApi`; Domain không biết HTTP, EF Core hay dịch vụ ngoài.

## Luồng một request

1. Request đi vào ASP.NET Core pipeline tại `OCS.Api`.
2. API áp dụng HTTPS redirection, CORS, JWT authentication và authorization.
3. Controller của một nghiệp vụ gọi application service tương ứng. Hiện repository chưa có controller nghiệp vụ; API chỉ công bố health endpoint.
4. Application service áp dụng use case và gọi abstraction như `IUnitOfWork` hoặc `IAuditContext`.
5. Infrastructure cung cấp implementation: `AppDbContext`, audit/timestamp interceptor, cache, object storage hoặc background job khi nghiệp vụ cần.
6. API trả JSON hoặc `ProblemDetails` theo [API contract](API_CONTRACT.md).

Lỗi validation và lỗi chưa xử lý dùng `ProblemDetails` với `code` và `traceId`. Mọi endpoint nghiệp vụ mới phải tuân theo [API contract](API_CONTRACT.md), không tạo một quy ước response riêng.

## Hạ tầng runtime

| Thành phần | Mục đích | Ứng xử khi chưa cấu hình |
| --- | --- | --- |
| Neon PostgreSQL + EF Core/Npgsql | Lưu dữ liệu nghiệp vụ và migration. | API không có database nghiệp vụ để hoạt động đầy đủ. |
| Redis | Distributed cache. | Fallback sang distributed memory cache. |
| Hangfire + PostgreSQL | Chạy background job. | Không đăng ký Hangfire server nếu thiếu connection string. |
| Cloudflare R2 qua S3 API | Lưu object/file. | Không đăng ký `IAmazonS3` nếu thiếu thông tin R2. |
| Health checks | Kiểm tra Neon, Redis và R2 ở `/api/health`; liveness ở `/api/health/live`. | Readiness phản ánh dependency chưa sẵn sàng. |

Các cấu hình là environment variable của ASP.NET Core. Chỉ commit giá trị mẫu trống; không đưa connection string, JWT key hay R2 secret vào mã nguồn hoặc tài liệu.

## Persistence và audit

`AppDbContext` chứa 30 `DbSet` và nạp toàn bộ entity configuration từ `OCS.Infrastructure`. Schema PostgreSQL được quản lý bằng EF Core migration; [DatabaseScript.txt](DatabaseScript.txt) là script idempotent để triển khai database. Chi tiết bảng và các tên khác với báo cáo được ghi trong [DataModel.md](DataModel.md).

`TimestampSaveChangesInterceptor` xử lý trường thời gian của entity kế thừa `BaseEntity`. `AuditSaveChangesInterceptor` dùng `IAuditContext`; API thay context hệ thống bằng `HttpAuditContext` để gắn thông tin request vào audit log.

## Quy tắc khi thêm tính năng

1. Đặt entity, enum và business rule không phụ thuộc framework trong `OCS.Domain`.
2. Đặt command/query service, DTO và abstraction ở `OCS.Application`.
3. Chỉ đặt EF Core, external SDK và implementation interface trong `OCS.Infrastructure`; thêm migration nếu schema đổi.
4. Thêm controller, authorization, request/response model và OpenAPI ở `OCS.Api`.
5. Cập nhật [API contract](API_CONTRACT.md), [data model](DataModel.md), migration script và test tương ứng trong cùng thay đổi.

Không để controller thao tác trực tiếp với SDK hạ tầng hoặc để Domain tham chiếu `DbContext`, `HttpContext` hay package cloud.
