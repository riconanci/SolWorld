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

  constructor() {
    this.solanaService = new SolanaService();
    console.log('Holders service initialized');
  }

  /**
   * Get 20 random token holders for arena round
   */
  async getRandomHolders(): Promise<HoldersResponse> {
    try {
      console.log('🎯 Selecting 20 random holders for arena round...');

      // Get fresh or cached holders
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
   * Get available holders with caching
   */
  private async getAvailableHolders(): Promise<TokenHolder[]> {
    const now = Date.now();
    
    // Use cache if valid
    if (this.cachedHolders.length > 0 && (now - this.lastFetchTime) < this.cacheExpiry) {
      console.log(`📋 Using cached holders (${this.cachedHolders.length} available)`);
      return this.cachedHolders;
    }

    // Fetch fresh data
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
        // No real holders, use all mock
        const mockHolders = this.solanaService.generateMockHolders(50);
        this.cachedHolders = mockHolders;
        this.lastFetchTime = now;
        
        console.log(`🧪 Using ${mockHolders.length} mock holders (no real data)`);
        return mockHolders;
      }
    } catch (error) {
      console.error('Failed to fetch holders, using mock data:', error);
      
      // Fallback to mock on any error
      const mockHolders = this.solanaService.generateMockHolders(50);
      this.cachedHolders = mockHolders;
      this.lastFetchTime = now;
      
      return mockHolders;
    }
  }

  /**
   * Randomly select wallets from available holders
   */
  private selectRandomWallets(holders: TokenHolder[], count: number): {
    wallets: string[];
    source: 'blockchain' | 'mock' | 'mixed';
    mockUsed: number;
  } {
    if (holders.length === 0) {
      return { wallets: [], source: 'mock', mockUsed: 0 };
    }

    // Shuffle holders array
    const shuffled = [...holders].sort(() => Math.random() - 0.5);
    
    // Take the first 'count' items
    const selected = shuffled.slice(0, Math.min(count, shuffled.length));
    const wallets = selected.map(h => h.wallet);
    
    // Determine source type
    let source: 'blockchain' | 'mock' | 'mixed' = 'blockchain';
    let mockUsed = 0;
    
    // Count mock holders (they have generated addresses that are predictable)
    for (const holder of selected) {
      if (this.isMockAddress(holder.wallet)) {
        mockUsed++;
      }
    }
    
    if (mockUsed === selected.length) {
      source = 'mock';
    } else if (mockUsed > 0) {
      source = 'mixed';
    }

    // Ensure uniqueness (shouldn't be needed but safety check)
    const uniqueWallets = [...new Set(wallets)];
    
    console.log(`🎲 Selected ${uniqueWallets.length} unique wallets from ${holders.length} available`);
    if (mockUsed > 0) {
      console.log(`   (${mockUsed} mock addresses used)`);
    }
    
    return {
      wallets: uniqueWallets,
      source,
      mockUsed
    };
  }

  /**
   * Check if address appears to be a mock address
   * Mock addresses have a predictable pattern from generateMockAddress
   */
  private isMockAddress(address: string): boolean {
    // This is a heuristic - mock addresses are randomly generated
    // In production, you might track which addresses are mock differently
    return !this.solanaService.isValidSolanaAddress(address) || 
           address.length !== 44;
  }

  /**
   * Get holder statistics for monitoring
   */
  async getHolderStats() {
    try {
      const tokenStats = await this.solanaService.getTokenStats();
      const allHolders = await this.getAvailableHolders();
      
      const realHolders = allHolders.filter(h => !this.isMockAddress(h.wallet));
      const mockHolders = allHolders.filter(h => this.isMockAddress(h.wallet));
      
      return {
        blockchain: {
          totalHolders: tokenStats.totalHolders,
          eligibleHolders: tokenStats.eligibleHolders,
          totalSupply: tokenStats.totalSupply,
          minBalance: tokenStats.minBalance
        },
        cached: {
          total: allHolders.length,
          real: realHolders.length,
          mock: mockHolders.length,
          lastUpdate: new Date(this.lastFetchTime).toISOString(),
          cacheAge: Date.now() - this.lastFetchTime
        },
        config: {
          minBalance: config.MIN_HOLDER_BALANCE,
          roundPool: config.roundPoolSol,
          payoutPercent: config.PAYOUT_SPLIT_PERCENT
        }
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
}

export default HoldersService;