using System.Runtime.CompilerServices;
using System.Text;
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



    public MqttBroker(Logger logger, MqttConfig config, SerialPortRelayControl relay, CancellationToken appCancelToken)
    {
        this.logger = logger;
        this.relay = relay;
        this.config = config;
        this.mqttFactory = new MqttFactory();
        this.appCancelToken = appCancelToken;
    }

    private async Task ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e) 
    {
        await ProcessMessagePayloadAsync(e.ApplicationMessage);
    }

    private async Task ProcessMessagePayloadAsync(MqttApplicationMessage message)
    {
        logger.WriteLine(Logger.LogLevel.Info, $"MQTT: Received application message: {message.Topic}");
        
        if (null != message.PayloadSegment.Array) {
            byte[] bytes = message.PayloadSegment.Array;
            string messagePayload = Encoding.UTF8.GetString(bytes);
            logger.WriteLine(Logger.LogLevel.Debug, $"MQTT: {messagePayload}");
            
            if (messagePayload.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                relay.OpenRelay();
            }
            else if (messagePayload.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                relay.CloseRelay();
            }
        }
    }

    public async Task<bool> ConnectAsync() 
    {
        return await ConnectAsync(initialConnectionAttempts);
    }

    private async Task<bool> ConnectAsync(int remainingConnectionAttempts)
    {
        logger.WriteLine(Logger.LogLevel.Info, $"MQTT: Connecting to MQTT server: mqtt://{config.Host}:{config.Port}.");
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
                logger.WriteLine(Logger.LogLevel.Info, $"MQTT: Connected to MQTT server.");
                await PostConnectEventsAsync(true);
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.WriteLine(Logger.LogLevel.Warn, $"MQTT: Unable to connect to MQTT server: {ex.Message}.");
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
                
                logger.WriteLine(Logger.LogLevel.Info, $"MQTT: Attemping to reconnect to server. {remainingConnectionAttempts--} attempts remaining.");
                return await ConnectAsync(remainingConnectionAttempts--);
            }
            return false;
        }
    }

    private async Task PostConnectEventsAsync(bool firstConnect)
    {
        if (mqttClient == null) { return; }

        logger.WriteLine(Logger.LogLevel.Info, $"MQTT: Subscribing to topic: '{config.Topic}'");
        var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => { f.WithTopic(config.Topic); })
                .Build();

        var result = await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);
        logger.WriteLine(Logger.LogLevel.Info, $"MQTT: Subscribed to topic: '{config.Topic}'");

        // Keep alive timer
        if (keepAliveTimer == null) 
        {
            keepAliveTimer = new Timer(async _ => await KeepAliveTimerAsync(), null, keepAliveTimeoutMilliseconds, Timeout.Infinite);
        }
        else
        {
            // Make sure the keep alive timer is enabled
            keepAliveTimer.Change(keepAliveTimeoutMilliseconds, Timeout.Infinite);
        }
        
    }

    private async Task KeepAliveTimerAsync()
    {
        logger.WriteLine(Logger.LogLevel.Debug, "MQTT: Sending keep alive ping to server");
        bool success = false;
        using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(connectionTimeoutSeconds)))
        {
            success = await mqttClient.TryPingAsync(timeoutToken.Token);
        }

        if (!success)
        {
            logger.WriteLine(Logger.LogLevel.Info, "MQTT: Disconnected from server, attempting to reconnect.");
            if (await this.ConnectAsync(remainingConnectionAttempts: 0))
            {
                logger.WriteLine(Logger.LogLevel.Info, "MQTT: Reconnected.");
            }
            else
            {
                // Reschedule keep alive timer for reconnecton time period
                keepAliveTimer?.Change(reconnectTimeoutMilliseconds, Timeout.Infinite);
            }
        }
        else
        {
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

        logger.WriteLine(Logger.LogLevel.Info, "MQTT: Cleanly disconnecting from server.");
        var mqttClientDisconnectOptions = mqttFactory.CreateClientDisconnectOptionsBuilder().Build();
        using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(disconncetTimeoutSeconds)))
        {
            await mqttClient.DisconnectAsync(mqttClientDisconnectOptions, timeoutToken.Token);
        }

        
    }
}