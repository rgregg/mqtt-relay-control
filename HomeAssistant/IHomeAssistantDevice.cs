

public interface IHomeAssistantDevice
{
    public string? EntityId { get; }
    public string? Name { get; }
    public string? UniqueId { get; }
    public string DeviceType { get; }
    public string Icon { get; }

    public string? MqttPayloadOn { get; }
    public string? MqttPayloadOff { get; }
    public string? MqttPayloadAvailable {get;}
    public string? MqttPayloadNotAvailable {get;}

    public string MqttStateTopic { get; }
    public string MqttCommandTopic { get; }
    public string MqttAvailabilityTopic { get; }
    public int? MqttQualityOfServiceLevel { get; }
    public bool? MqttRetainValue { get; }

    public void RunCommand(string command);
    public string GetCurrentState();
    
}