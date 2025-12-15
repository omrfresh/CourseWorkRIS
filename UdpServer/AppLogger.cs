using System;
using System.IO;

public static class AppLogger
{
    private static readonly object _lock = new object();
    private static string _logFilePath;
    public static bool WriteToConsole { get; set; } = false;

    public static string LogFilePath => _logFilePath;

    public static void Initialize(string applicationName)
    {
        string tempPath = Path.GetTempPath();
        _logFilePath = Path.Combine(tempPath, "UdpImageProcessor.log");

        if (!File.Exists(_logFilePath) || new FileInfo(_logFilePath).LastWriteTime.Date != DateTime.Today)
        {
            File.WriteAllText(_logFilePath, string.Empty);
        }

        Log($"--- Приложение '{applicationName}' запущено ---");
    }

    public static void Log(string message)
    {
        lock (_lock)
        {
            try
            {
                string formattedMessage = $"{DateTime.Now:HH:mm:ss.fff} | {message}";

                File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);

                if (WriteToConsole)
                {
                    Console.WriteLine(formattedMessage);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}