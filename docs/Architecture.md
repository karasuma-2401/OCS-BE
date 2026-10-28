# Kiến trúc backend

Tài liệu này mô tả kiến trúc của mã nguồn `backend`. OCS được tổ chức theo kiến trúc monolithic N-tier tinh gọn (tương tự `eBoard-backend`), gom toàn bộ thành phần về project `src/OCS.Api` với sự phân chia thư mục rõ ràng và sử dụng .NET Dependency Injection tiêu chuẩn.

## Phạm vi

- Nền tảng: .NET 10, ASP.NET Core Web API và Entity Framework Core.
- Kiểu kiến trúc: monolithic N-tier tinh gọn, vận hành 100% online cho nền tảng Web.
- Composition root: `src/OCS.Api/Program.cs` và `Extensions/ServiceCollectionExtensions.cs`.

## Cấu trúc thư mục

Toàn bộ backend nằm gọn trong `src/OCS.Api`:

| Thư mục | Vai trò |
| --- | --- |
| `Controllers/` | HTTP REST endpoints, attribute routing, authorization. |
| `Services/` | Hiện thực nghiệp vụ (business logic): `AuthService`, `ProductService`, `TokenService`, `PasswordHasher`. |
| `Repositories/` | Hiện thực `UnitOfWork` và các repository (`ProductRepository`, `AuthRepository`), truy vấn qua EF Core. |
| `Interfaces/` | Khai báo abstraction: `Interfaces/Services/` và `Interfaces/Repositories/`. |
| `Entities/` | Domain entities, quan hệ dữ liệu và `BaseEntity`. |
| `Context/` | EF Core `AppDbContext`, Fluent API configurations (`Config/`), migrations, interceptors và seeds. |
| `Models/` | Data Transfer Objects (DTOs) cho request/response giữa Client và API. |
| `Common/` | Các cấu trúc chuẩn dùng chung: `Result<T>`, `ApplicationError`, `PagedRequest`, `PagedResponse`, `ApiErrorCodes`. |
| `Consts/` | Các enum định danh trạng thái, vai trò và phân loại nghiệp vụ. |
| `Extensions/` | Đăng ký dịch vụ DI (`ServiceCollectionExtensions`), ánh xạ kết quả (`ResultExtensions`), cấu hình API (`ApiConventionExtensions`). |
| `Storage/` | Cấu hình và service làm việc với Cloudflare R2 qua S3 API. |

## Luồng một request

1. Request đi vào ASP.NET Core pipeline tại `OCS.Api`.
2. Pipeline xử lý Exception Handling (`UseExceptionHandler`), `UseStatusCodePages`, HTTPS redirection, CORS, JWT Authentication và Authorization.
3. Controller tiếp nhận HTTP request, validate model tự động qua ASP.NET Core conventions.
4. Controller gọi Service tương ứng (ví dụ: `IAuthService`, `IProductService`).
5. Service thực hiện logic nghiệp vụ, gọi Repository qua `IUnitOfWork`.
6. Repository tương tác với cơ sở dữ liệu thông qua `AppDbContext`.
7. Service trả về `Result<T>` đóng gói dữ liệu thành công hoặc lỗi nghiệp vụ (`ApplicationError`).
8. Controller chuyển đổi `Result<T>` thành HTTP response (200 OK, 201 Created, hoặc Problem Details tương ứng với 400/401/403/404/409).

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

`AppDbContext` chứa 30 `DbSet` và nạp toàn bộ entity configuration từ thư mục `Context/Config`. Schema PostgreSQL được quản lý bằng EF Core migration; [DatabaseScript.txt](DatabaseScript.txt) là script idempotent để triển khai database.

- `TimestampSaveChangesInterceptor` tự động cập nhật `CreatedAt` và `UpdatedAt` cho entity kế thừa `BaseEntity`.
- `AuditSaveChangesInterceptor` ghi nhận lịch sử thay đổi thông qua `IAuditContext` (`HttpAuditContext` hoặc `SystemAuditContext`).

## Kiểm thử

- `OCS.Api.IntegrationTests`: sử dụng `WebApplicationFactory<Program>` để kiểm thử toàn diện HTTP pipeline, Problem Details contract, JWT authentication challenge, validation logic và API liveness endpoint.
