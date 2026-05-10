using System;
using Grpc.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.Tests.Network
{
    [TestClass]
    public class VcsClientSyncHandlerTests
    {
        private Mock<IAuthServiceClient> _authMock;
        private Mock<ISrsServiceClient> _srsMock;
        private VcsClientSyncHandler _handler;
        private VcsUiUpdateType? _lastUpdateType;

        [TestInitialize]
        public void Setup()
        {
            _authMock = new Mock<IAuthServiceClient>();
            _srsMock = new Mock<ISrsServiceClient>();
            _lastUpdateType = null;
            _handler = new VcsClientSyncHandler(
                (type, msg) => _lastUpdateType = type,
                _authMock.Object,
                _srsMock.Object);
        }

        [TestMethod]
        public void DiscoverFlows_Success_ReturnsFlows()
        {
            var expected = new FlowDiscoveryResponse { Success = true };
            expected.Result = new FlowDiscoveryResult();
            expected.Result.Flows.Add(new AuthFlowDefinition { FlowId = "vanguard_email_password", Description = "Email + Password" });
            _authMock.Setup(a => a.DiscoverAuthenticationFlows(
                It.Is<FlowDiscoveryRequest>(r => r.AuthenticationPlugin == "profile-vanguard"),
                It.IsAny<CallOptions>()))
                .Returns(expected);

            var result = _handler.DiscoverAuthenticationFlows("profile-vanguard");

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Flows.Count);
            Assert.AreEqual("vanguard_email_password", result.Flows[0].FlowId);
        }

        [TestMethod]
        public void DiscoverFlows_Timeout_ReturnsNull_AndPublishesConnectionError()
        {
            _authMock.Setup(a => a.DiscoverAuthenticationFlows(It.IsAny<FlowDiscoveryRequest>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

            var result = _handler.DiscoverAuthenticationFlows("profile-vanguard");

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.ConnectionError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_Success_ReturnsResponse()
        {
            var loginResult = new LoginResult { PlayerName = "Pilot1", Secret = "abc" };
            var authResponse = new AuthStepResponse { Success = true, Complete = loginResult };
            _authMock.Setup(a => a.ContinueAuth(
                It.Is<ContinueAuthRequest>(r => r.SessionId == "sess1"),
                It.IsAny<CallOptions>()))
                .Returns(authResponse);

            var result = _handler.ContinueAuth("sess1", new System.Collections.Generic.Dictionary<string, string> { { "otp", "123456" } });

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
        }

        [TestMethod]
        public void ContinueAuth_Failure_ReturnsNull_AndPublishesLoginError()
        {
            var authResponse = new AuthStepResponse { Success = false, ErrorMessage = "bad credentials" };
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Returns(authResponse);

            var result = _handler.ContinueAuth("sess1", new System.Collections.Generic.Dictionary<string, string>());

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.InternalLoginError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_Timeout_ReturnsNull_AndPublishesLoginError()
        {
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

            var result = _handler.ContinueAuth("sess1", new System.Collections.Generic.Dictionary<string, string>());

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.InternalLoginError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_RpcError_ReturnsNull_AndPublishesLoginError()
        {
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Throws(new RpcException(new Status(StatusCode.Internal, "server error")));

            var result = _handler.ContinueAuth("sess1", new System.Collections.Generic.Dictionary<string, string>());

            Assert.IsNull(result);
            Assert.AreEqual(VcsUiUpdateType.InternalLoginError, _lastUpdateType);
        }

        [TestMethod]
        public void ContinueAuth_Complete_PublishesInternalLoginSuccess()
        {
            var loginResult = new LoginResult { PlayerName = "Pilot1", Secret = "mysecret" };
            var authResponse = new AuthStepResponse { Success = true, Complete = loginResult };
            _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
                .Returns(authResponse);

            _handler.ContinueAuth("sess1", new System.Collections.Generic.Dictionary<string, string>());

            Assert.AreEqual(VcsUiUpdateType.InternalLoginSuccess, _lastUpdateType);
        }
    }
}
