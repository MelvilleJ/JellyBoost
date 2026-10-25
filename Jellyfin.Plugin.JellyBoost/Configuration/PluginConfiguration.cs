using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyBoost.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableServerSideBoost { get; set; } = true;

    public bool InjectWebClient { get; set; } = true;

    public double MaxGainDb { get; set; } = 20;

    public double DefaultGainDb { get; set; }

    public bool DefaultNightMode { get; set; }

    public List<UserBoostSettings> Users { get; set; } = [];

    // Devices whose web player applies the boost itself, so the server must not boost again.
    public List<ClientBoostDevice> ClientBoostDevices { get; set; } = [];
}

public class UserBoostSettings
{
    public Guid UserId { get; set; }

    public double GainDb { get; set; }

    public bool NightMode { get; set; }
}

public class ClientBoostDevice
{
    public string DeviceId { get; set; } = string.Empty;

    public DateTime LastSeenUtc { get; set; }
}
