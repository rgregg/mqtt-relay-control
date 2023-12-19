using System.Text.Json;
using System.Text.Json.Serialization;


/// <summary>
/// Implementation that maps data published to MQTT from Zigbee2MQTT
/// into a format that can be understood by SignalK via the signalk-to-mqtt bridge
/// </summary>
public class ZigbeeToSignalKMapping
{
    public const string SOURCE_ZIGBEE = "zigbee2mqtt";
    private readonly Logger Logger;
    private readonly ManagedMqttClient MqttClient;
    private readonly Dictionary<string, IZigbeeMapping> TopicMapping = new Dictionary<string, IZigbeeMapping>();

    private readonly string SystemId;
    public ZigbeeToSignalKMapping(MqttToSignalKConfig config, ManagedMqttClient mqttClient, Logger logger)
    {
        this.Logger = logger;
        this.MqttClient = mqttClient;

        if (config.SystemId == null)
            throw new InvalidOperationException("SingalK SystemID cannot be null.");

        SystemId = config.SystemId;
        var query = from m in config.Mappings
                    where m.Source == SOURCE_ZIGBEE
                    select m;
        foreach (var m in query)
        {
            IZigbeeMapping instance;
            if (m.Format == TemperatureMapping.FORMAT_TEMP)
            {
                instance = new TemperatureMapping(m, SystemId);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected format: {m.Format}");
            }

            TopicMapping.Add(instance.SourceTopic, instance);
        }
    }

    public async Task SubscribeToTopicsAsync()
    {
        MqttClient.TopicChanged += async (o, args) =>
        {
            IZigbeeMapping? instance;
            if (TopicMapping.TryGetValue(args.Topic, out instance))
            {
                Logger.WriteLine(Logger.LogLevel.Debug, $"Mapping {instance.SourceTopic} to {instance.GetType().Name}");
                await instance.ConvertDataAsync(args.Data, MqttClient, Logger);
            }
            else
            {
                Logger.WriteLine(Logger.LogLevel.Debug, $"Mapping for '{args.Topic}' not found.");
            }
        };

        foreach (var topic in TopicMapping.Keys)
        {
            await MqttClient.SubscribeTopicAsync(topic);
        }
    }
}

public interface IZigbeeMapping
{
    public string SourceTopic { get; }
    public Task ConvertDataAsync(string input, ManagedMqttClient client, Logger logger);
}

public class TemperatureMapping : IZigbeeMapping
{
    public const string FORMAT_TEMP = "temperature";
    private readonly string SystemId;
    public TemperatureMapping(MqttMappingConfig config, string systemId)
    {
        if (config.Source != ZigbeeToSignalKMapping.SOURCE_ZIGBEE)
            throw new InvalidDataException("Cannot use this class with a non-zigbee item.");
        if (config.Format != FORMAT_TEMP)
            throw new InvalidDataException("Cannot use this class with a non-temperature item.");
        if (string.IsNullOrEmpty(config.SourceTopic))
            throw new InvalidDataException("Source Topic cannot be null or empty.");
        if (string.IsNullOrEmpty(config.DestinationTopic))
            throw new InvalidDataException("Destination Topic cannot be null or empty.");

        this.SourceTopic = config.SourceTopic;
        this.DestinationTopicPrefix = config.DestinationTopic;
        this.SystemId = systemId;
    }

    public string SourceTopic { get; private set; }
    public string DestinationTopicPrefix { get; private set; }


    public async Task ConvertDataAsync(string sourceData, ManagedMqttClient client, Logger logger)
    {
        var data = JsonSerializer.Deserialize<ZigbeeTemperatureProbe>(sourceData);
        if (data == null)
        {
            logger.WriteLine(Logger.LogLevel.Warn, $"Unable to parse data as Zigbee temperature data.");
            return;
        }

        logger.WriteLine(Logger.LogLevel.Debug, $"Temperature conversion from {data} to signalK");

        if (null != data.Temperature)
        {
            // Need to convert from C to K
            double kelvin = data.Temperature.Value + 273.15;
            await PublishData(client, DestinationTopicPrefix, "temperature", kelvin.ToString("0.00"));
        }
        else
        {
            logger.WriteLine(Logger.LogLevel.Debug, $"No temperature data available from source.");
        }

        if (null != data.Humidity)
        {
            // Need to convert to a ratio (relativeHumidity)
            double ratio = data.Humidity.Value / 100.0;
            await PublishData(client, DestinationTopicPrefix, "relativeHumidity", ratio.ToString("0.00"));
        }
        else
        {
            logger.WriteLine(Logger.LogLevel.Debug, $"No relativeHumidity data available from source.");
        }
        if (null != data.Pressure)
        {
            // SignalK needs data in Pascals, Zigbee provides in hPa (x100)
            double pascals = data.Pressure.Value * 100;
            await PublishData(client, DestinationTopicPrefix, "pressure", pascals.ToString("0"));
        }
        else
        {
            logger.WriteLine(Logger.LogLevel.Debug, $"No pressure data available from source.");
        }
    }

    private async Task PublishData(ManagedMqttClient client, string topicPrefix, string dataName, string? value)
    {
        if (null == value)
        {
            throw new ArgumentNullException("value");
        }
        var topic = $"W/signalk/{SystemId}/{topicPrefix}/{dataName}";
        await client.PublishStringAsync(topic, value, 0, false);
    }

    private class ZigbeeTemperatureProbe
    {
        /// <summary>
        /// Temperature in degrees celsius
        /// </summary>
        [JsonPropertyName("temperature")]
        public float? Temperature { get; set; }
        /// <summary>
        /// Relatively humidity, in %
        /// </summary>
        [JsonPropertyName("humidity")]
        public float? Humidity { get; set; }
        /// <summary>
        /// Pressure measured in hPa
        /// </summary>
        [JsonPropertyName("pressure")]
        public float? Pressure { get; set; }
        /// <summary>
        /// Battery power remaining, measured in percent
        /// </summary>
        [JsonPropertyName("battery")]
        public float? Battery { get; set; }
        /// <summary>
        /// Battery voltage in millivolts
        /// </summary>
        [JsonPropertyName("voltage")]
        public int? Voltage { get; set; }
        /// <summary>
        /// Wireless link quality in lqi
        /// </summary>
        [JsonPropertyName("linkquality")]
        public int? LinkQuality { get; set; }
    }
}


/*
Water Leak Sensor
{"battery":77,"battery_low":null,"device_temperature":25,"linkquality":96,"power_outage_count":90,"voltage":2965,"water_leak":null}

Temperature Sensor
{"battery":77,"battery_low":null,"temperature":14.67,"humidity":74.75,"pressure":1021.4,"linkquality":96,"voltage":2965}
*/