using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

public class MqttBroker
{
    private readonly Logger logger;
    private readonly SerialPortRelayControl relay;
    private readonly MqttConfig config;
    private readonly MqttFactory mqttFactory;
    private readonly CancellationToken appCancelToken;
    private IMqttClient? mqttClient;
    private Timer? keepAliveTimer;
    private int keepAliveTimeoutMilliseconds = 5 * 1000;
    private int reconnectTimeoutMilliseconds = 60 * 1000;
    private int connectionTimeoutSeconds = 10;
    private int disconncetTimeoutSeconds = 2;
    private int initialConnectionAttempts = 10;
    private const string PAYLOAD_ON = "on";
    private const string PAYLOAD_OFF = "off";
    private const string PAYLOAD_AVAILABLE = "available";
    private const string PAYLOAD_OFFLINE = "offline";



    public MqttBroker(Logger logger, MqttConfig config, SerialPortRelayControl relay, CancellationToken appCancelToken)
    {
        this.logger = logger;
        this.relay = relay;
        this.config = config;
        this.mqttFactory = new MqttFactory();
        this.appCancelToken = appCancelToken;
        relay.RelayStateChanged += new EventHandler(async (obj, args)=> {
            await UpdateHomeAssistantState(relay.CurrentState);
        });

        if (config.KeepAliveSeconds.HasValue)
            keepAliveTimeoutMilliseconds = config.KeepAliveSeconds.Value * 1000;
        if (config.ReconnectTimeout.HasValue)
            reconnectTimeoutMilliseconds = config.ReconnectTimeout.Value * 1000;
        if (config.ConnectionTimeout.HasValue)
            connectionTimeoutSeconds = config.ConnectionTimeout.Value;
        if (config.InitialConnectionAttempts.HasValue)
            initialConnectionAttempts = config.InitialConnectionAttempts.Value;
    }

