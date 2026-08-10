using VIP1132.Services;

namespace VIP1132;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--zoom-helper", StringComparer.OrdinalIgnoreCase))
            return RunZoomHelper(args);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunZoomHelper(IReadOnlyList<string> args)
    {
        try
        {
            return ZoomHelper.RunAsync(GetArgument(args, "--report")).GetAwaiter().GetResult();
        }
        catch
        {
            return 4;
        }
    }

    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
