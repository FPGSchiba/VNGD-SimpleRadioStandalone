using System;
using System.IO;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Properties;
using NAudio.Wave;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class CachedAudioEffect
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly WaveFormat RequiredFormat = new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE,16,1);

        /** Needed for list view ***/
        public string Text
        {
            get
            {
                return FileName;
            }
        }

        /** Needed for list view ***/
        public object Value
        {
            get
            {
                return this;
            }
        }

        /** Needed for list view ***/
        public override string ToString()
        {
            return Text;
        }

        public string FileName { get; }

        public bool Loaded { get; } = false;

        public enum AudioEffectTypes
        {
            RADIO_TRANS_START = 0,
            RADIO_TRANS_END = 1,
            KY_58_TX = 2,
            KY_58_RX = 3,
            NATO_TONE=4,
            MIDS_TX = 5,
            MIDS_TX_END = 6,
            HAVEQUICK_TONE = 7,
            VHF_NOISE = 8,
            HF_NOISE = 9,
            UHF_NOISE = 10,
            FM_NOISE = 11,
        }

        public CachedAudioEffect(AudioEffectTypes audioEffect): this(audioEffect, audioEffect.ToString() + ".wav", AppDomain.CurrentDomain.BaseDirectory + "\\AudioEffects\\"+ audioEffect.ToString() + ".wav") { }

        public CachedAudioEffect(AudioEffectTypes audioEffect, string fileName, string path)
        {
            FileName = fileName;
            AudioEffectType = audioEffect;

            var file = path;

            AudioEffectBytes = new byte[0];

            if (File.Exists(file))
            {
                using (var reader = new WaveFileReader(file))
                {
                    //    Assert.AreEqual(16, reader.WaveFormat.BitsPerSample, "Only works with 16 bit audio");
                    if (reader.WaveFormat.BitsPerSample == RequiredFormat.BitsPerSample && reader.WaveFormat.SampleRate == reader.WaveFormat.SampleRate && reader.WaveFormat.Channels == 1)
                    {
                        AudioEffectBytes = new byte[reader.Length];
                        var read = reader.Read(AudioEffectBytes, 0, AudioEffectBytes.Length);
                        Logger.Info($"Read Effect {audioEffect} from {file} Successfully - Format {reader.WaveFormat}");

                        Loaded = true;
                    }
                    else
                    {
                        Logger.Info($"Unable to read Effect {audioEffect} from {file} Successfully - {reader.WaveFormat} is not {RequiredFormat} !");
                    }

                }
            }
            else
            {
                Logger.Info($"Unable to find file for effect {audioEffect} in AudioEffects\\{FileName} ");
            }
        }

        public AudioEffectTypes AudioEffectType { get; }

        public byte[] AudioEffectBytes { get; }
        public double[] AudioEffectDouble { get; set; }
    }
}