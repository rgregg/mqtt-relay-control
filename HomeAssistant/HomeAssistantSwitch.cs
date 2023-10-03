public class HomeAssistantSwitch : IHomeAssistantDevice
{
    public HomeAssistantSwitch(string? uniqueId, string? entityId, string? name, string? icon, string mqttPrefix)
    {
        this.UniqueId = uniqueId ?? "";
        this.EntityId = entityId ?? "";
        this.Name = name ?? "";
        this.Icon = icon ?? "mdi:switch";
        this.MqttPrefix = mqttPrefix;
    }

    public string UniqueId { get; set; }
    public string EntityId {get;set;}
    public string Name {get;set;}
    public string Icon {get;set;}
    public string DeviceType { get { return "switch"; }}
    public string? MqttPayloadOn { get; set; }
    public string? MqttPayloadOff {get; set;}
    public string? MqttPayloadAvailable {get;set;}
    public string? MqttPayloadNotAvailable {get;set;}
    public int? MqttQualityOfServiceLevel {get;set;}
    public bool? MqttRetainValue {get;set;}

    private readonly string MqttPrefix;
    public string MqttStateTopic 
    { 
        get { 
            return $"{MqttPrefix}/{this.UniqueId}";
        }
    }

    public string MqttCommandTopic 
    {
        get {
            return MqttStateTopic + "/set";
        }
    }

    public string MqttAvailabilityTopic
    {
        get { return MqttStateTopic + "/available"; }
    }

    public virtual void RunCommand(string command)
    {
        throw new NotImplementedException();
    }

    public virtual string GetCurrentState()
    {
        throw new NotImplementedException();
    }

}