 using AntennaSwitchWPF.RelayController;
 using Xunit;

 namespace AntennaSwitchWPF.Tests;

 /// <summary>
 /// Requires a KC868 relay controller
 /// </summary>
 public class RelayManagerIntegrationTests : IDisposable
 {
     private readonly RelayManager _relayManager;
     private readonly UdpMessageSender _sender;

     public RelayManagerIntegrationTests()
     {
         // Replace with your actual IP and port
         _sender = new UdpMessageSender("10.0.0.12", 12090);
         _relayManager = new RelayManager(_sender);
     }

     public void Dispose()
     {
         _relayManager.Dispose();
     }

     [Fact]
     public async Task SetRelayForAntennaAsync_ShouldSetCorrectRelay()
     {
         // Arrange
         const int relayId = 1;
         const int bandNumber = 1;

         // Act
         await _relayManager.SetRelayForAntennaAsync(relayId, bandNumber);

         // Assert
         Assert.True(_relayManager.GetRelayState(relayId));
         Assert.Equal(relayId, _relayManager.CurrentlySelectedRelay);

         // Verify other relays are off
         for (int i = 2; i <= 16; i++)
         {
             Assert.False(_relayManager.GetRelayState(i));
         }
     }

     [Fact]
     public async Task TurnOffAllRelays_ShouldTurnOffAllRelays()
     {
         // Arrange
         await _relayManager.SetRelayForAntennaAsync(1, 1);

         // Act
         await _relayManager.TurnOffAllRelays();
         var allRelayStates = _relayManager.GetAllRelayStates();

         // Assert
         foreach (var kv in allRelayStates)
         {
             Assert.False(kv.Value);
         }

         Assert.Equal(0, _relayManager.CurrentlySelectedRelay);
     }

     [Fact]
     public async Task SetRelayAsync_ShouldSetSpecificRelay()
     {
         // Arrange
         const int relayId = 3;

         // Act
         await _relayManager.SetRelayAsync(relayId, true);

         // Assert
         Assert.True(_relayManager.GetRelayState(relayId));
         Assert.Equal(relayId, _relayManager.CurrentlySelectedRelay);
     }
     [Fact]
     public async Task SetRelayAsync_ShouldReturnCorrectState()
     {
         // Arrange
         const int relayId = 3;
         const int bandNumber = 3;

         // Act
         // await _relayManager.TurnOffAllRelays();
         await _relayManager.SetRelayForAntennaAsync(relayId, bandNumber);

         // Assert
         Assert.True(_relayManager.GetRelayState(relayId));
         Assert.Equal(relayId, _relayManager.CurrentlySelectedRelay);
         
         await _relayManager.SetRelayForAntennaAsync(relayId: 4, bandNumber: 5);
         Assert.True(_relayManager.GetRelayState(4));
         Assert.False(_relayManager.GetRelayState(3));
     }

     [Fact]
     public async Task SendCommandAndValidateResponseAsync_ReturnsCorrectResponse()
     {
         // Arrange
         const int relayId = 1;
         const int bandNumber = 1;

         // Act
         await _relayManager.SetRelayForAntennaAsync(relayId, bandNumber);

         // Assert
         var result = await _sender.SendCommandAndValidateResponseAsync("RELAY-AOF-255,1,1", "RELAY-AOF-255,1,1,OK");
         Assert.True(result);
         result = await _sender.SendCommandAndValidateResponseAsync($"RELAY-SET-255,{relayId},1",
             $"RELAY-SET-255,{relayId},1,OK");
         Assert.True(result);
     }
 }
 