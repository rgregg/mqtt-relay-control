using MQTTnet;
using MQTTnet.Client;

public abstract class ManagedMqttClient
{
    protected Logger Logger {get; private set; }
    protected MqttConfig ClientConfig {get; private set;}
    private readonly MqttFactory mqttFactory;
    private readonly CancellationToken appCancelToken;
    private IMqttClient? mqttClient;
    private Timer? keepAliveTimer;
    protected int keepAliveTimeoutMilliseconds = 5 * 1000;
    protected int reconnectTimeoutMilliseconds = 60 * 1000;
    protected int connectionTimeoutSeconds = 10;
    protected int disconncetTimeoutSeconds = 2;
    protected int initialConnectionAttempts = 10;

    private readonly List<string> SubscribedTopics = new List<string>();


    public ManagedMqttClient(Logger logger, MqttConfig config, CancellationToken appCancelToken)
    {
        this.Logger = logger;
        this.ClientConfig = config;
        this.mqttFactory = new MqttFactory();
        this.appCancelToken = appCancelToken;
        
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

    public delegate void TopicChangedEventHandler(object sender, MqttTopicUpdateEventArgs args);
    public event TopicChangedEventHandler? TopicChanged;

    protected virtual Task ProcessMessagePayloadAsync(MqttApplicationMessage message)
    {
        Logger.WriteLine(Logger.LogLevel.Debug, $"ProcessMessagePayloadAsync: {message.Topic}");
        try
        {
            if (null == TopicChanged)
            {
                Logger.WriteLine(Logger.LogLevel.Debug, $"No event handler wired up.");
            }
            TopicChanged?.Invoke(this, new MqttTopicUpdateEventArgs(message.Topic, message.ConvertPayloadToString()));
        } 
        catch (Exception ex) 
        {
            Logger.WriteLine(Logger.LogLevel.Warn, $"Unable to process topic update: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    protected virtual Task PostConnectionEstablishedAsync(bool firstConnect)
    {
        return Task.CompletedTask;
    }
    protected virtual Task PostKeepAliveTimerConnectedAsync()
    {
        return Task.CompletedTask;
    }
    protected virtual Task PreDisconnectAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<bool> ConnectAsync() 
    {
        return await ConnectAsync(initialConnectionAttempts);
    }

    private async Task<bool> ConnectAsync(int remainingConnectionAttempts)
    {
        Logger.WriteLine(Logger.LogLevel.Info, $"Connecting to MQTT server: mqtt://{ClientConfig.Host}:{ClientConfig.Port}.");
        mqttClient = mqttFactory.CreateMqttClient();
        mqttClient.ApplicationMessageReceivedAsync += ApplicationMessageReceivedAsync;

        var clientOptionBuilder = new MqttClientOptionsBuilder()
                                .WithTcpServer(ClientConfig.Host, ClientConfig.Port);
        if (!string.IsNullOrEmpty(ClientConfig.Username) || !string.IsNullOrEmpty(ClientConfig.Password))
        {
            clientOptionBuilder = clientOptionBuilder.WithCredentials(ClientConfig.Username ?? "", ClientConfig.Password ?? "");
        }

        var clientOptions = clientOptionBuilder.Build();
        try 
        {
            using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(connectionTimeoutSeconds)))
            {
                await mqttClient.ConnectAsync(clientOptions, timeoutToken.Token);
                Logger.WriteLine(Logger.LogLevel.Info, $"Connected to MQTT server.");
                await OnConnectEventsAsync(true);
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(Logger.LogLevel.Warn, $"Unable to connect to MQTT server: {ex.Message}.");
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
                
                Logger.WriteLine(Logger.LogLevel.Info, $"Attemping to reconnect to server. {remainingConnectionAttempts--} attempts remaining.");
                return await ConnectAsync(remainingConnectionAttempts--);
            }
            return false;
        }
    }

    protected bool MqttClientExists 
    { 
        get { return mqttClient != null; }
    }

    private async Task OnConnectEventsAsync(bool firstConnect)
    {
        if (!MqttClientExists) { return; }
        await ResubscribeToTopicsAsync();
        ResetKeepAliveTimer();

        await PostConnectionEstablishedAsync(firstConnect);
    }

    protected async Task ResubscribeToTopicsAsync() 
    {
        foreach(var topic in SubscribedTopics)
        {
            await SubscribeTopicAsync(topic);
        }
    }

    public async Task SubscribeTopicAsync(string topic)
    {
        if (!MqttClientExists || mqttClient == null)
        {
            throw new InvalidOperationException("Cannot create a subscription without a connection.");
        }

        Logger.WriteLine(Logger.LogLevel.Info, $"Subscribing to topic: '{topic}'");
        var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                                                .WithTopicFilter(f => { f.WithTopic(topic); })
                                                .Build();

        try
        {
            var result = await mqttClient.SubscribeAsync(mqttSubscribeOptions, appCancelToken);
            Logger.WriteLine(Logger.LogLevel.Info, $"Subscribed to topic: '{topic}'");
            SubscribedTopics.Add(topic);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(Logger.LogLevel.Error, $"Unable to subscribe to topic: '{topic}': {ex.Message}");
        }
    }

    public async Task UnsubscribeTopicAsync(string topic)
    {
        var options = mqttFactory.CreateUnsubscribeOptionsBuilder()
                .WithTopicFilter(topic).Build();
        if (mqttClient != null)
        {
            var result = await mqttClient.UnsubscribeAsync(options, appCancelToken);
            Logger.WriteLine(Logger.LogLevel.Debug, $"Unsubscribed to topic: '{topic}'");
            SubscribedTopics.Remove(topic);
        }
    }

    public async Task PublishStringAsync(string topic, string payload, MQTTnet.Protocol.MqttQualityOfServiceLevel qualityOfService, bool persist)
    {
        Logger.WriteLine(Logger.LogLevel.Debug, $"Publishing topic '{topic}' with value '{payload}'.");
        await mqttClient.PublishStringAsync(topic, payload, qualityOfService, persist, appCancelToken);
    }

    private async Task KeepAliveTimerAsync()
    {
        Logger.WriteLine(Logger.LogLevel.Debug, "Sending keep alive ping to server");
        bool success = false;
        using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(connectionTimeoutSeconds)))
        {
            success = await mqttClient.TryPingAsync(timeoutToken.Token);
        }

        if (!success)
        {
            Logger.WriteLine(Logger.LogLevel.Info, "Disconnected from server, attempting to reconnect.");
            if (await this.ConnectAsync(remainingConnectionAttempts: 0))
            {
                Logger.WriteLine(Logger.LogLevel.Info, "Reconnected.");
                await ResubscribeToTopicsAsync();
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
            await PostKeepAliveTimerConnectedAsync();
        }
    }

    protected void ResetKeepAliveTimer() 
    {
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
    }

    public async Task DisconnectAsync()
    {        
        if (!MqttClientExists) { return; }
        
        if (keepAliveTimer != null)
        {
            keepAliveTimer.Change(0, Timeout.Infinite);
            keepAliveTimer.Dispose();
            keepAliveTimer = null;
        }

        await PreDisconnectAsync();

        Logger.WriteLine(Logger.LogLevel.Info, "Cleanly disconnecting from server.");
        var mqttClientDisconnectOptions = mqttFactory.CreateClientDisconnectOptionsBuilder().Build();
        using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(disconncetTimeoutSeconds)))
        {
            if (mqttClient != null)
            {
                await mqttClient.DisconnectAsync(mqttClientDisconnectOptions, timeoutToken.Token);
            }
        }
        
    }
}

public class MqttTopicUpdateEventArgs
{
    public string Topic {get; private set;}
    public string Data {get; private set;}

    public MqttTopicUpdateEventArgs(string topic, string data)
    {
        this.Topic = topic;
        this.Data = data;
    }
}