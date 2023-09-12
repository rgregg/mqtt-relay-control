using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Configuration 
{

    [YamlMember(Alias = "serial-port")]
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
}

public class LoggingConfig
{
    [YamlMember(Alias = "filename" )]
    public string? Filename {get;set;}

    [YamlMember(Alias = "level")]
    public Logger.LogLevel? Level {get;set;}
}