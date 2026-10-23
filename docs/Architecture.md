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
   Với dữ liệu sản phẩm, dùng `Features/Catalog/IProductRepository`; repository và unit of work dùng chung `AppDbContext` trong một DI scope.
5. Infrastructure cung cấp implementation: `AppDbContext`, audit/timestamp interceptor, cache, object storage hoặc background job khi nghiệp vụ cần.
6. API trả JSON hoặc `ProblemDetails` theo [API contract](API_CONTRACT.md).

Lỗi validation và lỗi chưa xử lý dùng `ProblemDetails` với `code` và `traceId`. Mọi endpoint nghiệp vụ mới phải tuân theo [API contract](API_CONTRACT.md), không tạo một quy ước response riêng.

Application trả `Result<T>` với `ApplicationError(Code, Message, Type)` khi có lỗi nghiệp vụ dự kiến. API dùng `ToActionResult(this)` để ánh xạ loại lỗi sang HTTP; Application không tham chiếu ASP.NET Core. Thành công tạo tài nguyên mới vẫn cần controller trả `201 Created` với `Location` theo API contract.

## Tổ chức tính năng

Application nhóm code trong `Features/<TênPhânHệ>/`: interface repository, service, DTO và validation liên quan đặt gần nhau. Hiện `Features/Catalog/IProductRepository` là mẫu truy cập dữ liệu đầu tiên; các phân hệ Identity, Appointments, Consignments, Sales và Payouts được bổ sung khi triển khai use case tương ứng.

Implementation EF Core nằm trong `Infrastructure/Persistence/Repositories`. Repository chỉ truy vấn và theo dõi thay đổi; application service gọi `IUnitOfWork.SaveChangesAsync` ở cuối thao tác. Không gọi `SaveChanges` riêng trong từng repository vì có thể tách một nghiệp vụ thành nhiều lần ghi không đồng bộ.

`Product` chốt giá và nguồn hàng qua `AssignConsignmentTerms` hoặc `AssignBuyoutTerms`. Sau khi chốt, giá và tỷ lệ hoa hồng không có setter công khai. Luồng bán dùng `OfferForSale` rồi `MarkSold`; service xử lý bán hàng sau này phải ghi hóa đơn và lịch sử trạng thái trong cùng lần lưu. Các luồng hết hạn, rút sớm và bồi thường cần có thao tác Domain riêng khi được triển khai.

## Hạ tầng runtime

| Thành phần | Mục đích | Ứng xử khi chưa cấu hình |
| --- | --- | --- |
| Neon PostgreSQL + EF Core/Npgsql | Lưu dữ liệu nghiệp vụ và migration. | API không có database nghiệp vụ để hoạt động đầy đủ. |
| Redis | Distributed cache. | Fallback sang distributed memory cache. |
| Hangfire + PostgreSQL | Chạy background job. | Không đăng ký Hangfire server nếu thiếu connection string. |
| Cloudflare R2 qua S3 API | Lưu object/file. | Không đăng ký `IAmazonS3` nếu thiếu thông tin R2. |
| Health checks | Kiểm tra Neon, Redis và R2 ở `/api/health`; liveness ở `/api/health/live`. | Readiness phản ánh dependency chưa sẵn sàng. |

Các cấu hình là environment variable của ASP.NET Core. Chỉ commit giá trị mẫu trống; không đưa connection string, JWT key hay R2 secret vào mã nguồn hoặc tài liệu.

Trong Production, ứng dụng kiểm tra có connection string, JWT issuer, audience và key trước khi đăng ký dịch vụ. JWT key phải có ít nhất 32 byte UTF-8. Kiểm tra kết nối thực tế vẫn thuộc readiness check.

## Persistence và audit

`AppDbContext` chứa 30 `DbSet` và nạp toàn bộ entity configuration từ `OCS.Infrastructure`. Schema PostgreSQL được quản lý bằng EF Core migration; [DatabaseScript.txt](DatabaseScript.txt) là script idempotent để triển khai database. Chi tiết bảng và các tên khác với báo cáo được ghi trong [DataModel.md](DataModel.md).

`TimestampSaveChangesInterceptor` xử lý trường thời gian của entity kế thừa `BaseEntity`. `AuditSaveChangesInterceptor` dùng `IAuditContext`; API thay context hệ thống bằng `HttpAuditContext` để gắn thông tin request vào audit log.

### Reference data và dữ liệu demo

`ReferenceDataSeed` chỉ đưa role, permission và quan hệ role/permission vào migration. Dữ liệu demo được tạo qua `DevelopmentDataSeeder`, chỉ cho phép môi trường `Development` hoặc `Testing`, chạy bằng lệnh riêng sau khi migrate database:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/OCS.Api -- --seed-demo
```

Lệnh tạo tài khoản mẫu ở trạng thái `PendingActivation`, không cấp mật khẩu mặc định, không tự migrate database và không ghi đè tài khoản đã tồn tại. Không chạy lệnh này như bước startup hoặc deploy thông thường.

Migration `20261023162400_SeparateDevelopmentSeed` giữ nguyên lịch sử migration cũ. Migration chỉ xóa bộ tài khoản demo cũ khi cả bộ vẫn đúng dữ liệu mẫu và không được bảng nghiệp vụ/audit tham chiếu; bộ đã sửa hoặc đã sử dụng được giữ nguyên để nhóm rà soát riêng. Bản ghi audit tổng hợp `DemoDataSeeded` được nhận diện bằng ID và nội dung mẫu để dọn riêng. Rollback không tự tái tạo dữ liệu demo đã xóa.

## Kiểm thử

- `OCS.Domain.Tests`: quy tắc chốt giá, hoa hồng và trạng thái sản phẩm.
- `OCS.Application.Tests`: kết quả thành công và lỗi nghiệp vụ.
- `OCS.Api.IntegrationTests`: test host trong bộ nhớ, kiểm tra HTTP/Problem Details, JWT challenge, validation và cấu hình Production.
- `OCS.Infrastructure.IntegrationTests`: kiểm tra model/migration; các test PostgreSQL dùng Testcontainers với database tạm, không dùng connection string của dự án.

Chạy `dotnet test OCS.sln` để kiểm tra thông thường. Muốn chạy cả PostgreSQL, khởi động Docker và đặt `$env:OCS_RUN_POSTGRES_TESTS = "1"` trước khi chạy. Khi chưa bật biến này, test database được báo skipped; CI bật biến này để chạy đầy đủ.

Nếu dùng một PostgreSQL test server riêng ở localhost, có thể đặt thêm `OCS_TEST_POSTGRES` làm connection string tới server đó. Helper chỉ chấp nhận địa chỉ loopback, tự tạo database ngẫu nhiên có tiền tố `ocs_test_` cho từng test và xóa đúng database đó sau khi chạy; không dùng database nghiệp vụ. Khi không có biến này, test tự chạy container PostgreSQL 17.

## Quy tắc khi thêm tính năng

1. Đặt entity, enum và business rule không phụ thuộc framework trong `OCS.Domain`.
2. Đặt command/query service, DTO và abstraction ở `OCS.Application`.
3. Chỉ đặt EF Core, external SDK và implementation interface trong `OCS.Infrastructure`; thêm migration nếu schema đổi.
4. Thêm controller, authorization, request/response model và OpenAPI ở `OCS.Api`.
5. Cập nhật [API contract](API_CONTRACT.md), [data model](DataModel.md), migration script và test tương ứng trong cùng thay đổi.

Không để controller thao tác trực tiếp với SDK hạ tầng hoặc để Domain tham chiếu `DbContext`, `HttpContext` hay package cloud.
