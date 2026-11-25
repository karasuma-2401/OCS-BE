namespace OCS.Api.Services;

public sealed class PayoutAutoCompletionJob(SettlementService settlementService)
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default) =>
        settlementService.CompleteExpiredAsync(cancellationToken);
}
