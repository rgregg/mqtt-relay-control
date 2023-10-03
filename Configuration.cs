using System.Security.Cryptography.X509Certificates;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Configuration
{
    [YamlMember(Alias = "mqtt")]
    public MqttConfig? Mqtt { get; set; }

    [YamlMember(Alias = "home_assistant")]
    public HomeAssistantConfig? HomeAssistant { get; set; }


    [YamlMember(Alias = "logging")]
    public LoggingConfig? Logging { get; set; }

    [YamlMember(Alias = "relay_control")]
    public List<RelayControlConfig>? RelayControl { get; set; }

    [YamlMember(Alias = "signalk")]
    public MqttToSignalKConfig? SignalKConfig { get; set; }


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
    public string? Host { get; set; }

    [YamlMember(Alias = "port")]
    public int? Port { get; set; }

    [YamlMember(Alias = "keep_alive_seconds")]
    public int? KeepAliveSeconds { get; set; }

    [YamlMember(Alias = "reconnect_timeout")]
    public int? ReconnectTimeout { get; set; }

    [YamlMember(Alias = "connection_timeout")]
    public int? ConnectionTimeout { get; set; }

    [YamlMember(Alias = "initial_connection_attempts")]
    public int? InitialConnectionAttempts { get; set; }

    [YamlMember(Alias = "username")]
    public string? Username { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }

}



public class RelayControlConfig
{
    [YamlMember(Alias = "guid")]
    public string? UniqueID { get; set; }

    [YamlMember(Alias = "entity_id")]
    public string? EntityId { get; set; }

    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "serial_port")]
    public SerialPortConfig? SerialPort { get; set; }

    [YamlMember(Alias = "icon")]
    public string? Icon { get; set; }

    public HomeAssistantSwitch ToSwitch(string mqttPrefix)
    {
        return new HomeAssistantSwitch(UniqueID ?? "", EntityId ?? "", Name ?? "", Icon ?? "mdi:switch", mqttPrefix);
    }
}

public class HomeAssistantConfig
{
    [YamlMember(Alias = "discovery")]
    public bool? DiscoveryEnabled { get; set; }

    [YamlMember(Alias = "discovery_prefix")]
    public string? DiscoveryPrefix { get; set; }

    [YamlMember(Alias = "device_topic_prefix")]
    public string? DeviceTopicPrefix { get; set; }

    [YamlMember(Alias = "device_unique_id")]
    public string? DeviceUniqueId {get;set;}
    [YamlMember(Alias = "device_model")]
    public string? DeviceModel {get;set;}
    [YamlMember(Alias = "device_name")]
    public string? DeviceName {get;set;}
}

public class LoggingConfig
{
    [YamlMember(Alias = "filename")]
    public string? Filename { get; set; }

    [YamlMember(Alias = "level")]
    public Logger.LogLevel? Level { get; set; }
}

public class MqttToSignalKConfig
{
    [YamlMember(Alias = "system_id")]
    public string? SystemId { get; set; }

    [YamlMember(Alias = "mqtt_mapping")]
    public List<MqttMappingConfig>? Mappings { get; set; }
}

public class MqttMappingConfig
{
    [YamlMember(Alias = "source")]
    public string? Source { get; set; }
    [YamlMember(Alias = "format")]
    public string? Format {get;set;}

    [YamlMember(Alias = "source_topic")]
    public string? SourceTopic { get; set; }

    [YamlMember(Alias = "dest_topic")]
    public string? DestinationTopic { get; set; }
}