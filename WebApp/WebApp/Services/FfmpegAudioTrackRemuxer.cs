using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class FfmpegAudioTrackRemuxer(IOptions<ThumbnailCacheOptions> thumbnailCacheOptions) : IAudioTrackRemuxer
{
    private const int MaxDiagnosticLength = 2000;

    private readonly string _previewRoot = Path.GetFullPath(thumbnailCacheOptions.Value.Path);

    public async Task<AudioTrackRemuxResult> RemuxAsync(
        VideoFileEntry source,
        int trackIndex,
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!VideoLibraryService.IsWithinRoot(_previewRoot, destinationPath) ||
            !VideoLibraryService.IsWithinRoot(_previewRoot, temporaryPath))
        {
            return AudioTrackRemuxResult.Failed("destination outside the configured preview root");
        }

        if (!SourceMatches(source))
        {
            return AudioTrackRemuxResult.Failed("source changed before remux started");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };

        foreach (var argument in BuildArguments(source.PhysicalPath, trackIndex, temporaryPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return AudioTrackRemuxResult.Failed("ffmpeg failed to start");
            }

            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                TryDelete(temporaryPath);
                throw;
            }

            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                TryDelete(temporaryPath);
                return AudioTrackRemuxResult.Failed(
                    Redact($"ffmpeg exited with code {process.ExitCode}: {stderr}", source.PhysicalPath, temporaryPath, destinationPath));
            }

            if (!IsValidOutput(temporaryPath))
            {
                TryDelete(temporaryPath);
                return AudioTrackRemuxResult.Failed("ffmpeg produced no readable output");
            }

            Publish(temporaryPath, destinationPath);
            return AudioTrackRemuxResult.Success();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            TryDelete(temporaryPath);
            return AudioTrackRemuxResult.Failed(
                Redact(exception.Message, source.PhysicalPath, temporaryPath, destinationPath));
        }
        finally
        {
            process?.Dispose();
        }
    }

    internal static IReadOnlyList<string> BuildArguments(string sourcePath, int trackIndex, string temporaryPath) =>
    [
        "-nostdin",
        "-hide_banner",
        "-loglevel", "error",
        "-i", sourcePath,
        "-map", "0:v",
        "-map", $"0:a:{trackIndex}",
        "-c", "copy",
        "-movflags", "+faststart",
        "-f", "mp4",
        "-y", temporaryPath
    ];

    private static bool SourceMatches(VideoFileEntry source)
    {
        try
        {
            var file = new FileInfo(source.PhysicalPath);
            return file.Exists && file.Length == source.SizeBytes && file.LastWriteTimeUtc == source.LastWriteTimeUtc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsValidOutput(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Publish(string temporaryPath, string destinationPath)
    {
        try
        {
            if (IsValidOutput(destinationPath))
            {
                TryDelete(temporaryPath);
                return;
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
        }
        catch (IOException)
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Redact(string diagnostic, string sourcePath, string temporaryPath, string destinationPath)
    {
        var redacted = diagnostic
            .Replace(sourcePath, "<source>", StringComparison.Ordinal)
            .Replace(temporaryPath, "<temp>", StringComparison.Ordinal)
            .Replace(destinationPath, "<final>", StringComparison.Ordinal)
            .Replace(_previewRoot, "<preview-root>", StringComparison.Ordinal);

        return redacted.Length > MaxDiagnosticLength ? redacted[..MaxDiagnosticLength] : redacted;
    }
}
