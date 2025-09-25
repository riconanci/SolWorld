// backend/src/services/holders.ts - COMPLETE VERSION WITH ALL FEATURES
import { SolanaService, TokenHolder } from './solana';
import config from '../env';

export interface TierBenefits {
  tier: number;
  name: string;
  minBalance: number;
  weaponQuality: 'Normal' | 'Good' | 'Excellent' | 'Masterwork';
  hasArmor: boolean;
  hasHelmet: boolean;
  hasAura: boolean;
  description: string;
  // Additional UI/combat modifiers
  damageMultiplier: number;
  accuracyBonus: number;
  healthMultiplier: number;
  glowColor?: string;
  tierIcon: string;
  borderColor: string;
}

export interface TieredFighter {
  wallet: string;
  balance: number;
  tier: TierBenefits;
}

export interface HoldersResponse {
  // Support both formats for backward compatibility
  wallets: string[];              // Original format for existing RimWorld mod
  fighters: TieredFighter[];      // New tiered format 
  roundRewardTotalSol: number;
  payoutPercent: number;
  source: 'blockchain' | 'mock' | 'mixed'; // Keep original naming
  stats: {
    totalAvailable: number;
    selected: number;
    mockUsed: number;
    tierDistribution: { [key: string]: number };
  };
}

class HoldersService {
  private solanaService: SolanaService;
  private cachedHolders: TokenHolder[] = [];
  private lastFetchTime: number = 0;
  private cacheExpiry: number = 5 * 60 * 1000; // 5 minutes
  private refreshTimer: NodeJS.Timeout | null = null;
  private isRefreshing: boolean = false;

  // TIER SYSTEM CONFIGURATION
  private static readonly TIER_BENEFITS: TierBenefits[] = [
    {
      tier: 1, name: "Basic Fighter", minBalance: 50000,
      weaponQuality: 'Normal', hasArmor: false, hasHelmet: false, hasAura: false,
      description: "Standard equipment",
      damageMultiplier: 1.0, accuracyBonus: 0, healthMultiplier: 1.0,
      tierIcon: "⚔️", borderColor: "#888888"
    },
    {
      tier: 2, name: "Armored Veteran", minBalance: 100000,
      weaponQuality: 'Normal', hasArmor: true, hasHelmet: false, hasAura: false,
      description: "Flak armor protection",
      damageMultiplier: 1.0, accuracyBonus: 0.05, healthMultiplier: 1.1,
      tierIcon: "🛡️", borderColor: "#4CAF50"
    },
    {
      tier: 3, name: "Elite Warrior", minBalance: 150000,
      weaponQuality: 'Good', hasArmor: true, hasHelmet: false, hasAura: false,
      description: "Good quality weapons + armor",
      damageMultiplier: 1.15, accuracyBonus: 0.1, healthMultiplier: 1.1,
      tierIcon: "🗡️", borderColor: "#2196F3"
    },
    {
      tier: 4, name: "Combat Specialist", minBalance: 250000,
      weaponQuality: 'Good', hasArmor: true, hasHelmet: true, hasAura: false,
      description: "Helmet + armor + good weapons",
      damageMultiplier: 1.15, accuracyBonus: 0.15, healthMultiplier: 1.2,
      tierIcon: "👑", borderColor: "#9C27B0"
    },
    {
      tier: 5, name: "Legendary Champion", minBalance: 500000,
      weaponQuality: 'Excellent', hasArmor: true, hasHelmet: true, hasAura: false,
      description: "Excellent weapons + full protection",
      damageMultiplier: 1.3, accuracyBonus: 0.2, healthMultiplier: 1.3,
      tierIcon: "⭐", borderColor: "#FF9800"
    },
    {
      tier: 6, name: "Mythical Warlord", minBalance: 1000000,
      weaponQuality: 'Excellent', hasArmor: true, hasHelmet: true, hasAura: true,
      description: "Glowing aura + elite equipment",
      damageMultiplier: 1.3, accuracyBonus: 0.25, healthMultiplier: 1.4,
      glowColor: "cyan", tierIcon: "💎", borderColor: "#E91E63"
    },
    {
      tier: 7, name: "Godlike Destroyer", minBalance: 1500000,
      weaponQuality: 'Masterwork', hasArmor: true, hasHelmet: true, hasAura: true,
      description: "Masterwork weapons + divine aura",
      damageMultiplier: 1.5, accuracyBonus: 0.3, healthMultiplier: 1.5,
      glowColor: "yellow", tierIcon: "🔱", borderColor: "#FFD700"
    }
  ];

