using Jellyfin.Plugin.JellyBoost.Boost;
using Jellyfin.Plugin.JellyBoost.Web;
using Xunit;

namespace Jellyfin.Plugin.JellyBoost.Tests;

public class AudioFiltersTests
{
    private const string Filters = "volume=6dB,alimiter=limit=0.95:level=0";

    [Fact]
    public void Build_ReturnsNull_WhenInactive()
    {
        Assert.Null(AudioFilters.Build(new BoostSettings(0, false)));
    }

    [Fact]
    public void Build_AddsGainAndLimiter()
    {
        Assert.Equal(Filters, AudioFilters.Build(new BoostSettings(6, false)));
    }

    [Fact]
    public void Build_PutsCompressorFirstInNightMode()
    {
        var filters = AudioFilters.Build(new BoostSettings(3.5, true));
        Assert.Equal("acompressor=threshold=0.0316:ratio=4:attack=5:release=250:makeup=2,volume=3.5dB,alimiter=limit=0.95:level=0", filters);
    }

    [Fact]
    public void Append_MergesIntoExistingDownmixFilter()
    {
        var args = "-i in.mkv -map 0:0 -map 0:1 -codec:v:0 copy -codec:a:0 libfdk_aac -ac 2 -ab 256000 -af \"pan=stereo|c0=c0,volume=2\" -f hls out.m3u8";
        var result = AudioFilters.AppendToFfmpegArguments(args, Filters);
        Assert.Equal("-i in.mkv -map 0:0 -map 0:1 -codec:v:0 copy -codec:a:0 libfdk_aac -ac 2 -ab 256000 -af \"pan=stereo|c0=c0,volume=2," + Filters + "\" -f hls out.m3u8", result);
    }

    [Fact]
    public void Append_InsertsFilterAfterAudioCodec_WhenNoneExists()
    {
        var args = "-i in.mkv -codec:v:0 copy -codec:a:0 aac -ab 192000 -f mp4 out.mp4";
        var result = AudioFilters.AppendToFfmpegArguments(args, Filters);
        Assert.Equal("-i in.mkv -codec:v:0 copy -codec:a:0 aac -af \"" + Filters + "\" -ab 192000 -f mp4 out.mp4", result);
    }

    [Theory]
    [InlineData("-i in.mkv -codec:v:0 copy -codec:a:0 copy -f hls out.m3u8")]
    [InlineData("-i in.mkv -vn -acodec copy out.mka")]
    [InlineData("-i in.mkv -codec:v:0 copy -an out.mp4")]
    public void Append_ReturnsNull_WhenAudioIsNotReencoded(string args)
    {
        Assert.Null(AudioFilters.AppendToFfmpegArguments(args, Filters));
    }

    [Fact]
    public void Append_IgnoresVideoFilterOptions()
    {
        var args = "-i in.mkv -vf \"scale=1280:-2\" -codec:v:0 libx264 -codec:a:0 aac out.mp4";
        var result = AudioFilters.AppendToFfmpegArguments(args, Filters);
        Assert.Equal("-i in.mkv -vf \"scale=1280:-2\" -codec:v:0 libx264 -codec:a:0 aac -af \"" + Filters + "\" out.mp4", result);
    }

    [Fact]
    public void InjectScript_AddsTagBeforeBodyEnd_Once()
    {
        var html = "<html><body><div></div></body></html>";
        var once = ClientScriptInjectionMiddleware.InjectScript(html);
        Assert.Equal("<html><body><div></div>" + ClientScriptInjectionMiddleware.ScriptTag + "</body></html>", once);
        Assert.Equal(once, ClientScriptInjectionMiddleware.InjectScript(once));
    }
}
