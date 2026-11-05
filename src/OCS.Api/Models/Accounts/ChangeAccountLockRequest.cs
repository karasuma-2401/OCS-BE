using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Accounts;

public sealed record ChangeAccountLockRequest([Required] bool? Locked);
