using System;
using System.Security.Claims;

namespace Jellyfin.Plugin.JellyBoost;

// Claim names mirror Jellyfin.Api.Constants.InternalClaimTypes, which is not shipped as a NuGet package.
internal static class ClaimsPrincipalExtensions
{
    public static Guid? GetJellyfinUserId(this ClaimsPrincipal user)
    {
        return Guid.TryParse(user.FindFirst("Jellyfin-UserId")?.Value, out var id) && id != Guid.Empty ? id : null;
    }

    public static string? GetJellyfinDeviceId(this ClaimsPrincipal user) => user.FindFirst("Jellyfin-DeviceId")?.Value;
}
