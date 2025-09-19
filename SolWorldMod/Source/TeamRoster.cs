// solworld/SolWorldMod/Source/TeamRoster.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SolWorldMod
{
    public class Fighter
    {
        public string WalletFull { get; set; }     // Full wallet address
        public string WalletShort { get; set; }    // Display name (xxxxx....xxxxx)
        public TeamColor Team { get; set; }
        public Pawn PawnRef { get; set; }          // Reference to spawned pawn
        public int Kills { get; set; }
        public bool Alive { get; set; } = true;
        
        public Fighter(string walletFull, TeamColor team)
        {
            WalletFull = walletFull;
            WalletShort = ShortenAddress(walletFull);
            Team = team;
            Kills = 0;
            Alive = true;
        }
        
        private static string ShortenAddress(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 10)
                return address;
                
            // Use traditional substring - no modern range operators
            return address.Substring(0, 5) + "...." + address.Substring(address.Length - 5);
        }
    }

    public class RoundRoster
    {
        public string MatchId { get; set; }
        public List<Fighter> Red { get; set; } = new List<Fighter>();
        public List<Fighter> Blue { get; set; } = new List<Fighter>();
        
        // Round timing
        public int PreviewTicks { get; set; }
        public int CombatTicks { get; set; }
        
        // Live counters - use LINQ (available in RimWorld 1.6)
        public int RedAlive => Red.Count(f => f.Alive);
        public int BlueAlive => Blue.Count(f => f.Alive);
        public int RedKills => Red.Sum(f => f.Kills);
        public int BlueKills => Blue.Sum(f => f.Kills);
        
        // Round reward info
        public float RoundRewardTotalSol { get; set; } = 1.0f;
        public float PayoutPercent { get; set; } = 0.20f;
        public float PerWinnerPayout => RoundRewardTotalSol * PayoutPercent / 10.0f;
        
        // State
        public bool IsLive { get; set; } = false;
        public TeamColor? Winner { get; set; } = null;
        
        public RoundRoster()
        {
            MatchId = GenerateMatchId();
            PreviewTicks = SolWorldSettings.PREVIEW_SECONDS * 60;
            CombatTicks = SolWorldSettings.COMBAT_SECONDS * 60;
        }
        
        private string GenerateMatchId()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Rand.Range(1000, 9999);
        }
        
        public TeamColor? DetermineWinner()
        {
            if (RedAlive > BlueAlive) return TeamColor.Red;
            if (BlueAlive > RedAlive) return TeamColor.Blue;
            
            // Tiebreak 1: Total team kills
            if (RedKills > BlueKills) return TeamColor.Red;
            if (BlueKills > RedKills) return TeamColor.Blue;
            
            // Tiebreak 2: Coin flip
            return Rand.Bool ? TeamColor.Red : TeamColor.Blue;
        }
        
        public List<Fighter> GetWinningTeam()
        {
            var winner = Winner ?? DetermineWinner();
            return winner == TeamColor.Red ? Red : Blue;
        }
        
        public List<Fighter> GetTeam(TeamColor team)
        {
            return team == TeamColor.Red ? Red : Blue;
        }
    }
}