  constructor() {
    this.solanaService = new SolanaService();
    this.startBackgroundRefresh();
    
    console.log('🏆 HoldersService initialized with tier system:');
    HoldersService.TIER_BENEFITS.forEach(tier => {
      console.log(`   Tier ${tier.tier}: ${tier.name} (${tier.minBalance.toLocaleString()}+ tokens)`);
    });
  }

  /**
   * Determine tier benefits based on token balance
   */
  private static getTierForBalance(balance: number): TierBenefits {
    // Find highest tier the balance qualifies for
    for (let i = HoldersService.TIER_BENEFITS.length - 1; i >= 0; i--) {
      if (balance >= HoldersService.TIER_BENEFITS[i].minBalance) {
        return HoldersService.TIER_BENEFITS[i];
      }
    }
    
    // Fallback to tier 1
    return HoldersService.TIER_BENEFITS[0];
  }

  /**
   * Convert TokenHolder to TieredFighter with tier benefits
   */
  private createTieredFighter(holder: TokenHolder): TieredFighter {
    const tier = HoldersService.getTierForBalance(holder.balance);
    
    return {
      wallet: holder.wallet,
      balance: holder.balance,
      tier: tier
    };
  }

  /**
   * Start 5-minute background refresh timer
   */
  private startBackgroundRefresh(): void {
    console.log('⏰ Starting 5-minute background holder refresh...');
    
    // Initial fetch (don't wait 5 minutes for first data)
    this.backgroundRefresh();
    
    // Set up recurring 5-minute refresh
    this.refreshTimer = setInterval(() => {
      this.backgroundRefresh();
    }, 5 * 60 * 1000); // 5 minutes
    
    console.log('✅ Background refresh timer active (every 5 minutes)');
  }

  /**
   * Background refresh function
   */
  private async backgroundRefresh(): Promise<void> {
    if (this.isRefreshing) {
      console.log('⏭️ Background refresh already in progress, skipping...');
      return;
    }

    try {
      this.isRefreshing = true;
      console.log('🔄 Background refresh: Fetching latest holder data...');
      
      const previousCount = this.cachedHolders.length;
      const previousRealCount = this.cachedHolders.filter(h => !this.isMockAddress(h.wallet)).length;
      
      // Force refresh by invalidating cache
      this.lastFetchTime = 0;
      
      // Fetch fresh data
      const freshHolders = await this.getAvailableHolders();
      const newRealCount = freshHolders.filter(h => !this.isMockAddress(h.wallet)).length;
      
      // Log changes
      if (newRealCount !== previousRealCount || freshHolders.length !== previousCount) {
        console.log('📊 Holder changes detected:');
        console.log(`   Real holders: ${previousRealCount} → ${newRealCount}`);
        console.log(`   Total cached: ${previousCount} → ${freshHolders.length}`);
        
        if (newRealCount > previousRealCount) {
          console.log('🆕 New token holders can now participate!');
        } else if (newRealCount < previousRealCount) {
          console.log('📉 Some holders dropped below minimum threshold');
        }
        
        // Log tier distribution changes
        const tieredFighters = freshHolders.map(h => this.createTieredFighter(h));
        const tierCounts: { [key: number]: number } = {};
        tieredFighters.forEach(f => {
          tierCounts[f.tier.tier] = (tierCounts[f.tier.tier] || 0) + 1;
        });
        
        console.log('🏆 Current tier distribution:');
        Object.entries(tierCounts).forEach(([tier, count]) => {
          const tierBenefits = HoldersService.TIER_BENEFITS[parseInt(tier) - 1];
          console.log(`   Tier ${tier}: ${count}x ${tierBenefits?.name || 'Unknown'}`);
        });
      } else {
        console.log('✅ Background refresh complete (no changes)');
      }
      
    } catch (error) {
      console.error('❌ Background refresh failed:', error);
    } finally {
      this.isRefreshing = false;
    }
  }

