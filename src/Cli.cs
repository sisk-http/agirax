namespace Sisk.Agirax
{
    internal static class Cli
    {
        private static void LogMessage(string level, string message)
        {
            Console.WriteLine($"{"[" + level + "]",-7} {message}");
        }

        internal static void Log(string message)
        {
            LogMessage("info", message);
        }

        internal static void Warn(string message)
        {
            LogMessage("warn", message);
        }

        internal static void Terminate()
        {
            if (Program.waitForExit)
            {
                Console.WriteLine("\nPress any key to exit. . .");
                Console.ReadKey();
            }
            Environment.Exit(0);
        }

        internal static void TerminateWithError(string error)
        {
            LogMessage("error", error);
            if (Program.waitForExit)
            {
                Console.WriteLine("\nPress any key to exit. . .");
                Console.ReadKey();
            }
            Environment.Exit(4);
        }
    }
}
