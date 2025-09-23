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
   */
  generateMockHolders(count: number = 50): TokenHolder[] {
    console.log(`🧪 Generating ${count} mock token holders for testing...`);
    
    const mockHolders: TokenHolder[] = [];
    
    for (let i = 0; i < count; i++) {
      // Generate realistic Solana addresses (44 characters, base58)
      const wallet = this.generateMockAddress();
      const balance = Math.random() * 1000 + config.MIN_HOLDER_BALANCE;
      
      mockHolders.push({
        wallet,
        balance,
        balanceFormatted: balance.toLocaleString(undefined, {
          minimumFractionDigits: 2,
          maximumFractionDigits: 6
        })
      });
    }

    // Sort by balance descending
    mockHolders.sort((a, b) => b.balance - a.balance);
    
    console.log(`✅ Generated ${mockHolders.length} mock holders`);
    return mockHolders;
  }

  /**
   * Generate a realistic mock Solana address
   */
  private generateMockAddress(): string {
    // Base58 alphabet
    const base58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    let address = '';
    
    // Solana addresses are typically 44 characters
    for (let i = 0; i < 44; i++) {
      address += base58[Math.floor(Math.random() * base58.length)];
    }
    
    return address;
  }

  /**
   * Validate if a string is a valid Solana address
   */
  isValidSolanaAddress(address: string): boolean {
    try {
      new PublicKey(address);
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Get connection for other services to use
   */
  getConnection(): Connection {
    return this.connection;
  }

  /**
   * Get token mint address
   */
  getTokenMint(): PublicKey {
    return this.tokenMint;
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
}

export default SolanaService;