using AIFarm.Presentation;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class GatewayConfigurationWireTests
    {
        [Test]
        public void UnchangedSavedKeyIsNull_NotAnInstructionToClearIt()
        {
            var settings = new GatewayConnectionController.Configuration { api_key = null };
            Assert.That(GatewayConnectionController.SerializeConfiguration(settings), Does.Contain("\"api_key\":null"));
            settings.api_key = "";
            Assert.That(GatewayConnectionController.SerializeConfiguration(settings), Does.Contain("\"api_key\":\"\""));
            settings.api_key = "contract-test-new-key";
            Assert.That(GatewayConnectionController.SerializeConfiguration(settings), Does.Contain("\"api_key\":\"contract-test-new-key\""));
        }
    }
}
