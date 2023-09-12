using System.IO.Ports;

public class SerialPortRelayControl
{
    private static readonly byte[] OpenRelayCommand = { 0xA0, 0x1, 0x1, 0xA2};
    private static readonly byte[] CloseRelayCommand = { 0xA0, 0x1, 0x0, 0xA1};

    public SerialPort port;
    private Logger logger;

    public SerialPortRelayControl(SerialPort port, Logger logger)
    {
        this.port = port;
        this.logger = logger;
    }
    
    public void OpenRelay()
    {
        logger.WriteLine(Logger.LogLevel.Info, "RELAY: Opening relay connection");
        SendBytes(OpenRelayCommand);
    }

    public void CloseRelay()
    {
        logger.WriteLine(Logger.LogLevel.Info, "RELAY: Closing relay connection");
        SendBytes(CloseRelayCommand);
    }

    private void SendBytes(byte[] bytes) 
    {
        if (!port.IsOpen) 
        { 
            logger.WriteLine(Logger.LogLevel.Info, "SerialPort is closed. Opening.");
            port.Open(); 
        }
        port.Write(bytes, 0, bytes.Length);
    }

    public void CloseSerialPort() 
    {
        port.Close();
    }

}