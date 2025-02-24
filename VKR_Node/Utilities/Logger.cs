namespace VKR_Node.Utilities;

public static class Logger
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLevel = level.ToString().ToUpper();

        Console.WriteLine($"[{timestamp}] [{logLevel}] {message}");
    }

    public static void Info(string message)
    {
        Log(message, LogLevel.Info);
    }

    public static void Warning(string message)
    {
        Log(message, LogLevel.Warning);
    }

    public static void Error(string message)
    {
        Log(message, LogLevel.Error);
    }
}