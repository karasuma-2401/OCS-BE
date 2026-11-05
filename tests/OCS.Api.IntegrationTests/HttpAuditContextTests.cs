using Microsoft.AspNetCore.Http;
using OCS.Api.Auditing;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class HttpAuditContextTests
{
    [Fact]
    public void SourceUsesStableActionInsteadOfLongAccountPath()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/accounts/00000000-0000-0000-0000-000000000001/lock-status";
        context.Request.RouteValues["controller"] = "Accounts";
        context.Request.RouteValues["action"] = "ChangeLockStatus";
        var audit = new HttpAuditContext(
            new HttpContextAccessor { HttpContext = context }, new SystemAuditContext());

        Assert.Equal("Accounts.ChangeLockStatus", audit.Source);
        Assert.True(audit.Source.Length <= 50);
    }
}
