using System.Text;

namespace CallOfSlop;

/// <summary>
/// Ship designs spawned from Creative's parts menu (its Designs tab, UI/UiSandboxDesigns.cs; DesignParts.cs). The client sends the design's
/// JSON inside a held spawn's prepare (CActKind.SpawnDesign in CActKind.SpawnPrepare): the host reads it as a design, keeps it in its table
/// and its world, and makes it ready as any parts-menu blueprint is (the spawner builds its elements a slice a tick), so its ghost comes out
/// and it is placed by aiming (GameSandboxBlueprintGhost.cs). The design reaches every peer before the body that names its elements.
/// </summary>
public sealed partial class Game
{
    /// <summary>Client: a design from the Designs tab, placed with a ghost.</summary>
    public void SandboxPrepareDesign(string json, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > DesignParts.MaxText)
        {
            SandboxSay($"the {name} is too big to send ({bytes.Length / 1024} KB)");
            return;
        }
        var w = new PacketWriter(bytes.Length + 64); // (a rare message, sized for its design)
        w.Begin(Msg.CAct).U8((byte)CActKind.SpawnDesign);
        w.U32((uint)bytes.Length).Bytes(bytes);
        SpawnPrepareSend(w, name);
    }

    /// <summary>Server: CActKind.SpawnDesign after its kind byte: the design read, kept and asked for as a blueprint of its body.</summary>
    private string? SandboxSpawnDesign(Player p, ref PacketReader r)
    {
        var n = r.U32();
        if (n > DesignParts.MaxText)
            return "that design is too big";
        var json = Encoding.UTF8.GetString(r.Take((int)n));
        if (SandboxMenuSpawnRefusal(p) is { } no)
            return no;
        if (!SandboxInfiniteParts)
            return "ship designs spawn only in a creative world";
        if (DesignParts.Canon(json) is not { } text || DesignParts.Register(text) is not { } hash)
            return $"that isn't a ship design this build reads ({DesignParts.LastRefusal})";
        var name = DesignParts.Find(hash)!.Name;
        Constructs.KeepShipDesign(hash); // (to everyone with the next flush, before the body)
        var bp = DesignParts.BlueprintOf(hash, name);
        var ground = ConstructWorld.Ground(World.Query, p.Position + GravityFrames.FlatForward(p.Position, p.Yaw) * 6f);
        if (VehicleSpawn.RequestBlueprint(bp, ground, p.Yaw, p.Id, p.Id, _blueprintSpawned ??= BlueprintSpawned) is { } busy)
            return busy;
        if (VehicleSpawn.HoldFor != p.Id)
            SandboxHands.Notices.Add((p.Id, $"placing the {name}"));
        Console.WriteLine($"[design] player {p.Id} asked for \"{name}\" ({hash}, {text.Length} bytes)");
        return null;
    }
}
