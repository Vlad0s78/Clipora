namespace Clipora.Shell;

public enum CliporaLaunchAction
{
    None,
    Open,
    Compress,
}

public enum CliporaCommandLineError
{
    None,
    InvalidArguments,
    InvalidPath,
}

public sealed record CliporaLaunchRequest(CliporaLaunchAction Action, string? VideoPath)
{
    public static CliporaLaunchRequest Empty { get; } = new(CliporaLaunchAction.None, null);
}

public static class CliporaCommandLine
{
    private const string CompressOption = "--compress";

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out CliporaLaunchRequest request,
        out CliporaCommandLineError error)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            request = CliporaLaunchRequest.Empty;
            error = CliporaCommandLineError.None;
            return true;
        }

        CliporaLaunchAction action;
        string path;
        if (arguments.Count == 1 &&
            !arguments[0].StartsWith("--", StringComparison.Ordinal))
        {
            action = CliporaLaunchAction.Open;
            path = arguments[0];
        }
        else if (arguments.Count == 2 &&
                 string.Equals(arguments[0], CompressOption, StringComparison.OrdinalIgnoreCase))
        {
            action = CliporaLaunchAction.Compress;
            path = arguments[1];
        }
        else
        {
            request = CliporaLaunchRequest.Empty;
            error = CliporaCommandLineError.InvalidArguments;
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Video path is empty.", nameof(arguments));
            }

            request = new CliporaLaunchRequest(action, Path.GetFullPath(path));
            error = CliporaCommandLineError.None;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            request = CliporaLaunchRequest.Empty;
            error = CliporaCommandLineError.InvalidPath;
            return false;
        }
    }
}
