namespace Whispy;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single instance: a second launch just exits quietly.
        using var mutex = new Mutex(initiallyOwned: true, "WhispyWindowsSingleInstance", out bool isNew);
        if (!isNew) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new TrayApplicationContext());
    }
}
