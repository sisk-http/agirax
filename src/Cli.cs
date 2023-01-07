namespace Sisk.Agirax
{
    internal static class Cli
    {
        private static void LogMessage(string level, string message)
        {
            string prefix = $"{DateTime.Now:R}{level,6} : ";
            Console.Write(prefix + string.Join("\n" + prefix, message.Split("\n")) + "\n");
        }

        internal static void Log(string message)
        {
            LogMessage("info", message);
        }

        internal static void Warn(string message)
        {
            Program.startedWithWarnings = true;
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
