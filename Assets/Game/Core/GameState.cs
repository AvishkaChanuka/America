namespace AmericanBattlefront.Core
{
    public enum GameState
    {
        Loading,        // Setting up level, placing props, baking NavMesh
        Ready,          // Briefing shown; waiting for player to press Start
        Playing,        // Active combat; wave director running
        WaveBreak,      // Between waves; no new damage or spawns
        Paused,         // Game paused
        Victory,        // All waves cleared; player alive
        Defeat,         // Player health reached zero
        Error           // Setup failed; show diagnostic
    }
}
