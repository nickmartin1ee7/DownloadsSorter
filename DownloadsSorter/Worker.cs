using System.Collections.Immutable;

using HeyRed.Mime;

using MimeDetective;
using MimeDetective.Engine;

namespace DownloadsSorter
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ContentInspector _mainMimeInspector;
        private readonly Magic _backupMimeInspector;
        private readonly Magic _verboseMimeInspector;
        private readonly FileSystemWatcher _fileWatcher;

        public Worker(ILogger<Worker> logger,
            FileSystemWatcher fileWatcher,
            ContentInspector mimeInspector)
        {
            _logger = logger;
            _mainMimeInspector = mimeInspector;
            _backupMimeInspector = new Magic(MagicOpenFlags.MAGIC_EXTENSION);
            _verboseMimeInspector = new Magic(MagicOpenFlags.MAGIC_NONE);
            _fileWatcher = fileWatcher;

            _fileWatcher.Created += OnFileEvent;
            _fileWatcher.Renamed += OnFileEvent;

            _fileWatcher.EnableRaisingEvents = true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(-1, stoppingToken);
            }
        }

        private async void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                var targetFile = new FileInfo(e.FullPath);

                if (targetFile.DirectoryName is null) return;

                byte[] fileContent;
                bool warnOnce = false;

                // Try to read the file until it's no longer in use by another process.
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

                var verboseMagicInfo = _verboseMimeInspector.Read(fileContent, fileContent.Length);
                var backupExtensions = _backupMimeInspector.Read(fileContent, fileContent.Length);

                _logger.LogDebug("{inspector} reports that file ({fileName}) is ({mimeType}) with the following extensions ({possibleExtensions})",
                    nameof(Magic), e.Name, verboseMagicInfo, backupExtensions);

                var mimeInfo = _mainMimeInspector.Inspect(fileContent);

                var bestDefinition = GetBestDefinition(mimeInfo);

                // Default to existing extension.
                string bestExtension = targetFile.Extension.Replace(".", "");

                bool identifiedMime = false;

                // Unknown MIME type by primary MIME inspector.
                if (bestDefinition is null)
                {
                    // Try backup MIME inspector
                    var potentialExtension = backupExtensions
                        .Split('/')
                        .FirstOrDefault();

                    if (potentialExtension is not null
                        && !potentialExtension.Contains('?'))
                    {
                        identifiedMime = true;

                        if (bestExtension != potentialExtension)
                        {
                            bestExtension = potentialExtension;

                            _logger.LogInformation("Updating file extension using ({identifier}) for {fileName} from {oldFileExtension} to {newFileExtension} in order to match it's MIME type",
                                nameof(Magic), targetFile.Name, targetFile.Extension, $".{bestExtension}");
                        }
                    }
                }
                // Replace definition if it doesn't match MIME type.
                else if (bestDefinition.Definition.File.Extensions.Any()
                    && !bestDefinition.Definition.File.Extensions.Contains(bestExtension))
                {
                    bestExtension = bestDefinition.Definition.File.Extensions.First();
                    identifiedMime = true;

                    _logger.LogInformation("Updating file extension using ({identifier}) for {fileName} from {oldFileExtension} to {newFileExtension} in order to match it's MIME type ({mimeType})",
                        nameof(ContentInspector), targetFile.Name, targetFile.Extension, $".{bestExtension}", bestDefinition.Definition.File.MimeType);
                }
                else
                {
                    // Primary MIME Inspector had a definition, and the extension was already accurate
                    identifiedMime = true;
                }

                if (!identifiedMime)
                {
                    _logger.LogWarning("No definitions found for file ({fileName})",
                        targetFile.Name);

                    return;
                }

                // Folder names look better uppercase.
                var categoryName = bestExtension.ToUpper();

                // New file in category folder. Handles duplicate file names as well.
                var newFilePath = DetermineNewFileEntry(
                    Directory.CreateDirectory(
                        Path.Combine(targetFile.DirectoryName,
                            categoryName)),
                    $"{Path.GetFileNameWithoutExtension(targetFile.FullName)}.{bestExtension}");

                _logger.LogInformation("New file ({fileName}) moving to category: {category}",
                    newFilePath.Name,
                    categoryName);

                // Finally, move to new category under new file name.
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