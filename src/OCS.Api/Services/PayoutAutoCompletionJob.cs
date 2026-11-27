using Hangfire;

namespace OCS.Api.Services;

public sealed class PayoutAutoCompletionJob(SettlementService settlementService, SystemAuditContext auditContext)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        auditContext.Initialize(nameof(PayoutAutoCompletionJob), Guid.NewGuid().ToString("N"));
        return settlementService.CompleteExpiredAsync(cancellationToken);
    }
}
