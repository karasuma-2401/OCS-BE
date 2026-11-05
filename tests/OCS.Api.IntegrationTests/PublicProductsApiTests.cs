using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Contracts.Products;
using OCS.Api.Context;
using OCS.Api.Context.Seeds;
using OCS.Api.Auditing;
using OCS.Api.Services;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Products;
using OCS.Api.Repositories;

namespace OCS.Api.IntegrationTests;

public sealed class PublicProductsApiTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "",
                    ["Redis:ConnectionString"] = "",
                    ["Jwt:Issuer"] = "test",
                    ["Jwt:Audience"] = "test",
                    ["Jwt:Key"] = "test-only-key-with-at-least-32-bytes",
                    ["R2:ServiceUrl"] = ""
                }));
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProductService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<IProductService, FakeTestProductService>();
            });
        });

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task AnonymousCaller_CanAccessPublicProductsEndpoint_WithoutTokensOrCookies()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/products");
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 OK but got {response.StatusCode}: {content}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<PagedResponse<PublicProductResponse>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.Page);
        Assert.Equal(20, payload.PageSize);
    }

    [Theory]
    [InlineData("/api/products/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/products/barcode/BC-TEST")]
    public async Task InternalProductReadEndpoints_RejectAnonymousAccess_With401Unauthorized(string route)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHORIZED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateProduct_RejectsAnonymousAccess_With401Unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsJsonAsync("/api/products", new
        {
            Name = "Sample",
            Barcode = "BC-001",
            Size = "M",
            ListedPrice = 100000m,
            CategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHORIZED", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("/api/products?page=0")]
    [InlineData("/api/products?pageSize=0")]
    [InlineData("/api/products?pageSize=101")]
    [InlineData("/api/products?page=2147483647&pageSize=100")]
    [InlineData("/api/products?minPrice=-10")]
    [InlineData("/api/products?maxPrice=-5")]
    [InlineData("/api/products?minPrice=500000&maxPrice=100000")]
    public async Task Validation_RejectsInvalidQueryParameters_With400ValidationFailed(string route)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Validation_RejectsOverlyLongQueryStrings()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var longQ = new string('x', 101);
        var longSize = new string('s', 31);

        using var responseQ = await client.GetAsync($"/api/products?q={longQ}");
        Assert.Equal(HttpStatusCode.BadRequest, responseQ.StatusCode);

        using var responseSize = await client.GetAsync($"/api/products?size={longSize}");
        Assert.Equal(HttpStatusCode.BadRequest, responseSize.StatusCode);
    }

    [Fact]
    public async Task RepositoryQuery_ReturnsOnlyProductsWithOnSaleStatus()
    {
        await using var db = CreateInMemoryDbContext();
        var repo = new ProductRepository(db);

        var activeCat = new Category { Id = Guid.NewGuid(), Name = "Active Cat", IsActive = true };
        var inactiveCat = new Category { Id = Guid.NewGuid(), Name = "Inactive Cat", IsActive = false };
        var activeBrand = new Brand { Id = Guid.NewGuid(), Name = "Active Brand", Tier = BrandTier.Commercial, DefaultCommissionRate = 15m, IsActive = true };
        var inactiveBrand = new Brand { Id = Guid.NewGuid(), Name = "Inactive Brand", Tier = BrandTier.Commercial, DefaultCommissionRate = 15m, IsActive = false };
        db.Categories.AddRange(activeCat, inactiveCat);
        db.Brands.AddRange(activeBrand, inactiveBrand);

        var validOnSale = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-VALID",
            Name = "Valid Product",
            Size = "M",
            ListedPrice = 200000m,
            Status = ProductStatus.OnSale,
            CategoryId = activeCat.Id,
            BrandId = activeBrand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var pendingProd = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-PENDING",
            Name = "Pending Product",
            Size = "M",
            ListedPrice = 200000m,
            Status = ProductStatus.PendingProcessing,
            CategoryId = activeCat.Id,
            BrandId = activeBrand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var soldProd = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-SOLD",
            Name = "Sold Product",
            Size = "M",
            ListedPrice = 200000m,
            Status = ProductStatus.Sold,
            CategoryId = activeCat.Id,
            BrandId = activeBrand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var prodWithInactiveBrand = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-INACT-BRAND",
            Name = "Inactive Brand Prod",
            Size = "M",
            ListedPrice = 200000m,
            Status = ProductStatus.OnSale,
            CategoryId = activeCat.Id,
            BrandId = inactiveBrand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var prodWithInactiveCat = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-INACT-CAT",
            Name = "Inactive Cat Prod",
            Size = "M",
            ListedPrice = 200000m,
            Status = ProductStatus.OnSale,
            CategoryId = inactiveCat.Id,
            BrandId = activeBrand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Products.AddRange(validOnSale, pendingProd, soldProd, prodWithInactiveBrand, prodWithInactiveCat);
        await db.SaveChangesAsync();

        var result = await repo.GetPublicProductsAsync(new GetPublicProductsRequest());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, item => item.Id == validOnSale.Id);
        Assert.Contains(result.Items, item => item.Id == prodWithInactiveBrand.Id);
        Assert.Contains(result.Items, item => item.Id == prodWithInactiveCat.Id);
        Assert.DoesNotContain(result.Items, item => item.Id == pendingProd.Id);
        Assert.DoesNotContain(result.Items, item => item.Id == soldProd.Id);
    }

    [Fact]
    public async Task RepositoryQuery_CoverImageSelection_DeterministicAndHandlesMissing()
    {
        await using var db = CreateInMemoryDbContext();
        var repo = new ProductRepository(db);

        var cat = new Category { Id = Guid.NewGuid(), Name = "Cat", IsActive = true };
        var brand = new Brand { Id = Guid.NewGuid(), Name = "Brand", Tier = BrandTier.LocalBrand, DefaultCommissionRate = 10m, IsActive = true };
        db.Categories.Add(cat);
        db.Brands.Add(brand);

        var prodWithCover = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-COVER",
            Name = "Prod With Cover",
            Size = "L",
            ListedPrice = 150000m,
            Status = ProductStatus.OnSale,
            CategoryId = cat.Id,
            BrandId = brand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var prodWithoutCover = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-NO-COVER",
            Name = "Prod Without Cover",
            Size = "S",
            ListedPrice = 250000m,
            Status = ProductStatus.OnSale,
            CategoryId = cat.Id,
            BrandId = brand.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        db.Products.AddRange(prodWithCover, prodWithoutCover);

        // Add Detail and Cover images for prodWithCover
        db.ProductImages.AddRange(
            new ProductImage
            {
                Id = Guid.NewGuid(),
                ProductId = prodWithCover.Id,
                Url = "https://cdn.example.com/detail.jpg",
                ImageType = ProductImageType.Detail,
                SortOrder = 0
            },
            new ProductImage
            {
                Id = Guid.NewGuid(),
                ProductId = prodWithCover.Id,
                Url = "https://cdn.example.com/cover.jpg",
                ImageType = ProductImageType.Cover,
                SortOrder = 1
            });

        // Add only Detail image for prodWithoutCover
        db.ProductImages.Add(new ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = prodWithoutCover.Id,
            Url = "https://cdn.example.com/detail-only.jpg",
            ImageType = ProductImageType.Detail,
            SortOrder = 0
        });

        await db.SaveChangesAsync();

        var result = await repo.GetPublicProductsAsync(new GetPublicProductsRequest());

        Assert.Equal(2, result.TotalCount);
        var first = result.Items.Single(x => x.Id == prodWithCover.Id);
        var second = result.Items.Single(x => x.Id == prodWithoutCover.Id);

        Assert.Equal("https://cdn.example.com/cover.jpg", first.CoverImageUrl);
        Assert.Null(second.CoverImageUrl);
    }

    [Fact]
    public async Task RepositoryQuery_PriceFormatting_FollowsTwoDecimalStringConvention()
    {
        await using var db = CreateInMemoryDbContext();
        var repo = new ProductRepository(db);

        var cat = new Category { Id = Guid.NewGuid(), Name = "Cat", IsActive = true };
        var brand = new Brand { Id = Guid.NewGuid(), Name = "Brand", Tier = BrandTier.Luxury, DefaultCommissionRate = 20m, IsActive = true };
        db.Categories.Add(cat);
        db.Brands.Add(brand);

        var prod = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-PRICE",
            Name = "Price Check Product",
            Size = "M",
            ListedPrice = 1250000.50m,
            Status = ProductStatus.OnSale,
            CategoryId = cat.Id,
            BrandId = brand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Products.Add(prod);
        await db.SaveChangesAsync();

        var result = await repo.GetPublicProductsAsync(new GetPublicProductsRequest());

        Assert.Single(result.Items);
        Assert.Equal("1250000.50", result.Items[0].ListedPrice);
    }

    [Fact]
    public async Task RepositoryQuery_SearchQ_IsCaseInsensitive_AndIgnoresWhitespace()
    {
        await using var db = CreateInMemoryDbContext();
        var repo = new ProductRepository(db);

        var catCoat = new Category { Id = Guid.NewGuid(), Name = "Áo khoác dạ", IsActive = true };
        var catPants = new Category { Id = Guid.NewGuid(), Name = "Quần Tây", IsActive = true };
        var brandZara = new Brand { Id = Guid.NewGuid(), Name = "Zara Men", Tier = BrandTier.Commercial, DefaultCommissionRate = 15m, IsActive = true };
        var brandUniqlo = new Brand { Id = Guid.NewGuid(), Name = "Uniqlo Japan", Tier = BrandTier.Commercial, DefaultCommissionRate = 15m, IsActive = true };
        db.Categories.AddRange(catCoat, catPants);
        db.Brands.AddRange(brandZara, brandUniqlo);

        var p1 = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-P1",
            Name = "Blazer Thanh Lịch",
            Size = "L",
            ListedPrice = 800000m,
            Status = ProductStatus.OnSale,
            CategoryId = catCoat.Id,
            BrandId = brandZara.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var p2 = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-P2",
            Name = "Quần Smart Ankle",
            Size = "M",
            ListedPrice = 450000m,
            Status = ProductStatus.OnSale,
            CategoryId = catPants.Id,
            BrandId = brandUniqlo.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        db.Products.AddRange(p1, p2);
        await db.SaveChangesAsync();

        // 1. Search by product name "blazer" (mixed case)
        var resName = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { Q = "  bLaZeR  " });
        Assert.Equal(1, resName.TotalCount);
        Assert.Equal(p1.Id, resName.Items[0].Id);

        // 2. Search by brand name "zara"
        var resBrand = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { Q = "ZARA" });
        Assert.Equal(1, resBrand.TotalCount);
        Assert.Equal(p1.Id, resBrand.Items[0].Id);

        // 3. Search by category name "quần"
        var resCat = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { Q = "quần" });
        Assert.Equal(1, resCat.TotalCount);
        Assert.Equal(p2.Id, resCat.Items[0].Id);

        // 4. Empty/whitespace Q returns all items
        var resEmpty = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { Q = "   " });
        Assert.Equal(2, resEmpty.TotalCount);
    }

    [Fact]
    public async Task RepositoryQuery_FilterByCategory_Brand_Size_PriceRanges_AndCombination()
    {
        await using var db = CreateInMemoryDbContext();
        var repo = new ProductRepository(db);

        var catA = new Category { Id = Guid.NewGuid(), Name = "Cat A", IsActive = true };
        var catB = new Category { Id = Guid.NewGuid(), Name = "Cat B", IsActive = true };
        var brandA = new Brand { Id = Guid.NewGuid(), Name = "Brand A", Tier = BrandTier.Commercial, DefaultCommissionRate = 15m, IsActive = true };
        var brandB = new Brand { Id = Guid.NewGuid(), Name = "Brand B", Tier = BrandTier.LocalBrand, DefaultCommissionRate = 12m, IsActive = true };
        db.Categories.AddRange(catA, catB);
        db.Brands.AddRange(brandA, brandB);

        var targetProd = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-TARGET",
            Name = "Target Item",
            Size = "M",
            ListedPrice = 500000m,
            Status = ProductStatus.OnSale,
            CategoryId = catA.Id,
            BrandId = brandA.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var otherProd = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-OTHER",
            Name = "Other Item",
            Size = "XL",
            ListedPrice = 900000m,
            Status = ProductStatus.OnSale,
            CategoryId = catB.Id,
            BrandId = brandB.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        db.Products.AddRange(targetProd, otherProd);
        await db.SaveChangesAsync();

        // Filter by category
        var resCat = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { CategoryId = catA.Id });
        Assert.Equal(1, resCat.TotalCount);
        Assert.Equal(targetProd.Id, resCat.Items[0].Id);

        // Filter by brand
        var resBrand = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { BrandId = brandB.Id });
        Assert.Equal(1, resBrand.TotalCount);
        Assert.Equal(otherProd.Id, resBrand.Items[0].Id);

        // Filter by size
        var resSize = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { Size = "  m  " });
        Assert.Equal(1, resSize.TotalCount);
        Assert.Equal(targetProd.Id, resSize.Items[0].Id);

        // Filter by price range
        var resPrice = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { MinPrice = 400000m, MaxPrice = 600000m });
        Assert.Equal(1, resPrice.TotalCount);
        Assert.Equal(targetProd.Id, resPrice.Items[0].Id);

        // Combined filter matching target
        var resCombo = await repo.GetPublicProductsAsync(new GetPublicProductsRequest
        {
            CategoryId = catA.Id,
            BrandId = brandA.Id,
            Size = "M",
            MinPrice = 400000m,
            MaxPrice = 600000m
        });
        Assert.Equal(1, resCombo.TotalCount);
        Assert.Equal(targetProd.Id, resCombo.Items[0].Id);

        // Combined filter with non-matching intersection returns empty
        var resEmptyCombo = await repo.GetPublicProductsAsync(new GetPublicProductsRequest
        {
            CategoryId = catA.Id,
            BrandId = brandB.Id
        });
        Assert.Equal(0, resEmptyCombo.TotalCount);
        Assert.Empty(resEmptyCombo.Items);
    }

    [Fact]
    public async Task RepositoryQuery_PaginationAndSorting_StableAcrossPages()
    {
        await using var db = CreateInMemoryDbContext();
        var repo = new ProductRepository(db);

        var cat = new Category { Id = Guid.NewGuid(), Name = "General Cat", IsActive = true };
        var brand = new Brand { Id = Guid.NewGuid(), Name = "General Brand", Tier = BrandTier.Commercial, DefaultCommissionRate = 15m, IsActive = true };
        db.Categories.Add(cat);
        db.Brands.Add(brand);

        var now = DateTimeOffset.UtcNow;
        var p1 = new Product { Id = Guid.NewGuid(), Barcode = "BC-1", Name = "P1 Newer", Size = "M", ListedPrice = 100000m, Status = ProductStatus.OnSale, CategoryId = cat.Id, BrandId = brand.Id, CreatedAt = now };
        var p2 = new Product { Id = Guid.NewGuid(), Barcode = "BC-2", Name = "P2 Middle", Size = "M", ListedPrice = 100000m, Status = ProductStatus.OnSale, CategoryId = cat.Id, BrandId = brand.Id, CreatedAt = now.AddMinutes(-5) };
        var p3 = new Product { Id = Guid.NewGuid(), Barcode = "BC-3", Name = "P3 Older", Size = "M", ListedPrice = 100000m, Status = ProductStatus.OnSale, CategoryId = cat.Id, BrandId = brand.Id, CreatedAt = now.AddMinutes(-10) };

        db.Products.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var page1 = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { Page = 1, PageSize = 2 });
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(p1.Id, page1.Items[0].Id);
        Assert.Equal(p2.Id, page1.Items[1].Id);

        var page2 = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { Page = 2, PageSize = 2 });
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page2.TotalPages);
        Assert.Single(page2.Items);
        Assert.Equal(p3.Id, page2.Items[0].Id);
    }

    [Fact]
    public async Task PrivacyGuarantee_ResponseNeverLeaksInternalOrConsignorData()
    {
        await using var db = CreateInMemoryDbContext();
        var repo = new ProductRepository(db);

        var customerId = Guid.NewGuid();
        var customerAccount = new Account
        {
            Id = customerId,
            FullName = "Khách Hàng Ký Gửi Bảo Mật",
            PhoneNumber = "0901234567",
            Email = "consignor@example.com",
            AccountType = AccountType.Customer,
            CustomerProfile = new CustomerProfile
            {
                Id = customerId,
                BankCode = "VCB",
                BankAccountNumber = "999888777",
                BankAccountHolderName = "KHACH HANG KY GUI"
            }
        };
        db.Accounts.Add(customerAccount);

        var employeeId = Guid.NewGuid();
        var contract = new ConsignmentContract
        {
            Id = Guid.NewGuid(),
            ContractNumber = "HD-SECRET-001",
            CustomerId = customerId,
            CreatedByEmployeeId = employeeId,
            SignedAt = DateTimeOffset.UtcNow.AddMonths(-1),
            EndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            ExpiryHandling = ExpiryHandling.Return,
            Status = ConsignmentContractStatus.Active
        };
        db.ConsignmentContracts.Add(contract);

        var cat = new Category { Id = Guid.NewGuid(), Name = "Category Safe", IsActive = true };
        var brand = new Brand { Id = Guid.NewGuid(), Name = "Brand Safe", Tier = BrandTier.Commercial, DefaultCommissionRate = 18m, IsActive = true };
        db.Categories.Add(cat);
        db.Brands.Add(brand);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "INTERNAL-BARCODE-999",
            Name = "Consignment Dress",
            Size = "M",
            ConditionPercentage = 95,
            Description = "Public dress description",
            CategoryId = cat.Id,
            BrandId = brand.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        product.AssignConsignmentTerms(contract.Id, 750000m, brand);
        product.OfferForSale();
        db.Products.Add(product);

        db.ProductImages.Add(new ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://cdn.example.com/dress.jpg",
            ImageType = ProductImageType.Cover,
            SortOrder = 0
        });

        await db.SaveChangesAsync();

        var result = await repo.GetPublicProductsAsync(new GetPublicProductsRequest());
        Assert.Single(result.Items);

        var json = JsonSerializer.Serialize(result, WebJsonOptions);

        // Forbidden internal & financial fields
        Assert.DoesNotContain("barcode", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("purchasePrice", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("agreedCommissionRate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("agreedBrandTier", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("consignmentContractId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("buyoutId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageLocationId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INTERNAL-BARCODE-999", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HD-SECRET-001", json, StringComparison.OrdinalIgnoreCase);

        // Forbidden consignor customer fields
        Assert.DoesNotContain("consignor@example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0901234567", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("999888777", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KHACH HANG KY GUI", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(customerId.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DevelopmentDataSeeder_SeedAsync_IsIdempotentAndPopulatesCatalogDemo()
    {
        await using var db = CreateInMemoryDbContext();
        var hostEnv = new FakeHostEnvironment { EnvironmentName = "Development" };
        var auditContext = new SystemAuditContext();
        var seeder = new DevelopmentDataSeeder(db, hostEnv, auditContext);

        // Run seed first time
        await seeder.SeedAsync();

        var onSaleCountFirst = await db.Products.CountAsync(p => p.Status == ProductStatus.OnSale);
        Assert.True(onSaleCountFirst >= 25);

        var categoriesCountFirst = await db.Categories.CountAsync();
        Assert.True(categoriesCountFirst >= 4);

        var brandsCountFirst = await db.Brands.CountAsync();
        Assert.True(brandsCountFirst >= 4);

        // Run seed second time (must be idempotent: no exceptions, no duplicate entries)
        await seeder.SeedAsync();

        var onSaleCountSecond = await db.Products.CountAsync(p => p.Status == ProductStatus.OnSale);
        Assert.Equal(onSaleCountFirst, onSaleCountSecond);

        // Verify public query on seeded data is driven by Product.Status only.
        var repo = new ProductRepository(db);
        var publicResult = await repo.GetPublicProductsAsync(new GetPublicProductsRequest { PageSize = 100 });

        // Ensure non-OnSale counterexamples never appear in public catalog.
        Assert.DoesNotContain(publicResult.Items, p => p.Name.Contains("Đang Thẩm Định", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicResult.Items, p => p.Name.Contains("Đã Bán", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicResult.Items, p => p.Name.Contains("Đã Hết Hạn", StringComparison.OrdinalIgnoreCase));

        // Ensure item without cover image has null coverImageUrl
        var noCoverItem = publicResult.Items.SingleOrDefault(p => p.Name == "Áo Thun Không Ảnh Bìa");
        Assert.NotNull(noCoverItem);
        Assert.Null(noCoverItem.CoverImageUrl);
    }

    private sealed class FakeTestProductService : IProductService
    {
        public Task<List<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ProductDto>());

        public Task<Result<ProductDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Failure<ProductDto>(ApplicationError.NotFound("PRODUCT.NOT_FOUND", "Not found")));

        public Task<Result<ProductDto>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Failure<ProductDto>(ApplicationError.NotFound("PRODUCT.NOT_FOUND", "Not found")));

        public Task<PagedResponse<PublicProductResponse>> GetPublicProductsAsync(
            GetPublicProductsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResponse<PublicProductResponse>(
                [
                    new PublicProductResponse(
                        Guid.NewGuid(),
                        "Sample Item",
                        "Description",
                        "150000.00",
                        "M",
                        90,
                        new PublicCategoryResponse(Guid.NewGuid(), "Cat"),
                        new PublicBrandResponse(Guid.NewGuid(), "Brand"),
                        "https://cdn.example.com/cover.jpg")
                ],
                request.Page,
                request.PageSize,
                1));

        public Task<Result<ProductDto>> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new ProductDto(Guid.NewGuid(), dto.Barcode, dto.Name, dto.Description, dto.ListedPrice, "OnSale", null, null, null)));
    }

    private sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OCS.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
