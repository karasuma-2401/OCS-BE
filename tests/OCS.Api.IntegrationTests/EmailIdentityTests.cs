using OCS.Api.Common;

namespace OCS.Api.IntegrationTests;

public sealed class EmailIdentityTests
{
    [Theory]
    [InlineData(" User.Name+tag@Example.com \t", "USER.NAME+TAG@EXAMPLE.COM")]
    [InlineData("a@xn--bcher-kva.example", "A@XN--BCHER-KVA.EXAMPLE")]
    [InlineData("\v\fa@example.com\r\n", "A@EXAMPLE.COM")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-an-email", null)]
    [InlineData("a@@example.com", null)]
    [InlineData("a..b@example.com", null)]
    [InlineData(".a@example.com", null)]
    [InlineData("a@-example.com", null)]
    [InlineData("a@localhost", null)]
    [InlineData("Name <a@example.com>", null)]
    [InlineData("nguyễn@example.com", null)]
    [InlineData("a@example.com\u00a0", null)]
    public void NormalizationPreservesIdentityAndRejectsUnsupportedAddresses(string? email, string? expected)
        => Assert.Equal(expected, EmailIdentity.Normalize(email));

    [Fact]
    public void LocalPartAndDomainLabelHaveExplicitBounds()
    {
        Assert.NotNull(EmailIdentity.Normalize(new string('a', 64) + "@example.com"));
        Assert.Null(EmailIdentity.Normalize(new string('a', 65) + "@example.com"));
        Assert.NotNull(EmailIdentity.Normalize("a@" + new string('a', 63) + ".com"));
        Assert.Null(EmailIdentity.Normalize("a@" + new string('a', 64) + ".com"));
    }
}
