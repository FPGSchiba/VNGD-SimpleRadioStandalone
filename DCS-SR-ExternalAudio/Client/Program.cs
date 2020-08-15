using System;
using System.Globalization;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.ExternalAudioClient.Client
{
    public class Program
    {
  
        private static void ConfigureLogging()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");
            logconsole.Layout = "${longdate}|${level:uppercase=true}|${message}";
            
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);

            // Apply config           
            NLog.LogManager.Configuration = config;
        }
        public static void Main(string[] args)
        {
            if (args.Length != 6)
            {
                Console.WriteLine("Error incorrect parameters - should be path frequency modulation coalition port name");
                Console.WriteLine("Example: \"C:\\FULL\\PATH\\TO\\File.mp3\" 251.0 AM 1 5002 ciribob-robot");
            }
            else
            {
                ConfigureLogging();

                string mp3 = args[0].Trim();
                double freq = double.Parse(args[1].Trim(), CultureInfo.InvariantCulture);
                string modulation = args[2].Trim().ToUpperInvariant();
                int coalition = int.Parse(args[3].Trim());
                int port = int.Parse(args[4].Trim());
                string name = args[5].Trim();

                Client.ExternalAudioClient client = new Client.ExternalAudioClient(mp3, freq, modulation, coalition, port, name);
                client.Start();

            }
        }
    }
}