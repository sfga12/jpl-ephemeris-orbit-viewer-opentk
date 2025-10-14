using System;

namespace JplEphemerisOrbitViewer
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using var scene = new Scene(1280, 720);
            scene.Run();
        }
    }
}
