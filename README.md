# Indexed Enemy Randomizer

A Terraria mod that replaces enemies, bosses, and critters using the world seed. If a Bunny becomes a Zombie in your world, it will keep doing so after you save and reload.

Built for tModLoader 2026.8 and Terraria 1.4.4.9.

## How it works

By default, pre-Hardmode enemies replace other pre-Hardmode enemies, and Hardmode enemies replace other Hardmode enemies. Entering Hardmode does not change the mapping. A Hardmode enemy spawned early with another mod still uses its Hardmode replacement.

Your world's evil determines the early pool. Enemies from the other evil join the Hardmode pool by default. You can disable that or combine both progression pools in the settings.

The mod replaces spawns where they normally happen. If Sharks become Eaters of Worlds, look for them where Sharks spawn. It does not add Shark spawns to ordinary land. All eligible enemies or groups have a place in the mapping, but finding one depends on where and when its original enemy can spawn.

Replacements use their own stats, drops, and boss defeat flags. Killing a Zombie that replaced a boss does not count as defeating that boss. Progression rules keep required bosses from being placed behind their own unlocks—for example, Skeletron cannot replace a Dungeon enemy.

## Groups and exceptions

Enemies that share a banner can share one mapping. Each spawn then picks a random variant from that group. Ordinary Zombies group together; Raincoat Zombies stay separate. Other grouping rules include:

- **The Twins:** spawn together by default. This can be disabled.
- **Scutlix:** the rider and mount always spawn together.
- **Truffle Worm:** its normal and fleeing forms share a mapping.
- **Critter movement forms:** Seagulls, Grebes, brown Ducks, white Ducks, Goldfish, and Gold Goldfish each keep their two forms together, even with banner grouping off.
- **Gold critters:** grouped families use vanilla gold odds, normally 1 in 400, with the nearest living player's luck.
- **Old One's Army:** each grouped species keeps one mapping across every tier. Its earliest tier determines its pool. Replacements upgrade after a mechanical boss and after Golem; families without a lower tier use their earliest form. Progression never reshuffles the mapping.

Wall of Flesh, Dungeon Guardian, pillars, and the regular cultists that summon Lunatic Cultist stay unchanged. **Lunatic Cultist himself is randomized.** Town NPCs are protected by default; pets, rescue forms, and special NPCs such as the Old Man remain protected even if town randomization is enabled.

Required body parts and exclusive attack helpers are excluded. This includes Duke Fishron's Sharkrons, Mini Star Cells, Alien Larvae, and Dungeon Spirits. They still appear through their normal encounters. Ordinary creatures spawned by other NPCs also stay unchanged by default: King Slime's slimes remain slimes, while naturally spawning slimes still randomize.

Golden Slime and Jungle Mimic are only replacement choices on Celebration-enabled worlds, including Zenith. Mechdusa stays intact on worlds that support its summon.

## Combat and events

Enemies no longer flee just because daytime arrives or their event ends. Bosses can fight outside their usual time and biome without environmental enrage. Normal despawning after players die or move too far away still applies, and aquatic creatures still need water.

Only one boss of each type can be active by default. Different bosses can coexist. Boss and miniboss limits have separate switches; blocked spawns are cancelled rather than rerolled.

Event progress follows replacement enemies. If Goblin Peons become Blue Slimes, killing Blue Slimes during the invasion advances it. Pillar shields work the same way, with kills counting anywhere on the map. Both have separate settings to count the original event species instead. Old One's Army replacements can drop Etherian Mana under the event's normal drop rules.

Event bars stay visible while an event is active, unless you hide them. Sonar, boss announcements, and wave messages use replacement names.

Seasonal enemies can appear as replacements year-round. All three cavern families—Crawdad, Giant Shelly, and Salamander—are available in every world. Town NPCs can randomly receive their shimmer appearance, and Red/Purple Solution can shimmer into each other at any progression stage.

## Commands

These commands are available to every player, without admin permissions.

| Command | What it does |
| --- | --- |
| `/spawnnpc Zombie` or `/spawnnpc 3` | Spawns that exact NPC beside you, bypassing its mapping. Boss limits still apply. |
| `/hint Zombie` | Tells you which original enemy to look for to find Zombies. |
| `/dumpmapping` | Saves the full mapping and settings to a text file. |

Names are case-insensitive. You can use full names, IDs, or an unambiguous partial name. `/hint twins` also works when the Twins are grouped.

Exports go in `IndexedEnemyRandomizer/MappingExports/` under the host's tModLoader save folder. On a dedicated server, the file is on the server. Chat shows the filename, not the host's full personal folder path.

In the server console, omit the slash; direct spawns appear at world spawn. If another mod uses the same command name, add the mod prefix, for example `/IndexedEnemyRandomizer:hint Zombie`.

## Settings

The host's settings apply to everyone. Changing which NPCs are included or grouped rebuilds the mapping for future spawns. Existing NPCs stay as they are.

| Setting | Default |
| --- | --- |
| Separate pre-Hardmode and Hardmode pools | On |
| Include opposite-evil enemies in Hardmode | On |
| Group NPC variants by banner | On |
| Group critters with their gold variants | On |
| Miscellaneous groupings | On |
| Group Old One's Army enemy tiers | On |
| Treat the Twins as one encounter | On |
| Include bosses in randomization | On |
| Block Hardmode statue loot before Hardmode | On |
| Reduce statue loot without a vanilla rate rule | On |
| Include town NPCs in randomization | Off |
| Keep NPC-spawned creatures unchanged | On |
| Limit each boss to one active encounter | On |
| Include minibosses in the one-at-a-time limit | On |
| Randomize town NPC shimmer appearance | On |
| Shimmer evil solutions at any time | On |
| Legacy event kill credit | Off |
| Legacy pillar kill credit | Off |

The miniboss limit works independently of the boss limit. The two legacy credit settings use actual event enemies instead of their mapped replacements. Combat, appearance, shimmer, and credit switches do not reshuffle enemies.

Statue loot uses the actual spawned species. By default, Hardmode species give no statue loot before Hardmode. Species without an existing statue rate get a 20% multiplier; existing vanilla rates stay unchanged. This uses vanilla's player-damage requirement as well. Turning these options off restores the original rules. Older saved boss-exclusion preferences are preserved when upgrading to the inclusion setting.

Miscellaneous groupings combine Bees, Scorpions, Ducks, Fairies, Macaws, Cockatiels, ordinary Dragonflies, Squirrels, Penguins, and Birds into their respective families. Each spawn chooses a random member. Gold grouping keeps vanilla gold odds. Turning miscellaneous grouping off leaves the other grouping settings and movement-form pairs intact.

## Mod compatibility

Modded NPCs can be replaced with vanilla enemies, but are not replacement targets themselves. Their replacements use a seeded fallback based on the world's current progression. Progression safeguards and retreat fixes are designed for vanilla NPCs; other mods may need their own support.

## Building and testing

Use **Build + Reload** under tModLoader's **Workshop → Develop Mods**, or run:

```powershell
dotnet build
```

To compile without installing a `.tmod`:

```powershell
dotnet build /p:BuildMod=false
```

Run the regression checks with:

```powershell
dotnet run --project Tests/IndexedEnemyRandomizer.Tests.csproj
```

The tests cover mapping stability, progression rules, commands, spawn handling, and compatibility of the patched game methods. They do not replace in-game or multiplayer playtesting.
