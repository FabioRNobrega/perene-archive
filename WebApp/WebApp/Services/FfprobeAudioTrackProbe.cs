using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class FfprobeAudioTrackProbe : IVideoAudioTrackProbe
{
    public async Task<IReadOnlyList<AudioTrackProbeResult>> GetAudioTracksAsync(string physicalPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
        {
            "-v", "error",
            "-select_streams", "a",
            "-show_entries", "stream=index,codec_name:stream_tags=language",
            "-of", "json",
            physicalPath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return [];
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }

            if (process.ExitCode != 0)
            {
                return [];
            }

            return ParseOutput(await stdoutTask);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return [];
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Parses ffprobe JSON. Returns an empty list unless the file has more than one audio stream;
    /// <see cref="AudioTrackProbeResult.Index"/> is the zero-based position among audio streams.
    /// </summary>
    internal static IReadOnlyList<AudioTrackProbeResult> ParseOutput(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out var streams) ||
                streams.ValueKind != JsonValueKind.Array ||
                streams.GetArrayLength() < 2)
            {
                return [];
            }

            var results = new List<AudioTrackProbeResult>();
            foreach (var stream in streams.EnumerateArray())
            {
                string? language = null;
                if (stream.TryGetProperty("tags", out var tags) &&
                    tags.ValueKind == JsonValueKind.Object &&
                    tags.TryGetProperty("language", out var languageElement) &&
                    languageElement.ValueKind == JsonValueKind.String)
                {
                    language = languageElement.GetString();
                    if (string.IsNullOrWhiteSpace(language) ||
                        string.Equals(language, "und", StringComparison.OrdinalIgnoreCase))
                    {
                        language = null;
                    }
                }

                string? codec = stream.TryGetProperty("codec_name", out var codecElement) &&
                                codecElement.ValueKind == JsonValueKind.String
                    ? codecElement.GetString()
                    : null;

                results.Add(new AudioTrackProbeResult(results.Count, language, codec));
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
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
}
