using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Delivers shield-credit projectiles directly to their pillar so kills anywhere in the world can count.
/// </summary>
public sealed class PillarCreditProjectile : GlobalProjectile
{
    /// <summary>
    /// Suppresses native species-based bolts in mapped mode, then delivers accepted bolts on the host.
    /// </summary>
    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || projectile.type != ProjectileID.TowerDamageBolt)
            return;
        if (SuppressNativeBolt(ModContent.GetInstance<RandomizerConfig>().LegacyPillarCredit, source))
        {
            // The native bolt describes the slain NPC's actual species. Mapped
            // mode issues its own single bolt for the inverse-mapped source.
            projectile.active = false;
            return;
        }
        // Reuse vanilla's shield-credit projectile behavior after removing distance as a delivery constraint.
        Deliver(projectile, Main.npc);
    }

    /// <summary>
    /// Distinguishes vanilla NPC-parent bolts from this mod's explicitly marked credit bolts.
    /// </summary>
    internal static bool SuppressNativeBolt(bool legacy, IEntitySource source) => !legacy
        && source is EntitySource_Parent { Entity: NPC } && source.Context != PillarCreditNPC.CreditContext;

    /// <summary>
    /// Validates the target pillar index and relocates the bolt for vanilla collision-based shield accounting.
    /// </summary>
    internal static void Deliver(Projectile projectile, NPC[] npcs)
    {
        // The projectile stores a target NPC slot, not a pillar type; validate the slot before vanilla can dereference it.
        int index = (int)projectile.ai[0];
        if (index < 0 || index >= npcs.Length || npcs[index] == null || !npcs[index].active
            || !PillarCreditNPC.IsPillar(npcs[index].type))
        {
            // Vanilla Kill indexes ai[0]; an absent pillar must not receive credit.
            projectile.active = false;
            return;
        }
        // Deliver the existing credit bolt directly, avoiding travel range/time
        // limits. Vanilla collision/Kill applies one credit and syncs packet 101.
        projectile.Center = npcs[index].Center;
        projectile.velocity = Vector2.Zero;
        projectile.netUpdate = true;
    }
}

/// <summary>
/// Creates pillar credit for inverse-mapped kills or missing vanilla lunar subtypes in legacy mode.
/// </summary>
public sealed class PillarCreditNPC : GlobalNPC
{
    /// <summary>
    /// Marks mod-issued credit projectiles so the native-bolt suppression hook preserves them.
    /// </summary>
    internal const string CreditContext = "IndexedEnemyRandomizer:PillarCredit";
    /// <summary>
    /// Recognizes the four lunar tower controller NPCs.
    /// </summary>
    internal static bool IsPillar(int type) => type is NPCID.LunarTowerSolar or NPCID.LunarTowerVortex
        or NPCID.LunarTowerNebula or NPCID.LunarTowerStardust;

    // Vanilla already creates a bolt for the other lunar species. Only add the
    // omitted larvae, small forms, and Stardust worm segments, so nearby kills
    // and distant kills have the same single-credit behavior.
    /// <summary>
    /// Returns legacy credit only for lunar children/segments omitted by native bolt creation.
    /// </summary>
    internal static int AdditionalCreditPillar(int type) => type switch
    {
        NPCID.VortexLarva => NPCID.LunarTowerVortex,
        NPCID.StardustWormBody or NPCID.StardustWormTail or NPCID.StardustCellSmall
            or NPCID.StardustJellyfishSmall or NPCID.StardustSpiderSmall => NPCID.LunarTowerStardust,
        _ => NPCID.None
    };

    /// <summary>
    /// Maps a lunar enemy species to the shield it should reduce, or None for unrelated species.
    /// </summary>
    internal static int CreditPillar(int type) => type switch
    {
        >= NPCID.SolarCrawltipedeHead and <= NPCID.SolarSolenian => NPCID.LunarTowerSolar,
        >= NPCID.VortexRifleman and <= NPCID.VortexSoldier => NPCID.LunarTowerVortex,
        NPCID.NebulaBrain or NPCID.NebulaHeadcrab or NPCID.NebulaBeast or NPCID.NebulaSoldier => NPCID.LunarTowerNebula,
        >= NPCID.StardustWormHead and <= NPCID.StardustSoldier => NPCID.LunarTowerStardust,
        _ => NPCID.None
    };

    /// <summary>
    /// Awards host-side shield credit under the selected mode, provided the matching pillar still has a shield.
    /// </summary>
    public override void OnKill(NPC npc)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;
        bool legacy = ModContent.GetInstance<RandomizerConfig>().LegacyPillarCredit;
        if (!legacy && !TwinsEncounter.CanCredit(npc))
            return;
        // Legacy mode adds only missing native species. Mapped mode derives the pillar from the replacement's source.
        int pillarType;
        if (legacy)
        {
            pillarType = AdditionalCreditPillar(npc.type);
        }
        else
        {
            int target = TwinsEncounter.CreditTarget(npc);
            int source = MappedEventCredit.SourceForTarget(target);
            pillarType = CreditPillar(source);
        }
        if (pillarType == NPCID.None)
            return;
        // Do not create shield credit after that pillar's shield is already broken.
        int remaining = pillarType switch
        {
            NPCID.LunarTowerSolar => NPC.ShieldStrengthTowerSolar,
            NPCID.LunarTowerVortex => NPC.ShieldStrengthTowerVortex,
            NPCID.LunarTowerNebula => NPC.ShieldStrengthTowerNebula,
            _ => NPC.ShieldStrengthTowerStardust
        };
        // The pillar must actually exist; the context marker ensures this new bolt survives our suppression hook.
        int index = NPC.FindFirstNPC(pillarType);
        if (remaining > 0 && index >= 0)
            Projectile.NewProjectile(new EntitySource_Misc(CreditContext), Main.npc[index].Center, Vector2.Zero,
                ProjectileID.TowerDamageBolt, 0, 0f, Main.myPlayer, index);
    }
}
