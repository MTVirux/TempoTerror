namespace TempoTerror;

using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using TempoTerror.Models;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public float DisplayTimeSeconds { get; set; } = 10f;

    public bool ShowGcd { get; set; } = true;

    public bool ShowOgcd { get; set; } = true;

    public bool ShowPet { get; set; } = true;

    public bool ShowAutoAttack { get; set; } = true;

    public bool ShowCastBars { get; set; } = true;

    public bool ShowGrid { get; set; } = true;

    public float IconScale { get; set; } = 1.0f;

    public Vector4 BackgroundColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.5f);

    public bool PinMainWindow { get; set; }

    public bool PinConfigWindow { get; set; }

    public bool ShowOnStart { get; set; } = true;

    public uint? SelectedPlayerId { get; set; }

    public List<ActionType> RowOrder { get; set; } = [ActionType.AutoAttack, ActionType.Gcd, ActionType.Ogcd, ActionType.Pet];

    public Dictionary<ActionType, float> RowHeights { get; set; } = new()
    {
        { ActionType.AutoAttack, 1.0f },
        { ActionType.Gcd, 1.0f },
        { ActionType.Ogcd, 1.0f },
        { ActionType.Pet, 1.0f },
    };
}
