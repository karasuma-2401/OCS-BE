using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

/// <summary>
/// Phiếu mua đứt từ khách ký gửi
/// </summary>
public sealed class Buyout : BaseEntity
{
    public string BuyoutNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid CreatedByEmployeeId { get; set; }

    public Guid? ApprovedByEmployeeId { get; set; }

    public decimal TotalPurchasePrice { get; set; }

    public BuyoutStatus Status { get; set; }
        = BuyoutStatus.Pending;

    public CustomerProfile Customer { get; set; } = null!;

    public EmployeeProfile CreatedByEmployee { get; set; } = null!;

    public EmployeeProfile? ApprovedByEmployee { get; set; }

    public ICollection<Product> Products { get; set; }
        = new List<Product>();
}