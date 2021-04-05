using System;
using System.Collections.Generic;
using System.Globalization;
using System.Speech.Synthesis;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
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

        public class Options
        {
            [Option('i',"file",
                SetName = "file",
                HelpText = "Full path to MP3 or Ogg - File must end .mp3 or .ogg",
                Required = true )]
            public string file { get; set; }

            [Option('t',"text",
                HelpText = "Text to say",
                SetName = "TTS",
                Required = true)]
            public string text { get; set; }

            [Option('I',"textFile",
                SetName = "textFile",
                HelpText = "Path to text file for TTS ",
                Required = true)]
            public string textFile { get; set; }

            [Option('f',"freqs",
                HelpText = "Frequency in MHz comma separated - 251.0,252.0 or just 252.0 ",
                Required = true)]
            public string freqs { get; set; }


            [Option('m',"modulations",
                HelpText = "Modulation AM or FM comma separated - AM,FM or just AM  ",
                Required = true)]
            public string modulations { get; set; }


            [Option('c',"coalition",
                HelpText = "Coalition - 0 is Spectator, 1 is Red, 2 is Blue",
                Required = true)]
            public int coalition { get; set; }

            [Option('s',"speed",
                Default = 1,
                HelpText = "Speed - 1 is normal -10 to 10 is the range",
                Required = false)]
            public int speed { get; set; }

            [Option('p',"port",
                HelpText = "Port - 5002 is the default",
                Default = 5002,
                Required = false)]
            public int port { get; set; }

            [Option('n',"name",
                HelpText = "Name - name of your transmitter - no spaces",
                Default = "DCS-STTS",
                Required = false)]
            public string name { get; set; }

            [Option('v',"volume",
                HelpText = "Volume - 1.0 is max, 0.0 is silence",
                Default = 1.0f,
                Required = false)]
            public float volume { get; set; }

            [Option('l',"culture",
                HelpText = "TTS culture - local for the voice",
                Required = false,
                Default = "en-GB")]
            public string culture { get; set; }

            [Option('g',"gender",
                HelpText = "TTS Gender - male/female",
                Required = false,
                Default = "female")]
            public string gender { get; set; }

            [Option('V',"voice",
                HelpText = "The voice NAME - see the list from --help or if using google see: https://cloud.google.com/text-to-speech/docs/voices ",
                Required = false)]
            public string voice { get; set; }

            [Option('h',"minimise",
                HelpText = "Minimise the command line window on run",
                Required = false,
                Default = false)]
            public bool minimise { get; set; }

            [Option('G',"googleCredentials",
                HelpText = "Full path to Google JSON Credentials file - see https://cloud.google.com/text-to-speech/docs/quickstart-client-libraries",
                Required = false)]
            public string googleCredentials { get; set; }
        }
        public static void Main(string[] args)
        {
            if ((args.Length < 7) || (args.Length > 9 ))
            {
                CultureInfo[] Cultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures);
                string availableCultures = "";
                var synthesizer = new SpeechSynthesizer();
                foreach (var voice in synthesizer.GetInstalledVoices())
                {
                    var info = voice.VoiceInfo;
                    availableCultures += info.Culture+"/"+info.Gender + " ";
                }

                Console.WriteLine("Error incorrect parameters - should be path or text frequency modulation coalition port name volume");
                Console.WriteLine("Example: \"C:\\FULL\\PATH\\TO\\File.mp3\" 251.0 AM 1 5002 ciribob-robot 0.5");
                Console.WriteLine("Example: \"I want this read out over this frequency - hello world! \" 251.0 AM 1 5002 ciribob-robot 0.5");
                Console.WriteLine("Example: \"I want this read out over TWO frequencies - hello world! \" 251.0,252.0 AM,AM 1 5002 ciribob-robot 0.5");
                Console.WriteLine("Path or Text - either a full path ending with .mp3 or text to read out");
                Console.WriteLine("Frequency in MHz comma separated - 251.0,252.0 or just 252.0  ");
                Console.WriteLine("Modulation AM or FM comma separated - AM,FM or just AM  ");
                Console.WriteLine("Coalition - 0 is Spectator, 1 is Red, 2 is Blue");
                Console.WriteLine("Port - 5002 is the default");
                Console.WriteLine("Name - name of your transmitter - no spaces");
                Console.WriteLine("Volume - 1.0 is max, 0.0 is silence");
                Console.WriteLine("(optional) TTS Gender - male/female");
                Console.WriteLine("(optional) TTS culture - local for the voice");
                Console.WriteLine("currently installed Voices on this system: " + availableCultures);

            }
            else
            {
                ConfigureLogging();

                string mp3 = args[0].Trim();
                string freqs = args[1].Trim();
                string modulations = args[2].Trim().ToUpperInvariant();
                int coalition = int.Parse(args[3].Trim());
                int port = int.Parse(args[4].Trim());
                string name = args[5].Trim();
                float volume = float.Parse(args[6].Trim(), CultureInfo.InvariantCulture);
                string gender = "female";
                string culture = "en-GB";
                if (args.Length > 7)
                {
                    gender = args[7].Trim().ToLowerInvariant();
                }
            }

            if (modulation.Count != freqDouble.Count)
            {
                Console.WriteLine($"Number of frequencies ({freqDouble.Count}) does not match number of modulations ({modulation.Count}) - They must match!" +
                                  $"\n\nFor example: --freq=251.0,252.0 --modulations=AM,AM ");
                Console.WriteLine("QUITTING!");
            }

            ExternalAudioClient client = new ExternalAudioClient(freqDouble.ToArray(), modulation.ToArray(), opts);
            client.Start();
        }

        private static void HandleParseError(IEnumerable errs)
        {
        
            Console.WriteLine("");
            Console.WriteLine("Example:\n --file=\"C:\\FULL\\PATH\\TO\\File.mp3\" --freqs=251.0 --modulations=AM --coalition=1 --port=5002 --name=\"ciribob-robot\" --volume=0.5");
            Console.WriteLine("Example:\n --text=\"I want this read out over this frequency - hello world! \" --freqs=251.0 --modulations=AM --coalition=1 --port=5002 --name=\"ciribob-robot\" --volume=0.5");
            Console.WriteLine("Example:\n --text=\"I want this read out over TWO frequencies - hello world! \" --freqs=251.0,252.0 --modulations=AM,AM --coalition=1 --port=5002 --name=\"ciribob-robot\" --volume=0.5");

            Console.WriteLine("Example:\n --textFile=\"C:\\FULL\\PATH\\TO\\FILE_TO_READ.txt\"--freqs=251.0,252.0 --modulations=AM,AM --coalition=1 --port=5002 --name=\"ciribob-robot\" --volume=0.5");
            Console.WriteLine("");
            Console.WriteLine("Currently compatible voices on this system: \n");
            var synthesizer = new SpeechSynthesizer();
            foreach (var voice in synthesizer.GetInstalledVoices())
            {
                if (voice.Enabled)

                {
                    culture = args[8].Trim();
                }

                //process freqs
                var freqStr = freqs.Split(',');

                List<double> freqDouble = new List<double>();
                foreach (var s in freqStr)
                {
                    freqDouble.Add(double.Parse(s, CultureInfo.InvariantCulture) * 1000000d);
                }

                var modulationStr = modulations.Split(',');

                List<RadioInformation.Modulation> modulation = new List<RadioInformation.Modulation>();
                foreach (var s in modulationStr)
                {
                    RadioInformation.Modulation mod;
                    if (RadioInformation.Modulation.TryParse(s.Trim().ToUpper(), out mod))
                    {
                        modulation.Add(mod);
                    }
                }

                if (modulation.Count != freqDouble.Count)
                {
                    Console.WriteLine($"Number of frequencies ({freqDouble.Count}) does not match number of modulations ({modulation.Count}) - They must match!" +
                                      $"\n\nFor example: 251.0,252.0 AM,AM ");
                    Console.WriteLine("QUITTING!");
                }
                else
                {

                    ExternalAudioClient client = new ExternalAudioClient(mp3, freqDouble.ToArray(), modulation.ToArray(), coalition, port, name, volume,gender,culture);
                    client.Start();
                }

            }
        }
    }
}
