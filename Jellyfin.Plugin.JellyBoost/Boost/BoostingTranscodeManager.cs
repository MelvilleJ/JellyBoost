using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBoost.Boost;

// Wraps Jellyfin's transcode manager so every streaming ffmpeg job, whatever client asked for it, can carry the boost.
public sealed class BoostingTranscodeManager : ITranscodeManager, IDisposable
{
    private readonly ITranscodeManager _inner;
    private readonly BoostSettingsStore _store;
    private readonly ILogger<BoostingTranscodeManager> _logger;

    public BoostingTranscodeManager(ITranscodeManager inner, BoostSettingsStore store, ILogger<BoostingTranscodeManager> logger)
    {
        _inner = inner;
        _store = store;
        _logger = logger;
    }

    public Task<TranscodingJob> StartFfMpeg(
        StreamState state,
        string outputPath,
        string commandLineArguments,
        Guid userId,
        TranscodingJobType transcodingJobType,
        CancellationTokenSource cancellationTokenSource,
        string? workingDirectory = null)
    {
        var arguments = ApplyBoost(state, userId, commandLineArguments);
        return _inner.StartFfMpeg(state, outputPath, arguments, userId, transcodingJobType, cancellationTokenSource, workingDirectory);
    }

    private string ApplyBoost(StreamState state, Guid userId, string arguments)
    {
        if (!_store.Configuration.EnableServerSideBoost
            || !state.IsOutputVideo
            || userId == Guid.Empty
            || _store.IsClientBoostDevice(state.Request.DeviceId))
        {
            return arguments;
        }

        var filters = AudioFilters.Build(_store.GetForUser(userId));
        if (filters is null)
        {
            return arguments;
        }

        var boosted = AudioFilters.AppendToFfmpegArguments(arguments, filters);
        if (boosted is null)
        {
            _logger.LogInformation("JellyBoost skipped item {ItemId}: audio is being copied, not transcoded", state.Request.Id);
            return arguments;
        }

        _logger.LogInformation("JellyBoost applying \"{Filters}\" for user {UserId} on item {ItemId}", filters, userId, state.Request.Id);
        return boosted;
    }

    public TranscodingJob? GetTranscodingJob(string playSessionId) => _inner.GetTranscodingJob(playSessionId);

    public TranscodingJob? GetTranscodingJob(string path, TranscodingJobType type) => _inner.GetTranscodingJob(path, type);

    public void PingTranscodingJob(string playSessionId, bool? isUserPaused) => _inner.PingTranscodingJob(playSessionId, isUserPaused);

    public Task KillTranscodingJobs(string deviceId, string? playSessionId, Func<string, bool> deleteFiles)
        => _inner.KillTranscodingJobs(deviceId, playSessionId, deleteFiles);

    public void ReportTranscodingProgress(
        TranscodingJob job,
        StreamState state,
        TimeSpan? transcodingPosition,
        float? framerate,
        double? percentComplete,
        long? bytesTranscoded,
        int? bitRate)
        => _inner.ReportTranscodingProgress(job, state, transcodingPosition, framerate, percentComplete, bytesTranscoded, bitRate);

    public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) => _inner.OnTranscodeBeginRequest(path, type);

    public void OnTranscodeEndRequest(TranscodingJob job) => _inner.OnTranscodeEndRequest(job);

    public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) => _inner.LockAsync(outputPath, cancellationToken);

    public void Dispose() => (_inner as IDisposable)?.Dispose();
}
