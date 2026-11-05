using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Accounts;

public sealed record ChangeAccountStatusRequest([Required] string Status);
