namespace TempoTerror.Services;

using System.Collections.Concurrent;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

public sealed class IconCache
{
    private readonly ITextureProvider textureProvider;
    private readonly IDataManager dataManager;
    private readonly ConcurrentDictionary<uint, ushort> actionIconMap = new();

    public IconCache(ITextureProvider textureProvider, IDataManager dataManager)
    {
        this.textureProvider = textureProvider;
        this.dataManager = dataManager;
    }

    public IDalamudTextureWrap? GetIcon(uint actionId, ushort knownIconId = 0)
    {
        var iconId = knownIconId != 0 ? knownIconId : this.LookupIconId(actionId);
        if (iconId == 0)
            return null;

        var tex = this.textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        var wrap = tex.GetWrapOrDefault();
        return wrap;
    }

    public IDalamudTextureWrap? GetIconWrap(uint actionId, ushort knownIconId = 0)
    {
        return this.GetIcon(actionId, knownIconId);
    }

    private ushort LookupIconId(uint actionId)
    {
        return this.actionIconMap.GetOrAdd(actionId, id =>
        {
            var row = this.dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(id);
            return row is not null ? (ushort)row.Value.Icon : (ushort)0;
        });
    }
}
