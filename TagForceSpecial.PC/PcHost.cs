using TagForceSpecial.PC.Game;

namespace TagForceSpecial.PC;

internal static class PcHost
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var options = HostOptions.Parse(args);
            if (options.OpenImporterOnLaunch)
            {
                Setup.WorkbenchLauncher.Start(options.DataRoot);
                return 0;
            }
            using var game = new GameLoop(options);
            game.Run();
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(error is IOException or UnauthorizedAccessException
                ? "Local application data could not be accessed. Check storage access and free space." : error.Message);
            return 1;
        }
    }
}
