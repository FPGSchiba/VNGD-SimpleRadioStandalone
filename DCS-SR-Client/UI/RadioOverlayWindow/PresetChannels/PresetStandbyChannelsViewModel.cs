﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings.RadioChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.PresetChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow.PresetChannels
{
    public class PresetStandbyChannelsViewModel
    {
        private readonly IPresetChannelsStore _channelsStore;
        private int _radioId;

        public DelegateCommand DropDownClosedCommand { get; set; }


        private readonly object _presetChannelLock = new object();
        private ObservableCollection<PresetChannel> _presetChannels;

        public ObservableCollection<PresetChannel> PresetChannels
        {
            get { return _presetChannels; }
            set
            {
                _presetChannels = value;
                BindingOperations.EnableCollectionSynchronization(_presetChannels, _presetChannelLock);
            }
        }

        public int RadioId
        {
            private get { return _radioId; }
            set
            {
                _radioId = value;
                Reload();
            }
        }

        public PresetStandbyChannelsViewModel(IPresetChannelsStore channels, int radioId)
        {
            _radioId = radioId;
            _channelsStore = channels;
            ReloadCommand = new DelegateCommand(OnReload);
            DropDownClosedCommand = new DelegateCommand(StandbyDropDownClosed);
            PresetChannels = new ObservableCollection<PresetChannel>();
        }


        public ICommand ReloadCommand { get; }

        private void StandbyDropDownClosed(object args)
        {
            if (SelectedPresetChannel != null
                && SelectedPresetChannel.Value is Double
                && (Double)SelectedPresetChannel.Value > 0 && RadioId > 0)
            {
                var logger = LogManager.GetCurrentClassLogger();
                logger.Info("Selected Standby Channel: {0}", SelectedPresetChannel.Value);
                RadioHelper.SelectStandbyRadioChannel(SelectedPresetChannel, RadioId);
            }
        }

        public PresetChannel SelectedPresetChannel { get; set; }

        public double Max { get; set; }
        public double Min { get; set; }

        public void Reload()
        {
            PresetChannels.Clear();

            string vngdFileName = "vngd-channels";

            int i = 1;
            foreach (var channel in _channelsStore.LoadFromStore(vngdFileName))
            {
                if (((double) channel.Value) <= Max
                    && ((double) channel.Value) >= Min)
                {
                    channel.Channel = i++;
                    PresetChannels.Add(channel);
                }
            }
        }

        private void OnReload()
        {
            Reload();
        }

        public void Clear()
        {
            PresetChannels.Clear();
        }
    }
}