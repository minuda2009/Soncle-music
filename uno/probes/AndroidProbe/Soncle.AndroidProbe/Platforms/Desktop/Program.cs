// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Uno.UI.Hosting;

namespace Soncle.AndroidProbe;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
