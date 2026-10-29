using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace MiniThemeStudio.App.Diagnostics;

public static class FileLogging
{
    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MiniThemeStudio",
        "Logs");

    // one file per day (minithemestudio-YYYYMMDD.log), the last two weeks are kept
    public static ILoggingBuilder AddFileLogging(this ILoggingBuilder builder)
    {
        Logger logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(LogsDirectory, "minithemestudio-.log"),
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return builder
            .SetMinimumLevel(LogLevel.Information)
            .AddSerilog(logger, dispose: true);
    }
}
