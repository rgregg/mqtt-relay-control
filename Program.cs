// See https://aka.ms/new-console-template for more information
using System.IO.Ports;
using System.Runtime.CompilerServices;
using YamlDotNet;

public static class Program {

    private const int EC_OK = 0;
    private const int EC_ERROR = 2;

    private enum CommandInput {
        CloseSerialPort,
        OpenSerialPort,
        OpenRelay,
        CloseRelay,
        Quit
    }

    private static Logger logger = new Logger(Logger.LogLevel.Debug);
    private static bool sigIntReceived = false;
    private static CancellationTokenSource appCancelTokenSource = new CancellationTokenSource();
    private static UserSettings? settings;

    static async Task<int> Main (string[] args)
    {
        var tcs = new TaskCompletionSource();
        ConfigureExitHandeling(tcs);

        

        Configuration? config = ParseArguments(args);
        if (null == config)
        {
            try 
            {
                config = await ConfigurationFromUserAsync();
            }
            catch 
            {
                return EC_ERROR;
            }
        }

        if (config.Logging != null)
        {
            logger.Configure(config.Logging);
        }
        settings = new UserSettings(new Logger(logger, "SETTINGS: "));
        await settings.LoadAsync();

        var relay = new SerialPortRelayControl(GetSerialPort(config), new Logger(logger, "RELAY: "), settings);

        if (null != config.Mqtt)
        {
            logger.WriteLine(Logger.LogLevel.Info, "Using MQTT broker. Starting client.");
            // launch the MQTT interface
            MqttBroker broker = new MqttBroker(new Logger(logger, "MQTT: "), config.Mqtt, relay, appCancelTokenSource.Token);
            if (await broker.ConnectAsync())
            {
                // Control flow stops on the next line until we receive a command to exit the process
                await tcs.Task;
                await broker.DisconnectAsync();
                return EC_OK;
            }
            else
            {
                // Failed to connect to the server, so we abort
                return EC_ERROR;
            }
        }
        else
        {
            // launch CLI based interface
            return await MainUserLoopAsync(config, relay);
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


        // for(int i = 0; i<args.Length; i++)
        // {
        //     string arg = args[i];
        //     switch(arg)
        //     {
        //         case "-c":  // configuration file
        //             if (i+1<args.Length) 
        //             {
        //                 string configFile = args[++i];
        //                 logger.WriteLine(Logger.LogLevel.Info, $"Using configuration file: {configFile}");
        //                 try 
        //                 {
        //                     return Configuration.FromYaml(configFile);
        //                 }
        //                 catch (Exception ex) 
        //                 {
        //                     logger.WriteLine(Logger.LogLevel.Error, "Error reading configuration: " + ex.ToString());
        //                     throw;
        //                 }
        //             }
        //             i++;
        //             break;
        //     }
        // }
        return null;
    }

    static async Task<Configuration> ConfigurationFromUserAsync() 
    {
        var config = new Configuration
        {
            SerialPort = new SerialPortConfig() { Port = await SelectSerialPortAsync(), Baud = 9600},
            Logging = new LoggingConfig() { Level = Logger.LogLevel.Warn }
        };
        return config;
    }

    static SerialPort GetSerialPort(Configuration config) 
    {
        if (null == config.SerialPort || null == config.SerialPort.Port)
        {
            throw new Exception("No serial port specified.");
        }
        
        SerialPort port = new SerialPort(config.SerialPort.Port);
        if (config.SerialPort.Baud.HasValue) 
        {
            port.BaudRate = config.SerialPort.Baud.Value;
        }
        logger.WriteLine(Logger.LogLevel.Info, $"Using serial port {port.PortName} and baud rate {port.BaudRate}.");
        return port;
    }

    static async Task<int> MainUserLoopAsync(Configuration config, SerialPortRelayControl relay) 
    {
        bool doLoop = true;
        var commands = Enum.GetNames(typeof(CommandInput));

        while(doLoop) 
        {
            string? input = await SelectStringFromListAsync(commands, "Command");
            if (null == input) 
            {
                logger.WriteLine(Logger.LogLevel.Warn, "Invalid input. Try again, using the full command or command number.");
                continue;
            }

            CommandInput command = Enum.Parse<CommandInput>(input, true);
            switch(command) 
            {
                case CommandInput.CloseSerialPort:
                    relay.CloseSerialPort();
                    break;

                case CommandInput.OpenRelay:
                    relay.OpenRelay();
                    break;

                case CommandInput.CloseRelay:
                    relay.CloseRelay();
                    break;

                case CommandInput.Quit:
                    relay.CloseSerialPort();
                    doLoop = false;
                    break;
            }
        }
        return EC_OK;
    }

    static async Task<string?> SelectStringFromListAsync(string[] list, string prompt)
    {
        int i = 1;
        foreach(var item in list) 
        {
            Console.WriteLine($"{i++} - {item}");
        }

        Console.Write($"{prompt}: ");
        var input = await ReadLineAsync(appCancelTokenSource.Token);
        if (null == input) 
        {
            return null;
        }

        if (int.TryParse(input, out int selectedIndex) && selectedIndex > 0 && selectedIndex <= list.Length)
        {
            // Accept the index as input
            return list[selectedIndex - 1];
        }
        else
        {
            // Accept the string value as input
            var query = (from p in list where p.ToLower() == input.ToLower() select p).FirstOrDefault();
            if (null != query)
            {
                return query;
            }
        }

        return null;
    }

    private static Task<string?>? readTask = null;

    private static async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        readTask ??= Task.Run(
            () => {
                try {
                return Console.ReadLine();
                } catch { return null; }
            }
        );
        await Task.WhenAny(readTask, Task.Delay(-1, cancellationToken));

        if (cancellationToken.IsCancellationRequested)
        {
            readTask = null;
            return null;
        }

        string? result = await readTask;
        readTask = null;

        return result;
    }

    static async Task<string> SelectSerialPortAsync() {
        var ports = SerialPort.GetPortNames();
        string? portName = await SelectStringFromListAsync(ports, "Select a serial port");
        if (null != portName)
            return portName;

        Console.WriteLine("Invalid selection.");
        throw new IndexOutOfRangeException("Invalid input. No port was selected.");
    }
}



