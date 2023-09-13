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

    public SerialPort port;
    private Logger logger;
    private UserSettings settings;

    public RelayState CurrentState {get; private set;}

    public event EventHandler? RelayStateChanged;

    public SerialPortRelayControl(SerialPort port, Logger logger, UserSettings settings)
    {
        this.port = port;
        this.logger = logger;
        this.settings = settings;
    }

    private void ResumeLastRelayState()
    {
        string? previousValue = settings.GetValue(LAST_RELAY_STATE);
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
        logger.WriteLine(Logger.LogLevel.Info, "Opening relay connection");
        SendBytes(OpenRelayCommand);
        this.CurrentState = RelayState.Open;

        settings.SetValue(LAST_RELAY_STATE, STATE_OPEN);
        settings.WriteAsync().Wait();

        RelayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseRelay()
    {
        logger.WriteLine(Logger.LogLevel.Info, "Closing relay connection");
        SendBytes(CloseRelayCommand);
        this.CurrentState = RelayState.Closed;

        settings.SetValue(LAST_RELAY_STATE, STATE_CLOSED);
        settings.WriteAsync().Wait();

        RelayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SendBytes(byte[] bytes) 
    {
        if (!port.IsOpen) 
        { 
            logger.WriteLine(Logger.LogLevel.Info, "SerialPort is closed. Opening before writing.");
            try
            {
                port.Open();
            }
            catch (Exception ex)
            {
                logger.WriteLine(Logger.LogLevel.Error, $"Unable to open serial port. {ex.Message}");
                return;
            }
        }
        port.Write(bytes, 0, bytes.Length);
    }

    public void CloseSerialPort() 
    {
        logger.WriteLine(Logger.LogLevel.Info, "SerialPort closed.");
        port.Close();
    }

}