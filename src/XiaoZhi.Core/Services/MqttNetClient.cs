using Microsoft.Extensions.Logging;
using MQTTnet;
using System.Text;
using System.Text.Json;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Services
{
    public class MqttNetClient : ICommunicationClient
    {
        private readonly string _brokerHost;
        private readonly int _port;
        private readonly string _clientId;
        private readonly string _topic;
        private readonly ILogger<MqttNetClient>? _logger;
        private IMqttClient? _mqttClient;
        private bool _disposed = false;

        public event EventHandler<ChatMessage>? MessageReceived;
        public event EventHandler<bool>? ConnectionStateChanged;

        public bool IsConnected => _mqttClient?.IsConnected ?? false;

        public MqttNetClient(string brokerHost, int port, string clientId, string topic, ILogger<MqttNetClient>? logger = null)
        {
            _brokerHost = brokerHost ?? throw new ArgumentNullException(nameof(brokerHost));
            _port = port;
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            _topic = topic ?? throw new ArgumentNullException(nameof(topic));
            _logger = logger;
        }
        public async Task ConnectAsync()
        {
            try
            {
                var factory = new MqttClientFactory();
                _mqttClient = factory.CreateMqttClient();

                // Configure MQTT client options
                var clientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(_brokerHost, _port)
                    .WithClientId(_clientId)
                    .WithCleanSession(true)
                    .Build();

                // Subscribe to events
                _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
                _mqttClient.ConnectedAsync += OnConnectedAsync;
                _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

                // Connect to broker
                await _mqttClient.ConnectAsync(clientOptions);

                // Subscribe to the topic
                var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(_topic)
                    .Build();

                await _mqttClient.SubscribeAsync(subscribeOptions);

                _logger?.LogInformation($"MQTT client connected to {_brokerHost}:{_port} and subscribed to topic '{_topic}'");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect MQTT client");
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_mqttClient != null)
            {
                try
                {
                    await _mqttClient.DisconnectAsync();
                    _logger?.LogInformation("MQTT client disconnected");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error disconnecting MQTT client");
                }
            }
        }

        public async Task SendMessageAsync(ChatMessage message)
        {
            if (_mqttClient == null || !IsConnected)
            {
                throw new InvalidOperationException("MQTT client is not connected");
            }

            try
            {
                var json = JsonSerializer.Serialize(message);
                var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(_topic)
                    .WithPayload(json)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _mqttClient.PublishAsync(applicationMessage);
                _logger?.LogDebug($"Chat message sent to topic '{_topic}'");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send MQTT chat message");
                throw;
            }
        }

        public async Task SendVoiceAsync(VoiceMessage voiceMessage)
        {
            if (_mqttClient == null || !IsConnected)
            {
                throw new InvalidOperationException("MQTT client is not connected");
            }

            try
            {
                var json = JsonSerializer.Serialize(voiceMessage);
                var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(_topic)
                    .WithPayload(json)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _mqttClient.PublishAsync(applicationMessage);
                _logger?.LogDebug($"Voice message sent to topic '{_topic}'");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send MQTT voice message");
                throw;
            }
        }
        private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

                // Try to deserialize as ChatMessage first
                try
                {
                    var chatMessage = JsonSerializer.Deserialize<ChatMessage>(payload);
                    if (chatMessage != null)
                    {
                        MessageReceived?.Invoke(this, chatMessage);
                        return Task.CompletedTask;
                    }
                }
                catch
                {
                    // If it fails, it might be a VoiceMessage, but we handle it as ChatMessage
                    // for the interface compatibility
                }

                // Try to deserialize as VoiceMessage and convert to ChatMessage
                try
                {
                    var voiceMessage = JsonSerializer.Deserialize<VoiceMessage>(payload);
                    if (voiceMessage != null)
                    {
                        // Convert VoiceMessage to ChatMessage for interface compatibility
                        var chatMessage = new ChatMessage
                        {
                            Id = Guid.NewGuid().ToString(),
                            Content = "Voice message received",
                            Type = "voice",
                            Timestamp = voiceMessage.Timestamp,
                            AudioData = voiceMessage.Data
                        };
                        MessageReceived?.Invoke(this, chatMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not deserialize received message");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing received MQTT message");
            }

            return Task.CompletedTask;
        }

        private Task OnConnectedAsync(MqttClientConnectedEventArgs e)
        {
            _logger?.LogInformation("MQTT client connected");
            ConnectionStateChanged?.Invoke(this, true);
            return Task.CompletedTask;
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            _logger?.LogWarning($"MQTT client disconnected: {e.Reason}");
            ConnectionStateChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    DisconnectAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during MQTT client disposal");
                }

                _mqttClient?.Dispose();
                _disposed = true;
            }
        }
    }
}