namespace Jellyfin.Plugin.JellyBoost.Boost;

public sealed record BoostSettings(double GainDb, bool NightMode)
{
    public bool IsActive => GainDb > 0 || NightMode;
}
