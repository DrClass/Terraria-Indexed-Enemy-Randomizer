using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Shows concise replacement names for boss spawns and event waves, deferring announcements until a spawn succeeds.
/// </summary>
public sealed class SpawnMessages : ModSystem
{
    /// <summary>
    /// Hooks wave text and patches native announcement sites that run before replacement selection.
    /// </summary>
    public override void Load()
    {
        On_Lang.GetInvasionWaveText += WaveText;
        foreach (string name in new[] { "SpawnBoss", "SpawnOnPlayer", "SpawnSkeletron", "NewNPC" })
            MonoModHooks.Modify(typeof(NPC).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!,
                il => PatchAnnouncements(il, name));
    }

    /// <summary>
    /// Removes the wave-text hook; tModLoader removes the owned IL patches.
    /// </summary>
    public override void Unload() => On_Lang.GetInvasionWaveText -= WaveText;

    /// <summary>
    /// Resolves a source to its mapped representative without rolling a random variant or changing spawn state.
    /// </summary>
    internal static int PreviewTarget(int source)
    {
        if (!ContentSamples.NpcsByNetId.TryGetValue(source, out NPC sample) || !RandomizerSystem.CanRandomize(sample))
            return source;
        // Preview the group deterministically without consuming RNG that the eventual spawn needs for its member roll.
        var map = RandomizerSystem.GetMapping();
        return map.Replacement(source, sample.ModNPC?.FullName ?? source.ToString(CultureInfo.InvariantCulture),
            sample.ModNPC == null ? map.IsEarlySource(source) : !Main.hardMode);
    }

    /// <summary>
    /// Chooses one stable member ID for a group's display name instead of listing every variant.
    /// </summary>
    internal static int NameId(WorldMapping map, int id) => map.Members(id)[0];

    /// <summary>
    /// Creates concise network-localizable text, with special labels for paired encounters and Truffle Worms.
    /// </summary>
    internal static NetworkText ShortName(int id)
    {
        var map = RandomizerSystem.GetMapping();
        if (map.IsTwinsGroup(id))
            return NetworkText.FromLiteral("The Twins");
        if (map.IsScutlixGroup(id))
            return NetworkText.FromLiteral("Scutlix and rider");
        if (map.IsTruffleWormGroup(id))
            id = NPCID.TruffleWorm;
        return Lang.GetNPCName(NameId(map, id)).ToNetworkText();
    }

    /// <summary>
    /// Rebuilds a wave announcement from distinct mapped target groups using vanilla wave localization keys.
    /// </summary>
    private static NetworkText WaveText(On_Lang.orig_GetInvasionWaveText orig, int wave, short[] npcIds)
    {
        // Collapse source variants that resolve to the same group so the wave message does not repeat the same enemy.
        int[] targets = npcIds.Select(id => PreviewTarget(id)).Distinct().ToArray();
        if (targets.Length == 0)
            return orig(wave, npcIds);
        // Vanilla's format expects the wave label first, followed by each enemy name as a separate localization argument.
        object[] names = new object[targets.Length + 1];
        if (wave == -1)
            names[0] = NetworkText.FromKey("Game.FinalWave");
        else if (wave == 1)
            names[0] = NetworkText.FromKey("Game.FirstWave");
        else
            names[0] = NetworkText.FromKey("Game.Wave", wave);
        for (int i = 0; i < targets.Length; i++)
            names[i + 1] = ShortName(targets[i]);
        return NetworkText.FromKey("Game.InvasionWave_Type" + targets.Length, names);
    }

    /// <summary>
    /// Announces when either the original source or the resulting replacement is a boss.
    /// </summary>
    internal static bool ShouldAnnounce(bool originalBoss, bool replacementBoss) => originalBoss || replacementBoss;

