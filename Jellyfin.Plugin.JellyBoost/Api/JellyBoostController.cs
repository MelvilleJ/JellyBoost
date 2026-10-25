using System.Net.Mime;
using System.Reflection;
using Jellyfin.Plugin.JellyBoost.Boost;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyBoost.Api;

[ApiController]
[Route("JellyBoost")]
public class JellyBoostController : ControllerBase
{
    private readonly BoostSettingsStore _store;

    public JellyBoostController(BoostSettingsStore store)
    {
        _store = store;
    }

    [HttpGet("jellyboost.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.jellyboost.js");
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, "application/javascript");
    }

    [HttpGet("Settings")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<BoostSettingsDto> GetSettings()
    {
        var userId = User.GetJellyfinUserId();
        if (userId is null)
        {
            return BadRequest("A user session is required.");
        }

        return ToDto(_store.GetForUser(userId.Value));
    }

    [HttpPost("Settings")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<BoostSettingsDto> SaveSettings([FromBody] BoostSettingsUpdate update)
    {
        var userId = User.GetJellyfinUserId();
        if (userId is null)
        {
            return BadRequest("A user session is required.");
        }

        return ToDto(_store.SaveForUser(userId.Value, update.GainDb, update.NightMode));
    }

    [HttpPost("ClientBoost")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetClientBoost([FromBody] ClientBoostUpdate update)
    {
        var deviceId = User.GetJellyfinDeviceId();
        if (string.IsNullOrEmpty(deviceId))
        {
            return BadRequest("A device id is required.");
        }

        _store.SetClientBoostDevice(deviceId, update.Enabled);
        return NoContent();
    }

    private BoostSettingsDto ToDto(BoostSettings settings)
    {
        return new BoostSettingsDto(
            settings.GainDb,
            settings.NightMode,
            _store.MaxGainDb,
            _store.Configuration.EnableServerSideBoost,
            _store.IsClientBoostDevice(User.GetJellyfinDeviceId()));
    }
}

public sealed record BoostSettingsDto(double GainDb, bool NightMode, double MaxGainDb, bool ServerSideBoostEnabled, bool ClientBoostDevice);

public sealed record BoostSettingsUpdate(double GainDb, bool NightMode);

public sealed record ClientBoostUpdate(bool Enabled);
