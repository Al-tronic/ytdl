namespace ytdl;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
	static YoutubeClient? Client;
	static string CurrentVideo = "";
	static CancellationTokenSource Source = new();
	static CancellationToken Token = Source.Token;
	static async Task<int> Main(string[] args)
	{
		ParseCommandOptions(args);
		Client = new();
		Console.CancelKeyPress += new(CleanupDuringCancel);
		Stopwatch sw = Stopwatch.StartNew();
		foreach (string url in URLs)
		{
			Console.WriteLine($"Downloading {url}");
			if (url.Contains("/watch?"))
			{
				Video video = await Client.Videos.GetAsync(args[0]);
				var manifest = await Client.Videos.Streams.GetManifestAsync(video.Url);
				if (AudioOnly)
				{
					var audiosstream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
					await DownloadAudio(audiosstream, video.Title);
					continue;
				}
				var audiostream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
				var videostream = manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
				await DownloadVideo([audiostream, videostream], video.Title, videostream.Container.Name);
				continue;
			}
			else if (url.Contains("playlist"))
			{
				Playlist playlist = await Client.Playlists.GetAsync(url);
				Console.WriteLine($"Downloading playlist \"{playlist.Title}\"");
				await foreach (PlaylistVideo video in Client.Playlists.GetVideosAsync(url))
				{
					try
					{
						var manifest = await Client.Videos.Streams.GetManifestAsync(video.Url);
						CurrentVideo = video.Title;
						if (AudioOnly)
						{
							var audiosstream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
							await DownloadAudio(audiosstream, video.Title);
							continue;
						}
						var audiostream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
						var videostream = manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
						await DownloadVideo([audiostream, videostream], video.Title, videostream.Container.Name);
					}
					catch (Exception e)
					{
						Console.WriteLine($"An exception occured trying to download video {video.Title}: {e.Message}");
						continue;
					}
				}
			}
			else if (url.Contains("/@") || url.Contains("/c/") || url.Contains("/channel/"))
			{
				var channel = await Client.Channels.GetByHandleAsync(url);
				Console.WriteLine($"Downloading all uploads from channel \"{channel.Title}\"");
				await foreach (PlaylistVideo video in Client.Channels.GetUploadsAsync(channel.Id))
				{
					try
					{
						var manifest = await Client.Videos.Streams.GetManifestAsync(video.Url);
						CurrentVideo = video.Title;
						if (AudioOnly)
						{
							var audiosstream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
							await DownloadAudio(audiosstream, video.Title);
							continue;
						}
						var audiostream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
						var videostream = manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
						IStreamInfo[] streams = [audiostream, videostream];
						if (GetCaptions)
						{
							var captions = await Client.Videos.ClosedCaptions.GetManifestAsync(url);
							var lang = captions.GetByLanguage("EN"); // TODO: Add getting other languages
							await Client.Videos.ClosedCaptions.DownloadAsync(lang, $"{video.Title}.srt");
						}
						await DownloadVideo(streams, video.Title, videostream.Container.Name);
						CurrentVideo = "";
					}
					catch (Exception e)
					{
						Console.WriteLine($"An exception occured trying to download video \"{video.Title}\": {e.Message}");
						CurrentVideo = "";
						continue;
					}
				}
			}

		}
		Console.WriteLine($"Done in {sw.Elapsed}.");
		return 0;
	}

	static async Task DownloadAudio(IStreamInfo stream, string title)
	{
		if (Client is null)
			return;
		Console.Write($"{title} - ");
		CurrentRow = Console.GetCursorPosition().Left;
		CurrentCollumn = title.Length + 3;
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
		CurrentCollumn = title.Length + 3;
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
		catch (TaskCanceledException)
		{
			return;
		}
		catch (Exception e)
		{
			Console.WriteLine($"An exception occured: {e.GetType()}");
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
			{ "cc|closed-captions", "Download Closed-Captions if they're available.", cc => GetCaptions = cc != null },
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

	static string RemoveInvalidChars(string filepath)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string outpath = "";
		foreach (char c in filepath)
		{
			if (invalid.Contains(c))
				continue;
			outpath += c;
		}
		return outpath;
	}
}
