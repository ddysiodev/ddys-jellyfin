using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Ddys.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Ddys;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string PluginName = "低端影视 DDYS";
    public const string PluginDescription = "低端影视 API 的官方 Jellyfin Server 频道插件。";

    public static readonly Guid PluginId = Guid.Parse("1bb6d203-7ff2-40c1-a0b6-7f8355120b61");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Configuration.Normalize();
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => PluginName;

    public override Guid Id => PluginId;

    public override string Description => PluginDescription;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

    public PluginConfiguration Options
    {
        get
        {
            Configuration.Normalize();
            return Configuration;
        }
    }

    public void SaveNormalizedConfiguration(PluginConfiguration configuration)
    {
        configuration.Normalize();
        UpdateConfiguration(configuration);
        Api.DdysClient.ClearCache();
    }
}