    /// <summary>
    /// Sends the standard awakening message after a replacement has survived encounter checks and partner creation.
    /// </summary>
    internal static void Announce(NPC npc, bool originalBoss)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !ShouldAnnounce(originalBoss, BossRules.IsBossRoot(npc)))
            return;
        // NetworkText lets each client render the standard awakening phrase in its own language.
        NetworkText message = NetworkText.FromKey("Announcement.HasAwoken", ShortName(npc.netID));
        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(message, new Color(175, 75, 255));
        else
            Main.NewText(message.ToString(), 175, 75, 255);
    }

    /// <summary>
    /// Identifies native spawn announcements that should wait for randomization, preserving bypassed/helper summons.
    /// </summary>
    private static bool Suppress(int type, IEntitySource source) => !SpecialSeedRules.CreatingMechdusa
        && source is not DirectNpcSpawnSource
        && ContentSamples.NpcsByNetId.TryGetValue(type, out NPC sample) && RandomizerSystem.CanRandomize(sample)
        && !NpcChildProjectiles.PreserveSpawn(type, source, ModContent.GetInstance<RandomizerConfig>().PreserveNpcChildren);

    // Delay vanilla's early announcements until replacement and boss-limit checks
    // have succeeded. Other chat and protected/unrandomized summons stay vanilla.
    /// <summary>
    /// Conditionally skips native chat calls for pending replacements while preserving the evaluation stack.
    /// </summary>
    internal static void PatchAnnouncements(ILContext il, string methodName)
    {
        var cursor = new ILCursor(il);
        int count = 0;
        while (cursor.TryGotoNext(MoveType.Before, i => i.OpCode == OpCodes.Call
            && i.Operand is MethodReference m && m.ReturnType.FullName == "System.Void"
            && (m.DeclaringType.FullName == typeof(Main).FullName && m.Name == "NewText"
                || m.DeclaringType.FullName == typeof(ChatHelper).FullName && m.Name == "BroadcastChatMessage")))
        {
            // Remember the original call and its successor before inserting a conditional branch around it.
            Instruction call = cursor.Next;
            var method = (MethodReference)call.Operand;
            Instruction after = call.Next;
            // These spawn methods keep the source type in different argument slots; Skeletron has an implicit fixed type.
            if (methodName == "SpawnSkeletron")
                cursor.Emit(OpCodes.Ldc_I4, NPCID.SkeletronHead);
            else if (methodName == "NewNPC")
                cursor.Emit(OpCodes.Ldarg, 3);
            else if (methodName == "SpawnBoss")
                cursor.Emit(OpCodes.Ldarg, 2);
            else
                cursor.Emit(OpCodes.Ldarg, 1);
            if (methodName == "NewNPC")
                cursor.Emit(OpCodes.Ldarg_0);
            else
                cursor.Emit(OpCodes.Ldnull);
            cursor.EmitDelegate<Func<int, IEntitySource, bool>>(Suppress);
            // If the message stays native, execute its original call with the untouched argument stack.
            cursor.Emit(OpCodes.Brfalse, call);
            // Skipping a void call must also pop every already-evaluated argument, or the patched IL stack is invalid.
            for (int i = 0; i < method.Parameters.Count; i++)
                cursor.Emit(OpCodes.Pop);
            cursor.Emit(OpCodes.Br, after);
            cursor.Goto(after, MoveType.Before);
            count++;
        }
        if (count == 0)
            throw new InvalidOperationException("Cannot locate spawn announcements in " + methodName);
    }
}

/// <summary>
/// Previews randomized fishing NPCs in sonar text, asking the host for authoritative names in multiplayer.
/// </summary>
public sealed class RandomizerSonar : ModPlayer
{
    /// <summary>
    /// Monotonic catch sequence used to reject responses belonging to an older fishing result.
    /// </summary>
    private int requestId;
    /// <summary>
    /// World position at which the latest requested sonar popup should appear.
    /// </summary>
    private Vector2 pendingPosition;

    /// <summary>
    /// Handles kind 0 preview requests on the server and kind 1 localized-name responses on clients.
    /// The echoed request ID prevents a delayed answer from replacing a newer catch's popup.
    /// </summary>
    internal static void HandlePacket(BinaryReader reader, int sender)
    {
        // Both packet forms begin with a message kind and request sequence, followed by kind-specific payload.
        byte kind = reader.ReadByte();
        int request = reader.ReadInt32();
        if (kind == 0 && Main.netMode == NetmodeID.Server)
        {
            // Validate the requesting player and source ID before using host mapping data to construct a response.
            int source = reader.ReadInt32();
            if (sender < 0 || sender >= Main.maxPlayers || !Main.player[sender].active
                || !ContentSamples.NpcsByNetId.ContainsKey(source))
                return;
            // Echo the sequence with a serialized NetworkText so the client can localize and reject stale results.
            var response = ModContent.GetInstance<IndexedEnemyRandomizer>().GetPacket();
            response.Write((byte)1);
            response.Write(request);
            SpawnMessages.ShortName(SpawnMessages.PreviewTarget(source)).Serialize(response);
            response.Send(sender);
        }
        else if (kind == 1 && Main.netMode == NetmodeID.MultiplayerClient)
        {
            // Replies belong to the local player; only its newest outstanding catch can display this text.
            string text = NetworkText.Deserialize(reader).ToString();
            var sonar = Main.LocalPlayer.GetModPlayer<RandomizerSonar>();
            if (request != sonar.requestId || !Main.LocalPlayer.sonarPotion)
                return;
            PopupText.AssignAsSonarText(PopupText.NewText(Popup(text), sonar.pendingPosition));
        }
    }

    /// <summary>
    /// Creates the standard orange, upward-moving sonar popup with a three-second lifetime.
    /// </summary>
    private static AdvancedPopupRequest Popup(string text) => new()
    {
        Text = text, Color = Color.Orange, DurationInFrames = 180, Velocity = new Vector2(0, -4)
    };

    /// <summary>
    /// Updates only the sonar preview for an NPC catch; the actual catch remains available for normal spawn handling.
    /// </summary>
    public override void CatchFish(FishingAttempt attempt, ref int itemDrop, ref int npcSpawn,
        ref AdvancedPopupRequest sonar, ref Vector2 sonarPosition)
    {
        // Advance even for item catches or no potion, invalidating old NPC-preview replies after any newer catch.
        requestId++;
        if (npcSpawn <= 0 || !Player.sonarPotion)
            return;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            // World seeds/settings belong to the host. Do not guess the map on
            // a client or display the original catch while awaiting its answer.
            pendingPosition = sonarPosition;
            var request = Mod.GetPacket();
            request.Write((byte)0);
            request.Write(requestId);
            request.Write(npcSpawn);
            request.Send();
            sonar = Popup("");
            sonar.DurationInFrames = 1;
        }
        else
            sonar = Popup(SpawnMessages.ShortName(SpawnMessages.PreviewTarget(npcSpawn)).ToString());
    }
}
