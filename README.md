# ytdl - A YouTube video downloader.

## Requirements
This project requires [ffmpeg](https://www.ffmpeg.org/) to be available from your system's PATH, otherwise you won't be able to download [DASH streams](https://en.wikipedia.org/wiki/Dynamic_Adaptive_Streaming_over_HTTP)
and you will be limited to below 720p.
## Building
Install the [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) and run `dotnet build -c Release`, or use [Visual Studio](https://visualstudio.microsoft.com/) (Windows only).
