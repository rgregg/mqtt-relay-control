// See https://aka.ms/new-console-template for more information
using System.IO.Ports;
using System.Runtime.CompilerServices;
using YamlDotNet;

public static class Program {

    private const int EC_OK = 0;
    private const int EC_ERROR = 2;

    private static Logger logger = new Logger(Logger.LogLevel.Debug);
    private static bool sigIntReceived = false;
    private static CancellationTokenSource appCancelTokenSource = new CancellationTokenSource();
    private static UserSettings? settings;

    static async Task<int> Main (string[] args)
    {
        var exitHandler = new TaskCompletionSource();
        ConfigureExitHandeling(exitHandler);

        Configuration? config = ParseArguments(args);
        if (null == config)
        {
            Console.WriteLine("Must specify configuration file in the command line arguments");
            return EC_ERROR;
        }

        if (config.Logging != null)
        {
            logger.Configure(config.Logging);
        }
        settings = new UserSettings(new Logger(logger, "SETTINGS: "));
        await settings.LoadAsync();

        if (config.Mqtt == null)
        {
            logger.WriteLine(Logger.LogLevel.Error, "Must specify MQTT configuration in the config file.");
            return EC_ERROR;
        }

        // launch the MQTT interface
        HomeAssistantMqttClient client = new HomeAssistantMqttClient(new Logger(logger, "MQTT: "), config.Mqtt, config.HomeAssistant, appCancelTokenSource.Token);
        
        // Register any configured HA devices
        var mqttPrefix = config.HomeAssistant?.DeviceTopicPrefix ?? "devices/relaycontrol";
        var switches = from d in config.RelayControl
                                 select new SerialPortSwitch(d, mqttPrefix, new Logger(logger, "RELAY: "), settings);
        client.RegisterDevices(switches);

        // Register any SignalK mappings in the configuration
        ZigbeeToSignalKMapping? signalk = null;
        if (config.SignalKConfig != null)
        {
            signalk = new ZigbeeToSignalKMapping(config.SignalKConfig, client, new Logger(logger, "SignalK: "));
        }

        if (await client.ConnectAsync())
        {
            if (null != signalk) 
            {
                await signalk.SubscribeToTopicsAsync();
            }

            // Control flow stops on the next line until we receive a command to exit the process
            await exitHandler.Task;
            await client.DisconnectAsync();
            return EC_OK;
        }
        else
        {
            // Failed to connect to the server, so we abort
            return EC_ERROR;
        }
    }

    static void ConfigureExitHandeling(TaskCompletionSource tcs)
    {
        Console.CancelKeyPress += (_, ea) =>
        {
            // Tell .NET to not terminate the process
            ea.Cancel = true;
            logger.WriteLine(Logger.LogLevel.Info, "received SIGINT (Ctrl+C)");
            tcs.SetResult();
            sigIntReceived = true;
            appCancelTokenSource.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!sigIntReceived)
            {
                logger.WriteLine(Logger.LogLevel.Debug, "Received SIGTERM");
                tcs.SetResult();
            }
            else
            {
                logger.WriteLine(Logger.LogLevel.Debug, "Received SIGTERM, ignoring it because already processed SIGINT");
            }
        };
    }

    static Configuration? ParseArguments(string[] args)
    {
        if (args.Length == 1)
        {
            var configFile = args[0];
            logger.WriteLine(Logger.LogLevel.Info, $"Using configuration file: {configFile}");
            try 
            {
                return Configuration.FromYaml(configFile);
            }
            catch (Exception ex) 
            {
                logger.WriteLine(Logger.LogLevel.Error, "Error reading configuration: " + ex.ToString());
                throw;
            }
        }
        return null;
    }
}



