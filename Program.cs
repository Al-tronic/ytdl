namespace ytdl;

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Mono.Options;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

class Program
{
	static int CurrentRow, CurrentCollumn;
	static int LastPercent = -1;
	static List<string> URLs = new();
	static bool AudioOnly = false;
	static bool GetCaptions = false;
	static string CaptionLang = "EN";
	static bool NoDASH = false;
	static bool PlaylistFolder = false;
	static bool ChannelFolder = false;
	static bool SaveThumbnails = false;
	static string OutputDir = "";
	static YoutubeClient? Client;
	static HttpClient? httpClient = null;
	static readonly CancellationTokenSource Source = new();
	static readonly CancellationToken Token = Source.Token;
	static async Task<int> Main(string[] args)
	{
		ParseCommandOptions(args);
		OutputDir = Directory.GetCurrentDirectory();
		Client = new();
		if (SaveThumbnails) httpClient = new();
		Console.CancelKeyPress += new(CleanupDuringCancel);
		Stopwatch sw = Stopwatch.StartNew();
		foreach (string url in URLs)
		{
			Console.WriteLine($"Downloading {url}");
			if (url.Contains("/watch?"))
			{
				Video video = await Client.Videos.GetAsync(url);
				if (SaveThumbnails)
				{
					Console.WriteLine($"Downloading all thumbnails for video {video.Title}");
					foreach (var thumbnail in video.Thumbnails)
					{
						Stream stream = await httpClient.GetStreamAsync(thumbnail.Url);
						FileStream outstream = File.OpenWrite(RemoveInvalidChars(video.Title) + ".webm");
						await stream.CopyToAsync(outstream);
					}
				}
				var manifest = await Client.Videos.Streams.GetManifestAsync(video.Url);
				await DownloadFunc(manifest, video.Title, url);
				continue;
			}
			else if (url.Contains("playlist"))
			{
				Playlist playlist = await Client.Playlists.GetAsync(url);
				Console.WriteLine($"Downloading playlist \"{playlist.Title}\"");
				if (PlaylistFolder)
				{
					Directory.CreateDirectory(RemoveInvalidPathChars(playlist.Title));
					Directory.SetCurrentDirectory(RemoveInvalidPathChars(playlist.Title));
				}
				await foreach (PlaylistVideo video in Client.Playlists.GetVideosAsync(url))
				{
					if (SaveThumbnails)
					{
						Console.WriteLine($"Downloading all thumbnails for video {video.Title}");
						foreach (var thumbnail in video.Thumbnails)
						{
							Stream stream = await httpClient.GetStreamAsync(thumbnail.Url);
							FileStream outstream = File.OpenWrite(RemoveInvalidChars(video.Title));
							await stream.CopyToAsync(outstream);
						}
					}
					var manifest = await Client.Videos.Streams.GetManifestAsync(video.Url);
					await DownloadFunc(manifest, video.Title, video.Url);
				}
				if (PlaylistFolder) Directory.SetCurrentDirectory(OutputDir);
			}
			else if (url.Contains("/@") || url.Contains("/c/") || url.Contains("/channel/"))
			{
				var channel = await Client.Channels.GetByHandleAsync(url);
				Console.WriteLine($"Downloading all uploads from channel \"{channel.Title}\"");
				if (ChannelFolder)
				{
					Directory.CreateDirectory(RemoveInvalidPathChars(channel.Title));
					Directory.SetCurrentDirectory(RemoveInvalidPathChars(channel.Title));
				}
				await foreach (PlaylistVideo video in Client.Channels.GetUploadsAsync(channel.Id))
				{
					try
					{
						if (SaveThumbnails)
						{
							Console.WriteLine($"Downloading all thumbnails for video {video.Title}");
							foreach (var thumbnail in video.Thumbnails)
							{
								NetworkStream stream = (NetworkStream)await httpClient.GetStreamAsync(thumbnail.Url);
								FileStream outstream = File.OpenWrite(RemoveInvalidChars(video.Title));
								await stream.CopyToAsync(outstream);
							}
						}
						var manifest = await Client.Videos.Streams.GetManifestAsync(video.Url);
						await DownloadFunc(manifest, video.Title, video.Url);
					}
					catch (Exception e)
					{
						Console.WriteLine($"An exception occured trying to download video \"{video.Title}\": {e.Message}");
						continue;
					}
				}
				if (ChannelFolder) Directory.SetCurrentDirectory(OutputDir);
			}
		}

		Console.WriteLine($"Done in {FormatTimespan(sw.Elapsed)}.");
		return 0;
	}

