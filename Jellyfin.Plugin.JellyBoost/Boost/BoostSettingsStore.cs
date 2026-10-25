using System;
using System.Linq;
using Jellyfin.Plugin.JellyBoost.Configuration;

namespace Jellyfin.Plugin.JellyBoost.Boost;

public class BoostSettingsStore
{
    private static readonly TimeSpan _clientDeviceExpiry = TimeSpan.FromDays(60);
    private static readonly TimeSpan _clientDeviceRefreshInterval = TimeSpan.FromHours(12);

    private readonly object _writeLock = new();

    public virtual PluginConfiguration Configuration => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public double MaxGainDb => Math.Max(0, Configuration.MaxGainDb);

    public BoostSettings GetForUser(Guid userId)
    {
        var config = Configuration;
        var user = config.Users.FirstOrDefault(u => u.UserId == userId);
        var gain = user?.GainDb ?? config.DefaultGainDb;
        return new BoostSettings(Math.Clamp(gain, 0, MaxGainDb), user?.NightMode ?? config.DefaultNightMode);
    }

    public BoostSettings SaveForUser(Guid userId, double gainDb, bool nightMode)
    {
        var settings = new BoostSettings(Math.Round(Math.Clamp(gainDb, 0, MaxGainDb), 1), nightMode);
        lock (_writeLock)
        {
            var config = Configuration;
            var user = config.Users.FirstOrDefault(u => u.UserId == userId);
            if (user is null)
            {
                user = new UserBoostSettings { UserId = userId };
                config.Users.Add(user);
            }

            user.GainDb = settings.GainDb;
            user.NightMode = settings.NightMode;
            Save();
        }

        return settings;
    }

    public bool IsClientBoostDevice(string? deviceId)
    {
        return !string.IsNullOrEmpty(deviceId)
            && Configuration.ClientBoostDevices.Any(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
    }

    public void SetClientBoostDevice(string deviceId, bool enabled)
    {
        lock (_writeLock)
        {
            var config = Configuration;
            var now = DateTime.UtcNow;
            var existing = config.ClientBoostDevices.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));

            // Clients re-register on every page load; skip disk writes when nothing meaningful changed.
            if (enabled && existing is not null && now - existing.LastSeenUtc < _clientDeviceRefreshInterval)
            {
                return;
            }

            if (!enabled && existing is null)
            {
                return;
            }

            config.ClientBoostDevices.RemoveAll(d => d == existing || now - d.LastSeenUtc > _clientDeviceExpiry);
            if (enabled)
            {
                config.ClientBoostDevices.Add(new ClientBoostDevice { DeviceId = deviceId, LastSeenUtc = now });
            }

            Save();
        }
    }

    protected virtual void Save() => Plugin.Instance?.SaveConfiguration();
}
