using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class CustomerProfile
{
    public Guid Id { get; set; }

    public string? Address { get; set; }

    public string? BankCode { get; set; }               // ngân hàng

    public string? BankAccountNumber { get; set; }      // số tài khoản

    public string? BankAccountHolderName { get; set; }  // tên tk ngân hàng

    public CustomerStatus Status { get; set; }
        = CustomerStatus.Active;

    public Account Account { get; set; } = null!;
}