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
    }
}
