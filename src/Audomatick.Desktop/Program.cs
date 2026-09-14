using Velopack;

namespace Audomatick.Desktop;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Invariant: VelopackApp.Build().Run() MUST be invoked at the very entry point before any UI initialization
        VelopackApp.Build().Run();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
