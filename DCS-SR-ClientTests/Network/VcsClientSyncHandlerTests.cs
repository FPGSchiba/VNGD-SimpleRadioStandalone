using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Singletons;

namespace Vanguard.VCS.Client.Tests.Network
{
    [TestClass]
    public class VcsClientSyncHandlerTests
    {
        private Mock<IEventBus> _eventBusMock;
        private VcsClientSyncHandler _handler;
        private VcsUiUpdateType? _lastUpdateType;

        [TestInitialize]
        public void Setup()
        {
            _eventBusMock = new Mock<IEventBus>();
            _lastUpdateType = null;
            _handler = new VcsClientSyncHandler(
                (type, msg) => _lastUpdateType = type,
                _eventBusMock.Object,
                null); // RadioStateManager not needed for these tests
            // Clear the singleton so tests don't pollute each other
            ConnectedClientsSingleton.Instance.Clear();
        }

        [TestMethod]
        public void ProcessServerUpdate_ClientJoined_PublishesClientSyncUpdate()
        {
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ClientJoined,
                ClientUpdate = new ClientUpdate
                {
                    ClientGuid = Guid.NewGuid().ToString(),
                    ClientInfo = new ClientInfo { Name = "Pilot1" },
                    RadioInfo = new RadioInfo { Muted = false },
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.AreEqual(VcsUiUpdateType.ClientSyncUpdate, _lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ClientLeft_PublishesClientSyncUpdate()
        {
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ClientLeft,
                ClientUpdate = new ClientUpdate
                {
                    ClientGuid = Guid.NewGuid().ToString(),
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.AreEqual(VcsUiUpdateType.ClientSyncUpdate, _lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ClientRadioUpdate_PublishesClientSyncUpdate()
        {
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ClientRadioUpdate,
                ClientUpdate = new ClientUpdate
                {
                    ClientGuid = Guid.NewGuid().ToString(),
                    RadioInfo = new RadioInfo { Muted = false },
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.AreEqual(VcsUiUpdateType.ClientSyncUpdate, _lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ClientInfoUpdate_PublishesClientSyncUpdate()
        {
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ClientInfoUpdate,
                ClientUpdate = new ClientUpdate
                {
                    ClientGuid = Guid.NewGuid().ToString(),
                    ClientInfo = new ClientInfo { Name = "Pilot2" },
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.AreEqual(VcsUiUpdateType.ClientSyncUpdate, _lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ServerSettingsChanged_PublishesClientSyncUpdate()
        {
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ServerSettingsChanged,
                SettingsUpdate = new ServerSettings(),
            };

            _handler.ProcessServerUpdate(update);

            Assert.AreEqual(VcsUiUpdateType.ClientSyncUpdate, _lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ServerActionKick_PublishesConnectionLost()
        {
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ServerAction,
                ServerAction = new ServerAction
                {
                    Type = ServerAction.Types.ActionType.Kick,
                    TargetClientGuid = Guid.NewGuid().ToString(),
                    Reason = "Kicked by admin",
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.AreEqual(VcsUiUpdateType.ConnectionLost, _lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ServerActionBan_PublishesConnectionLost()
        {
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ServerAction,
                ServerAction = new ServerAction
                {
                    Type = ServerAction.Types.ActionType.Ban,
                    TargetClientGuid = Guid.NewGuid().ToString(),
                    Reason = "Banned by admin",
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.AreEqual(VcsUiUpdateType.ConnectionLost, _lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ServerActionMute_DoesNotPublishConnectionLost()
        {
            _lastUpdateType = null;
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ServerAction,
                ServerAction = new ServerAction
                {
                    Type = ServerAction.Types.ActionType.Mute,
                    TargetClientGuid = Guid.NewGuid().ToString(),
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.IsNull(_lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ServerActionUnmute_DoesNotPublishConnectionLost()
        {
            _lastUpdateType = null;
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ServerAction,
                ServerAction = new ServerAction
                {
                    Type = ServerAction.Types.ActionType.Unmute,
                    TargetClientGuid = Guid.NewGuid().ToString()
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.IsNull(_lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_Unknown_DoesNotPublishAnyUpdate()
        {
            _lastUpdateType = null;
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.Unknown,
            };

            _handler.ProcessServerUpdate(update);

            Assert.IsNull(_lastUpdateType);
        }

        [TestMethod]
        public void ProcessServerUpdate_ClientJoined_WithEmptyName_StoresEmptyStringNotDashes()
        {
            var guid = Guid.NewGuid();
            var update = new ServerUpdate
            {
                Type = ServerUpdate.Types.UpdateType.ClientJoined,
                ClientUpdate = new ClientUpdate
                {
                    ClientGuid = guid.ToString(),
                    ClientInfo = new ClientInfo { Name = "" },
                    RadioInfo = new RadioInfo { Muted = false },
                }
            };

            _handler.ProcessServerUpdate(update);

            Assert.IsTrue(ConnectedClientsSingleton.Instance.TryGetValue(guid, out var client));
            Assert.AreEqual("", client.Name); // must NOT be "---"
        }
    }
}
