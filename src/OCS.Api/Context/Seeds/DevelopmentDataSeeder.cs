using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OCS.Api.Interfaces.Services;
using OCS.Api.Services;
using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Context.Seeds;

public sealed class DevelopmentDataSeeder(AppDbContext dbContext, IHostEnvironment environment, SystemAuditContext auditContext)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
        }

        auditContext.Initialize("DemoSeed");

        // 1. Internal Accounts (Staff, Manager, Admin)
        var defaultStaffPasswordHash = BCrypt.Net.BCrypt.HashPassword("DevStaff@123");
        (Guid Id, string Name, string Phone, string? Username, Guid Role)[] staffAccounts =
        [
            (Guid.Parse("10000000-0000-0000-0000-000000000001"), "Admin Dev", "0900000001", "admin_dev", Role.AdminRoleId),
            (Guid.Parse("10000000-0000-0000-0000-000000000002"), "Manager Dev", "0900000002", "manager_dev", Role.ManagerRoleId),
            (Guid.Parse("10000000-0000-0000-0000-000000000003"), "Staff Dev", "0900000003", "staff_dev", Role.StaffRoleId)
        ];

        foreach (var (id, name, phone, username, roleId) in staffAccounts)
        {
            if (await dbContext.Accounts.AnyAsync(account => account.Id == id, cancellationToken))
            {
                continue;
            }

            var account = new Account
            {
                Id = id,
                PhoneNumber = phone,
                Username = username,
                FullName = name,
                AccountType = AccountType.Internal,
                Status = AccountStatus.Active,
                PasswordHash = defaultStaffPasswordHash
            };

            var profile = new EmployeeProfile
            {
                Id = id,
                Account = account
            };

            var accountRole = new AccountRole
            {
                AccountId = id,
                RoleId = roleId
            };

            dbContext.Accounts.Add(account);
            dbContext.EmployeeProfiles.Add(profile);
            dbContext.AccountRoles.Add(accountRole);
        }

        // 2. Customer Accounts (Khách hàng mẫu theo Figma)
        (Guid Id, string Name, string Phone)[] customerAccounts =
        [
            (Guid.Parse("10000000-0000-0000-0000-000000000004"), "Khách Hàng Ký Gửi", "0901234567"),
            (Guid.Parse("10000000-0000-0000-0000-000000000010"), "Nguyễn Thu Trang", "0908123456"),
            (Guid.Parse("10000000-0000-0000-0000-000000000011"), "Trần Mai Chi", "0912345678"),
            (Guid.Parse("10000000-0000-0000-0000-000000000012"), "Hoàng Yến", "0987654321")
        ];

        foreach (var (id, name, phone) in customerAccounts)
        {
            if (await dbContext.Accounts.AnyAsync(account => account.Id == id, cancellationToken))
            {
                continue;
            }

            var account = new Account
            {
                Id = id,
                PhoneNumber = phone,
                Username = null,
                FullName = name,
                AccountType = AccountType.Customer,
                Status = AccountStatus.Active
            };

            var profile = new CustomerProfile
            {
                Id = id,
                Account = account,
                Status = CustomerStatus.Active
            };

            var accountRole = new AccountRole
            {
                AccountId = id,
                RoleId = Role.CustomerRoleId
            };

            dbContext.Accounts.Add(account);
            dbContext.CustomerProfiles.Add(profile);
            dbContext.AccountRoles.Add(accountRole);
        }

        // 3. Appointment Time Slots
        (Guid Id, TimeOnly Start, TimeOnly End, int Max)[] timeSlots =
        [
            (Guid.Parse("11111111-0000-0000-0000-000000000001"), new TimeOnly(9, 0), new TimeOnly(10, 0), 3),
            (Guid.Parse("11111111-0000-0000-0000-000000000002"), new TimeOnly(10, 0), new TimeOnly(11, 0), 3),
            (Guid.Parse("11111111-0000-0000-0000-000000000003"), new TimeOnly(11, 0), new TimeOnly(12, 0), 3)
        ];

        var slotMap = new Dictionary<TimeOnly, Guid>();
        foreach (var (id, start, end, max) in timeSlots)
        {
            var existing = await dbContext.AppointmentTimeSlots.FirstOrDefaultAsync(s => s.Id == id || (s.StartTime == start && s.EndTime == end), cancellationToken);
            if (existing != null)
            {
                slotMap[start] = existing.Id;
                continue;
            }

            dbContext.AppointmentTimeSlots.Add(new AppointmentTimeSlot
            {
                Id = id,
                StartTime = start,
                EndTime = end,
                MaxCustomers = max,
                IsActive = true
            });
            slotMap[start] = id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 4. Demo Appointments matching Figma
        (Guid Id, string Tracking, Guid CustomerId, Guid SlotId, DateOnly Date, int Items, AppointmentStatus Status, string Note)[] demoAppointments =
        [
            (
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                "LH-012",
                Guid.Parse("10000000-0000-0000-0000-000000000010"),
                Guid.Parse("11111111-0000-0000-0000-000000000001"),
                new DateOnly(2026, 10, 28),
                6,
                AppointmentStatus.Completed,
                "Áo khoác dạ và chân váy len"
            ),
            (
                Guid.Parse("20000000-0000-0000-0000-000000000002"),
                "LH-261028-018",
                Guid.Parse("10000000-0000-0000-0000-000000000011"),
                Guid.Parse("11111111-0000-0000-0000-000000000002"),
                new DateOnly(2026, 10, 28),
                8,
                AppointmentStatus.CheckedIn,
                "Quần áo và một túi xách"
            ),
            (
                Guid.Parse("20000000-0000-0000-0000-000000000003"),
                "LH-020",
                Guid.Parse("10000000-0000-0000-0000-000000000012"),
                Guid.Parse("11111111-0000-0000-0000-000000000003"),
                new DateOnly(2026, 10, 28),
                4,
                AppointmentStatus.Scheduled,
                "Váy công sở và áo sơ mi"
            )
        ];

        foreach (var (id, tracking, customerId, slotId, date, items, status, note) in demoAppointments)
        {
            if (await dbContext.Appointments.AnyAsync(a => a.Id == id, cancellationToken))
            {
                continue;
            }

            var effectiveSlotId = slotMap.Values.Contains(slotId) ? slotId
                : (slotMap.TryGetValue(slotId == Guid.Parse("11111111-0000-0000-0000-000000000001") ? new TimeOnly(9, 0)
                    : slotId == Guid.Parse("11111111-0000-0000-0000-000000000002") ? new TimeOnly(10, 0)
                    : new TimeOnly(11, 0), out var mappedId) ? mappedId : slotId);

            dbContext.Appointments.Add(new Appointment
            {
                Id = id,
                TrackingNumber = tracking,
                CustomerId = customerId,
                TimeSlotId = effectiveSlotId,
                AppointmentDate = date,
                EstimatedItemCount = items,
                SubmissionMethod = SubmissionMethod.InPerson,
                Status = status,
                Note = note
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 5. Demo Categories
        (Guid Id, string Name, bool IsActive)[] demoCategories =
        [
            (Guid.Parse("30000000-0000-0000-0000-000000000001"), "Áo khoác", true),
            (Guid.Parse("30000000-0000-0000-0000-000000000002"), "Đầm & Váy", true),
            (Guid.Parse("30000000-0000-0000-0000-000000000003"), "Quần & Jeans", true),
            (Guid.Parse("30000000-0000-0000-0000-000000000004"), "Phụ kiện", true),
            (Guid.Parse("30000000-0000-0000-0000-000000000005"), "Đồ thanh lý cũ", false)
        ];

        var categoryMap = new Dictionary<Guid, Category>();
        foreach (var (id, name, isActive) in demoCategories)
        {
            var existing = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (existing != null)
            {
                categoryMap[id] = existing;
                continue;
            }

            var category = new Category
            {
                Id = id,
                Name = name,
                IsActive = isActive
            };
            dbContext.Categories.Add(category);
            categoryMap[id] = category;
        }

        // 6. Demo Brands
        (Guid Id, string Name, BrandTier Tier, decimal Rate, bool IsActive)[] demoBrands =
        [
            (Guid.Parse("40000000-0000-0000-0000-000000000001"), "Zara", BrandTier.Commercial, 15m, true),
            (Guid.Parse("40000000-0000-0000-0000-000000000002"), "Uniqlo", BrandTier.Commercial, 15m, true),
            (Guid.Parse("40000000-0000-0000-0000-000000000003"), "Routine", BrandTier.LocalBrand, 12m, true),
            (Guid.Parse("40000000-0000-0000-0000-000000000004"), "Chanel", BrandTier.Luxury, 20m, true),
            (Guid.Parse("40000000-0000-0000-0000-000000000005"), "OldBrand Inactive", BrandTier.NoBrand, 10m, false)
        ];

        var brandMap = new Dictionary<Guid, Brand>();
        foreach (var (id, name, tier, rate, isActive) in demoBrands)
        {
            var existing = await dbContext.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (existing != null)
            {
                brandMap[id] = existing;
                continue;
            }

            var brand = new Brand
            {
                Id = id,
                Name = name,
                Tier = tier,
                DefaultCommissionRate = rate,
                IsActive = isActive
            };
            dbContext.Brands.Add(brand);
            brandMap[id] = brand;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 7. Demo Consignment Contract & Buyout
        var contractId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        if (!await dbContext.ConsignmentContracts.AnyAsync(c => c.Id == contractId, cancellationToken))
        {
            dbContext.ConsignmentContracts.Add(new ConsignmentContract
            {
                Id = contractId,
                ContractNumber = "HD-2026-0001",
                CustomerId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
                CreatedByEmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                SignedAt = DateTimeOffset.UtcNow.AddMonths(-1),
                EndsAt = DateTimeOffset.UtcNow.AddMonths(2),
                ExpiryHandling = ExpiryHandling.Return,
                Status = ConsignmentContractStatus.Active
            });
        }

        var buyoutId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        if (!await dbContext.Buyouts.AnyAsync(b => b.Id == buyoutId, cancellationToken))
        {
            dbContext.Buyouts.Add(new Buyout
            {
                Id = buyoutId,
                BuyoutNumber = "BO-2026-0001",
                CustomerId = Guid.Parse("10000000-0000-0000-0000-000000000010"),
                CreatedByEmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                ApprovedByEmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                TotalPurchasePrice = 25000000m,
                Status = BuyoutStatus.Approved
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 8. Demo Catalog Products (At least 25 OnSale across >= 3 categories, >= 4 brands, >= 4 sizes)
        var zara = brandMap[Guid.Parse("40000000-0000-0000-0000-000000000001")];
        var uniqlo = brandMap[Guid.Parse("40000000-0000-0000-0000-000000000002")];
        var routine = brandMap[Guid.Parse("40000000-0000-0000-0000-000000000003")];
        var chanel = brandMap[Guid.Parse("40000000-0000-0000-0000-000000000004")];
        var inactiveBrand = brandMap[Guid.Parse("40000000-0000-0000-0000-000000000005")];

        var catCoat = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var catDress = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var catPants = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var catAccessory = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var catInactive = Guid.Parse("30000000-0000-0000-0000-000000000005");

        var baseTime = DateTimeOffset.UtcNow.AddDays(-10);

        (Guid Id, string Barcode, string Name, Guid CatId, Brand Brand, string Size, decimal Price, int Condition, bool isConsignment, decimal? BuyPrice)[] onSaleProducts =
        [
            (Guid.Parse("70000000-0000-0000-0000-000000000001"), "BC-0001", "Áo Blazer Zara Dạ Nâu", catCoat, zara, "M", 650000m, 95, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000002"), "BC-0002", "Áo Khoác Phao Uniqlo Siêu Nhẹ", catCoat, uniqlo, "L", 850000m, 90, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000003"), "BC-0003", "Áo Khoác Denim Routine Phom Rộng", catCoat, routine, "XL", 490000m, 92, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000004"), "BC-0004", "Áo Dạ Tweed Chanel Cổ Điển", catCoat, chanel, "S", 12500000m, 98, false, 8000000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000005"), "BC-0005", "Đầm Lụa Dạ Hội Zara Đen", catDress, zara, "S", 720000m, 90, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000006"), "BC-0006", "Đầm Suông Linen Uniqlo Mát", catDress, uniqlo, "M", 380000m, 88, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000007"), "BC-0007", "Chân Váy Xếp Ly Routine", catDress, routine, "S", 290000m, 85, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000008"), "BC-0008", "Đầm Tweed Chanel Haute", catDress, chanel, "M", 18500000m, 99, false, 12000000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000009"), "BC-0009", "Quần Tây Ống Rộng Zara", catPants, zara, "L", 420000m, 90, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000010"), "BC-0010", "Quần Jeans Co Giãn Uniqlo EZY", catPants, uniqlo, "M", 550000m, 92, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000011"), "BC-0011", "Quần Khaki Routine Slimfit", catPants, routine, "L", 350000m, 89, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000012"), "BC-0012", "Quần Âu Chanel Thiết Kế", catPants, chanel, "S", 8900000m, 95, false, 5500000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000013"), "BC-0013", "Khăn Choàng Cổ Lụa Chanel", catAccessory, chanel, "Free", 4500000m, 96, false, 3000000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000014"), "BC-0014", "Thắt Lưng Da Routine Nam", catAccessory, routine, "L", 220000m, 90, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000015"), "BC-0015", "Túi Tote Canvas Uniqlo Eco", catAccessory, uniqlo, "Free", 150000m, 85, true, null),
            (Guid.Parse("70000000-0000-0000-0000-000000000016"), "BC-0016", "Túi Xách Da Zara Nữ", catAccessory, zara, "Free", 690000m, 93, false, 400000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000017"), "BC-0017", "Áo Măng Tô Dạ Dáng Dài Zara", catCoat, zara, "L", 1150000m, 94, false, 750000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000018"), "BC-0018", "Áo Len Cổ Lọ Uniqlo Merino", catCoat, uniqlo, "S", 450000m, 91, false, 280000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000019"), "BC-0019", "Áo Cardigan Dệt Kim Routine", catCoat, routine, "M", 390000m, 88, false, 240000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000020"), "BC-0020", "Đầm Maxi Hoa Nhí Zara", catDress, zara, "M", 480000m, 90, false, 300000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000021"), "BC-0021", "Đầm Sơ Mi Cotton Uniqlo", catDress, uniqlo, "XL", 520000m, 92, false, 320000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000022"), "BC-0022", "Váy Jean Chữ A Routine", catDress, routine, "S", 310000m, 87, false, 190000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000023"), "BC-0023", "Quần Shorts Jeans Zara", catPants, zara, "S", 280000m, 89, false, 170000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000024"), "BC-0024", "Quần Jogger Nỉ Uniqlo", catPants, uniqlo, "XL", 360000m, 90, false, 220000m),
            (Guid.Parse("70000000-0000-0000-0000-000000000025"), "BC-0025", "Quần Baggy Kẻ Caro Routine", catPants, routine, "M", 330000m, 88, false, 200000m)
        ];

        string[] curatedCovers =
        [
            "https://images.unsplash.com/photo-1591047139829-d91aecb6caea?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1548883354-7622d03aca27?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1576995853123-5a10305d93c0?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1539571696357-5a69c17a67c6?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1595777457583-95e059d581b8?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1515372039744-b8f02a3ae446?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1583496661160-fb5886a0aaaa?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1566174053879-31528523f8ae?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1509551388413-e18d0ac5d495?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1541099649105-f69ad21f3246?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1624378439575-d8705ad7ae80?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1584370848010-d7fe6bc767ec?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1606760227091-3dd870d97f1d?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1624222247344-550fb60583dc?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1544816155-12df9643f363?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1548036328-c9fa89d128fa?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1539533018447-63fcce667823?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1576871337632-b9aef4c17ab9?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1434389677669-e08b4cac3105?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1496747611176-843222e1e57c?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1502716119720-b23a93e5fe1b?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1551803091-e20673f15770?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1565084888279-aca607ecce0c?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1552902865-b72c031ac5ea?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1517445312882-bc9910d016b7?w=800&auto=format&fit=crop&q=80"
        ];

        string[] curatedDetailsSide =
        [
            "https://images.unsplash.com/photo-1507679799987-c73779587ccf?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1551537482-f2075a1d41f2?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1601333144130-8cbb312386b6?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1515886657613-9f3515b0c78f?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1509631179647-0177331693ae?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1572804013309-59a88b7e92f1?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1582142306909-195724d33ffc?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1512436991641-6745cdb1723f?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1542272604-780c96856592?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1584917865442-de89df76afd3?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1590874103328-eac38a683ce7?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1544441893-675973e31985?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1554412933-514a83d2f3c8?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1508296695146-257a814070b4?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1550614000-4895a10e1bfd?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1525507119028-ed4c629a60a3?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1558769132-cb1aea458c5e?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1503342217505-b0a15ec3261c?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1485968579580-b6d095142e6e?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1485230895905-ec40ba36b9bc?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1490481651871-ab68de25d43d?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1469334031218-e382a71b716b?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1445205170230-053b83016050?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1483985988355-763728e1935b?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1515886657613-9f3515b0c78f?w=800&auto=format&fit=crop&q=80"
        ];

        string[] curatedDetailsBack =
        [
            "https://images.unsplash.com/photo-1544022613-e87ca75a784a?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1516762689617-e1cffcef479d?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1489987707025-afc232f7ea0f?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1529720317453-c8da503f2051?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1506152983158-b4a74a01c721?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1520006403909-838d6b92c22e?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1518049362265-d5b2a6467637?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1479064555552-3ef4979f8908?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1508746829417-e6f548d8d6ed?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1519748771451-a14ef59b36cb?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1509631179647-0177331693ae?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1520006403909-838d6b92c22e?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1544441893-675973e31985?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1515372039744-b8f02a3ae446?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1558769132-cb1aea458c5e?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1503342217505-b0a15ec3261c?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1516762689617-e1cffcef479d?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1572804013309-59a88b7e92f1?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1507679799987-c73779587ccf?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1548883354-7622d03aca27?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1595777457583-95e059d581b8?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1541099649105-f69ad21f3246?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1584370848010-d7fe6bc767ec?w=800&auto=format&fit=crop&q=80",
            "https://images.unsplash.com/photo-1566174053879-31528523f8ae?w=800&auto=format&fit=crop&q=80"
        ];

        for (int i = 0; i < onSaleProducts.Length; i++)
        {
            var pInfo = onSaleProducts[i];
            var existingProduct = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == pInfo.Id, cancellationToken);
            if (existingProduct == null)
            {
                var product = new Product
                {
                    Id = pInfo.Id,
                    Barcode = pInfo.Barcode,
                    Name = pInfo.Name,
                    CategoryId = pInfo.CatId,
                    BrandId = pInfo.Brand.Id,
                    Size = pInfo.Size,
                    ConditionPercentage = pInfo.Condition,
                    Description = $"Mô tả chi tiết sản phẩm {pInfo.Name}, chất lượng đảm bảo.",
                    CreatedAt = baseTime.AddHours(i + 1)
                };

                if (pInfo.isConsignment)
                {
                    product.AssignConsignmentTerms(contractId, pInfo.Price, pInfo.Brand);
                }
                else
                {
                    product.AssignBuyoutTerms(buyoutId, pInfo.Price, pInfo.BuyPrice!.Value);
                }

                product.OfferForSale();
                dbContext.Products.Add(product);
            }

            var coverUrl = curatedCovers[i % curatedCovers.Length];
            var sideUrl = curatedDetailsSide[i % curatedDetailsSide.Length];
            var backUrl = curatedDetailsBack[i % curatedDetailsBack.Length];

            // Cover Image (SortOrder = 0)
            var coverImageId = Guid.Parse($"71000000-0000-0000-0000-{i + 1:D12}");
            var existingCover = await dbContext.ProductImages.FirstOrDefaultAsync(img => img.Id == coverImageId, cancellationToken);
            if (existingCover == null)
            {
                dbContext.ProductImages.Add(new ProductImage
                {
                    Id = coverImageId,
                    ProductId = pInfo.Id,
                    Url = coverUrl,
                    ImageType = ProductImageType.Cover,
                    SortOrder = 0
                });
            }
            else
            {
                existingCover.Url = coverUrl;
            }

            // Products i = 20 & 21: Edge case - ONLY Cover image (skip detail images)
            if (i == 20 || i == 21)
            {
                continue;
            }

            // Detail Image 1 (SortOrder = 1 - Multi-angle side/profile)
            var detail1ImageId = Guid.Parse($"72000000-0000-0000-0000-{i + 1:D12}");
            var existingDetail1 = await dbContext.ProductImages.FirstOrDefaultAsync(img => img.Id == detail1ImageId, cancellationToken);
            if (existingDetail1 == null)
            {
                dbContext.ProductImages.Add(new ProductImage
                {
                    Id = detail1ImageId,
                    ProductId = pInfo.Id,
                    Url = sideUrl,
                    ImageType = ProductImageType.Detail,
                    SortOrder = 1
                });
            }
            else
            {
                existingDetail1.Url = sideUrl;
            }

            // Product i = 22: Edge case - Cover + Detail + Incident image (to verify Incident is excluded in public detail)
            if (i == 22)
            {
                var incidentImageId = Guid.Parse($"74000000-0000-0000-0000-{i + 1:D12}");
                var existingIncident = await dbContext.ProductImages.FirstOrDefaultAsync(img => img.Id == incidentImageId, cancellationToken);
                if (existingIncident == null)
                {
                    dbContext.ProductImages.Add(new ProductImage
                    {
                        Id = incidentImageId,
                        ProductId = pInfo.Id,
                        Url = "https://images.unsplash.com/demo/incident-scratch-proof.jpg",
                        ImageType = ProductImageType.Incident,
                        SortOrder = 99
                    });
                }
                continue;
            }

            // Detail Image 2 (SortOrder = 2 - Multi-angle back/texture)
            var detail2ImageId = Guid.Parse($"73000000-0000-0000-0000-{i + 1:D12}");
            var existingDetail2 = await dbContext.ProductImages.FirstOrDefaultAsync(img => img.Id == detail2ImageId, cancellationToken);
            if (existingDetail2 == null)
            {
                dbContext.ProductImages.Add(new ProductImage
                {
                    Id = detail2ImageId,
                    ProductId = pInfo.Id,
                    Url = backUrl,
                    ImageType = ProductImageType.Detail,
                    SortOrder = 2
                });
            }
            else
            {
                existingDetail2.Url = backUrl;
            }
        }

        // 9. Counterexamples: ensure they exist but never appear in public catalog
        // 9a. PendingProcessing product (appraised but not offered for sale)
        var pendingId = Guid.Parse("70000000-0000-0000-0000-000000000026");
        if (!await dbContext.Products.AnyAsync(p => p.Id == pendingId, cancellationToken))
        {
            var p = new Product
            {
                Id = pendingId,
                Barcode = "BC-0026",
                Name = "Áo Len Đang Thẩm Định",
                CategoryId = catCoat,
                BrandId = zara.Id,
                Size = "M",
                ConditionPercentage = 80,
                Description = "Sản phẩm đang trong trạng thái chờ xử lý",
                CreatedAt = baseTime.AddHours(26)
            };
            p.AssignConsignmentTerms(contractId, 500000m, zara);
            dbContext.Products.Add(p);
        }

        // 9b. Sold product
        var soldId = Guid.Parse("70000000-0000-0000-0000-000000000027");
        if (!await dbContext.Products.AnyAsync(p => p.Id == soldId, cancellationToken))
        {
            var p = new Product
            {
                Id = soldId,
                Barcode = "BC-0027",
                Name = "Túi Đã Bán",
                CategoryId = catAccessory,
                BrandId = chanel.Id,
                Size = "Free",
                ConditionPercentage = 95,
                Description = "Sản phẩm đã bán thành công",
                CreatedAt = baseTime.AddHours(27)
            };
            p.AssignBuyoutTerms(buyoutId, 15000000m, 10000000m);
            p.OfferForSale();
            p.MarkSold();
            dbContext.Products.Add(p);
        }

        // 9c. Expired product
        var expiredId = Guid.Parse("70000000-0000-0000-0000-000000000028");
        if (!await dbContext.Products.AnyAsync(p => p.Id == expiredId, cancellationToken))
        {
            var p = new Product
            {
                Id = expiredId,
                Barcode = "BC-0028",
                Name = "Váy Đã Hết Hạn Ký Gửi",
                CategoryId = catDress,
                BrandId = routine.Id,
                Size = "S",
                ConditionPercentage = 85,
                Description = "Hợp đồng đã quá hạn",
                CreatedAt = baseTime.AddHours(28)
            };
            p.AssignConsignmentTerms(contractId, 300000m, routine);
            p.OfferForSale();
            p.Status = ProductStatus.Expired;
            dbContext.Products.Add(p);
        }

        // 9d. Inactive Brand product
        var inactiveBrandProdId = Guid.Parse("70000000-0000-0000-0000-000000000029");
        if (!await dbContext.Products.AnyAsync(p => p.Id == inactiveBrandProdId, cancellationToken))
        {
            var p = new Product
            {
                Id = inactiveBrandProdId,
                Barcode = "BC-0029",
                Name = "Sản Phẩm Thương Hiệu Đã Dừng",
                CategoryId = catCoat,
                BrandId = inactiveBrand.Id,
                Size = "L",
                ConditionPercentage = 75,
                Description = "Brand không còn active",
                CreatedAt = baseTime.AddHours(29)
            };
            p.AssignConsignmentTerms(contractId, 200000m, inactiveBrand);
            p.OfferForSale();
            dbContext.Products.Add(p);
        }

        // 9e. Inactive Category product
        var inactiveCatProdId = Guid.Parse("70000000-0000-0000-0000-000000000030");
        if (!await dbContext.Products.AnyAsync(p => p.Id == inactiveCatProdId, cancellationToken))
        {
            var p = new Product
            {
                Id = inactiveCatProdId,
                Barcode = "BC-0030",
                Name = "Sản Phẩm Danh Mục Không Hoạt Động",
                CategoryId = catInactive,
                BrandId = routine.Id,
                Size = "M",
                ConditionPercentage = 70,
                Description = "Category không active",
                CreatedAt = baseTime.AddHours(30)
            };
            p.AssignConsignmentTerms(contractId, 150000m, routine);
            p.OfferForSale();
            dbContext.Products.Add(p);
        }

        // 9f. Product OnSale with ONLY Detail image (no Cover image) -> CoverImageUrl should be null in public response
        var noCoverProdId = Guid.Parse("70000000-0000-0000-0000-000000000031");
        var existingNoCoverProd = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == noCoverProdId, cancellationToken);
        if (existingNoCoverProd == null)
        {
            var p = new Product
            {
                Id = noCoverProdId,
                Barcode = "BC-0031",
                Name = "Áo Thun Không Ảnh Bìa",
                CategoryId = catCoat,
                BrandId = uniqlo.Id,
                Size = "XL",
                ConditionPercentage = 90,
                Description = "Chỉ có ảnh chi tiết không có ảnh bìa",
                CreatedAt = baseTime.AddHours(10)
            };
            p.AssignConsignmentTerms(contractId, 250000m, uniqlo);
            p.OfferForSale();
            dbContext.Products.Add(p);
        }
        else
        {
            existingNoCoverProd.CreatedAt = baseTime.AddHours(10);
        }

        var detailImgId = Guid.Parse("71000000-0000-0000-0000-000000000031");
        var whiteTeeDetailUrl = "https://images.unsplash.com/photo-1521572267360-ee0c2909d518?w=800&auto=format&fit=crop&q=80";
        var existingNoCoverDetail = await dbContext.ProductImages.FirstOrDefaultAsync(img => img.Id == detailImgId, cancellationToken);
        if (existingNoCoverDetail == null)
        {
            dbContext.ProductImages.Add(new ProductImage
            {
                Id = detailImgId,
                ProductId = noCoverProdId,
                Url = whiteTeeDetailUrl,
                ImageType = ProductImageType.Detail,
                SortOrder = 1
            });
        }
        else
        {
            existingNoCoverDetail.Url = whiteTeeDetailUrl;
        }

        // 9g. Consignment product with active customer profile to verify zero data leak
        var consignorProdId = Guid.Parse("70000000-0000-0000-0000-000000000032");
        if (!await dbContext.Products.AnyAsync(p => p.Id == consignorProdId, cancellationToken))
        {
            var p = new Product
            {
                Id = consignorProdId,
                Barcode = "BC-0032",
                Name = "Khăn Quàng Ký Gửi Mẫu Bảo Mật",
                CategoryId = catAccessory,
                BrandId = zara.Id,
                Size = "Free",
                ConditionPercentage = 95,
                Description = "Sản phẩm ký gửi bảo mật thông tin chủ sở hữu",
                CreatedAt = baseTime.AddHours(32)
            };
            p.AssignConsignmentTerms(contractId, 320000m, zara);
            p.OfferForSale();
            dbContext.Products.Add(p);
        }

        var coverImgId = Guid.Parse("71000000-0000-0000-0000-000000000032");
        var scarfCoverUrl = "https://images.unsplash.com/photo-1520903920243-00d872a2d1c9?w=800&auto=format&fit=crop&q=80";
        var existingScarfCover = await dbContext.ProductImages.FirstOrDefaultAsync(img => img.Id == coverImgId, cancellationToken);
        if (existingScarfCover == null)
        {
            dbContext.ProductImages.Add(new ProductImage
            {
                Id = coverImgId,
                ProductId = consignorProdId,
                Url = scarfCoverUrl,
                ImageType = ProductImageType.Cover,
                SortOrder = 0
            });
        }
        else
        {
            existingScarfCover.Url = scarfCoverUrl;
        }

        var scarfDetailImgId = Guid.Parse("72000000-0000-0000-0000-000000000032");
        var scarfDetailUrl = "https://images.unsplash.com/photo-1608256246200-53e635b5b65f?w=800&auto=format&fit=crop&q=80";
        var existingScarfDetail = await dbContext.ProductImages.FirstOrDefaultAsync(img => img.Id == scarfDetailImgId, cancellationToken);
        if (existingScarfDetail == null)
        {
            dbContext.ProductImages.Add(new ProductImage
            {
                Id = scarfDetailImgId,
                ProductId = consignorProdId,
                Url = scarfDetailUrl,
                ImageType = ProductImageType.Detail,
                SortOrder = 1
            });
        }
        else
        {
            existingScarfDetail.Url = scarfDetailUrl;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SeedInventoryLocationsAsync(cancellationToken);
        await SeedPosDemoInvoicesAsync(cancellationToken);
    }

    public async Task SeedPosDemoInvoicesAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
        }

        auditContext.Initialize("DemoSeed");

        (Guid Id, string InvoiceNumber, decimal TotalAmount)[] demoUnpaidInvoices =
        [
            (Guid.Parse("80000000-0000-0000-0000-000000000001"), "HD-POS-DEMO-001", 650000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000002"), "HD-POS-DEMO-002", 1230000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000003"), "HD-POS-TEST-10K", 10000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000004"), "HD-POS-TEST-20K", 20000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000005"), "HD-POS-TEST-10K-A", 10000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000006"), "HD-POS-TEST-10K-B", 10000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000007"), "HD-POS-TEST-10K-C", 10000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000008"), "HD-POS-TEST-10K-D", 10000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000009"), "HD-POS-TEST-10K-E", 10000m),
            (Guid.Parse("80000000-0000-0000-0000-000000000010"), "HD-POS-TEST-10K-F", 10000m)
        ];

        var staffEmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        foreach (var (id, invoiceNumber, totalAmount) in demoUnpaidInvoices)
        {
            var exists = await dbContext.SaleInvoices
                .AnyAsync(x => x.Id == id || x.InvoiceNumber == invoiceNumber, cancellationToken);
            if (exists)
            {
                continue;
            }

            dbContext.SaleInvoices.Add(new SaleInvoice
            {
                Id = id,
                InvoiceNumber = invoiceNumber,
                SoldByEmployeeId = staffEmployeeId,
                TotalAmount = totalAmount,
                PaymentStatus = InvoicePaymentStatus.Unpaid
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedInventoryLocationsAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
        }

        auditContext.Initialize("DemoSeed");

        (Guid Id, string Code, string Name, Guid CategoryId)[] samples =
        [
            (Guid.Parse("75000000-0000-0000-0000-000000000001"), "DEMO-A01", "Kệ áo (mẫu)", Guid.Parse("30000000-0000-0000-0000-000000000001")),
            (Guid.Parse("75000000-0000-0000-0000-000000000002"), "DEMO-B01", "Kệ đầm và váy (mẫu)", Guid.Parse("30000000-0000-0000-0000-000000000002")),
            (Guid.Parse("75000000-0000-0000-0000-000000000003"), "DEMO-C01", "Kệ quần (mẫu)", Guid.Parse("30000000-0000-0000-0000-000000000003")),
            (Guid.Parse("75000000-0000-0000-0000-000000000004"), "DEMO-D01", "Kệ phụ kiện (mẫu)", Guid.Parse("30000000-0000-0000-0000-000000000004"))
        ];

        var locationsByCategory = new Dictionary<Guid, Guid>();
        foreach (var sample in samples)
        {
            var location = await dbContext.StorageLocations.SingleOrDefaultAsync(
                x => x.Id == sample.Id || x.Code == sample.Code, cancellationToken);
            if (location is null)
            {
                location = new StorageLocation { Id = sample.Id, Code = sample.Code, Name = sample.Name };
                dbContext.StorageLocations.Add(location);
            }
            else if (location.Id != sample.Id || location.Code != sample.Code || !location.IsActive)
            {
                throw new InvalidOperationException($"Demo location {sample.Code} conflicts with an existing or inactive location.");
            }

            locationsByCategory.Add(sample.CategoryId, location.Id);
        }

        var demoProducts = Enumerable.Range(1, 26).Concat([31, 32])
            .ToDictionary(i => Guid.Parse($"70000000-0000-0000-0000-{i:D12}"), i => $"BC-{i:D4}");
        var demoIds = demoProducts.Keys.ToArray();
        var products = await dbContext.Products
            .Where(p => demoIds.Contains(p.Id) && p.StorageLocationId == null &&
                (p.Status == ProductStatus.OnSale || p.Status == ProductStatus.PendingProcessing))
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            if (product.Barcode == demoProducts[product.Id] && locationsByCategory.TryGetValue(product.CategoryId, out var locationId))
            {
                product.StorageLocationId = locationId;
                product.InventoryVersion = Guid.NewGuid();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
