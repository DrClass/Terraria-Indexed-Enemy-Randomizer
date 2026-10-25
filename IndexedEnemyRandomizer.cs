using Terraria.ModLoader;

namespace IndexedEnemyRandomizer
{
	/// <summary>
	/// Entry point loaded by tModLoader. Feature-specific systems contain the gameplay logic;
	/// this class owns the shared AI hooks and dispatches the mod's network packets.
	/// </summary>
	public class IndexedEnemyRandomizer : Mod
	{
		/// <summary>
		/// Installs the vanilla AI overrides when tModLoader loads the mod.
		/// </summary>
		public override void Load() => PersistentEnemyAI.Load();
		/// <summary>
		/// Detaches shared AI hooks and releases their current-NPC reference.
		/// </summary>
		public override void Unload() => PersistentEnemyAI.Unload();
		/// <summary>
		/// Routes incoming mod packets to the host-authoritative sonar preview protocol.
		/// </summary>
		public override void HandlePacket(System.IO.BinaryReader reader, int whoAmI) => RandomizerSonar.HandlePacket(reader, whoAmI);
	}
}
