schtasks /end /tn "Downloads Sorter"
dotnet publish .\DownloadsSorter.sln /p:PublishProfile=FolderProfile
schtasks /run /tn "Downloads Sorter"
PAUSE