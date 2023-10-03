using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MQTTnet;

public class HomeAssistantMqttClient : ManagedMqttClient
{
    public const string PAYLOAD_ON = "on";
    public const string PAYLOAD_OFF = "off";
    public const string PAYLOAD_AVAILABLE = "available";
    public const string PAYLOAD_OFFLINE = "offline";

    protected HomeAssistantConfig HomeAssistantConfig {get; private set;}
    protected IReadOnlyList<IHomeAssistantDevice> Devices {get { return internalDevices; } }
    private List<IHomeAssistantDevice> internalDevices = new List<IHomeAssistantDevice>();

    private readonly Dictionary<string, IHomeAssistantDevice> CommandTopicToDeviceLookupTable = new Dictionary<string, IHomeAssistantDevice>();

    public HomeAssistantMqttClient(Logger logger, 
                                   MqttConfig clientConfig, 
                                   HomeAssistantConfig? homeAssistantConfig,
                                   CancellationToken appCancellationToken)
        : base(logger, clientConfig, appCancellationToken)
    {
        this.HomeAssistantConfig = homeAssistantConfig ?? new HomeAssistantConfig { DiscoveryEnabled = false };
    }

    protected bool IsHomeAssistantDiscoveryEnabled
    {
        get { return HomeAssistantConfig.DiscoveryEnabled ?? false; }
    }

    protected override async Task PostKeepAliveTimerConnectedAsync()
    {
        // Update availability to indicate we're online
        await UpdateDevicesAvailabilityAsync(this.Devices, true);
    }

    protected override async Task PostConnectionEstablishedAsync(bool firstConnect)
    {
        // Listen for notifications on the command topic
        if (firstConnect)
        {
            await SubscribeToCommandTopicsAsync(this.Devices);
        }

        // Register with Home Assistant
        if (IsHomeAssistantDiscoveryEnabled)
        {
            await UpdateHomeAssistantDiscoveryAsync(HomeAssistantConfig, Devices);
        }

        // Update availability to indicate we're online
        await UpdateDevicesAvailabilityAsync(this.Devices, true);
        await SetDeviceCurrentStateAsync();
    }

    public void RegisterDevices(IEnumerable<IHomeAssistantDevice> devices)
    {
        internalDevices.AddRange(devices);
    }

    protected async Task SubscribeToCommandTopicsAsync(IEnumerable<IHomeAssistantDevice> devices)
    {
        foreach(var device in devices)
        {
            var topic = device.MqttCommandTopic;
            await this.SubscribeTopicAsync(topic);

            if (!CommandTopicToDeviceLookupTable.ContainsKey(topic))
            {
                CommandTopicToDeviceLookupTable.Add(topic, device);
            }
            else
            {
                Logger.WriteLine(Logger.LogLevel.Warn, $"Duplicate device topic: '{topic}' for {device.Name} and {CommandTopicToDeviceLookupTable[topic].Name}.");
            }
        }
    }

    // Handle command topic changes
    protected override async Task ProcessMessagePayloadAsync(MqttApplicationMessage message)
    {
        string topic = message.Topic;
        string data = message.ConvertPayloadToString();

        Logger.WriteLine(Logger.LogLevel.Info, $"Topic '{topic}' updated with '{data}'.");

        IHomeAssistantDevice? device;
        if (CommandTopicToDeviceLookupTable.TryGetValue(topic, out device))
        {
            Logger.WriteLine(Logger.LogLevel.Info, $"Device '{device.Name}' running command '{data}'.");
            device.RunCommand(data);
        } 
        await base.ProcessMessagePayloadAsync(message);
    }

    protected async Task UpdateDevicesAvailabilityAsync(IEnumerable<IHomeAssistantDevice> devices, bool isAvailable)
    {
        if (!IsHomeAssistantDiscoveryEnabled) return;

        foreach(var device in devices)
        {
            string payload = isAvailable ? device.MqttPayloadAvailable ?? PAYLOAD_AVAILABLE 
                                         : device.MqttPayloadNotAvailable ?? PAYLOAD_OFFLINE;
            try
            {
                var topic = device.MqttAvailabilityTopic;
                Logger.WriteLine(Logger.LogLevel.Debug, $"Setting device '{device.Name}' availability topic '{topic}' to '{payload}'.");
                await PublishStringAsync(topic, payload, 0, true);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(Logger.LogLevel.Error, $"Unable to publish availability information: {ex.Message}");
            }
        }
    }

