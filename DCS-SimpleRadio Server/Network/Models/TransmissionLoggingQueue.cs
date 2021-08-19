using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Settings;
using NLog;
using NLog.Targets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network.Models
{
    class TransmissionLoggingQueue
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private ConcurrentDictionary<SRClient, TransmissionLog> _currentTransmissionLog { get; } = new ConcurrentDictionary<SRClient, TransmissionLog>();
        private bool _stop;
        private bool _log;
        private readonly ServerSettingsStore _serverSettings = ServerSettingsStore.Instance;
        private readonly XDocument _nlogConfig = XDocument.Load("NLog.config");

        public TransmissionLoggingQueue()
        {
            _log = _serverSettings.GetGeneralSetting(ServerSettingsKeys.TRANSMISSION_LOG_ENABLED).BoolValue;
            _stop = false;
        }
        
        //public int FileArchivePeriod { get {return _nlogConfig.Descendants(_nlogConfig.Root.GetDefaultNamespace() + "target")

        public void LogTransmission(SRClient client)
        {
            if (!_stop)
            {
                _currentTransmissionLog.AddOrUpdate(client, new TransmissionLog(client.LastTransmissionReceived, client.TransmittingFrequency),
                   (k, v) => UpdateTransmission(client, v));
            }
        }

        private TransmissionLog UpdateTransmission(SRClient client, TransmissionLog log)
        {
            log.TransmissionEnd = client.LastTransmissionReceived;
            return log;
        }

        public void Start()
        {
            new Thread(LogCompleteTransmissions).Start();
        }

        public void Stop()
        {
            _stop = true;
                        
        }

        public void UpdateArchivePeriod(string days)
        {
            FileTarget target = (FileTarget)LogManager.Configuration.FindTargetByName("asyncTransmissionFileTarget");
            Console.WriteLine(target.ArchiveEvery);
            target.MaxArchiveFiles = int.Parse(days);
            LogManager.ReconfigExistingLoggers();
            Console.WriteLine(target.ArchiveEvery);
        }

        private void LogCompleteTransmissions()
        {
            while(!_stop)
            {

                if (_log != !_serverSettings.GetGeneralSetting(ServerSettingsKeys.TRANSMISSION_LOG_ENABLED).BoolValue)
                {
                    _log = !_serverSettings.GetGeneralSetting(ServerSettingsKeys.TRANSMISSION_LOG_ENABLED).BoolValue;
                    string newSetting = _log ? "TRANSMISSION LOGGING ENABLED" : "TRANSMISSION LOGGING DISABLED";

                    Logger.Info($"{newSetting}");
                }

                if(_log && !_currentTransmissionLog.IsEmpty)
                {
                    foreach (KeyValuePair<SRClient, TransmissionLog> LoggedTransmission in _currentTransmissionLog)
                    {
                        if (LoggedTransmission.Value.IsComplete())
                        {
                            if (_currentTransmissionLog.TryRemove(LoggedTransmission.Key, out TransmissionLog completedLog))
                            {
                                Logger.Info($"{LoggedTransmission.Key.ClientGuid}, {LoggedTransmission.Key.Name}, " +
                                    $"{LoggedTransmission.Key.Coalition}, {LoggedTransmission.Value.TransmissionFrequency}. " +
                                    $"{completedLog.TransmissionStart}, {completedLog.TransmissionEnd}, {LoggedTransmission.Key.VoipPort}");
                            }
                        }
                    }
                }
            }
        }
    }
}
