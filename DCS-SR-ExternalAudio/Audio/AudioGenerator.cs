using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using FragLabs.Audio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.ExternalAudioClient.Audio
{
    public class AudioGenerator
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string path;

        public static readonly int INPUT_SAMPLE_RATE = 16000;
        public static readonly int INPUT_AUDIO_LENGTH_MS = 40;

        public static readonly int SEGMENT_FRAMES = (INPUT_SAMPLE_RATE / 1000) * INPUT_AUDIO_LENGTH_MS
            ; //640 is 40ms as INPUT_SAMPLE_RATE / 1000 *40 = 640

        private readonly float volume;
        private readonly VoiceGender SpeakerGender;
        private string SpeakerCulture;

        public AudioGenerator(string path, float volume, string SpeakerGender, string SpeakerCulture)
        {
            this.path = path;
            this.volume = volume;
            if (SpeakerGender.ToLower() == "male") {
                this.SpeakerGender = VoiceGender.Male;
            } 
            else if (SpeakerGender.ToLower() == "neutral")
            {
                this.SpeakerGender = VoiceGender.Neutral;
            }
            else
            {
                this.SpeakerGender = VoiceGender.Female;
            }

            this.SpeakerCulture = SpeakerCulture;

        }

        public VoiceGender SpeakerGender { get; set; }

        private byte[] GoogleTTS(string msg)
        {
            try
            {
                TextToSpeechClientBuilder builder = new TextToSpeechClientBuilder
                {
                    CredentialsPath = opts.googleCredentials
                };

                TextToSpeechClient client = builder.Build();

                SynthesisInput input = new SynthesisInput
                {
                    Text = msg
                };

                VoiceSelectionParams voice = null;

                if (!string.IsNullOrEmpty(opts.voice))
                {
                    voice = new VoiceSelectionParams()
                    {
                        Name = opts.voice,
                        LanguageCode = opts.voice.Substring(0,5),
                    };
                }
                else
                {
                    voice = new VoiceSelectionParams
                    {
                        LanguageCode = opts.culture,
                    };

                    switch (opts.gender.ToLowerInvariant().Trim())
                    {
                        case "male":
                            voice.SsmlGender = SsmlVoiceGender.Male;
                            break;
                        case "neutral":
                            voice.SsmlGender = SsmlVoiceGender.Neutral;
                            break;
                        case "female":
                            voice.SsmlGender = SsmlVoiceGender.Female;
                            break;
                        default:
                            voice.SsmlGender = SsmlVoiceGender.Male;
                            break;
                    }
                }

                AudioConfig config = new AudioConfig
                {
                    AudioEncoding = AudioEncoding.Linear16,
                    SampleRateHertz = INPUT_SAMPLE_RATE
                };

                var response = client.SynthesizeSpeech(new SynthesizeSpeechRequest
                {
                    Input = input,
                    Voice = voice,
                    AudioConfig = config
                });

                var tempFile = Path.GetTempFileName();

                using (var stream = File.Create(tempFile))
                {
                    response.AudioContent.WriteTo(stream);
                }

                byte[] bytes = null;
                using (var reader = new WaveFileReader(tempFile))
                {
                    bytes = new byte[reader.Length];
                    var read = reader.Read(bytes, 0, bytes.Length);
                    Logger.Info($"Success with Google TTS - read {read} bytes");
                }

                //cleanup
                File.Delete(tempFile);

                return bytes;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error with Google Text to Speech");
            }
            return new byte[0];
        }
    

        private byte[] TextToSpeech()
        {
            try
            {
                using (var synth = new SpeechSynthesizer())
                using (var stream = new MemoryStream())
                {
                    bool isVoiceInstalled = false;
                        foreach (var voice in synth.GetInstalledVoices())
                    {
                        var info = voice.VoiceInfo;
                        if (this.SpeakerCulture == info.Culture.ToString())
                        {
                            isVoiceInstalled = true;
                            break;
                        }
                    }
                    if (!isVoiceInstalled) this.SpeakerCulture = "en-GB";

                    synth.SelectVoiceByHints(this.SpeakerGender, VoiceAge.Adult, 0, new CultureInfo(this.SpeakerCulture, false));
                    synth.Rate = 1;

                    var intVol = (int)(volume * 100.0);

                    if (intVol > 100)
                    {
                        intVol = 100;
                    }

                    synth.Volume = intVol;
                    
                    synth.SetOutputToAudioStream(stream,
                        new SpeechAudioFormatInfo(INPUT_SAMPLE_RATE, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                    
                    synth.Speak(path);

                    return stream.ToArray();
                   
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error with Text to Speech");
            }
             return new byte[0];
        }

        private IWaveProvider GetMP3WaveProvider()
        {
            Logger.Info($"Reading MP3 @ {path}");

            var mp3Reader = new Mp3FileReader(path);
            int bytes = (int)mp3Reader.Length;
            byte[] buffer = new byte[bytes];

            Logger.Info($"Read MP3 @ {mp3Reader.WaveFormat}");

            if (mp3Reader.WaveFormat.SampleRate < INPUT_SAMPLE_RATE)
            {
                Logger.Error($"MP3 Sample rate must be at least 16000 but is {mp3Reader.WaveFormat.SampleRate} - Quitting. Use Audacity or another tool to resample as 16000 or Higher");
                Environment.Exit(1);
            }

            int read = mp3Reader.Read(buffer, 0, (int)bytes);
            BufferedWaveProvider bufferedWaveProvider = new BufferedWaveProvider(mp3Reader.WaveFormat)
            {
                BufferLength = read * 2, ReadFully = false, DiscardOnBufferOverflow = true
            };

            bufferedWaveProvider.AddSamples(buffer, 0, read);
            VolumeSampleProvider volumeSample =
                new VolumeSampleProvider(bufferedWaveProvider.ToSampleProvider()) {Volume = volume};

            mp3Reader.Close();
            mp3Reader.Dispose();

            Logger.Info($"Convert to Mono 16bit PCM");

            //after this we've got 16 bit PCM Mono  - just need to sort sample rate
            return volumeSample.ToMono().ToWaveProvider16(); 
        }

        private byte[] GetMP3Bytes()
        {
            List<byte> resampledBytesList = new List<byte>();
            var waveProvider = GetMP3WaveProvider();

            Logger.Info($"Convert to Mono 16bit PCM 16000KHz from {waveProvider.WaveFormat}");
            //loop thorough in up to 1 second chunks
            var resample = new EventDrivenResampler(waveProvider.WaveFormat, new WaveFormat(INPUT_SAMPLE_RATE, 1));

            byte[] buffer = new byte[waveProvider.WaveFormat.AverageBytesPerSecond * 2];

            int read = 0;
            while ((read = waveProvider.Read(buffer, 0, waveProvider.WaveFormat.AverageBytesPerSecond)) > 0)
            {
                //resample as we go
                resampledBytesList.AddRange(resample.ResampleBytes(buffer, read));
            }

            Logger.Info($"Converted to Mono 16bit PCM 16000KHz from {waveProvider.WaveFormat}");

            return resampledBytesList.ToArray();
        }

        public List<byte[]> GetOpusBytes()
        {
            List<byte[]> opusBytes = new List<byte[]>();

            byte[] resampledBytes;

            if (path.ToLower().EndsWith(".mp3"))
            {
                Logger.Info($"Reading MP3 it looks like a file");
                resampledBytes = GetMP3Bytes();
            }
            else
            {
                Logger.Info($"Doing Text To Speech as its not an MP3 path");
                resampledBytes = TextToSpeech();
            }

            Logger.Info($"Encode as Opus");
            var _encoder = OpusEncoder.Create(INPUT_SAMPLE_RATE, 1, FragLabs.Audio.Codecs.Opus.Application.Voip);

            int pos = 0;
            while (pos +(SEGMENT_FRAMES*2) < resampledBytes.Length)
            {
                
                byte[] buf = new byte[SEGMENT_FRAMES * 2];
                Buffer.BlockCopy(resampledBytes, pos,buf,0,SEGMENT_FRAMES*2);

                var outLength = 0;
                var frame = _encoder.Encode(buf, buf.Length, out outLength);

                if (outLength > 0)
                {
                    //create copy with small buffer
                    var encoded = new byte[outLength];

                    Buffer.BlockCopy(frame, 0, encoded, 0, outLength);

                    opusBytes.Add(encoded);
                }

                pos += (SEGMENT_FRAMES * 2);
            }
            
            if (pos+1 < resampledBytes.Length)
            {
                //last bit - less than 40 ms
                byte[] buf = new byte[SEGMENT_FRAMES * 2];
                Buffer.BlockCopy(resampledBytes, pos, buf, 0, resampledBytes.Length - pos);

                var outLength = 0;
                var frame = _encoder.Encode(buf, buf.Length, out outLength);

                if (outLength > 0)
                {
                    //create copy with small buffer
                    var encoded = new byte[outLength];

                    Buffer.BlockCopy(frame, 0, encoded, 0, outLength);

                    opusBytes.Add(encoded);
                }

            }
            
            _encoder.Dispose();
            Logger.Info($"Finished encoding as Opus");

            return opusBytes;
        }
    }
}