    protected async Task UpdateHomeAssistantDiscoveryAsync(HomeAssistantConfig config, IEnumerable<IHomeAssistantDevice> devices)
    {
        if (!IsHomeAssistantDiscoveryEnabled)
        {
            return;
        }
        
        foreach(var device in devices)
        {
            await UpdateDeviceDiscoveryAsync(config, device);
        }
    }

    protected async Task UpdateDeviceDiscoveryAsync(HomeAssistantConfig config, IHomeAssistantDevice device)
    {

        if (string.IsNullOrEmpty(device.EntityId))
        {
            throw new InvalidDataException("Home Assistant discovery requires the entity_id be configured.");
        }

        string prefix = config.DiscoveryPrefix ?? "homeassistant";
        string discoveryTopic = $"{prefix}/{device.DeviceType}/{device.EntityId}/config";
        Logger.WriteLine(Logger.LogLevel.Debug, $"Adding discovery info under topic: {discoveryTopic}");

        var dataObj = new {
            name = device.Name,
            uniq_id = device.UniqueId,
            state_topic = device.MqttStateTopic,
            
            command_topic = device.MqttCommandTopic,
            payload_on = device.MqttPayloadOn ?? PAYLOAD_ON,
            payload_off = device.MqttPayloadOff ?? PAYLOAD_OFF,
            icon = device.Icon ?? "mdi:switch",
            
            availability_topic = device.MqttAvailabilityTopic,
            payload_available = device.MqttPayloadAvailable ?? PAYLOAD_AVAILABLE,
            payload_not_available = device.MqttPayloadNotAvailable ?? PAYLOAD_OFFLINE,

            qos = device.MqttQualityOfServiceLevel,
            retain = device.MqttRetainValue,

            dev = new {
                name = config.DeviceName ?? "MQTT Helper",
                model = config.DeviceModel ?? "none",
                sw_version = "1.0",
                manufacturer = "Ryan Gregg",
                identifiers = config.DeviceUniqueId
            }
        };

        string payload = JsonSerializer.Serialize(dataObj);
        Logger.WriteLine(Logger.LogLevel.Debug, $"Discovery payload: {payload}");
        try 
        {
            await PublishStringAsync(discoveryTopic, payload, 0, true);
            Logger.WriteLine(Logger.LogLevel.Info, "Published Home Assistant discovery payload.");
        }
        catch (Exception ex)
        {
            Logger.WriteLine(Logger.LogLevel.Warn, $"Error publishing discovery payload: {ex.Message}");
        }
    }

    protected override async Task PreDisconnectAsync()
    {
        await UpdateDevicesAvailabilityAsync(Devices, false);
    }

    protected async Task SetDeviceStateAsync(IHomeAssistantDevice device, string payload)
    {
        if (!IsHomeAssistantDiscoveryEnabled) return;

        Logger.WriteLine(Logger.LogLevel.Debug, $"Setting device '{device.Name}' state to '{payload}'");

        try
        {
            await PublishStringAsync(device.MqttStateTopic, payload, 0, true);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(Logger.LogLevel.Error, $"Unable to publish state information: {ex.Message}");
        }
    }

    protected async Task SetDeviceCurrentStateAsync()
    {
        foreach(var device in Devices)
        {
            string? newState = device.GetCurrentState();
            if (newState != null)
            {
                Logger.WriteLine(Logger.LogLevel.Info, $"Setting state for '{device.Name}' to '{newState}'.");
                await SetDeviceStateAsync(device, newState);
            }
        }
    }

    public delegate void DeviceStateEventHandler(object sender, DeviceStateEventArgs args);
    public event DeviceStateEventHandler? UpdateDeviceState;

    protected string? GetDeviceState(IHomeAssistantDevice device) 
    {
        var args = new DeviceStateEventArgs(device);
        UpdateDeviceState?.Invoke(this, args);
        return args.State;
    }

}

public class DeviceStateEventArgs : EventArgs
{
    public IHomeAssistantDevice Device {get; private set; }
    public string? State { get; set; }

    public DeviceStateEventArgs(IHomeAssistantDevice device)
    {
        this.Device = device;
        this.State = null;
    }

}