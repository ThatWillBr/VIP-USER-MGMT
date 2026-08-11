namespace VIP1132;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
