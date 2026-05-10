using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
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
    }
}
