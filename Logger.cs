public class Logger
{

    public enum LogLevel
    {
        Debug = 10,
        Info = 5,
        Warn = 3,
        Error = 1
    }

    public LogLevel Level {get;set;}

    private StreamWriter outputWriter;

    private readonly string prefixText = "";

    public Logger() : this(LogLevel.Info)
    {

    }

    public Logger(string filename) : this(LogLevel.Info, new StreamWriter(File.OpenWrite(filename)))
    {
    }

    public Logger(LogLevel level, StreamWriter writer)
    {
        this.Level = level;
        this.outputWriter = writer;
    }

    public Logger(LogLevel level) : this(level, new StreamWriter(Console.OpenStandardOutput()))
    {

    }

    public Logger(Logger parent, string prefixText)
    {
        this.prefixText = prefixText;
        this.outputWriter = parent.outputWriter;
        this.Level = parent.Level;
    }

    public void Configure(LoggingConfig config)
    {
        if (config.Level.HasValue) {
            this.Level = config.Level.Value;
            this.WriteLine(LogLevel.Debug, $"Logger level set to {this.Level}");
        }
        if (config.Filename != null) {
            this.outputWriter = File.CreateText(config.Filename);
            this.WriteLine(LogLevel.Debug, $"Logger output changed to {config.Filename}");
        }
    }


    public void WriteLine(LogLevel level, string line)
    {
        if (this.Level >= level) 
        {
            outputWriter.WriteLine(prefixText + line);
            outputWriter.Flush();
        }
    }

}