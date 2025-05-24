using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Client.UI;
using System;
using System.Windows.Data;

namespace Vanguard.VCS.Client.Utils.ValueConverters
{
    class ConnectionStatusImageConverter : IValueConverter
    {
		private ClientStateSingleton _clientState { get; } = ClientStateSingleton.Instance;

		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			bool connected = (bool)value;
			if (connected) {
				return Images.IconConnected;
			} else if (_clientState.IsConnectionErrored)
			{
				return Images.IconDisconnectedError;
			} else
			{
				return Images.IconDisconnected;
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
