using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Network.Models;

namespace Vanguard.VCS.Client.Tests.Network
{
    [TestClass]
    public class RadioStateManagerTests
    {
        [TestMethod]
        public void CreateDefaultState_ReturnsElevenRadios()
        {
            var state = RadioStateManager.CreateDefaultState();
            Assert.AreEqual(11, state.Radios.Count);
        }

        [TestMethod]
        public void LoadRadioConfig_ValidFile_LoadsRadios()
        {
            var radios = new List<object>
            {
                new { name = "Primary", frequencyHz = 127500000.0, enabled = true, isIntercom = false },
                new { name = "Intercom", frequencyHz = 0.0, enabled = true, isIntercom = true }
            };
            // RadioStateManager resolves the config file relative to AppContext.BaseDirectory
            var configPath = Path.Combine(AppContext.BaseDirectory, "radio-config.json");
            File.WriteAllText(configPath, JsonConvert.SerializeObject(radios));
            try
            {
                var state = RadioStateManager.LoadRadioConfig();
                Assert.AreEqual(2, state.Radios.Count);
                Assert.AreEqual("Primary", state.Radios[0].Name);
                Assert.IsTrue(state.Radios[1].IsIntercom);
            }
            finally
            {
                File.Delete(configPath);
            }
        }

        [TestMethod]
        public void Start_CallsRadioUpdateImmediately()
        {
            var called = false;
            var manager = new RadioStateManager(() => called = true);
            manager.Start();
            System.Threading.Thread.Sleep(300);
            manager.Stop();
            Assert.IsTrue(called);
        }

        [TestMethod]
        public void SetState_PublishesLocalRadioStateChangedEvent()
        {
            LocalRadioStateChangedEvent received = null;
            var hub = new Easy.MessageHub.MessageHub();
            var bus = new EventBus(hub);
            bus.Subscribe<LocalRadioStateChangedEvent>(e => received = e);

            var manager = new RadioStateManager(null, bus);
            var newState = RadioStateManager.CreateDefaultState();
            manager.SetState(newState);

            Assert.IsNotNull(received);
            Assert.AreSame(newState, received.State);
        }

        [TestMethod]
        public void UpdateRadioFrequency_DeltaHz_UpdatesFrequencyAndPublishesEvent()
        {
            LocalRadioStateChangedEvent received = null;
            var hub = new Easy.MessageHub.MessageHub();
            var bus = new EventBus(hub);
            bus.Subscribe<LocalRadioStateChangedEvent>(e => received = e);

            var manager = new RadioStateManager(null, bus);
            var originalFreq = manager.CurrentState.Radios[0].FrequencyHz;
            manager.UpdateRadioFrequency(1, 100.0);

            Assert.IsNotNull(received);
            Assert.AreEqual(originalFreq + 100.0, received.State.Radios[0].FrequencyHz, 0.001);
        }

        [TestMethod]
        public void SetRadioModulation_EnablesRadio_PublishesEvent()
        {
            LocalRadioStateChangedEvent received = null;
            var hub = new Easy.MessageHub.MessageHub();
            var bus = new EventBus(hub);
            bus.Subscribe<LocalRadioStateChangedEvent>(e => received = e);

            var manager = new RadioStateManager(null, bus);
            manager.SetRadioModulation(1, enabled: true, isIntercom: false);

            Assert.IsNotNull(received);
            Assert.IsTrue(received.State.Radios[0].Enabled);
            Assert.IsFalse(received.State.Radios[0].IsIntercom);
        }

        [TestMethod]
        public void SelectedRadioIndex_DefaultsToMinusOne()
        {
            var manager = new RadioStateManager(null, null);
            Assert.AreEqual(-1, manager.SelectedRadioIndex);
        }

        [TestMethod]
        public void SetUpdateCallback_ReplacesCallback()
        {
            var callCount = 0;
            var manager = new RadioStateManager(null, null);
            manager.SetUpdateCallback(() => callCount++);
            manager.Start();
            System.Threading.Thread.Sleep(200);
            manager.Stop();
            Assert.IsTrue(callCount >= 1);
        }
    }
}
