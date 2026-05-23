using System;
using System.Collections.Generic;
using Grpc.Core;
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

        // -----------------------------------------------------------------------
        // gRPC auth / service tests — use the internal constructor
        // -----------------------------------------------------------------------

        private Mock<IAuthServiceClient> _authMock;
        private Mock<ISrsServiceClient> _srsMock;

        private VcsClientSyncHandler BuildGrpcHandler()
        {
            _authMock = new Mock<IAuthServiceClient>();
            _srsMock = new Mock<ISrsServiceClient>();
            return new VcsClientSyncHandler(
                (type, msg) => _lastUpdateType = type,
                _authMock.Object,
                _srsMock.Object);
        }

        [TestMethod]
        public void DiscoverFlows_Success_ReturnsFlows()
        {
            var handler = BuildGrpcHandler();
            var expected = new FlowDiscoveryResponse { Success = true };
            expected.Result = new FlowDiscoveryResult();
            expected.Result.Flows.Add(new AuthFlowDefinition { FlowId = "vanguard_email_password", Description = "Email + Password" });
            _authMock.Setup(a => a.DiscoverAuthenticationFlows(
                It.Is<FlowDiscoveryRequest>(r => r.AuthenticationPlugin == "profile-vanguard"),
                It.IsAny<CallOptions>()))
                .Returns(expected);

            var result = handler.DiscoverAuthenticationFlows("profile-vanguard");

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Flows.Count);
            Assert.AreEqual("vanguard_email_password", result.Flows[0].FlowId);
        }

        [TestMethod]
        public void DiscoverFlows_Timeout_ReturnsNull_AndPublishesConnectionError()
        {
            var handler = BuildGrpcHandler();
            _authMock.Setup(a => a.DiscoverAuthenticationFlows(It.IsAny<FlowDiscoveryRequest>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

            var result = handler.DiscoverAuthenticationFlows("profile-vanguard");

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.ConnectionError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_Success_ReturnsResponse()
        {
            var handler = BuildGrpcHandler();
            var loginResult = new LoginResult { PlayerName = "Pilot1", Secret = "abc" };
            var authResponse = new AuthStepResponse { Success = true, Complete = loginResult };
            _authMock.Setup(a => a.ContinueAuth(
                It.Is<ContinueAuthRequest>(r => r.SessionId == "sess1"),
                It.IsAny<CallOptions>()))
                .Returns(authResponse);

            var result = handler.ContinueAuth("sess1", new Dictionary<string, string> { { "otp", "123456" } });

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
        }

        [TestMethod]
        public void ContinueAuth_Failure_ReturnsNull_AndPublishesLoginError()
        {
            var handler = BuildGrpcHandler();
            var authResponse = new AuthStepResponse { Success = false, ErrorMessage = "bad credentials" };
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Returns(authResponse);

            var result = handler.ContinueAuth("sess1", new Dictionary<string, string>());

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.InternalLoginError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_Timeout_ReturnsNull_AndPublishesLoginError()
        {
            var handler = BuildGrpcHandler();
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

            var result = handler.ContinueAuth("sess1", new Dictionary<string, string>());

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.InternalLoginError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_RpcError_ReturnsNull_AndPublishesLoginError()
        {
            var handler = BuildGrpcHandler();
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.Internal, "server error")));

            var result = handler.ContinueAuth("sess1", new Dictionary<string, string>());

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.InternalLoginError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_Complete_PublishesInternalLoginSuccess()
        {
            var handler = BuildGrpcHandler();
            var loginResult = new LoginResult { PlayerName = "Pilot1", Secret = "mysecret" };
            var authResponse = new AuthStepResponse { Success = true, Complete = loginResult };
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Returns(authResponse);

            handler.ContinueAuth("sess1", new Dictionary<string, string>());

            Assert.AreEqual(VcsUiUpdateType.InternalLoginSuccess, _lastUpdateType);
        }

        [TestMethod]
        public void UpdateClientInfo_Success_ReturnsTrue()
        {
            var handler = BuildGrpcHandler();
            _srsMock.Setup(s => s.UpdateClientInfo(It.IsAny<ClientInfo>(), It.IsAny<CallOptions>()))
                .Returns(new ServerResponse { Success = true });

            var result = handler.UpdateClientInfo("Pilot1", "Blue", "unit-42", 1);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void UpdateClientInfo_Failure_ReturnsFalse()
        {
            var handler = BuildGrpcHandler();
            _srsMock.Setup(s => s.UpdateClientInfo(It.IsAny<ClientInfo>(), It.IsAny<CallOptions>()))
                .Returns(new ServerResponse { Success = false, ErrorMessage = "not authenticated" });

            var result = handler.UpdateClientInfo("Pilot1", "Blue", "unit-42", 1);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void UpdateClientInfo_Timeout_ReturnsFalse()
        {
            var handler = BuildGrpcHandler();
            _srsMock.Setup(s => s.UpdateClientInfo(It.IsAny<ClientInfo>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

            var result = handler.UpdateClientInfo("Pilot1", "Blue", "unit-42", 1);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void UpdateClientInfo_RpcError_ReturnsFalse()
        {
            var handler = BuildGrpcHandler();
            _srsMock.Setup(s => s.UpdateClientInfo(It.IsAny<ClientInfo>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.Internal, "server error")));

            var result = handler.UpdateClientInfo("Pilot1", "Blue", "unit-42", 1);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void FetchServerSettings_Success_PublishesServerSettingsFetched()
        {
            var handler = BuildGrpcHandler();
            var settings = new ServerSettings();
            settings.TestFrequencies.Add(121.5f);
            _srsMock.Setup(s => s.GetServerSettings(It.IsAny<Empty>(), It.IsAny<CallOptions>()))
                .Returns(settings);

            handler.FetchServerSettings();

            Assert.AreEqual(VcsUiUpdateType.ServerSettingsFetched, _lastUpdateType);
        }

        [TestMethod]
        public void FetchServerSettings_Timeout_PublishesServerSettingsError()
        {
            var handler = BuildGrpcHandler();
            _srsMock.Setup(s => s.GetServerSettings(It.IsAny<Empty>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

            handler.FetchServerSettings();

            Assert.AreEqual(VcsUiUpdateType.ServerSettingsError, _lastUpdateType);
        }

        [TestMethod]
        public void FetchServerSettings_RpcError_PublishesServerSettingsError()
        {
            var handler = BuildGrpcHandler();
            _srsMock.Setup(s => s.GetServerSettings(It.IsAny<Empty>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.Internal, "server error")));

            handler.FetchServerSettings();

            Assert.AreEqual(VcsUiUpdateType.ServerSettingsError, _lastUpdateType);
        }
    }
}
