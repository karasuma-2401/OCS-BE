using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class BuyoutInspection : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Guid InspectedByEmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ConditionPercentage { get; set; }
    public Guid BrandId { get; set; }
    public Guid CategoryId { get; set; }
    public string InspectionNotes { get; set; } = string.Empty;
    public CustomerProfile Customer { get; set; } = null!;
    public EmployeeProfile InspectedByEmployee { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Category Category { get; set; } = null!;
}

public sealed class BuyoutProposalItem
{
    public Guid BuyoutId { get; set; }
    public Guid InspectionId { get; set; }
    public decimal ListedPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public Buyout Buyout { get; set; } = null!;
    public BuyoutInspection Inspection { get; set; } = null!;
}
