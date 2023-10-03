using System.IO.Ports;

public class SerialPortRelayControl
{
    public enum RelayState
    {
        Unknown = 0,
        Open,
        Closed
    }

    private static readonly byte[] OpenRelayCommand = { 0xA0, 0x1, 0x1, 0xA2};
    private static readonly byte[] CloseRelayCommand = { 0xA0, 0x1, 0x0, 0xA1};

    private readonly SerialPort Port;
    private readonly Logger Logger;
    private readonly UserSettings Settings;
    private readonly string Identifier;

    public RelayState CurrentState {get; private set;}

    public event EventHandler? RelayStateChanged;

    public SerialPortRelayControl(string identifier, SerialPort port, Logger logger, UserSettings settings)
    {
        this.Port = port;
        this.Logger = logger;
        this.Settings = settings;
        this.Identifier = identifier;
    }

    private void ResumeLastRelayState()
    {
        string? previousValue = Settings.GetValue(Identifier + LAST_RELAY_STATE);
        if (!string.IsNullOrEmpty(previousValue))
        {
            switch (previousValue)
            {
                case STATE_OPEN:
                    OpenRelay();
                    break;
                case STATE_CLOSED:
                    CloseRelay();
                    break;
            }
        }
    }
    
    private const string LAST_RELAY_STATE = "LastRelayState";
    private const string STATE_OPEN = "open";
    private const string STATE_CLOSED = "closed";

    public void OpenRelay()
    {
        Logger.WriteLine(Logger.LogLevel.Info, "Opening relay connection");
        SendBytes(OpenRelayCommand);
        this.CurrentState = RelayState.Open;

        Settings.SetValue(Identifier + LAST_RELAY_STATE, STATE_OPEN);
        Settings.WriteAsync().Wait();

        RelayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseRelay()
    {
        Logger.WriteLine(Logger.LogLevel.Info, "Closing relay connection");
        SendBytes(CloseRelayCommand);
        this.CurrentState = RelayState.Closed;

        Settings.SetValue(Identifier + LAST_RELAY_STATE, STATE_CLOSED);
        Settings.WriteAsync().Wait();

        RelayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SendBytes(byte[] bytes) 
    {
        if (!Port.IsOpen) 
        { 
            Logger.WriteLine(Logger.LogLevel.Info, "SerialPort is closed. Opening before writing.");
            try
            {
                Port.Open();
            }
            catch (Exception ex)
            {
                Logger.WriteLine(Logger.LogLevel.Error, $"Unable to open serial port. {ex.Message}");
                return;
            }
        }
        Port.Write(bytes, 0, bytes.Length);
    }

    public void CloseSerialPort() 
    {
        Logger.WriteLine(Logger.LogLevel.Info, "SerialPort closed.");
        Port.Close();
    }

}