namespace NEMO
{
	public interface IWorld
	{
		int width { get; }
		int height { get; }
		long totalTicks { get; }
		ICell[] iGrid { get; }
	}

	public interface ICell
	{
		bool isBlock { get; }
		ICreature? occupant { get; }
		object? foodItem { get; }
		SignalData[] signals { get; }
	}

	public interface ICreature
	{
		int x { get; }
		int y { get; }
		int lastX { get; }
		int lastY { get; }
		int facingDirection { get; }
		int lastFacing { get; }
		int age { get; }
		float energy { get; set; }
		float startingEnergy { get; }
		int genomeHash { get; }
		byte colorR { get; }
		byte colorG { get; }
		byte colorB { get; }
		float intentMove { get; set; }
		float intentMoveX { get; set; }
		float intentMoveY { get; set; }
		float intentRotate { get; set; }
		float intentConsume { get; set; }
		float intentAttack { get; set; }
		float intentSignalIntensity { get; set; }
		int intentSignalChannel { get; set; }
		float intentSignalDecay { get; set; }
		bool isDead { get; }
		float[] phenoCache { get; }
		float GetPheno(PType type);
		IWorld world { get; }
	}
}