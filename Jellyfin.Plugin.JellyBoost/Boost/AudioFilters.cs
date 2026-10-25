using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyBoost.Boost;

// Keep these values in sync with the Web Audio graph in Web/jellyboost.js so both paths sound alike.
public static partial class AudioFilters
{
    private const string NightModeCompressor = "acompressor=threshold=0.0316:ratio=4:attack=5:release=250:makeup=2";
    private const string Limiter = "alimiter=limit=0.95:level=0";

    public static string? Build(BoostSettings settings)
    {
        if (!settings.IsActive)
        {
            return null;
        }

        var filters = new List<string>();
        if (settings.NightMode)
        {
            filters.Add(NightModeCompressor);
        }

        if (settings.GainDb > 0)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"volume={settings.GainDb:0.#}dB"));
        }

        filters.Add(Limiter);
        return string.Join(',', filters);
    }

    // Returns null when the arguments do not re-encode audio, since filters cannot apply to a copied stream.
    public static string? AppendToFfmpegArguments(string arguments, string filters)
    {
        var codec = AudioCodecRegex().Match(arguments);
        if (!codec.Success || codec.Groups["codec"].Value == "copy")
        {
            return null;
        }

        var existing = AudioFilterRegex().Match(arguments);
        if (existing.Success)
        {
            var chain = existing.Groups["chain"];
            var merged = chain.Length == 0 ? filters : chain.Value + "," + filters;
            return arguments[..chain.Index] + merged + arguments[(chain.Index + chain.Length)..];
        }

        var insertAt = codec.Index + codec.Length;
        return arguments[..insertAt] + " -af \"" + filters + "\"" + arguments[insertAt..];
    }

    [GeneratedRegex(@"(?<=^|\s)-(?:codec:a|c:a|acodec)(?::0)?\s+(?<codec>[^\s""]+)")]
    private static partial Regex AudioCodecRegex();

    [GeneratedRegex(@"(?<=^|\s)-af\s+""(?<chain>[^""]*)""")]
    private static partial Regex AudioFilterRegex();
}
