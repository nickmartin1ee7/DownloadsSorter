using DownloadsSorter;

using MimeDetective;
using MimeDetective.Definitions;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<ContentInspector>(_ => new ContentInspectorBuilder
        {
            Definitions = Default.All(),
            Parallel = true
        }.Build());
        services.AddSingleton<FileSystemWatcher>(_ => new FileSystemWatcher(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"))
        {
            NotifyFilter = NotifyFilters.FileName,
        });
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
