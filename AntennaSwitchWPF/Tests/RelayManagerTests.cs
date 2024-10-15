using AntennaSwitchWPF.RelayController;
using Moq;
using Xunit;

namespace AntennaSwitchWPF.Tests
{
    public class RelayManagerTests
    {
        private readonly Mock<IUdpMessageSender> _mockSender;
        private readonly RelayManager _relayManager;

        public RelayManagerTests()
        {
            _mockSender = new Mock<IUdpMessageSender>();
            _relayManager = new RelayManager(_mockSender.Object);
        }

        [Fact]
        public async Task SetRelayForAntennaAsync_ShouldNotChangeWhenAlreadySet()
        {
            // Arrange
            const int relayId = 1;
            const int bandNumber = 1;
            _mockSender.Setup(s => s.SendCommandAndValidateResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Set the relay initially
            await _relayManager.SetRelayForAntennaAsync(relayId, bandNumber);
            _mockSender.Invocations.Clear(); // Clear the invocation history

            // Act
            await _relayManager.SetRelayForAntennaAsync(relayId, bandNumber);

            // Assert
            _mockSender.Verify(s => s.SendCommandAndValidateResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task TurnOffAllRelays_ShouldSendCorrectCommand()
        {
            // Arrange
            _mockSender.Setup(s => s.SendCommandAndValidateResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            await _relayManager.TurnOffAllRelays();

            // Assert
            _mockSender.Verify(s => s.SendCommandAndValidateResponseAsync("RELAY-AOF-255,1,1", "RELAY-AOF-255,1,1,OK", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetRelayAsync_ShouldSendCorrectCommand()
        {
            // Arrange
            const int relayId = 1;
            const bool state = true;
            _mockSender.Setup(s => s.SendCommandAndValidateResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            await _relayManager.SetRelayAsync(relayId, state);

            // Assert
            _mockSender.Verify(s => s.SendCommandAndValidateResponseAsync($"RELAY-SET-255,{relayId},1", $"RELAY-SET-255,{relayId},1,OK", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetRelaysForBandAsync_ShouldReturnCorrectRelays()
        {
            // Arrange
            const int bandNumber = 1;
            var antennaConfigs = new List<AntennaConfig>
            {
                new() { Port = "1", Is160M = true },
                new() { Port = "2", Is160M = false },
                new() { Port = "3", Is160M = true }
            };

            // Act
            var relays = await _relayManager.GetRelaysForBandAsync(bandNumber, antennaConfigs);

            // Assert
            Assert.Equal([1, 3], relays);
        }

        [Fact]
        public void Dispose_ShouldDisposeSenderAndSemaphore()
        {
            // Act
            _relayManager.Dispose();

            // Assert
            _mockSender.Verify(s => s.Dispose(), Times.Once);
        }
    }
}
