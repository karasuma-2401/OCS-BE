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
        var error = new ApplicationError("CATALOG.NOT_FOUND", "Product was not found.", ErrorType.NotFound);
        var result = Result.Failure<string>(error);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Same(error, result.Error);
    }
}
