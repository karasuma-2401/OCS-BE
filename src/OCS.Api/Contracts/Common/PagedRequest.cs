using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Contracts.Common;

public sealed class PagedRequest
{
    public const int MaximumPageSize = 100;

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, MaximumPageSize)]
    public int PageSize { get; init; } = 20;

    public int Skip => (Page - 1) * PageSize;
}
