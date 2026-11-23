namespace OCS.Api.Services;

public sealed class ExpiryProcessingJob(ExpiryProcessingService processing)
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default) =>
        processing.ExpireAsync(cancellationToken);
}
