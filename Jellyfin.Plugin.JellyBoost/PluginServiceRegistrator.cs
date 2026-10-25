using System;
using System.Linq;
using Jellyfin.Plugin.JellyBoost.Boost;
using Jellyfin.Plugin.JellyBoost.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBoost;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<BoostSettingsStore>();
        serviceCollection.AddSingleton<PlaybackInfoBoostFilter>();
        serviceCollection.Configure<MvcOptions>(options => options.Filters.AddService<PlaybackInfoBoostFilter>());
        serviceCollection.AddTransient<IStartupFilter, ClientScriptStartupFilter>();
        DecorateTranscodeManager(serviceCollection);
    }

    // Plugins register after the core services, so replacing the descriptor here wins.
    private static void DecorateTranscodeManager(IServiceCollection services)
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(ITranscodeManager));
        if (original is null)
        {
            return;
        }

        services.Remove(original);
        services.AddSingleton<ITranscodeManager>(provider => new BoostingTranscodeManager(
            CreateInner(provider, original),
            provider.GetRequiredService<BoostSettingsStore>(),
            provider.GetRequiredService<ILogger<BoostingTranscodeManager>>()));
    }

    private static ITranscodeManager CreateInner(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ITranscodeManager instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (ITranscodeManager)descriptor.ImplementationFactory(provider);
        }

        var type = descriptor.ImplementationType
            ?? throw new InvalidOperationException("Cannot determine the ITranscodeManager implementation to wrap.");
        return (ITranscodeManager)ActivatorUtilities.CreateInstance(provider, type);
    }
}
