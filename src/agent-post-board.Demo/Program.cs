namespace AgentPostBoard.Demo;

/// <summary>
/// Entry point for the demo. Parses <c>role=&lt;role&gt;</c> and dispatches to one of the
/// four roles defined by <see cref="DemoRole"/>. The board lives in a temp directory
/// so the demo is self-contained.
/// </summary>
public static class Program
{
    /// <summary>Run the demo with the supplied command-line arguments.</summary>
    /// <param name="args">Process arguments. Expects a single <c>role=&lt;role&gt;</c> token.</param>
    /// <returns>Process exit code. <c>0</c> on clean shutdown.</returns>
    public static Task<int> Main(string[] args)
    {
        throw new NotImplementedException();
    }
}
