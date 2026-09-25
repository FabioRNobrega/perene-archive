using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Services;

internal interface IVideoConversionProbe { Task<VideoConversionProbeResult?> ProbeAsync(string path, CancellationToken token); }
internal sealed class FfprobeVideoConversionProbe : IVideoConversionProbe
{
    public async Task<VideoConversionProbeResult?> ProbeAsync(string path, CancellationToken token)
    {
        var info = new ProcessStartInfo { FileName = "ffprobe", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=format_name,duration:stream=index,codec_type,codec_name,bit_rate,width,height:stream_tags=language", "-of", "json", path }) info.ArgumentList.Add(arg);
        try { using var p = Process.Start(info); if (p is null) return null; var output = await p.StandardOutput.ReadToEndAsync(token); await p.WaitForExitAsync(token); var parsed = p.ExitCode == 0 ? Parse(output) : null; return parsed is null ? null : parsed with { HasClosedCaptions = await HasClosedCaptionsAsync(path, token) }; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or JsonException) { return null; }
    }
    private static async Task<bool> HasClosedCaptionsAsync(string path, CancellationToken token)
    {
        var info = new ProcessStartInfo { FileName = "ffprobe", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-read_intervals", "%+#360", "-show_entries", "frame=best_effort_timestamp_time:frame_side_data=side_data_type", "-of", "json", path }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info); if (process is null) return false;
        var output = await process.StandardOutput.ReadToEndAsync(token); await process.WaitForExitAsync(token);
        return process.ExitCode == 0 && output.Contains("ATSC A53 Part 4 Closed Captions", StringComparison.Ordinal);
    }
    internal static VideoConversionProbeResult? Parse(string json)
    {
        using var d = JsonDocument.Parse(json); var root = d.RootElement;
        if (!root.TryGetProperty("format", out var format) || !format.TryGetProperty("format_name", out var f)) return null;
        var streams = root.TryGetProperty("streams", out var array) ? array.EnumerateArray().ToList() : [];
        var v = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "video");
        if (v.ValueKind == JsonValueKind.Undefined || !v.TryGetProperty("codec_name", out var vc) || !v.TryGetProperty("width", out var w) || !v.TryGetProperty("height", out var h) || w.GetInt32() <= 0 || h.GetInt32() <= 0) return null;
        var a = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "audio");
        var duration = format.TryGetProperty("duration", out var dp) && double.TryParse(dp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        long? bitrate = v.TryGetProperty("bit_rate", out var bp) && long.TryParse(bp.GetString(), out var b) ? b : null;
        var subtitles = streams
            .Where(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "subtitle" && s.TryGetProperty("index", out var index) && index.TryGetInt32(out _))
            .Select((s, ordinal) => CreateSubtitle(s, ordinal + 1))
            .Where(s => s is not null).Select(s => s!).ToList();
        return new(f.GetString() ?? "", vc.GetString() ?? "", a.ValueKind != JsonValueKind.Undefined && a.TryGetProperty("codec_name", out var ac) ? ac.GetString() : null, bitrate, w.GetInt32(), h.GetInt32(), duration, subtitles);
    }
    private static VideoConversionSubtitleStream? CreateSubtitle(JsonElement stream, int ordinal)
    {
        if (!stream.TryGetProperty("index", out var index) || !index.TryGetInt32(out var streamIndex) || streamIndex < 0) return null;
        var codec = stream.TryGetProperty("codec_name", out var codecValue) ? SafeToken(codecValue.GetString(), 48) : null;
        if (string.IsNullOrWhiteSpace(codec)) return null;
        string? language = null;
        if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var languageValue)) language = SafeToken(languageValue.GetString(), 32)?.ToLowerInvariant();
        return new(streamIndex, codec, language, language is null ? $"Track {ordinal} (language unknown)" : $"Track {ordinal} ({language})");
    }
    private static string? SafeToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var token = new string(value.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(maximumLength).ToArray());
        return token.Length == 0 ? null : token;
    }
}
internal sealed class MediaConversionPlanner(IOptions<VideoConversionOptions> options)
{
    public MediaAction Plan(VideoConversionProbeResult p, long? sourceSizeBytes) {
        var compatibleVideo = string.Equals(p.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
        var compatibleAudio = p.AudioCodec is null || string.Equals(p.AudioCodec, "aac", StringComparison.OrdinalIgnoreCase);
        var mp4 = p.Container.Split(',').Any(x => x.Equals("mov", StringComparison.OrdinalIgnoreCase) || x.Equals("mp4", StringComparison.OrdinalIgnoreCase));
        if (compatibleVideo && compatibleAudio && !mp4) return MediaAction.Remux;
        if (compatibleVideo && !compatibleAudio) return MediaAction.ConvertAudio;
        if (compatibleVideo && compatibleAudio && mp4)
        {
            var isLargeShortSource = sourceSizeBytes > options.Value.LargeSourceBytes && p.Duration < options.Value.ShortDurationMaximum;
            return isLargeShortSource ? MediaAction.CompressVideo : MediaAction.Keep;
        }

        return MediaAction.FullTranscode;
    }
}
internal interface IVideoConversionJobQueue { bool TryEnqueue(VideoConversionJob job); Task<VideoConversionJob> DequeueAsync(CancellationToken token); void Complete(); int ActiveCount { get; } }
internal sealed class VideoConversionJobQueue(IOptions<VideoConversionOptions> options) : IVideoConversionJobQueue
{ private readonly Channel<VideoConversionJob> _channel = Channel.CreateBounded<VideoConversionJob>(new BoundedChannelOptions(options.Value.QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }); private int _active;
 public bool TryEnqueue(VideoConversionJob job) { if (!_channel.Writer.TryWrite(job)) return false; Interlocked.Increment(ref _active); return true; } public async Task<VideoConversionJob> DequeueAsync(CancellationToken token) => await _channel.Reader.ReadAsync(token); public void Complete() => Interlocked.Decrement(ref _active); public int ActiveCount => Volatile.Read(ref _active); }
internal interface IVideoConversionJobStatusStore { bool HasActiveSource(string sourceId); void Seed(VideoConversionJob job); void Remove(string id); void Processing(string id); bool TryBeginProcessing(string id); bool Pause(string id); bool Resume(string id); bool Stop(string id); void Progress(string id, VideoConversionProgress progress); void Complete(string id, string outputId, long size); void Fail(string id, string message); void Skip(string id, string message); VideoConversionStatus? Get(string id); IReadOnlyList<VideoConversionStatus> GetAll(); }
internal sealed class VideoConversionJobStatusStore : IVideoConversionJobStatusStore
{ private readonly ConcurrentDictionary<string, VideoConversionStatus> _jobs = new(); private readonly ConcurrentQueue<string> _order = new(); private readonly object _runGate = new();
 public bool HasActiveSource(string sourceId) => _jobs.Values.Any(x => x.SourceId == sourceId && x.State is (VideoConversionJobState.Pending or VideoConversionJobState.Processing or VideoConversionJobState.Paused or VideoConversionJobState.Finalizing));
 public void Seed(VideoConversionJob j) { _jobs[j.JobId] = new(j.JobId, j.Source.Id, j.Source.Name, j.Action, VideoConversionJobState.Pending, j.Source.SizeBytes, QueuedAtUtc: DateTimeOffset.UtcNow, SourceDurationSeconds: j.Probe.Duration.TotalSeconds, ProfileLabel: j.Profile?.Label, OutputHeight: j.Profile?.OutputHeight, EstimatedSizeBytes: j.Profile?.EstimatedSizeBytes); _order.Enqueue(j.JobId); } public void Remove(string id) => _jobs.TryRemove(id, out _);
 public void Processing(string id) => Transition(id, VideoConversionJobState.Pending, x => x with { State = VideoConversionJobState.Processing, StartedAtUtc = DateTimeOffset.UtcNow });
 public bool Pause(string id) => Transition(id, VideoConversionJobState.Processing, x => x with { State = VideoConversionJobState.Paused });
 public bool TryBeginProcessing(string id) { lock (_runGate) { if (IsRunningOther(id)) return false; return Transition(id, VideoConversionJobState.Pending, x => x with { State = VideoConversionJobState.Processing, StartedAtUtc = DateTimeOffset.UtcNow }); } }
 public bool Resume(string id) { lock (_runGate) { if (IsRunningOther(id)) return false; return Transition(id, VideoConversionJobState.Paused, x => x with { State = VideoConversionJobState.Processing }); } }
 private bool IsRunningOther(string id) => _jobs.Values.Any(x => x.JobId != id && x.State is VideoConversionJobState.Processing or VideoConversionJobState.Finalizing);
 public bool Stop(string id) => Transition(id, [VideoConversionJobState.Processing, VideoConversionJobState.Paused], x => x with { State = VideoConversionJobState.Stopped, Diagnostic = "Stopped. Temporary conversion output was deleted; the original source was not changed.", FinishedAtUtc = DateTimeOffset.UtcNow });
 public void Progress(string id, VideoConversionProgress progress) => UpdateIf(id, x => x.State is VideoConversionJobState.Processing or VideoConversionJobState.Finalizing, x => progress.IsFinalizing ? x with { State = VideoConversionJobState.Finalizing } : x with { State = VideoConversionJobState.Processing, ProcessedDurationSeconds = Math.Max(x.ProcessedDurationSeconds ?? 0, progress.ProcessedDurationSeconds ?? 0), Speed = progress.Speed ?? x.Speed });
 public void Complete(string id,string output,long size)=>UpdateIf(id,x=>x.State is VideoConversionJobState.Processing or VideoConversionJobState.Finalizing,x=>x with{State=VideoConversionJobState.Completed,OutputItemId=output,OutputSizeBytes=size,ProcessedDurationSeconds=x.SourceDurationSeconds,FinishedAtUtc=DateTimeOffset.UtcNow}); public void Fail(string id,string msg)=>UpdateIf(id,x=>x.State is not VideoConversionJobState.Stopped,x=>x with{State=VideoConversionJobState.Failed,Diagnostic=msg,FinishedAtUtc=DateTimeOffset.UtcNow}); public void Skip(string id,string msg)=>UpdateIf(id,x=>x.State is not VideoConversionJobState.Stopped,x=>x with{State=VideoConversionJobState.Skipped,Diagnostic=msg,FinishedAtUtc=DateTimeOffset.UtcNow}); public VideoConversionStatus? Get(string id) => _jobs.GetValueOrDefault(id);
 private bool Transition(string id, VideoConversionJobState expected, Func<VideoConversionStatus,VideoConversionStatus> f) => Transition(id, [expected], f); private bool Transition(string id, VideoConversionJobState[] expected, Func<VideoConversionStatus,VideoConversionStatus> f) { while (_jobs.TryGetValue(id, out var current)) { if (!expected.Contains(current.State)) return false; if (_jobs.TryUpdate(id, f(current), current)) return true; } return false; } private void UpdateIf(string id, Func<VideoConversionStatus,bool> predicate, Func<VideoConversionStatus,VideoConversionStatus> f) { while(_jobs.TryGetValue(id,out var current)) { if(!predicate(current)||_jobs.TryUpdate(id,f(current),current)) return; } } public IReadOnlyList<VideoConversionStatus> GetAll()=>_order.Select(x=>_jobs.GetValueOrDefault(x)).Where(x=>x is not null).Select(x=>x!).ToList(); }
internal sealed class VideoConversionNamingService { public string GetNextPath(ArchiveItemEntry source) { var dir=Path.GetDirectoryName(source.PhysicalPath)!; var stem=Path.GetFileNameWithoutExtension(source.Name); for(var n=1;;n++){var candidate=Path.Combine(dir,$"{stem} Converted {n:0000}.mp4");if(!File.Exists(candidate))return candidate;} } }
internal interface IVideoConversionGenerator { Task<VideoConversionGenerationResult> GenerateAsync(VideoConversionJob job, Action<VideoConversionProgress>? reportProgress, IVideoConversionProcessController controller, CancellationToken token); }
internal sealed class FfmpegVideoConversionGenerator(IVideoConversionProbe probe, VideoConversionNamingService naming, VideoConversionArgumentBuilder arguments, IOptions<VideoConversionOptions> options) : IVideoConversionGenerator
{
 public async Task<VideoConversionGenerationResult> GenerateAsync(VideoConversionJob job, Action<VideoConversionProgress>? reportProgress, IVideoConversionProcessController controller, CancellationToken token)
 {
  if (job.Action == MediaAction.Keep) return new(true, true, Diagnostic: "Already browser-compatible at the configured bitrate.");
  if (!Matches(job.Source)) return new(false, false, Diagnostic: "Source changed before conversion started.");
  var dest = naming.GetNextPath(job.Source); var temp = Path.Combine(Path.GetDirectoryName(dest)!, $".{Path.GetFileNameWithoutExtension(dest)}.{Guid.NewGuid():N}.tmp.mp4"); var captions = $"{temp}.cc.srt";
  try {
   var drive = new DriveInfo(Path.GetPathRoot(dest)!); if (drive.AvailableFreeSpace < (job.Source.SizeBytes ?? 0) * 2 + options.Value.FreeSpaceReserveBytes) return new(false,false,Diagnostic:"Not enough free space for a safe conversion.");
   if (job.Profile?.BurnClosedCaptions == true && !await ExtractClosedCaptionsAsync(job.Source.PhysicalPath, captions, token)) return new(false, false, Diagnostic:"Closed captions could not be extracted from this media.");
   var psi=new ProcessStartInfo { FileName="ffmpeg", UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true }; foreach(var arg in arguments.Build(job.Source.PhysicalPath,temp,job,options.Value.H264Crf, job.Profile?.BurnClosedCaptions == true ? captions : null)) psi.ArgumentList.Add(arg);
   using var p=Process.Start(psi); if(p is null)return new(false,false,Diagnostic:"ffmpeg failed to start."); if (!controller.RegisterProcess(job.JobId, p)) { try { p.Kill(); } catch { } return new(false,false,true,Diagnostic:"Conversion stopped."); } var progressTask=ReadProgressAsync(p.StandardOutput,reportProgress,token); var errorTask=p.StandardError.ReadToEndAsync(token); try { await p.WaitForExitAsync(token); } catch (OperationCanceledException) { try { if (!p.HasExited) p.WaitForExit(); } catch { } } finally { controller.UnregisterProcess(job.JobId,p); } await progressTask; var error=await errorTask; if(!controller.CanPublish(job.JobId)) return new(false,false,true,Diagnostic:"Conversion stopped."); if(p.ExitCode!=0)return new(false,false,Diagnostic:"ffmpeg could not convert this media."); reportProgress?.Invoke(new(null,null,true));
   if (!controller.CanPublish(job.JobId)) return new(false,false,true,Diagnostic:"Conversion stopped."); var output=await probe.ProbeAsync(temp,token); if(output is null || !IsValidMp4(output) || output.HasSubtitles) return new(false,false,Diagnostic: output?.HasSubtitles == true ? "Embedded subtitles could not be safely published to MP4." : "Converted output failed validation.");
   var outputSize = new FileInfo(temp).Length;
   if (job.Action == MediaAction.CompressVideo && !MeetsMinimumSavings(job.Source.SizeBytes, outputSize, options.Value.MinimumSavingsPercent)) return new(true,true,Diagnostic:$"Optimized output did not meet the configured {options.Value.MinimumSavingsPercent}% savings threshold.");
   if (!controller.CanPublish(job.JobId)) return new(false,false,true,Diagnostic:"Conversion stopped."); File.Move(temp,dest); return new(true,false,false,dest,outputSize);
  } catch(OperationCanceledException){ return new(false,false,!controller.CanPublish(job.JobId),Diagnostic:"Conversion cancelled during shutdown."); } catch(Exception e) when(e is IOException or UnauthorizedAccessException or Win32Exception){ return new(false,false,Diagnostic:"Conversion could not publish output."); } finally { try { if(File.Exists(temp)) File.Delete(temp); if(File.Exists(captions)) File.Delete(captions); } catch {} }
 }
 private static async Task<bool> ExtractClosedCaptionsAsync(string source, string captions, CancellationToken token)
 { var psi = new ProcessStartInfo { FileName = "ffmpeg", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
   foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"movie='{VideoConversionArgumentBuilder.EscapeFilterValue(source)}'[out+subcc]", "-map", "0:s:0", "-c:s", "srt", "-y", captions }) psi.ArgumentList.Add(arg);
   using var process = Process.Start(psi); if (process is null) return false; await process.StandardOutput.ReadToEndAsync(token); await process.StandardError.ReadToEndAsync(token); await process.WaitForExitAsync(token); return process.ExitCode == 0 && new FileInfo(captions).Length > 0; }
 internal static bool MeetsMinimumSavings(long? sourceSizeBytes, long outputSizeBytes, int minimumSavingsPercent) =>
  sourceSizeBytes is > 0 && outputSizeBytes >= 0 && outputSizeBytes * 100m <= sourceSizeBytes.Value * (100 - minimumSavingsPercent);
 internal static VideoConversionProgress? ParseProgress(IReadOnlyDictionary<string,string> values) { if (values.TryGetValue("progress",out var state) && state == "end") return new(null,null,true); double? processed = values.TryGetValue("out_time_us",out var time) && long.TryParse(time,CultureInfo.InvariantCulture,out var microseconds) && microseconds >= 0 ? microseconds / 1_000_000d : null; double? speed = values.TryGetValue("speed",out var speedValue) && speedValue.EndsWith('x') && double.TryParse(speedValue[..^1],NumberStyles.Float,CultureInfo.InvariantCulture,out var parsedSpeed) && parsedSpeed > 0 ? parsedSpeed : null; return processed is null && speed is null ? null : new(processed,speed); }
 private static async Task ReadProgressAsync(StreamReader reader, Action<VideoConversionProgress>? reportProgress, CancellationToken token) { var values = new Dictionary<string,string>(StringComparer.Ordinal); while(await reader.ReadLineAsync(token) is { } line) { var separator=line.IndexOf('='); if(separator<=0) continue; values[line[..separator]]=line[(separator+1)..]; if(!string.Equals(line[..separator],"progress",StringComparison.Ordinal)) continue; var progress=ParseProgress(values); if(progress is not null) reportProgress?.Invoke(progress); values.Clear(); } }
 internal static IReadOnlyList<string> BuildArguments(string source,string output,MediaAction action,int crf) { var a=new List<string>{"-nostdin","-hide_banner","-loglevel","error","-progress","pipe:1","-nostats","-i",source,"-map","0","-map_metadata","0","-map_chapters","0"}; switch(action){case MediaAction.Remux:a.AddRange(["-c","copy"]);break;case MediaAction.ConvertAudio:a.AddRange(["-c:v","copy","-c:a","aac","-profile:a","aac_low","-pix_fmt","yuv420p"]);break;case MediaAction.CompressVideo:a.AddRange(["-c:v","libx264","-crf",crf.ToString(CultureInfo.InvariantCulture),"-c:a","aac","-profile:a","aac_low","-pix_fmt","yuv420p"]);break;default:a.AddRange(["-c:v","libx264","-crf",crf.ToString(CultureInfo.InvariantCulture),"-c:a","aac","-profile:a","aac_low","-pix_fmt","yuv420p"]);break;} a.AddRange(["-movflags","+faststart","-y",output]);return a; }
 private static bool Matches(ArchiveItemEntry source){try{var f=new FileInfo(source.PhysicalPath);return f.Exists&&f.Length==source.SizeBytes&&f.LastWriteTimeUtc==source.LastWriteTimeUtc;}catch{return false;}} private static bool IsValidMp4(VideoConversionProbeResult p)=>p.Duration>TimeSpan.Zero&&p.Container.Contains("mp4",StringComparison.OrdinalIgnoreCase)&&p.VideoCodec.Equals("h264",StringComparison.OrdinalIgnoreCase)&&(p.AudioCodec is null||p.AudioCodec.Equals("aac",StringComparison.OrdinalIgnoreCase));
}
internal sealed class VideoConversionBackgroundWorker(IVideoConversionJobQueue queue, IVideoConversionGenerator generator, IVideoConversionJobStatusStore statuses, IVideoConversionProcessController controller, IArchiveService archive, ILogger<VideoConversionBackgroundWorker> logger) : BackgroundService
{
 private readonly ConcurrentDictionary<Task, byte> _running = new();
 protected override async Task ExecuteAsync(CancellationToken token)
 {
  // Only one job may be Processing at a time; a Paused job frees the slot so the next queued job can start.
  while (!token.IsCancellationRequested)
  {
   VideoConversionJob job;
   try { job = await queue.DequeueAsync(token); while (!statuses.TryBeginProcessing(job.JobId)) { if (statuses.Get(job.JobId)?.State != VideoConversionJobState.Pending) break; await Task.Delay(500, token); } } catch (OperationCanceledException) { break; }
   if (statuses.Get(job.JobId)?.State != VideoConversionJobState.Processing) { queue.Complete(); continue; }
   var run = RunJobAsync(job, token); _running[run] = 0; _ = run.ContinueWith(t => _running.TryRemove(t, out _), TaskScheduler.Default);
  }
  await Task.WhenAll(_running.Keys);
 }
 private async Task RunJobAsync(VideoConversionJob job, CancellationToken token)
 {
  var jobToken = controller.Begin(job.JobId, token);
  try { var result = await generator.GenerateAsync(job, progress => statuses.Progress(job.JobId, progress), controller, jobToken); if (result.Stopped) statuses.Stop(job.JobId); else if (result.Skipped) statuses.Skip(job.JobId, result.Diagnostic ?? "Skipped."); else if (result.Success && result.DestinationPath is not null) statuses.Complete(job.JobId, archive.ComputeItemId(job.Source.Category.Key, result.DestinationPath), result.OutputSizeBytes ?? 0); else statuses.Fail(job.JobId, result.Diagnostic ?? "Conversion failed."); }
  catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Video conversion failed for job {JobId}", job.JobId); statuses.Fail(job.JobId, "Unexpected conversion error."); }
  finally { controller.Complete(job.JobId); queue.Complete(); }
 }
}
