using System.IO.Ports;

/// <summary>
/// Implementation of a Home Assistant Switch that is mapped to a serial port relay
/// </summary>
public class SerialPortSwitch : HomeAssistantSwitch
{
    private readonly Logger logger;
    public SerialPortSwitch(RelayControlConfig device, string mqttPrefix, Logger logger, UserSettings settings)
        : base(device.UniqueID, device.EntityId, device.Name, device.Icon, mqttPrefix)
    {
        this.logger = logger;
        this.PortConfig = device.SerialPort;
        this.SerialPort = GetSerialPort(this.PortConfig);
        this.RelayControl = new SerialPortRelayControl(device.EntityId ?? "no-entity-id-specified", this.SerialPort, logger, settings);
        this.MqttPayloadOn = HomeAssistantMqttClient.PAYLOAD_ON;
        this.MqttPayloadOff = HomeAssistantMqttClient.PAYLOAD_OFF;
    }

    private SerialPortConfig? PortConfig { get; set; }
    private SerialPort? SerialPort { get; set; }
    private SerialPortRelayControl RelayControl {get;set;}

    
    private SerialPort GetSerialPort(SerialPortConfig? config) 
    {
        if (null == config || null == config.Port)
        {
            throw new Exception("No serial port specified.");
        }
        
        SerialPort port = new SerialPort(config.Port);
        if (config.Baud.HasValue) 
        {
            port.BaudRate = config.Baud.Value;
        }
        logger.WriteLine(Logger.LogLevel.Debug, $"Device {this.Name} using serial port {port.PortName} and baud rate {port.BaudRate}.");
        return port;
    }

    public override void RunCommand(string command)
    {
        if (command.Equals(MqttPayloadOn ?? HomeAssistantMqttClient.PAYLOAD_ON))
        {
            RelayControl.OpenRelay();
        }
        else if (command.Equals(MqttPayloadOff ?? HomeAssistantMqttClient.PAYLOAD_OFF))
        {
            RelayControl.CloseRelay();
        }
        else
        {
            logger.WriteLine(Logger.LogLevel.Warn, $"Unhandled command: '{command}'.");
        }
    }

    public override string GetCurrentState()
    {
        switch(RelayControl.CurrentState)
        {
            case SerialPortRelayControl.RelayState.Open:
                return MqttPayloadOn ?? HomeAssistantMqttClient.PAYLOAD_ON;
            case SerialPortRelayControl.RelayState.Closed:
            case SerialPortRelayControl.RelayState.Unknown:
            default:
                return MqttPayloadOff ?? HomeAssistantMqttClient.PAYLOAD_OFF;;
        }
    }

}