using UnityEngine;

namespace KioskMP
{
	public static partial class Game
	{
		private static readonly float[] _spawnDistances = { 0f, 0.6f, 1.2f, 1.8f };

		public static Vector3 SpawnOffset(int id, Vector3 left)
		{
			if (id < 0 || id > 3)
			{
				return Vector3.zero;
			}
			return left * _spawnDistances[id];
		}
	}
}
