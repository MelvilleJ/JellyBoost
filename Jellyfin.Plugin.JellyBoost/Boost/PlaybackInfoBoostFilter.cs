using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBoost.Boost;

// Direct play bypasses ffmpeg entirely, so for boosted users the server steers clients to an
// audio-only transcode: video is still stream-copied, only the audio track is re-encoded.
public class PlaybackInfoBoostFilter : IAsyncActionFilter
{
    private readonly BoostSettingsStore _store;
    private readonly ILogger<PlaybackInfoBoostFilter> _logger;

    public PlaybackInfoBoostFilter(BoostSettingsStore store, ILogger<PlaybackInfoBoostFilter> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IsPostedPlaybackInfo(context) && ShouldForceAudioTranscode(context))
        {
            // Query parameters take precedence over the request body in MediaInfoController.
            context.ActionArguments["enableDirectPlay"] = false;
            context.ActionArguments["enableDirectStream"] = false;
            context.ActionArguments["allowAudioStreamCopy"] = false;
            context.ActionArguments["enableTranscoding"] = true;
            context.ActionArguments["allowVideoStreamCopy"] = true;
        }

        return next();
    }

    private static bool IsPostedPlaybackInfo(ActionExecutingContext context)
    {
        return context.ActionDescriptor is ControllerActionDescriptor
        {
            ControllerName: "MediaInfo",
            ActionName: "GetPostedPlaybackInfo"
        };
    }

    private bool ShouldForceAudioTranscode(ActionExecutingContext context)
    {
        if (!_store.Configuration.EnableServerSideBoost)
        {
            return false;
        }

        var user = context.HttpContext.User;
        var userId = user.GetJellyfinUserId();
        if (userId is null || !_store.GetForUser(userId.Value).IsActive)
        {
            return false;
        }

        var deviceId = user.GetJellyfinDeviceId();
        if (_store.IsClientBoostDevice(deviceId))
        {
            return false;
        }

        _logger.LogDebug("JellyBoost forcing audio transcode for user {UserId} on device {DeviceId}", userId, deviceId);
        return true;
    }
}
