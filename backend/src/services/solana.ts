// backend/src/services/solana.ts
import { 
  Connection, 
  PublicKey, 
  ParsedAccountData,
  TokenAmount,
  GetProgramAccountsFilter
} from '@solana/web3.js';
import config from '../env';

export interface TokenHolder {
  wallet: string;
  balance: number;
  balanceFormatted: string;
}

export interface TokenStats {
  totalHolders: number;
  eligibleHolders: number;
  totalSupply: number;
  minBalance: number;
}

export class SolanaService {
  private connection: Connection;
  private tokenMint: PublicKey;

  constructor() {
    this.connection = new Connection(config.RPC_ENDPOINT, 'confirmed');
    this.tokenMint = new PublicKey(config.TEST_TOKEN_MINT);
    
    console.log('Solana service initialized:', {
      endpoint: config.RPC_ENDPOINT,
      tokenMint: config.TEST_TOKEN_MINT,
      minBalance: config.MIN_HOLDER_BALANCE
    });
  }

  /**
   * Fetch all token holders with balance >= minimum threshold
   */
  async getTokenHolders(): Promise<TokenHolder[]> {
    try {
      console.log('🔍 Fetching token holders from blockchain...');
      
      // Get all token accounts for this mint
      const filters: GetProgramAccountsFilter[] = [
        {
          dataSize: 165, // Token account data size
        },
        {
          memcmp: {
            offset: 0, // Mint address offset
            bytes: this.tokenMint.toBase58(),
          },
        },
      ];

      const tokenAccounts = await this.connection.getParsedProgramAccounts(
        new PublicKey('TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA'), // SPL Token program
        {
          filters,
          commitment: 'confirmed',
        }
      );

      console.log(`Found ${tokenAccounts.length} token accounts`);

      const holders: TokenHolder[] = [];
      const devWallet = config.DEV_WALLET_ADDRESS;

      for (const account of tokenAccounts) {
        try {
          const accountData = account.account.data as ParsedAccountData;
          const info = accountData.parsed.info;
          
          const owner = info.owner;
          const tokenAmount: TokenAmount = info.tokenAmount;
          const balance = parseFloat(tokenAmount.amount) / Math.pow(10, tokenAmount.decimals);

          // Filter criteria:
          // 1. Must have minimum balance
          // 2. Must not be the dev wallet (exclude from selection)
          // 3. Account must not be frozen
          if (balance >= config.MIN_HOLDER_BALANCE && 
              owner !== devWallet && 
              info.state === 'initialized') {
            
            holders.push({
              wallet: owner,
              balance: balance,
              balanceFormatted: balance.toLocaleString(undefined, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 6
              })
            });
          }
        } catch (error) {
          console.warn('Failed to parse token account:', error);
        }
      }

      // Sort by balance descending
      holders.sort((a, b) => b.balance - a.balance);

      console.log(`✅ Found ${holders.length} eligible holders (min balance: ${config.MIN_HOLDER_BALANCE})`);
      
      if (holders.length > 0) {
        console.log(`Top holder: ${holders[0].wallet} with ${holders[0].balanceFormatted} tokens`);
        console.log(`Bottom holder: ${holders[holders.length - 1].wallet} with ${holders[holders.length - 1].balanceFormatted} tokens`);
      }

      return holders;

    } catch (error) {
      console.error('❌ Failed to fetch token holders:', error);
      
      // Return empty array on error - service will handle fallback
      return [];
    }
  }

  /**
   * Check if connection is healthy
   */
  async checkConnection(): Promise<boolean> {
    try {
      const version = await this.connection.getVersion();
      console.log('Solana connection healthy:', version);
      return true;
    } catch (error) {
      console.error('Solana connection failed:', error);
      return false;
    }
  }

  /**
   * Get current slot for debugging
   */
  async getCurrentSlot(): Promise<number> {
    try {
      return await this.connection.getSlot();
    } catch (error) {
      console.error('Failed to get current slot:', error);
      return 0;
    }
  }

  /**
   * Get token statistics for monitoring
   */
  async getTokenStats(): Promise<TokenStats> {
    try {
      const holders = await this.getTokenHolders();
      
      // Get total supply from mint info
      const mintInfo = await this.connection.getParsedAccountInfo(this.tokenMint);
      let totalSupply = 0;
      
      if (mintInfo.value?.data && 'parsed' in mintInfo.value.data) {
        const mintData = mintInfo.value.data.parsed.info;
        totalSupply = parseFloat(mintData.supply) / Math.pow(10, mintData.decimals);
      }

      return {
        totalHolders: holders.length,
        eligibleHolders: holders.filter(h => h.balance >= config.MIN_HOLDER_BALANCE).length,
        totalSupply,
        minBalance: config.MIN_HOLDER_BALANCE
      };

    } catch (error) {
      console.error('Failed to get token stats:', error);
      return {
        totalHolders: 0,
        eligibleHolders: 0,
        totalSupply: 0,
        minBalance: config.MIN_HOLDER_BALANCE
      };
    }
  }

  /**
   * Generate mock holders for testing when real data is unavailable
   * NOW WITH PROPER TIER DISTRIBUTION!
   */
  generateMockHolders(count: number = 50): TokenHolder[] {
    console.log(`🧪 Generating ${count} TIERED mock token holders for testing...`);
    
    // Define tier ranges based on the tier system
    const tierRanges = [
      { tier: 1, min: 50000, max: 99999, weight: 8 },      // Basic
      { tier: 2, min: 100000, max: 249999, weight: 5 },    // Armored  
      { tier: 3, min: 250000, max: 499999, weight: 3 },    // Elite
      { tier: 4, min: 500000, max: 749999, weight: 2 },    // Specialist
      { tier: 5, min: 750000, max: 999999, weight: 1 },    // Champion
      { tier: 6, min: 1000000, max: 1499999, weight: 1 },  // Warlord
      { tier: 7, min: 1500000, max: 5000000, weight: 1 }   // God
    ];

    // Calculate total weight for distribution
    const totalWeight = tierRanges.reduce((sum, tier) => sum + tier.weight, 0);
    
    const mockHolders: TokenHolder[] = [];
    let holdersCreated = 0;

    // Create holders for each tier based on weight distribution
    for (const tierRange of tierRanges) {
      // Calculate how many holders this tier should get
      const tierCount = Math.floor((tierRange.weight / totalWeight) * count);
      
      console.log(`🎯 Creating ${tierCount} Tier ${tierRange.tier} holders (${tierRange.min.toLocaleString()}-${tierRange.max.toLocaleString()} tokens)`);

      for (let i = 0; i < tierCount && holdersCreated < count; i++) {
        // Generate realistic Solana addresses (44 characters, base58)
        const wallet = this.generateMockAddress();
        
        // Generate balance within tier range
        const balance = Math.floor(
          Math.random() * (tierRange.max - tierRange.min) + tierRange.min
        );
        
        mockHolders.push({
          wallet,
          balance,
          balanceFormatted: balance.toLocaleString(undefined, {
            minimumFractionDigits: 2,
            maximumFractionDigits: 6
          })
        });
        
        holdersCreated++;
      }
    }

    // Fill any remaining spots with Tier 1 fighters
    while (mockHolders.length < count) {
      const wallet = this.generateMockAddress();
      const balance = Math.floor(Math.random() * 49999 + 50000); // Tier 1 range
      
      mockHolders.push({
        wallet,
        balance,
        balanceFormatted: balance.toLocaleString(undefined, {
          minimumFractionDigits: 2,
          maximumFractionDigits: 6
        })
      });
    }

    // Sort by balance descending (highest tiers first)
    mockHolders.sort((a, b) => b.balance - a.balance);
    
    // Log the tier distribution we created
    console.log(`✅ Generated ${mockHolders.length} tiered mock holders:`);
    const tierCounts: { [key: number]: number } = {};
    
    mockHolders.forEach(holder => {
      const tier = this.getTierFromBalance(holder.balance);
      tierCounts[tier] = (tierCounts[tier] || 0) + 1;
    });
    
    Object.entries(tierCounts).forEach(([tier, count]) => {
      console.log(`   Tier ${tier}: ${count} holders`);
    });
    
    console.log(`   Highest: ${mockHolders[0].balanceFormatted} tokens (Tier ${this.getTierFromBalance(mockHolders[0].balance)})`);
    console.log(`   Lowest: ${mockHolders[mockHolders.length - 1].balanceFormatted} tokens (Tier ${this.getTierFromBalance(mockHolders[mockHolders.length - 1].balance)})`);
    
    return mockHolders;
  }

  /**
   * Helper method to determine tier from balance
   */
  private getTierFromBalance(balance: number): number {
    if (balance >= 1500000) return 7; // God
    if (balance >= 1000000) return 6; // Warlord
    if (balance >= 750000) return 5;  // Champion
    if (balance >= 500000) return 4;  // Specialist
    if (balance >= 250000) return 3;  // Elite
    if (balance >= 100000) return 2;  // Armored
    return 1; // Basic
  }

  /**
   * Generate a realistic mock Solana address
   */
  private generateMockAddress(): string {
    const chars = 'ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz123456789';
    let result = '';
    
    // Generate 44 character base58 string (standard Solana address length)
    for (let i = 0; i < 44; i++) {
      result += chars[Math.floor(Math.random() * chars.length)];
    }
    
    return result;
  }
}

export default SolanaService;