using System.Text.Json;
public class UserSettings
{
    private Dictionary<string, string> userSettings = new Dictionary<string, string>();
    private readonly string filePath;
    private readonly Logger logger;
    public UserSettings(Logger logger)
    {
        this.logger = logger;
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        filePath = Path.Combine(appdata, "mqtt-relay-control", "settings.json");

        logger.WriteLine(Logger.LogLevel.Debug, $"Settings file: {filePath}");
    }

    bool hasDataChanged = false;

    public async Task LoadAsync() 
    {
        logger.WriteLine(Logger.LogLevel.Debug, "Reading data from disk.");
        if (File.Exists(filePath))
        {
            using (var reader = File.OpenText(filePath))
            {
                var data = await reader.ReadToEndAsync();

                try
                {
                Dictionary<string, string>? parsedData = JsonSerializer.Deserialize<Dictionary<string, string>>(data);
                if (null != parsedData)
                {
                    userSettings = parsedData;
                    hasDataChanged = false;
                }
                } catch (Exception ex)
                {
                    logger.WriteLine(Logger.LogLevel.Warn, $"Unable to parse user settings file: {ex.Message}.");
                    userSettings.Clear();
                    hasDataChanged = false;
                }
            }
        }
        else
        {
            logger.WriteLine(Logger.LogLevel.Debug, "No user settings on disk.");
            userSettings.Clear();
            hasDataChanged = false;
        }
    }

    public async Task WriteAsync()
    {
        if (!hasDataChanged)
        {
            logger.WriteLine(Logger.LogLevel.Debug, "No data changed, write skipped.");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        } catch (Exception ex) 
        {
            logger.WriteLine(Logger.LogLevel.Error, $"Unable to create app data directory. Settings will not persist. {ex.Message}");
            return;
        }

        string jsonString = JsonSerializer.Serialize(userSettings);

        using (var output = File.OpenWrite(filePath))
        {
            StreamWriter writer = new StreamWriter(output);
            await writer.WriteLineAsync(jsonString);
            await writer.FlushAsync();
            logger.WriteLine(Logger.LogLevel.Debug, "Wrote user settings to disk.");
        }
    }

    public string? GetValue(string name)
    {
        if (userSettings.ContainsKey(name))
        {
            return userSettings[name];
        }
        return null;
    }

    public void SetValue(string name, string value)
    {
        if (userSettings.ContainsKey(name) && userSettings[name] == value)
        {
            logger.WriteLine(Logger.LogLevel.Debug, "SetValue was same as existing value, update skipped.");
            return;
        }
        
        userSettings[name] = value;
        hasDataChanged = true;
    }
}