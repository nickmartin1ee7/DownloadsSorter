using DownloadsSorter;

using MimeDetective;
using MimeDetective.Definitions;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $"{nameof(DownloadsSorter)}", ".log"),
        rollingInterval: RollingInterval.Day,
        retainedFileTimeLimit: TimeSpan.FromDays(7))
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateLogger();

using var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .UseWindowsService()
    .ConfigureServices(services =>
    {
        // MimeDetector
        services.AddSingleton<ContentInspector>(_ => new ContentInspectorBuilder
        {
            Definitions = Default.All(),
            Parallel = true
        }.Build());
        // System.FileSystemWatcher
        services.AddSingleton<FileSystemWatcher>(_ => new FileSystemWatcher(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"))
        {
            NotifyFilter = NotifyFilters.FileName,
        });
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
