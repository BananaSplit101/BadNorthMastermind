namespace BadNorthMP.Game
{
    /// <summary>
    /// What the mastermind keeps across islands in one multiplayer session (lives on the defender, who is
    /// authoritative). Reset when the lobby ends.
    /// </summary>
    internal static class MastermindSession
    {
        private static float carriedGold;

        public static void Carry(float gold) => carriedGold = gold;

        public static float TakeCarriedGold()
        {
            float gold = carriedGold;
            carriedGold = 0f;
            return gold;
        }

        public static void Reset() => carriedGold = 0f;
    }
}
