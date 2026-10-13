using OCS.Application.Common;

namespace OCS.Application.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    public void SuccessContainsDataWithoutError()
    {
        var result = Result.Success("ocs");

        Assert.True(result.IsSuccess);
        Assert.Equal("ocs", result.Data);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FailureContainsErrorWithoutData()
    {
        var result = Result.Failure<string>("invalid");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal("invalid", result.Error);
    }
}
