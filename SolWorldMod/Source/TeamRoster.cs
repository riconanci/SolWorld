// solworld/SolWorldMod/Source/TeamRoster.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SolWorldMod
{
    public class Fighter : IExposable
    {
        // Use fields instead of auto-properties to avoid ref/out issues
        private string walletFull;
        private string walletShort;
        private TeamColor team;
        private Pawn pawnRef;
        private int kills;
        private bool alive = true;
        
        public string WalletFull 
        { 
            get { return walletFull; } 
            set { walletFull = value; } 
        }
        
        public string WalletShort 
        { 
            get { return walletShort; } 
            set { walletShort = value; } 
        }
        
        public TeamColor Team 
        { 
            get { return team; } 
            set { team = value; } 
        }
        
        public Pawn PawnRef 
        { 
            get { return pawnRef; } 
            set { pawnRef = value; } 
        }
        
        public int Kills 
        { 
            get { return kills; } 
            set { kills = value; } 
        }
        
        public bool Alive 
        { 
            get { return alive; } 
            set { alive = value; } 
        }
        
        public Fighter()
        {
            // Parameterless constructor for serialization
            walletFull = "";
            walletShort = "";
            team = TeamColor.Red;
            pawnRef = null;
            kills = 0;
            alive = true;
        }
        
        public Fighter(string walletFullAddress, TeamColor fighterTeam)
        {
            walletFull = walletFullAddress;
            walletShort = ShortenAddress(walletFullAddress);
            team = fighterTeam;
            pawnRef = null;
            kills = 0;
            alive = true;
        }
        
        private static string ShortenAddress(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 10)
                return address;
                
            // Use traditional substring - no modern range operators
            return address.Substring(0, 5) + "...." + address.Substring(address.Length - 5);
        }
        
        public void ExposeData()
        {
            Scribe_Values.Look(ref walletFull, "walletFull", "");
            Scribe_Values.Look(ref walletShort, "walletShort", "");
            Scribe_Values.Look(ref team, "team", TeamColor.Red);
            Scribe_References.Look(ref pawnRef, "pawnRef");
            Scribe_Values.Look(ref kills, "kills", 0);
            Scribe_Values.Look(ref alive, "alive", true);
        }
    }

    public class RoundRoster : IExposable
    {
        // Use fields instead of auto-properties
        private string matchId;
        private List<Fighter> red;
        private List<Fighter> blue;
        private int previewTicks;
        private int combatTicks;
        private float roundRewardTotalSol = 1.0f;
        private float payoutPercent = 0.20f;
        private bool isLive = false;
        private TeamColor? winner = null;
        
        // NEW: Loadout information fields
        private string loadoutPresetName = "";
        private string loadoutDescription = "";
        
        public string MatchId 
        { 
            get { return matchId; } 
            set { matchId = value; } 
        }
        
        public List<Fighter> Red 
        { 
            get { return red; } 
            set { red = value; } 
        }
        
        public List<Fighter> Blue 
        { 
            get { return blue; } 
            set { blue = value; } 
        }
        
        public int PreviewTicks 
        { 
            get { return previewTicks; } 
            set { previewTicks = value; } 
        }
        
        public int CombatTicks 
        { 
            get { return combatTicks; } 
            set { combatTicks = value; } 
        }
        
        public float RoundRewardTotalSol 
        { 
            get { return roundRewardTotalSol; } 
            set { roundRewardTotalSol = value; } 
        }
        
        public float PayoutPercent 
        { 
            get { return payoutPercent; } 
            set { payoutPercent = value; } 
        }
        
        public bool IsLive 
        { 
            get { return isLive; } 
            set { isLive = value; } 
        }
        
        public TeamColor? Winner 
        { 
            get { return winner; } 
            set { winner = value; } 
        }
        
        // NEW: Loadout information properties
        public string LoadoutPresetName 
        { 
            get { return loadoutPresetName; } 
            set { loadoutPresetName = value; } 
        }
        
        public string LoadoutDescription 
        { 
            get { return loadoutDescription; } 
            set { loadoutDescription = value; } 
        }
        
        // Live counters - use LINQ (available in RimWorld 1.6)
        public int RedAlive 
        { 
            get { return red?.Count(f => f.Alive) ?? 0; } 
        }
        
        public int BlueAlive 
        { 
            get { return blue?.Count(f => f.Alive) ?? 0; } 
        }
        
        public int RedKills 
        { 
            get { return red?.Sum(f => f.Kills) ?? 0; } 
        }
        
        public int BlueKills 
        { 
            get { return blue?.Sum(f => f.Kills) ?? 0; } 
        }
        
        public float PerWinnerPayout 
        { 
            get { return roundRewardTotalSol * payoutPercent / 10.0f; } 
        }
        
        public RoundRoster()
        {
            matchId = GenerateMatchId();
            red = new List<Fighter>();
            blue = new List<Fighter>();
            previewTicks = SolWorldSettings.PREVIEW_SECONDS * 60;
            combatTicks = SolWorldSettings.COMBAT_SECONDS * 60;
            roundRewardTotalSol = 1.0f;
            payoutPercent = 0.20f;
            isLive = false;
            winner = null;
            loadoutPresetName = "";
            loadoutDescription = "";
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
            var winnerTeam = winner ?? DetermineWinner();
            return winnerTeam == TeamColor.Red ? red : blue;
        }
        
        public List<Fighter> GetTeam(TeamColor teamColor)
        {
            return teamColor == TeamColor.Red ? red : blue;
        }
        
        public void ExposeData()
        {
            Scribe_Values.Look(ref matchId, "matchId", "");
            Scribe_Collections.Look(ref red, "red", LookMode.Deep);
            Scribe_Collections.Look(ref blue, "blue", LookMode.Deep);
            Scribe_Values.Look(ref previewTicks, "previewTicks", SolWorldSettings.PREVIEW_SECONDS * 60);
            Scribe_Values.Look(ref combatTicks, "combatTicks", SolWorldSettings.COMBAT_SECONDS * 60);
            Scribe_Values.Look(ref roundRewardTotalSol, "roundRewardTotalSol", 1.0f);
            Scribe_Values.Look(ref payoutPercent, "payoutPercent", 0.20f);
            Scribe_Values.Look(ref isLive, "isLive", false);
            Scribe_Values.Look(ref winner, "winner");
            
            // NEW: Save/load loadout information
            Scribe_Values.Look(ref loadoutPresetName, "loadoutPresetName", "");
            Scribe_Values.Look(ref loadoutDescription, "loadoutDescription", "");
            
            // Initialize lists if null after loading
            if (red == null) red = new List<Fighter>();
            if (blue == null) blue = new List<Fighter>();
            if (string.IsNullOrEmpty(matchId)) matchId = GenerateMatchId();
        }
    }
}