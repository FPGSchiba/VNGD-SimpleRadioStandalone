using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences
{
    public class CsvAddressStore : ISavedAddressStore
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string _fileNameAndPath;

        public CsvAddressStore()
        {
            _fileNameAndPath = Path.Combine(Environment.CurrentDirectory, "SavedAddresses.csv");
        }

        public IEnumerable<AddressSetting> LoadFromStore()
        {
            try
            {
                if (File.Exists(_fileNameAndPath))
                {
                    return ReadFile();
                }
            }
            catch (Exception exception)
            {
                var message = $"Failed to load settings: {exception}";
                Logger.Error(exception, message);
                System.Windows.MessageBox.Show(message);
            }
            return Enumerable.Empty<AddressSetting>();
        }

        public void SaveToStore(IEnumerable<AddressSetting> savedAddresses)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var savedAddress in savedAddresses)
                {
                    sb.AppendLine($"{savedAddress.Name},{savedAddress.Address},{savedAddress.IsDefault}");
                }
                File.WriteAllText(_fileNameAndPath, sb.ToString());
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save preferences");
            }
        }

        private IEnumerable<AddressSetting> ReadFile()
        {
            var allLines = File.ReadAllLines(_fileNameAndPath);
            IList<AddressSetting> savedAddresses = new List<AddressSetting>();

            foreach (var line in allLines)
            {
                try
                {
                    var address = Parse(line);
                    savedAddresses.Add(address);
                }
                catch (Exception ex)
                {
                    var message = $"Failed to parse saved address from csv, text: {line}";
                    Logger.Error(ex, message);
                }
            }

            return savedAddresses;
        }

        private AddressSetting Parse(string line)
        {
            var split = line.Split(',');
            if (split.Length == 3)
            {
                bool isDefault;

                if (bool.TryParse(split[2], out isDefault))
                {
                    return new AddressSetting(split[0], split[1], isDefault);
                }
                throw new ArgumentException("isDefault parameter cannot be cast to a boolean");
            }
            throw new ArgumentOutOfRangeException(nameof(line), @"address can only be 3 segments");
        }
    }
}