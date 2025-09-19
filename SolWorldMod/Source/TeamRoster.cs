// solworld/SolWorldMod/Source/TeamRoster.cs
using System;
using System.Collections.Generic;
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
            WalletShort = ShortenAddress(walletFull); // Inline method for now
            Team = team;
            Kills = 0;
            Alive = true;
        }
        
        private static string ShortenAddress(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 10)
                return address;
                
            return $"{address.Substring(0, 5)}....{address.Substring(address.Length - 5)}";
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
        
        // Live counters
        public int RedAlive => Red.Where(f => f.Alive).Count();
        public int BlueAlive => Blue.Where(f => f.Alive).Count();
        public int RedKills => Red.Select(f => f.Kills).Sum();
        public int BlueKills => Blue.Select(f => f.Kills).Sum();
        
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
            PreviewTicks = SolWorldSettings.PREVIEW_SECONDS * 60; // Convert to ticks
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