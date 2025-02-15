using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.SettingPages
{
    public partial class RadioEffectsPage : Page
    {
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        
        private readonly Brush _enabledBrush = Brushes.MediumSeaGreen;
        private readonly Brush _disabledBrush = Brushes.IndianRed;
        public RadioEffectsPage()
        {
            InitializeComponent();
            
            TxStart.Background = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start) ? _enabledBrush : _disabledBrush;
            TxStartText.Text = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start) ? "On" : "Off";
            TxEnd.Background = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_End) ? _enabledBrush : _disabledBrush;
            TxEndText.Text = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start) ? "On" : "Off";
            RxStart.Background = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_Start) ? _enabledBrush : _disabledBrush;
            RxStartText.Text = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start) ? "On" : "Off";
            RxEnd.Background = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_End) ? _enabledBrush : _disabledBrush;
            RxEndText.Text = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start) ? "On" : "Off";
            
            RadioEndTransmitEffect.IsEnabled = false;
            RadioEndTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.RadioTransmissionEnd;
            RadioEndTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedRadioTransmissionEndEffect;
            RadioEndTransmitEffect.IsEnabled = true;

            RadioStartTransmitEffect.IsEnabled = false;
            RadioStartTransmitEffect.SelectedIndex = 0;
            RadioStartTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.RadioTransmissionStart;
            RadioStartTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedRadioTransmissionStartEffect;
            RadioStartTransmitEffect.IsEnabled = true;

            IntercomStartTransmitEffect.IsEnabled = false;
            IntercomStartTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.IntercomTransmissionStart;
            IntercomStartTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedIntercomTransmissionStartEffect;
            IntercomStartTransmitEffect.IsEnabled = true;

            IntercomEndTransmitEffect.IsEnabled = false;
            IntercomEndTransmitEffect.SelectedIndex = 0;
            IntercomEndTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.IntercomTransmissionEnd;
            IntercomEndTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedIntercomTransmissionEndEffect;
            IntercomEndTransmitEffect.IsEnabled = true;
        }

        private void TxEnd_OnClick(object sender, RoutedEventArgs e)
        {
            var enabled = !_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_End);
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_End, enabled);
            TxEnd.Background = enabled ? _enabledBrush : _disabledBrush;
            TxEndText.Text = enabled ? "On" : "Off";
        }

        private void TxStart_OnClick(object sender, RoutedEventArgs e)
        {
            var enabled = !_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start);
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start, enabled);
            TxStart.Background = enabled ? _enabledBrush : _disabledBrush;
            TxStartText.Text = enabled ? "On" : "Off";
        }

        private void RxStart_OnClick(object sender, RoutedEventArgs e)
        {
            var enabled = !_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_Start);
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_Start, enabled);
            RxStart.Background = enabled ? _enabledBrush : _disabledBrush;
            RxStartText.Text = enabled ? "On" : "Off";
        }

        private void RxEnd_OnClick(object sender, RoutedEventArgs e)
        {
            var enabled = !_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_End);
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_End, enabled);
            RxEnd.Background = enabled ? _enabledBrush : _disabledBrush;
            RxEndText.Text = enabled ? "On" : "Off";
        }
    }
}