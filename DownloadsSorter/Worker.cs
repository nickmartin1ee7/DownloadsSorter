using System.Collections.Immutable;

using MimeDetective;
using MimeDetective.Engine;

namespace DownloadsSorter
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ContentInspector _mimeInspector;
        private readonly FileSystemWatcher _fileWatcher;

        public Worker(ILogger<Worker> logger,
            ContentInspector mimeInspector,
            FileSystemWatcher fileWatcher)
        {
            _logger = logger;
            _mimeInspector = mimeInspector;
            _fileWatcher = fileWatcher;

            _fileWatcher.Created += OnFileCreated;

            _fileWatcher.EnableRaisingEvents = true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(-1, stoppingToken);
            }
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                var targetFile = new FileInfo(e.FullPath);

                if (targetFile.DirectoryName is null) return;

                byte[] fileContent;
                bool warnOnce = false;

                while (true)
                {
                    try
                    {
                        fileContent = await File.ReadAllBytesAsync(targetFile.FullName);
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (!warnOnce)
                        {
                            _logger.LogWarning("Failed to read new file ({fileName}): {exMessage}",
                                targetFile.Name,
                                ex.Message);

                            warnOnce = true;
                        }

                        await Task.Delay(1000);
                    }
                }

                var mimeInfo = _mimeInspector.Inspect(fileContent);

                var bestDefinition = GetBestDefinition(mimeInfo);

                if (bestDefinition is null)
                {
                    _logger.LogWarning("No definitions found for file ({fileName})", targetFile.Name);
                }

                string bestExtension = targetFile.Extension.Replace(".", "");

                if (bestDefinition != null
                    && bestDefinition.Definition.File.Extensions.Any()
                    && !bestDefinition.Definition.File.Extensions.Contains(bestExtension))
                {
                    bestExtension = bestDefinition.Definition.File.Extensions.First();
                    _logger.LogInformation("Updating file extension for {fileName} from {oldFileExtension} to {newFileExtension} in order to match it's MIME type ({mimeType})",
                        targetFile.Name, targetFile.Extension, $".{bestExtension}", bestDefinition.Definition.File.MimeType);
                }

                var categoryName = bestExtension.ToUpper();

                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    _logger.LogWarning("No extension/category able to be determined for file ({fileName}). Giving up!", targetFile.Name);
                    return;
                }

                var newFilePath = DetermineNewFileEntry(
                    Directory.CreateDirectory(
                        Path.Combine(targetFile.DirectoryName,
                            categoryName)),
                    $"{Path.GetFileNameWithoutExtension(targetFile.FullName)}.{bestExtension}");

                _logger.LogInformation("New file ({fileName}) moving to category: {category}",
                    targetFile.Name,
                    categoryName);

                targetFile.MoveTo(newFilePath.FullName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle new file ({fileName})", e.Name);
            }
        }

        private static DefinitionMatch? GetBestDefinition(ImmutableArray<DefinitionMatch> matches)
        {
            if (!matches.Any()) return null;
            return matches.OrderByDescending(m => m.Percentage).FirstOrDefault();
        }

        private static FileInfo DetermineNewFileEntry(DirectoryInfo saveLocation, string fileName)
        {
            var existingFiles = saveLocation.GetFiles();
            var idealFileInfo = new FileInfo(Path.Combine(saveLocation.FullName, fileName));
            bool idealFileAlreadyExists = existingFiles.FirstOrDefault(f => f.Name == idealFileInfo.Name) is not null;
            var actualFileInfo = idealFileInfo;

            if (idealFileAlreadyExists)
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(idealFileInfo.Name);
                var existingSimilarFiles = existingFiles.Count(f => f.Name.StartsWith(fileNameWithoutExtension));
                var filePath = Path.Combine(saveLocation.FullName, $"{fileNameWithoutExtension}_{existingSimilarFiles}{actualFileInfo.Extension}");
                actualFileInfo = new FileInfo(filePath); // Recreate filename with fileName_#_.ext
            }

            return actualFileInfo;
        }
    }
}