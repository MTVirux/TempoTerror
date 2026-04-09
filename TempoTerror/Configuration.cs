namespace TempoTerror;

using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Newtonsoft.Json;
using TempoTerror.Models;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string WebSocketUrl { get; set; } = "ws://127.0.0.1:10501/ws";

    public float DisplayTimeSeconds { get; set; } = 10f;

    public bool ShowGcd { get; set; } = true;

    public bool ShowOgcd { get; set; } = true;

    public bool ShowPet { get; set; } = true;

    public bool ShowAutoAttack { get; set; } = true;

    public bool ShowCastBars { get; set; } = true;

    public bool ShowGrid { get; set; } = true;

    public bool ShowHorizontalGridLines { get; set; } = true;

    public bool ShowRowLabels { get; set; } = true;

#if DEBUG
    public bool ShowDebugInfo { get; set; } = true;
#endif

    public float IconScale { get; set; } = 1.0f;

    public Vector4 BackgroundColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.0f);

    public bool PinMainWindow { get; set; }

    public bool PinConfigWindow { get; set; }

    public bool DisableEscClose { get; set; } = true;

    public bool ShowTitleBar { get; set; }

    public bool HideOutOfCombat { get; set; }

    public float HideOutOfCombatDelay { get; set; } = 3.0f;

    public bool ShowOnStart { get; set; } = true;

    public uint? SelectedPlayerId { get; set; }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<ActionType> RowOrder { get; set; } = [ActionType.AutoAttack, ActionType.Gcd, ActionType.Ogcd, ActionType.Pet];

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<ActionType, float> RowHeights { get; set; } = new()
    {
        { ActionType.AutoAttack, 1.0f },
        { ActionType.Gcd, 1.0f },
        { ActionType.Ogcd, 0.71f },
        { ActionType.Pet, 0.8f },
    };
}
