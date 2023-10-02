using MQTTnet;
using System.Text;

public class SerialRelayMqttBroker : MqttBroker
{

    private readonly SerialPortRelayControl relay;

    public SerialRelayMqttBroker(Logger logger, MqttConfig config, SerialPortRelayControl relay, CancellationToken appCancelToken)
        : base(logger, config, appCancelToken)
    {
        this.relay = relay;

        relay.RelayStateChanged += new EventHandler(async (obj, args)=> {
            await UpdateHomeAssistantState(relay.CurrentState);
        });
    }

    protected override async Task ProcessMessagePayloadAsync(MqttApplicationMessage message)
    {
        logger.WriteLine(Logger.LogLevel.Info, $"Received application message: {message.Topic}");
        
        if (null != message.PayloadSegment.Array) {
            byte[] bytes = message.PayloadSegment.Array;
            string messagePayload = Encoding.UTF8.GetString(bytes);
            logger.WriteLine(Logger.LogLevel.Debug, $"{messagePayload}");
            
            await Task.Run(() => {
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
            });
        }
    }

    protected override async Task UpdateDefaultStateAsync()
    {
        if (config.HomeAssistant != null)
        {
            await UpdateHomeAssistantState(relay.CurrentState);
        }
    }

    protected async Task UpdateHomeAssistantState(SerialPortRelayControl.RelayState state)
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
            await PublishStringAsync(StateTopic, payload, 0, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.WriteLine(Logger.LogLevel.Error, $"Unable to publish state information: {ex.Message}");
        }
    }

}