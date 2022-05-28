# DownloadsSorter

This quality of life service sorts your downloads folder for you and renames the file extension to match the file's actual MIME type.

![Example sorted Downloads folder](https://user-images.githubusercontent.com/58752614/170612703-6a60745e-ba16-4223-b075-d7bc2be393f7.png)

## Details

* Stores logs in: `%appdata%\DownloadsSorter\`
* Designed to be run as a background service. I've found setting it up as a scheduled task works well.
  *  `schtasks /create /sc ONLOGON /tn "Downloads Sorter" /u <username> /p <password> /tr "Path/To/DownloadSorter.exe"`
