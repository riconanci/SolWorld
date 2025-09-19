// solworld/SolWorldMod/Source/TeamTypes.cs
namespace SolWorldMod
{
    public enum TeamColor
    {
        Red,
        Blue
    }

    public enum ArenaState
    {
        Idle,       // Waiting for next round
        Preview,    // 30s paused preview phase
        Combat,     // 4m combat phase
        Ended,      // Round ended, processing results
        Resetting   // Arena cleanup and reset
    }
}