using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Configuration 
{

    [YamlMember(Alias = "serial_port")]
    public SerialPortConfig? SerialPort {get;set;}
    [YamlMember(Alias = "mqtt")]
    public MqttConfig? Mqtt {get;set;}

    [YamlMember(Alias = "logging")]
    public LoggingConfig? Logging {get;set;}

    public static Configuration FromYaml(string path)
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
//                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            .Build();
        var myConfig = deserializer.Deserialize<Configuration>(File.ReadAllText(path));
        return myConfig;
    }
}


public class SerialPortConfig
{
    [YamlMember(Alias = "port")]
    public string? Port { get; set; }
    
    [YamlMember(Alias = "baud")]
    public int? Baud { get; set; }
}

public class MqttConfig
{
    [YamlMember(Alias = "host")]
    public string? Host {get;set;}
    
    [YamlMember(Alias = "port")]
    public int? Port {get;set;}
    
    [YamlMember(Alias = "topic")]
    public string? Topic {get;set;}
    
    [YamlMember(Alias = "home_assistant")]
    public HomeAssistantConfig? HomeAssistant {get;set;}

    [YamlMember(Alias = "keep_alive_seconds")]
    public int? KeepAliveSeconds {get;set;}

    [YamlMember(Alias = "reconnect_timeout")]
    public int? ReconnectTimeout {get;set;}

    [YamlMember(Alias = "connection_timeout")]
    public int? ConnectionTimeout {get;set;}

    [YamlMember(Alias = "initial_connection_attempts")]
    public int? InitialConnectionAttempts {get;set;}
}

public class HomeAssistantConfig
{
    [YamlMember(Alias = "discovery")]
    public bool? DiscoveryEnabled {get;set;}

    [YamlMember(Alias = "guid")]
    public string? UniqueID {get;set;}

    [YamlMember(Alias ="discovery_prefix")]
    public string? DiscoveryPrefix {get;set;}

    [YamlMember(Alias = "entity_id")]
    public string? EntityId {get;set;}

    [YamlMember(Alias = "name")]
    public string? Name {get;set;}

}

public class LoggingConfig
{
    [YamlMember(Alias = "filename" )]
    public string? Filename {get;set;}

    [YamlMember(Alias = "level")]
    public Logger.LogLevel? Level {get;set;}
}