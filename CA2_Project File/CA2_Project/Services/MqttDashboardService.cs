
using System;
using System.Text.Json;
using System.Threading.Tasks;
using MQTTnet;

namespace CA2_Project.Services
{
    public class MqttDashboardService : IDisposable
    {
        private readonly MqttClientFactory factory = new();
        private readonly IMqttClient client;
        private readonly object commandLock = new();

        public const string TelemetryTopic =
            "ca2/conveyor/telemetry";

        public const string CommandTopic =
            "ca2/conveyor/command";

        public const string StatusTopic =
            "ca2/conveyor/status";

        private const int AckTimeoutSeconds = 5;

        public bool IsConnected => client.IsConnected;

        public double Temperature { get; private set; }
        public bool SensorDetected { get; private set; }
        public bool RunEnable { get; private set; }

        public bool HasReceivedData { get; private set; }
        public DateTime? LastUpdated { get; private set; }

        public string DeviceStatus { get; private set; }
            = "NO DATA";

        public string LastMessage { get; private set; }
            = "No message received";

        public string LastError { get; private set; }
            = "";

        public string LastCommandId { get; private set; }
            = "None";

        public string CommandState { get; private set; }
            = "NO COMMAND";

        public string LastCommand { get; private set; }
            = "None";

        public string CommandDetail { get; private set; }
            = "No command has been sent.";

        private string? pendingCommandId;

        public event Action? DataChanged;

        // Dedicated command event for Dashboard logging.
        public event Action<string, string>? CommandEvent;

        public MqttDashboardService()
        {
            client = factory.CreateMqttClient();

            client.ApplicationMessageReceivedAsync += e =>
            {
                try
                {
                    string payload =
                        e.ApplicationMessage.ConvertPayloadToString();

                    string topic = e.ApplicationMessage.Topic;

                    LastMessage = payload;

                    if (topic == TelemetryTopic)
                    {
                        using var document =
                            JsonDocument.Parse(payload);

                        var root = document.RootElement;

                        double temperature = root
                            .GetProperty("temperature")
                            .GetDouble();

                        int sensor = root
                            .GetProperty("sensor_pnp")
                            .GetInt32();

                        bool run = root
                            .GetProperty("run_enable")
                            .GetBoolean();

                        Temperature = temperature;
                        SensorDetected = sensor == 1;
                        RunEnable = run;

                        HasReceivedData = true;
                        LastUpdated = DateTime.Now;
                    }
                    else if (topic == StatusTopic)
                    {
                        HandleAcknowledgement(payload);
                    }

                    LastError = "";
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }

                DataChanged?.Invoke();
                return Task.CompletedTask;
            };

            client.ConnectedAsync += e =>
            {
                DataChanged?.Invoke();
                return Task.CompletedTask;
            };

            client.DisconnectedAsync += e =>
            {
                DataChanged?.Invoke();
                return Task.CompletedTask;
            };
        }

        public async Task ConnectAsync()
        {
            if (client.IsConnected)
                return;

            try
            {
                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer("127.0.0.1", 1883)
                    .WithClientId(
                        "CA2_Blazor_" +
                        Guid.NewGuid().ToString("N")[..8])
                    .Build();

                await client.ConnectAsync(options);

                var subscriptions = factory
                    .CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(TelemetryTopic)
                    .WithTopicFilter(StatusTopic)
                    .Build();

                await client.SubscribeAsync(subscriptions);

                LastError = "";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            DataChanged?.Invoke();
        }

        public async Task PublishCommandAsync(string command)
        {
            if (!client.IsConnected)
                throw new InvalidOperationException(
                    "MQTT Broker is disconnected.");

            if (command != "START" && command != "STOP")
                throw new ArgumentException(
                    "Only START or STOP is supported.");

            string commandId = Guid.NewGuid().ToString("N");

            lock (commandLock)
            {
                if (pendingCommandId != null)
                {
                    throw new InvalidOperationException(
                        "Another command is awaiting acknowledgement.");
                }

                pendingCommandId = commandId;
                LastCommandId = commandId;
                LastCommand = command;
                CommandState = "PENDING";
                CommandDetail = "Waiting for device acknowledgement.";
            }

            DataChanged?.Invoke();

            string payload = JsonSerializer.Serialize(new
            {
                command_id = commandId,
                command = command,
                timestamp = DateTimeOffset.UtcNow
            });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(CommandTopic)
                .WithPayload(payload)
                .Build();

            try
            {
                await client.PublishAsync(message);
            }
            catch (Exception ex)
            {
                lock (commandLock)
                {
                    if (pendingCommandId == commandId)
                    {
                        pendingCommandId = null;
                        CommandState = "PUBLISH FAILED";
                        CommandDetail = ex.Message;
                    }
                }

                CommandEvent?.Invoke(
                    "WARNING",
                    $"Command {command} publish failed: {ex.Message}");

                DataChanged?.Invoke();
                throw;
            }

            CommandEvent?.Invoke(
                "INFO",
                $"Command {command} sent. ID: {commandId}");

            _ = WatchAcknowledgementAsync(commandId);
        }

        private void HandleAcknowledgement(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            // Legacy status messages without Command ID
            // may still be displayed, but cannot confirm a command.
            if (!root.TryGetProperty("command_id", out var idElement))
            {
                DeviceStatus = payload;
                return;
            }

            string? receivedId = idElement.GetString();

            string result = root.TryGetProperty(
                "result", out var resultElement)
                ? resultElement.GetString() ?? ""
                : "";

            bool matched = false;

            lock (commandLock)
            {
                if (receivedId != null &&
                    pendingCommandId == receivedId)
                {
                    pendingCommandId = null;

                    CommandState =
                        result == "SIMULATED_EXECUTION"
                            ? "ACKNOWLEDGED"
                            : "DEVICE RESPONSE";

                    CommandDetail =
                        $"Device response: {result}";

                    matched = true;
                }
            }

            DeviceStatus = payload;

            if (matched)
            {
                CommandEvent?.Invoke(
                    "RECOVERY",
                    $"Command acknowledged. ID: {receivedId}. " +
                    $"Result: {result}");
            }
            else
            {
                CommandEvent?.Invoke(
                    "INFO",
                    $"Unmatched status message received. " +
                    $"ID: {receivedId}");
            }
        }

        private async Task WatchAcknowledgementAsync(
            string commandId)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(AckTimeoutSeconds));

            bool timedOut = false;

            lock (commandLock)
            {
                if (pendingCommandId == commandId)
                {
                    pendingCommandId = null;
                    CommandState = "TIMEOUT";
                    CommandDetail =
                        "No matching acknowledgement within 5 seconds.";

                    timedOut = true;
                }
            }

            if (timedOut)
            {
                CommandEvent?.Invoke(
                    "ALARM",
                    $"Command acknowledgement timeout. " +
                    $"ID: {commandId}");

                DataChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