    private async Task ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e) 
    {
        await ProcessMessagePayloadAsync(e.ApplicationMessage);
    }

    private async Task ProcessMessagePayloadAsync(MqttApplicationMessage message)
    {
        logger.WriteLine(Logger.LogLevel.Info, $"Received application message: {message.Topic}");
        
        if (null != message.PayloadSegment.Array) {
            byte[] bytes = message.PayloadSegment.Array;
            string messagePayload = Encoding.UTF8.GetString(bytes);
            logger.WriteLine(Logger.LogLevel.Debug, $"{messagePayload}");
            
            try
            {
                if (messagePayload.Equals(PAYLOAD_ON, StringComparison.OrdinalIgnoreCase))
                {
                    relay.OpenRelay();
                }
                else if (messagePayload.Equals(PAYLOAD_OFF, StringComparison.OrdinalIgnoreCase))
                {
                    relay.CloseRelay();
                }
            }
            catch (Exception ex)
            {
                logger.WriteLine(Logger.LogLevel.Warn, $"Unable to change relay state: {ex.Message}");
            }
        }
    }

    public async Task<bool> ConnectAsync() 
    {
        return await ConnectAsync(initialConnectionAttempts);
    }

    private async Task<bool> ConnectAsync(int remainingConnectionAttempts)
    {
        logger.WriteLine(Logger.LogLevel.Info, $"Connecting to MQTT server: mqtt://{config.Host}:{config.Port}.");
        mqttClient = mqttFactory.CreateMqttClient();
        mqttClient.ApplicationMessageReceivedAsync += ApplicationMessageReceivedAsync;

        var clientOptions = new MqttClientOptionsBuilder()
                                .WithTcpServer(config.Host, config.Port)
                                .Build();
        try 
        {
            using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(connectionTimeoutSeconds)))
            {
                await mqttClient.ConnectAsync(clientOptions, timeoutToken.Token);
                logger.WriteLine(Logger.LogLevel.Info, $"Connected to MQTT server.");
                await PostConnectEventsAsync(true);
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.WriteLine(Logger.LogLevel.Warn, $"Unable to connect to MQTT server: {ex.Message}.");
            if (remainingConnectionAttempts > 0) 
            {
                try
                {
                    await Task.Delay(reconnectTimeoutMilliseconds, appCancelToken);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
                
                logger.WriteLine(Logger.LogLevel.Info, $"Attemping to reconnect to server. {remainingConnectionAttempts--} attempts remaining.");
                return await ConnectAsync(remainingConnectionAttempts--);
            }
            return false;
        }
    }

    private bool IsHomeAssistantEnabled
    {
        get { return config.HomeAssistant != null; }
    }

    private string StateTopic 
    {
        get 
        { 
            if (config.Topic != null)
                return config.Topic; 
            else
                throw new InvalidOperationException("Topic cannot be null");
        }
    }

    private string CommandTopic
    {
        get 
        {
            return StateTopic + "/set";
        }
    }

    private string AvailabilityTopic
    {
        get
        {
            return StateTopic + "/available";
        }
    }

    private async Task PostConnectEventsAsync(bool firstConnect)
    {
        if (mqttClient == null) { return; }

        // Listen for notifications on the command topic
        logger.WriteLine(Logger.LogLevel.Info, $"Subscribing to topic: '{CommandTopic}'");
        var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => { f.WithTopic(CommandTopic); })
                .Build();

        var result = await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);
        logger.WriteLine(Logger.LogLevel.Info, $"Subscribed to topic: '{CommandTopic}'");

        // Update availability to indicate we're online
        await UpdateHomeAssistantAvailabilityAsync(true);


        // Configure our keep alive timer to make sure we stay connected to the MQTT server
        if (keepAliveTimer == null) 
        {
            keepAliveTimer = new Timer(async _ => await KeepAliveTimerAsync(), null, keepAliveTimeoutMilliseconds, Timeout.Infinite);
        }
        else
        {
            // Make sure the keep alive timer is enabled
            keepAliveTimer.Change(keepAliveTimeoutMilliseconds, Timeout.Infinite);
        }

        // Register with Home Assistant
        if (config.HomeAssistant != null)
        {
            await UpdateHomeAssistantDiscoveryAsync(config.HomeAssistant);
            await UpdateHomeAssistantState(relay.CurrentState);

        }
    }

    private async Task UpdateHomeAssistantAvailabilityAsync(bool isAvailable)
    {
        if (!IsHomeAssistantEnabled) return;

        string payload = isAvailable ? PAYLOAD_AVAILABLE : PAYLOAD_OFFLINE;
        logger.WriteLine(Logger.LogLevel.Debug, $"Setting availability to {payload}.");

        try
        {
            await mqttClient.PublishStringAsync(AvailabilityTopic, payload, 0, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.WriteLine(Logger.LogLevel.Error, $"Unable to publish availability information: {ex.Message}");
        }
    }

    private async Task UpdateHomeAssistantState(SerialPortRelayControl.RelayState state)
    {
        if (!IsHomeAssistantEnabled) return;

        string payload;
        switch(state)
        {
            case SerialPortRelayControl.RelayState.Open:
                payload = PAYLOAD_ON;
                break;
            case SerialPortRelayControl.RelayState.Closed:
                payload = PAYLOAD_OFF;
                break;
            default:
                payload = "unknown";
                break;
        }
        logger.WriteLine(Logger.LogLevel.Debug, $"Setting state to {payload}");

        try
        {
            await mqttClient.PublishStringAsync(StateTopic, payload, 0, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.WriteLine(Logger.LogLevel.Error, $"Unable to publish state information: {ex.Message}");
        }
    }

    private async Task UpdateHomeAssistantDiscoveryAsync(HomeAssistantConfig config)
    {
        if (!IsHomeAssistantEnabled) return;

        if (!config.DiscoveryEnabled.HasValue || !config.DiscoveryEnabled.Value)
        {
            return;
        }
        // if (string.IsNullOrEmpty(config.UniqueID))
        // {
        //     throw new InvalidDataException("Home Assistant discovery required a guid to be configured.");
        // }
        if (string.IsNullOrEmpty(config.EntityId))
        {
            throw new InvalidDataException("Home Assistant discovery requires the entity_id be configured.");
        }

        string prefix = config.DiscoveryPrefix ?? "homeassistant";
        string discoveryTopic = $"{prefix}/switch/{config.EntityId}/config";
        logger.WriteLine(Logger.LogLevel.Debug, $"Adding discovery info under topic: {discoveryTopic}");

        var dataObj = new {
            name = config.Name,
            uniq_id = config.UniqueID,
            state_topic = StateTopic,
            
            command_topic = CommandTopic,
            payload_on = PAYLOAD_ON,
            payload_off = PAYLOAD_OFF,
            icon = "mdi:broadcast",
            
            availability_topic = AvailabilityTopic,
            payload_available = PAYLOAD_AVAILABLE,
            payload_not_available = PAYLOAD_OFFLINE,

            qos = 0,
            retain = true,

            dev = new {
                name = "mqtt-relay-controller",
                model = "csharp",
                sw_version = "1.0",
                manufacturer = "TaskTask LLC",
                identifiers = "33564eb9"
            }
        };

        string payload = JsonSerializer.Serialize(dataObj);
        logger.WriteLine(Logger.LogLevel.Debug, $"Discovery payload: {payload}");

        if (null != mqttClient) 
        {
            try 
            {
                await mqttClient.PublishStringAsync(discoveryTopic, payload, 0, true, appCancelToken);
                logger.WriteLine(Logger.LogLevel.Info, "Published Home Assistant discovery payload.");
            }
            catch (Exception ex)
            {
                logger.WriteLine(Logger.LogLevel.Warn, $"Error publishing discovery payload: {ex.Message}");
            }
        }
        else
        {
            logger.WriteLine(Logger.LogLevel.Warn, "Discovery incomplete, no connection to MQTT server."); 
        }
    }

    private async Task KeepAliveTimerAsync()
    {
        logger.WriteLine(Logger.LogLevel.Debug, "Sending keep alive ping to server");
        bool success = false;
        using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(connectionTimeoutSeconds)))
        {
            success = await mqttClient.TryPingAsync(timeoutToken.Token);
        }

        if (!success)
        {
            logger.WriteLine(Logger.LogLevel.Info, "Disconnected from server, attempting to reconnect.");
            if (await this.ConnectAsync(remainingConnectionAttempts: 0))
            {
                logger.WriteLine(Logger.LogLevel.Info, "Reconnected.");
            }
            else
            {
                // Reschedule keep alive timer for reconnecton time period
                keepAliveTimer?.Change(reconnectTimeoutMilliseconds, Timeout.Infinite);
            }
        }
        else
        {
            // Update availability to indicate we're online
            await UpdateHomeAssistantAvailabilityAsync(true);

            // Reschedule keepalive timer for next interval
            keepAliveTimer?.Change(keepAliveTimeoutMilliseconds, Timeout.Infinite);
        }
    }

    public async Task DisconnectAsync()
    {        
        if (mqttClient == null) { return; }
        
        if (keepAliveTimer != null)
        {
            keepAliveTimer.Change(0, Timeout.Infinite);
            keepAliveTimer.Dispose();
            keepAliveTimer = null;
        }

        await UpdateHomeAssistantAvailabilityAsync(false);

        logger.WriteLine(Logger.LogLevel.Info, "Cleanly disconnecting from server.");
        var mqttClientDisconnectOptions = mqttFactory.CreateClientDisconnectOptionsBuilder().Build();
        using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(disconncetTimeoutSeconds)))
        {
            await mqttClient.DisconnectAsync(mqttClientDisconnectOptions, timeoutToken.Token);
        }

        
    }
}