	static async Task DownloadFunc(StreamManifest? manifest, string videoTitle, string url)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		ArgumentNullException.ThrowIfNull(Client);
		try
		{
			if (AudioOnly)
			{
				var audiosstream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
				await DownloadAudio(audiosstream, videoTitle);
				return;
			}
			if (GetCaptions)
			{
				var captions = await Client.Videos.ClosedCaptions.GetManifestAsync(url);
				var lang = captions.GetByLanguage(CaptionLang);
				Console.Write($"Captions for {videoTitle} ({CaptionLang})");
				CurrentRow = Console.GetCursorPosition().Top;
				CurrentCollumn = Console.GetCursorPosition().Left;
				await Client.Videos.ClosedCaptions.DownloadAsync(lang, $"{RemoveInvalidChars(videoTitle)}-{CaptionLang}.srt", default, Token);
				Console.SetCursorPosition(CurrentCollumn, CurrentRow);
				Console.WriteLine("Completed.");
			}
			if (NoDASH)
			{
				var stream = manifest.GetMuxedStreams().GetWithHighestVideoQuality();
				Console.Write($"{videoTitle} - ");
				CurrentRow = Console.GetCursorPosition().Top;
				CurrentCollumn = Console.GetCursorPosition().Left;
				await Client.Videos.Streams.DownloadAsync(stream, $"{RemoveInvalidChars(videoTitle)}.{stream.Container.Name}",
				new Progress<double>(percent =>
				{
					int CurrentPercent = (int)(percent * 100);
					if (CurrentPercent != LastPercent)
					{
						LastPercent = CurrentPercent;
						Console.SetCursorPosition(CurrentCollumn, CurrentRow);
						Console.Write($"{CurrentPercent}%");
					}
				}), Token);
				return;
			}
			var audiostream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
			var videostream = manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
			await DownloadVideo([audiostream, videostream], videoTitle, videostream.Container.Name);
			return;
		}
		catch (Exception e)
		{
			Console.WriteLine($"An exception occured trying to download {url}: {e.GetType()} {e.Message}");
		}
	}

	static async Task DownloadAudio(IStreamInfo stream, string title)
	{
		if (Client is null)
			return;
		Console.Write($"{title} - ");
		CurrentRow = Console.GetCursorPosition().Top;
		CurrentCollumn = Console.GetCursorPosition().Left;
		try
		{
		await Client.Videos.Streams.DownloadAsync(stream, $"{RemoveInvalidChars(title)}.{stream.Container.Name}",
		new Progress<double>(percent => 
			{
				int CurrentPercent = (int)(percent * 100);
				if (CurrentPercent != LastPercent)
				{
					LastPercent = CurrentPercent;
					Console.SetCursorPosition(CurrentCollumn, CurrentRow);
					Console.Write($"{CurrentPercent}%");
				}
			}), Token);
		}
		catch (IOException e)
		{
			Console.WriteLine("Failed to write the video's streams: " + e.Message);
			Exit(1);
		}
		catch (HttpRequestException e)
		{
			Console.WriteLine($"An HTTP error occured: {e.Message}");
			Console.WriteLine("403 errors usually happen on age restricted videos.");
			return;
		}
		catch (Win32Exception)
		{
			Console.WriteLine("Downloading DASH streams requires having ffmpeg installed and available from your system's PATH.");
			Console.WriteLine("Download it here: https://www.ffmpeg.org/");
			Exit(1);
		}
		catch (TaskCanceledException)
		{
			return;
		}
		catch (Exception e)
		{
			Console.WriteLine($"An exception occured: {e.GetType()} {e.Message}");
#if DEBUG
			Console.WriteLine(e.StackTrace);
#endif
			return;
		}
		Console.SetCursorPosition(CurrentCollumn, CurrentRow);
		Console.WriteLine("Completed.");
	}

	static async Task DownloadVideo(IReadOnlyList<IStreamInfo> streams, string title, string container)
	{
		if (Client is null)
			return;
		Console.Write($"{title} - ");
		CurrentRow = Console.GetCursorPosition().Top;
		CurrentCollumn = Console.GetCursorPosition().Left;
		try
		{
		await Client.Videos.DownloadAsync(streams, new ConversionRequestBuilder($"{RemoveInvalidChars(title)}.{container}").Build(),
		new Progress<double>(percent => 
			{
				int CurrentPercent = (int)(percent * 100);
				if (CurrentPercent != LastPercent)
				{
					LastPercent = CurrentPercent;
					Console.SetCursorPosition(CurrentCollumn, CurrentRow);
					Console.Write($"{CurrentPercent}%");
				}
			}), Token);
		}
		catch (IOException e)
		{
			Console.WriteLine("Failed to write the video's streams: " + e.Message);
			Exit(1);
		}
		catch (HttpRequestException e)
		{
			Console.WriteLine($"An HTTP error occured: {e.Message}");
			Console.WriteLine("403 errors usually happen on age restricted videos.");
			return;
		}
		catch (Win32Exception)
		{
			Console.WriteLine("Downloading DASH streams requires having ffmpeg installed and available from your system's PATH.");
			Console.WriteLine("Download it here: https://www.ffmpeg.org/");
			Exit(1);
		}
		catch (TaskCanceledException)
		{
			return;
		}
		catch (Exception e)
		{
			Console.WriteLine($"An exception occured: {e.GetType()} {e.Message}");
#if DEBUG
			Console.WriteLine(e.StackTrace);
#endif
			return;
		}
		Console.SetCursorPosition(CurrentCollumn, CurrentRow);
		Console.WriteLine("Completed.");
	}

	static void ParseCommandOptions(string[] args)
	{
		string outpath = "";
		bool help = false;
		OptionSet options = new OptionSet {
			{ "o|outpath=", "A path to download videos and their streams to.", o => outpath = o },
			{ "a|audio-only", "Download only the audio streams from the given URLs", a => AudioOnly = a != null },
			{ "cc|closed-captions", "Download Closed-Captions if they're available. Uses English by default",
			cc => GetCaptions = cc != null },
			{ "D|no-dash", "Don't download DASH streams. This skips the requirement of ffmpeg, but limits video quality to " +
			"below 720p.", nd => NoDASH = nd != null },
			{ "cl|caption-lang=", "Caption language to download, if it's available. Must be the 2 letter ISO 3166 language code.",
			cl => CaptionLang = cl },
			{ "pf|playlist-folders", "Download playlists to a folder with the name of the playlist.", pf =>  PlaylistFolder = pf != null },
			{ "cf|channel-folders", "Download channels to a folder with the channel's name.", cf => ChannelFolder = cf != null },
			{ "uf|use-folders", "Download playlists and channels to folders with their names. Equivalent to setting --channel-folders " + 
			"and --playlist-folders.", uf => { if (uf != null) { ChannelFolder = true; PlaylistFolder = true; } }},
			{ "st|save-thumbnails", "Download the video's thumbnails.", sf => SaveThumbnails = sf != null },
			{ "h|help", "Show help message and exit.", h => help = h != null }
		};
		try { URLs = options.Parse(args); }
		catch (OptionException e)
		{
			Console.WriteLine($"ytdl: {e.Message}");
			Console.WriteLine("Try --help to see all switches/usages.");
			Exit(1);
		}
		if (help)
		{
			Console.WriteLine("ytdl: Download YouTube channels, videos, and playlists.");
			Console.WriteLine("Usage: [-o Outpath] URL(s)");
			Console.WriteLine("Options:");
			options.WriteOptionDescriptions(Console.Out);
			Exit(1);
		}
		if (URLs.Count == 0)
		{
			Console.WriteLine("No URLs provided.");
			Console.WriteLine("Usage: [-o Outpath] URL(s)");
			Console.WriteLine("Try --help to see all switches/usages.");
			Exit(1);
		}
		if (outpath != "")
		{
			if (!Path.Exists(outpath))
			{
				Console.WriteLine("Provided output directory doesn't exist.");
				Exit(1);
			}
			Directory.SetCurrentDirectory(outpath);
			Console.WriteLine($"Saving videos to {outpath}");
		}
		if (AudioOnly) Console.WriteLine("Downloading videos as audio only.");
	}

	protected static void CleanupDuringCancel(object? sender, ConsoleCancelEventArgs e)
	{
		Console.WriteLine("Canceling downloads...");
		Source.Cancel();
		Console.WriteLine("Deleting temporary files...");
		CleanupTempFiles();
		Exit(1);
	}

	static void CleanupTempFiles()
	{
		Regex regex = new(".*[.]stream-.?[.]tmp");
		foreach (string file in Directory.GetFiles(Directory.GetCurrentDirectory()))
		{
			if (regex.IsMatch(file))
				File.Delete(file);
		}
	}

	[DoesNotReturn]
	static void Exit(int code)
	{
		Environment.Exit(code);
	}

	static string FormatTimespan(TimeSpan span)
	{
		string outstr = "";
		if (span.Hours == 0)
		{
			outstr = span.ToString("mm\\:ss\\.ff");
			return outstr;
		}
		outstr = span.ToString("hh\\:mm\\:ss\\.ff");
		return outstr;
	}

	static string RemoveInvalidChars(string filepath)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string outpath = "";
		foreach (char c in filepath)
		{
			if (invalid.Contains(c) || (c == '&' && !NoDASH))
				continue;
			outpath += c;
		}
		return outpath;
	}

	static string RemoveInvalidPathChars(string path)
	{
		char[] invalid = Path.GetInvalidPathChars();
		string outpath = "";
		foreach (char c in path)
		{
			if (invalid.Contains(c))
				continue;
			outpath += c;
		}
		return outpath;
	}
}