  /**
   * Stop background refresh (for cleanup)
   */
  stopBackgroundRefresh(): void {
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
      this.refreshTimer = null;
      console.log('⏹️ Background refresh timer stopped');
    }
  }

  /**
   * Get 20 random token holders with tier information
   */
  async getRandomHolders(): Promise<HoldersResponse> {
    try {
      console.log('🎯 Selecting 20 random tiered fighters for arena round...');

      // Get fresh or cached holders
      const allHolders = await this.getAvailableHolders();
      
      // Convert to tiered fighters
      const tieredFighters = allHolders.map(holder => this.createTieredFighter(holder));
      
      // Select 20 unique fighters
      const selected = this.selectRandomFighters(tieredFighters, 20);
      
      // Calculate tier distribution
      const tierDistribution: { [key: string]: number } = {};
      selected.forEach(fighter => {
        const tierName = `T${fighter.tier.tier} ${fighter.tier.name}`;
        tierDistribution[tierName] = (tierDistribution[tierName] || 0) + 1;
      });
      
      // Determine source classification
      const realCount = selected.filter(f => !this.isMockAddress(f.wallet)).length;
      const mockCount = selected.length - realCount;
      
      let source: 'blockchain' | 'mock' | 'mixed' = 'blockchain';
      if (mockCount === selected.length) source = 'mock';
      else if (mockCount > 0) source = 'mixed';
      
      // Build response with both formats for compatibility
      const response: HoldersResponse = {
        wallets: selected.map(f => f.wallet),        // Original format
        fighters: selected,                          // New tiered format
        roundRewardTotalSol: config.roundPoolSol,
        payoutPercent: config.PAYOUT_SPLIT_PERCENT,
        source: source,
        stats: {
          totalAvailable: allHolders.length,
          selected: selected.length,
          mockUsed: mockCount,
          tierDistribution: tierDistribution
        }
      };

      // Log tier distribution
      console.log(`✅ Selected 20 tiered fighters:`);
      Object.entries(tierDistribution).forEach(([tierName, count]) => {
        console.log(`   ${count}x ${tierName}`);
      });
      
      console.log(`   Pool: ${response.roundRewardTotalSol} SOL | Payout: ${response.payoutPercent * 100}%`);
      
      return response;

    } catch (error) {
      console.error('❌ Failed to get random tiered holders:', error);
      
      // Emergency fallback: generate mock tiered fighters
      const mockFighters = this.generateMockTieredFighters(20);
      
      const tierDistribution: { [key: string]: number } = {};
      mockFighters.forEach(fighter => {
        const tierName = `T${fighter.tier.tier} ${fighter.tier.name}`;
        tierDistribution[tierName] = (tierDistribution[tierName] || 0) + 1;
      });
      
      return {
        wallets: mockFighters.map(f => f.wallet),
        fighters: mockFighters,
        roundRewardTotalSol: config.roundPoolSol,
        payoutPercent: config.PAYOUT_SPLIT_PERCENT,
        source: 'mock',
        stats: {
          totalAvailable: mockFighters.length,
          selected: mockFighters.length,
          mockUsed: mockFighters.length,
          tierDistribution: tierDistribution
        }
      };
    }
  }

  /**
   * Get available holders with caching (restored original logic)
   */
  private async getAvailableHolders(): Promise<TokenHolder[]> {
    const now = Date.now();
    
    // Use cache if valid (background refresh keeps this fresh)
    if (this.cachedHolders.length > 0 && (now - this.lastFetchTime) < this.cacheExpiry) {
      console.log(`📋 Using cached holders (${this.cachedHolders.length} available)`);
      return this.cachedHolders;
    }

    // Fetch fresh data (mainly for initial startup or cache miss)
    console.log('🔄 Fetching fresh holder data...');
    
    try {
      const realHolders = await this.solanaService.getTokenHolders();
      
      if (realHolders.length >= 20) {
        // Sufficient real holders
        this.cachedHolders = realHolders;
        this.lastFetchTime = now;
        console.log(`✅ Cached ${realHolders.length} real holders`);
        return realHolders;
      } else if (realHolders.length > 0) {
        // Some real holders, supplement with mock
        const needed = 20 - realHolders.length;
        const mockHolders = this.solanaService.generateMockHolders(needed + 10);
        
        this.cachedHolders = [...realHolders, ...mockHolders];
        this.lastFetchTime = now;
        
        console.log(`📊 Mixed data: ${realHolders.length} real + ${mockHolders.length} mock holders`);
        return this.cachedHolders;
      } else {
        // No real holders found, use all mock
        const mockHolders = this.solanaService.generateMockHolders(30);
        
        this.cachedHolders = mockHolders;
        this.lastFetchTime = now;
        
        console.log(`🧪 Using ${mockHolders.length} mock holders (no real holders found)`);
        return mockHolders;
      }
    } catch (error) {
      console.error('❌ Failed to fetch holders, using cached or mock data');
      
      // Return cached data if available, otherwise generate mock
      if (this.cachedHolders.length > 0) {
        console.log(`📋 Returning ${this.cachedHolders.length} cached holders due to fetch error`);
        return this.cachedHolders;
      }
      
      const mockHolders = this.solanaService.generateMockHolders(30);
      this.cachedHolders = mockHolders;
      this.lastFetchTime = now;
      
      console.log(`🧪 Generated ${mockHolders.length} emergency mock holders`);
      return mockHolders;
    }
  }

  /**
   * Select random fighters from available pool
   */
  private selectRandomFighters(fighters: TieredFighter[], count: number): TieredFighter[] {
    if (fighters.length === 0) {
      return [];
    }
    
    // Shuffle and select
    const shuffled = [...fighters].sort(() => Math.random() - 0.5);
    return shuffled.slice(0, Math.min(count, shuffled.length));
  }

  /**
   * Generate mock tiered fighters for testing
   */
  private generateMockTieredFighters(count: number): TieredFighter[] {
    const mockFighters: TieredFighter[] = [];
    
    // Create diverse tier distribution for testing
    const tierDistribution = [
      { tier: 1, count: 8 },  // 8 basic fighters
      { tier: 2, count: 4 },  // 4 armored
      { tier: 3, count: 3 },  // 3 elite
      { tier: 4, count: 2 },  // 2 specialists
      { tier: 5, count: 2 },  // 2 champions
      { tier: 6, count: 1 },  // 1 warlord
      { tier: 7, count: 0 }   // 0 gods (rare)
    ];
    
    let fighterIndex = 0;
    
    for (const { tier, count: tierCount } of tierDistribution) {
      const tierBenefits = HoldersService.TIER_BENEFITS[tier - 1];
      
      for (let i = 0; i < tierCount && fighterIndex < count; i++) {
        // Generate realistic balance for tier
        const minBalance = tierBenefits.minBalance;
        const maxBalance = tier < 7 ? HoldersService.TIER_BENEFITS[tier].minBalance - 1 : minBalance * 3;
        const balance = Math.floor(Math.random() * (maxBalance - minBalance) + minBalance);
        
        mockFighters.push({
          wallet: this.generateMockWallet(`MOCK${fighterIndex + 1}`),
          balance: balance,
          tier: tierBenefits
        });
        
        fighterIndex++;
      }
    }
    
    // Fill remaining slots with tier 1 fighters
    while (mockFighters.length < count) {
      mockFighters.push({
        wallet: this.generateMockWallet(`MOCK${mockFighters.length + 1}`),
        balance: Math.floor(Math.random() * 50000 + 50000),
        tier: HoldersService.TIER_BENEFITS[0]
      });
    }
    
    return mockFighters;
  }

  /**
   * Generate mock Solana wallet address
   */
  private generateMockWallet(seed: string): string {
    const chars = 'ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz123456789';
    let wallet = seed;
    
    while (wallet.length < 44) {
      wallet += chars[Math.floor(Math.random() * chars.length)];
    }
    
    return wallet.substring(0, 44);
  }

  /**
   * Check if an address is a mock address
   */
  private isMockAddress(address: string): boolean {
    // Mock addresses start with specific prefixes or have recognizable patterns
    return address.startsWith('MOCK') || 
           address.includes('mock') || 
           address.includes('test') ||
           address.length !== 44; // Real Solana addresses are 44 chars
  }

  /**
   * Get holder statistics (including background refresh info)
   */
  async getHolderStats(): Promise<any> {
    try {
      const tokenStats = await this.solanaService.getTokenStats();
      const cacheAge = this.lastFetchTime > 0 ? Date.now() - this.lastFetchTime : 0;
      const realHoldersInCache = this.cachedHolders.filter(h => !this.isMockAddress(h.wallet)).length;
      const mockHoldersInCache = this.cachedHolders.length - realHoldersInCache;
      
      return {
        blockchain: {
          totalHolders: tokenStats.totalHolders,
          eligibleHolders: tokenStats.eligibleHolders,
          minBalance: tokenStats.minBalance
        },
        cache: {
          totalCached: this.cachedHolders.length,
          realCached: realHoldersInCache,
          mockCached: mockHoldersInCache,
          ageMinutes: Math.floor(cacheAge / 1000 / 60),
          isStale: cacheAge > this.cacheExpiry
        },
        backgroundRefresh: {
          active: this.refreshTimer !== null,
          intervalMinutes: 5,
          isRefreshing: this.isRefreshing,
          nextRefreshMinutes: this.refreshTimer ? Math.floor((this.cacheExpiry - cacheAge) / 1000 / 60) : 0
        },
        tiers: this.getTierStatistics(),
        lastUpdate: this.lastFetchTime > 0 ? new Date(this.lastFetchTime).toISOString() : 'never'
      };
    } catch (error) {
      console.error('Failed to get holder stats:', error);
      return null;
    }
  }

  /**
   * Force refresh the holder cache
   */
  async refreshCache(): Promise<{ success: boolean; count: number; source: string }> {
    try {
      console.log('🔄 Force refreshing holder cache...');
      
      this.lastFetchTime = 0; // Invalidate cache
      const holders = await this.getAvailableHolders();
      
      const realCount = holders.filter(h => !this.isMockAddress(h.wallet)).length;
      const mockCount = holders.length - realCount;
      
      let source = 'blockchain';
      if (mockCount === holders.length) source = 'mock';
      else if (mockCount > 0) source = 'mixed';
      
      return {
        success: true,
        count: holders.length,
        source: `${realCount} real + ${mockCount} mock`
      };
      
    } catch (error) {
      console.error('Failed to refresh cache:', error);
      return {
        success: false,
        count: 0,
        source: 'error'
      };
    }
  }

  /**
   * Test holder selection (for debugging)
   */
  async testSelection(rounds: number = 3): Promise<any> {
    console.log(`🧪 Testing tiered holder selection for ${rounds} rounds...`);
    
    const results = [];
    
    for (let i = 0; i < rounds; i++) {
      const response = await this.getRandomHolders();
      
      results.push({
        round: i + 1,
        walletsSelected: response.wallets.length,
        fightersSelected: response.fighters.length,
        source: response.source,
        stats: response.stats,
        tierDistribution: response.stats.tierDistribution,
        sampleWallet: response.wallets[0]?.slice(0, 8) + '...' || 'none',
        sampleFighter: response.fighters[0] ? {
          tier: response.fighters[0].tier.tier,
          name: response.fighters[0].tier.name,
          balance: response.fighters[0].balance
        } : null
      });
      
      // Small delay between tests
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    
    console.log('Test results:', results);
    return results;
  }

  /**
   * Get background refresh status
   */
  getRefreshStatus(): {
    active: boolean;
    isRefreshing: boolean;
    cacheAge: number;
    nextRefresh: number;
    cachedHolders: number;
  } {
    const cacheAge = this.lastFetchTime > 0 ? Date.now() - this.lastFetchTime : 0;
    const nextRefresh = Math.max(0, this.cacheExpiry - cacheAge);
    
    return {
      active: this.refreshTimer !== null,
      isRefreshing: this.isRefreshing,
      cacheAge: Math.floor(cacheAge / 1000), // seconds
      nextRefresh: Math.floor(nextRefresh / 1000), // seconds
      cachedHolders: this.cachedHolders.length
    };
  }

  /**
   * Get tier statistics for monitoring
   */
  getTierStatistics(): { [key: string]: any } {
    const tieredFighters = this.cachedHolders.map(h => this.createTieredFighter(h));
    
    const tierCounts: { [key: number]: number } = {};
    const tierBalances: { [key: number]: number } = {};
    
    tieredFighters.forEach(f => {
      tierCounts[f.tier.tier] = (tierCounts[f.tier.tier] || 0) + 1;
      tierBalances[f.tier.tier] = (tierBalances[f.tier.tier] || 0) + f.balance;
    });
    
    const stats: { [key: string]: any } = {
      totalHolders: tieredFighters.length,
      tierBreakdown: {}
    };
    
    HoldersService.TIER_BENEFITS.forEach(tier => {
      const count = tierCounts[tier.tier] || 0;
      const totalBalance = tierBalances[tier.tier] || 0;
      const avgBalance = count > 0 ? totalBalance / count : 0;
      
      stats.tierBreakdown[`tier${tier.tier}`] = {
        name: tier.name,
        minBalance: tier.minBalance,
        holders: count,
        averageBalance: Math.round(avgBalance),
        benefits: {
          weaponQuality: tier.weaponQuality,
          armor: tier.hasArmor,
          helmet: tier.hasHelmet,
          aura: tier.hasAura,
          damageMultiplier: tier.damageMultiplier,
          accuracyBonus: tier.accuracyBonus,
          healthMultiplier: tier.healthMultiplier
        },
        ui: {
          icon: tier.tierIcon,
          borderColor: tier.borderColor,
          glowColor: tier.glowColor
        }
      };
    });
    
    return stats;
  }

  /**
   * Cleanup method for graceful shutdown
   */
  cleanup(): void {
    this.stopBackgroundRefresh();
    this.cachedHolders = [];
    console.log('🧹 Tiered holders service cleaned up');
  }
}

export default HoldersService;