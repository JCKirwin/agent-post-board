namespace AgentPostBoard.Demo;

/// <summary>
/// The role a demo process plays when launched with <c>dotnet run -- role=&lt;role&gt;</c>.
/// Three friends post their board-game session results; one reader tails the board
/// and prints a running leaderboard.
/// </summary>
public enum DemoRole
{
    /// <summary>Posts session results as the first friend.</summary>
    FriendOne,

    /// <summary>Posts session results as the second friend.</summary>
    FriendTwo,

    /// <summary>Posts session results as the third friend.</summary>
    FriendThree,

    /// <summary>Tails the board and renders a running leaderboard.</summary>
    Reader,
}
