using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Accounts;

public sealed record UpdateAccountRolesRequest([Required, MinLength(1)] List<string> Roles);
