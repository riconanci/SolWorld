// backend/src/services/holders.ts
import SolanaService, { TokenHolder } from './solana';
import config from '../env';

export interface HoldersResponse {
  wallets: string[];
  roundRewardTotalSol: number;
  payoutPercent: number;
  source: 'blockchain' | 'mock' | 'mixed';
  stats: {
    totalAvailable: number;
    selected: number;
    mockUsed: number;
  };
}

export class HoldersService {
  private solanaService: SolanaService;
  private lastFetchTime: number = 0;
  private cachedHolders: TokenHolder[] = [];
  private cacheExpiry: number = 5 * 60 * 1000; // 5 minutes cache
  private refreshTimer: NodeJS.Timeout | null = null;
  private isRefreshing: boolean = false;

  constructor() {
    this.solanaService = new SolanaService();
    
    // Start background refresh timer immediately (Option A)
    this.startBackgroundRefresh();
    
    console.log('Holders service initialized');
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
   * Get 20 random token holders for arena round
   */
  async getRandomHolders(): Promise<HoldersResponse> {
    try {
      console.log('🎯 Selecting 20 random holders for arena round...');

      // Get fresh or cached holders (now always from cache due to background refresh)
      const allHolders = await this.getAvailableHolders();
      
      // Select 20 unique holders
      const selected = this.selectRandomWallets(allHolders, 20);
      
      // Build response
      const response: HoldersResponse = {
        wallets: selected.wallets,
        roundRewardTotalSol: config.roundPoolSol,
        payoutPercent: config.PAYOUT_SPLIT_PERCENT,
        source: selected.source,
        stats: {
          totalAvailable: allHolders.length,
          selected: selected.wallets.length,
          mockUsed: selected.mockUsed
        }
      };

      console.log(`✅ Selected ${response.stats.selected} holders (${response.source})`);
      console.log(`   Pool: ${response.roundRewardTotalSol} SOL | Payout: ${response.payoutPercent * 100}%`);
      
      return response;

    } catch (error) {
      console.error('❌ Failed to get random holders:', error);
      
      // Emergency fallback: all mock data
      const mockHolders = this.solanaService.generateMockHolders(30);
      const wallets = mockHolders.slice(0, 20).map(h => h.wallet);
      
      return {
        wallets,
        roundRewardTotalSol: config.roundPoolSol,
        payoutPercent: config.PAYOUT_SPLIT_PERCENT,
        source: 'mock',
        stats: {
          totalAvailable: mockHolders.length,
          selected: wallets.length,
          mockUsed: wallets.length
        }
      };
    }
  }

  /**
   * Get available holders with caching (now primarily serves cached data)
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
   * Select random wallets from available holders
   */
  private selectRandomWallets(allHolders: TokenHolder[], count: number): { 
    wallets: string[];
    source: 'blockchain' | 'mock' | 'mixed';
    mockUsed: number;
  } {
    if (allHolders.length === 0) {
      return { wallets: [], source: 'mock', mockUsed: 0 };
    }

    // Shuffle and select
    const shuffled = [...allHolders].sort(() => Math.random() - 0.5);
    const selected = shuffled.slice(0, Math.min(count, shuffled.length));
    
    // Determine source type
    const realCount = selected.filter(h => !this.isMockAddress(h.wallet)).length;
    const mockCount = selected.length - realCount;
    
    let source: 'blockchain' | 'mock' | 'mixed' = 'blockchain';
    if (mockCount === selected.length) source = 'mock';
    else if (mockCount > 0) source = 'mixed';
    
    return {
      wallets: selected.map(h => h.wallet),
      source,
      mockUsed: mockCount
    };
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
    console.log(`🧪 Testing holder selection for ${rounds} rounds...`);
    
    const results = [];
    
    for (let i = 0; i < rounds; i++) {
      const response = await this.getRandomHolders();
      
      results.push({
        round: i + 1,
        walletsSelected: response.wallets.length,
        source: response.source,
        stats: response.stats,
        sampleWallet: response.wallets[0]?.slice(0, 8) + '...' || 'none'
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
   * Cleanup method for graceful shutdown
   */
  cleanup(): void {
    this.stopBackgroundRefresh();
    this.cachedHolders = [];
    console.log('🧹 Holders service cleaned up');
  }
}

export default HoldersService;