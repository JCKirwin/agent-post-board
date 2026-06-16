using Xunit;

namespace AgentPostBoard.Demo.Tests;

// The demo dispatches on a single `role=<role>` argument. These tests pin the mapping from
// the friendly role names the README documents to the DemoRole the process plays.
public sealed class ProgramRoleParsingTests
{
    [Theory]
    [InlineData("role=alex", DemoRole.FriendOne)]
    [InlineData("role=blair", DemoRole.FriendTwo)]
    [InlineData("role=casey", DemoRole.FriendThree)]
    [InlineData("role=reader", DemoRole.Reader)]
    [InlineData("role=READER", DemoRole.Reader)]
    public void TryParseRole_MapsKnownRoles(string arg, DemoRole expected)
    {
        var parsed = Program.TryParseRole(new[] { arg }, out var role);

        Assert.True(parsed);
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData("role=stranger")]
    [InlineData("nonsense")]
    [InlineData("")]
    public void TryParseRole_RejectsUnknownOrMissingRole(string arg)
    {
        var parsed = Program.TryParseRole(new[] { arg }, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParseRole_RejectsEmptyArgs()
    {
        var parsed = Program.TryParseRole(System.Array.Empty<string>(), out _);

        Assert.False(parsed);
    }
}
