// backend/src/services/treasury.ts
import { 
  Keypair, 
  Connection, 
  Transaction, 
  SystemProgram, 
  sendAndConfirmTransaction,
  PublicKey,
  LAMPORTS_PER_SOL
} from '@solana/web3.js';
import bs58 from 'bs58';
import config from '../env.js';

export interface SplitResult {
  success: boolean;
  devTransferSignature?: string;
  treasuryBalance?: number;
  error?: string;
}

export interface PayoutResult {
  success: boolean;
  totalPaid: number;
  perWinner: number;
  signatures: string[];
  failedPayouts: string[];
  error?: string;
}

export class TreasuryService {
  private connection: Connection;
  private treasuryKeypair: Keypair;
  private devWallet: PublicKey;

  constructor() {
    this.connection = new Connection(config.RPC_ENDPOINT, 'confirmed');
    
    try {
      // Initialize treasury keypair
      const treasurySecretKey = bs58.decode(config.TREASURY_WALLET_PRIVATE_KEY);
      this.treasuryKeypair = Keypair.fromSecretKey(treasurySecretKey);
      
      // Initialize dev wallet public key
      this.devWallet = new PublicKey(config.DEV_WALLET_ADDRESS);
      
      console.log('Treasury service initialized:');
      console.log('  Treasury:', this.treasuryKeypair.publicKey.toBase58());
      console.log('  Dev wallet:', this.devWallet.toBase58());
      
    } catch (error) {
      console.error('Failed to initialize treasury service:', error);
      throw new Error('Invalid wallet configuration');
    }
  }

  /**
   * Split claimed rewards: 80% to dev wallet, 20% to treasury
   */
  async splitRewards(claimedSol: number): Promise<SplitResult> {
    try {
      console.log(`💰 Splitting ${claimedSol.toFixed(6)} SOL rewards...`);

      // Calculate split amounts
      const devAmount = claimedSol * (1 - config.PAYOUT_SPLIT_PERCENT);
      const treasuryAmount = claimedSol * config.PAYOUT_SPLIT_PERCENT;

      console.log(`  Dev (${((1 - config.PAYOUT_SPLIT_PERCENT) * 100).toFixed(0)}%): ${devAmount.toFixed(6)} SOL`);
      console.log(`  Treasury (${(config.PAYOUT_SPLIT_PERCENT * 100).toFixed(0)}%): ${treasuryAmount.toFixed(6)} SOL`);

      // Check treasury balance
      const currentBalance = await this.getTreasuryBalance();
      console.log(`  Current treasury balance: ${currentBalance.toFixed(6)} SOL`);

      // In development mode, simulate the transfer
      if (config.IS_DEV) {
        console.log('🛠️ Development mode: Simulating transfer...');
        
        const mockSignature = this.generateMockSignature();
        const newBalance = currentBalance + treasuryAmount;
        
        console.log(`✅ Simulated split complete`);
        console.log(`  Mock signature: ${mockSignature}`);
        console.log(`  New treasury balance: ${newBalance.toFixed(6)} SOL`);
        
        return {
          success: true,
          devTransferSignature: mockSignature,
          treasuryBalance: newBalance
        };
      }

      // Production mode: Execute actual transfers
      // This would require the treasury to already have the claimed funds
      // In practice, the claiming process would deposit directly to treasury
      
      console.log('⚠️ Production mode: Actual transfers not implemented');
      console.log('   In production, integrate with actual SOL transfers');
      
      return {
        success: false,
        error: 'Production transfers not implemented - integrate with Solana transfers'
      };

    } catch (error) {
      console.error('❌ Failed to split rewards:', error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown split error'
      };
    }
  }

  /**
   * Pay winners from treasury balance
   */
  async payWinners(winnerWallets: string[]): Promise<PayoutResult> {
    try {
      console.log(`💸 Paying ${winnerWallets.length} winners from treasury...`);

      // Validate input
      if (winnerWallets.length === 0) {
        return {
          success: false,
          totalPaid: 0,
          perWinner: 0,
          signatures: [],
          failedPayouts: [],
          error: 'No winner wallets provided'
        };
      }

      // Check treasury balance
      const treasuryBalance = await this.getTreasuryBalance();
      console.log(`💰 Treasury balance: ${treasuryBalance.toFixed(6)} SOL`);

      // Calculate payout amounts
      const totalPayout = config.roundPoolSol * config.PAYOUT_SPLIT_PERCENT;
      const perWinner = totalPayout / winnerWallets.length;
      
      console.log(`📊 Payout calculation:`);
      console.log(`  Total pool: ${config.roundPoolSol} SOL`);
      console.log(`  Payout portion: ${(config.PAYOUT_SPLIT_PERCENT * 100)}%`);
      console.log(`  Total payout: ${totalPayout.toFixed(6)} SOL`);
      console.log(`  Per winner: ${perWinner.toFixed(6)} SOL`);

      // Check if treasury has enough balance
      const requiredBalance = totalPayout + config.GAS_RESERVE_SOL;
      if (treasuryBalance < requiredBalance) {
        return {
          success: false,
          totalPaid: 0,
          perWinner: 0,
          signatures: [],
          failedPayouts: winnerWallets,
          error: `Insufficient treasury balance: ${treasuryBalance.toFixed(6)} SOL < ${requiredBalance.toFixed(6)} SOL required`
        };
      }

      // Validate all winner wallets
      const validWallets: string[] = [];
      const invalidWallets: string[] = [];

      for (const wallet of winnerWallets) {
        try {
          new PublicKey(wallet);
          validWallets.push(wallet);
        } catch {
          console.warn(`⚠️ Invalid wallet address: ${wallet}`);
          invalidWallets.push(wallet);
        }
      }

      console.log(`✅ Valid wallets: ${validWallets.length}`);
      if (invalidWallets.length > 0) {
        console.warn(`❌ Invalid wallets: ${invalidWallets.length}`);
      }

      // Execute payouts
      if (config.IS_DEV) {
        // Development mode: Simulate payouts
        console.log('🛠️ Development mode: Simulating payouts...');
        
        const signatures = validWallets.map(() => this.generateMockSignature());
        
        console.log(`✅ Simulated ${validWallets.length} payouts`);
        console.log(`  Sample signature: ${signatures[0]}`);
        
        return {
          success: true,
          totalPaid: validWallets.length * perWinner,
          perWinner,
          signatures,
          failedPayouts: invalidWallets
        };
        
      } else {
        // Production mode: Execute real transfers
        console.log('🚀 Production mode: Executing real payouts...');
        
        const signatures: string[] = [];
        const failedPayouts: string[] = [];

        for (const wallet of validWallets) {
          try {
            const signature = await this.transferSol(wallet, perWinner);
            signatures.push(signature);
            console.log(`✅ Paid ${wallet}: ${signature}`);
          } catch (error) {
            console.error(`❌ Failed to pay ${wallet}:`, error);
            failedPayouts.push(wallet);
          }
        }

        const successfulPayouts = signatures.length;
        const totalPaid = successfulPayouts * perWinner;

        console.log(`📊 Payout summary:`);
        console.log(`  Successful: ${successfulPayouts}/${validWallets.length}`);
        console.log(`  Total paid: ${totalPaid.toFixed(6)} SOL`);
        console.log(`  Failed: ${failedPayouts.length}`);

        return {
          success: failedPayouts.length === 0,
          totalPaid,
          perWinner,
          signatures,
          failedPayouts: [...failedPayouts, ...invalidWallets]
        };
      }

    } catch (error) {
      console.error('❌ Critical error in payWinners:', error);
      return {
        success: false,
        totalPaid: 0,
        perWinner: 0,
        signatures: [],
        failedPayouts: winnerWallets,
        error: error instanceof Error ? error.message : 'Unknown payout error'
      };
    }
  }

  /**
   * Transfer SOL from treasury to a wallet
   */
  private async transferSol(recipientAddress: string, amountSol: number): Promise<string> {
    const recipient = new PublicKey(recipientAddress);
    const lamports = Math.floor(amountSol * LAMPORTS_PER_SOL);

    const transaction = new Transaction().add(
      SystemProgram.transfer({
        fromPubkey: this.treasuryKeypair.publicKey,
        toPubkey: recipient,
        lamports
      })
    );

    const signature = await sendAndConfirmTransaction(
      this.connection,
      transaction,
      [this.treasuryKeypair],
      { commitment: 'confirmed' }
    );

    return signature;
  }

  /**
   * Get treasury wallet balance
   */
  async getTreasuryBalance(): Promise<number> {
    try {
      const balance = await this.connection.getBalance(this.treasuryKeypair.publicKey);
      return balance / LAMPORTS_PER_SOL;
    } catch (error) {
      console.error('Failed to get treasury balance:', error);
      return 0;
    }
  }

  /**
   * Get treasury wallet address
   */
  getTreasuryAddress(): string {
    return this.treasuryKeypair.publicKey.toBase58();
  }

  /**
   * Get dev wallet address
   */
  getDevAddress(): string {
    return this.devWallet.toBase58();
  }

  /**
   * Emergency drain treasury to dev wallet
   */
  async emergencyDrain(): Promise<{ success: boolean; signature?: string; error?: string }> {
    try {
      console.log('🚨 Emergency drain initiated...');

      const balance = await this.getTreasuryBalance();
      const drainAmount = balance - config.GAS_RESERVE_SOL;

      if (drainAmount <= 0) {
        return {
          success: false,
          error: 'Insufficient balance to drain'
        };
      }

      if (config.IS_DEV) {
        console.log(`🛠️ Would drain ${drainAmount.toFixed(6)} SOL to dev wallet`);
        return {
          success: true,
          signature: this.generateMockSignature()
        };
      }

      // Execute actual drain in production
      const signature = await this.transferSol(this.devWallet.toBase58(), drainAmount);
      
      console.log(`✅ Emergency drain complete: ${signature}`);
      return { success: true, signature };

    } catch (error) {
      console.error('❌ Emergency drain failed:', error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown drain error'
      };
    }
  }

  /**
   * Generate mock transaction signature for testing
   */
  private generateMockSignature(): string {
    const chars = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    let signature = '';
    for (let i = 0; i < 88; i++) {
      signature += chars[Math.floor(Math.random() * chars.length)];
    }
    return signature;
  }

  /**
   * Test treasury service functionality
   */
  async testService(): Promise<{ success: boolean; details: any }> {
    try {
      const treasuryBalance = await this.getTreasuryBalance();
      
      return {
        success: true,
        details: {
          treasuryAddress: this.getTreasuryAddress(),
          devAddress: this.getDevAddress(),
          treasuryBalance: treasuryBalance.toFixed(6) + ' SOL',
          gasReserve: config.GAS_RESERVE_SOL + ' SOL',
          payoutSplit: (config.PAYOUT_SPLIT_PERCENT * 100) + '%',
          roundPool: config.roundPoolSol + ' SOL',
          connection: this.connection.rpcEndpoint,
          mode: config.IS_DEV ? 'Development (Mock)' : 'Production'
        }
      };
    } catch (error) {
      return {
        success: false,
        details: {
          error: error instanceof Error ? error.message : 'Unknown error'
        }
      };
    }
  }

  /**
   * Get service status for monitoring
   */
  getStatus() {
    return {
      service: 'Treasury',
      treasuryWallet: this.treasuryKeypair.publicKey.toBase58(),
      devWallet: this.devWallet.toBase58(),
      rpcEndpoint: this.connection.rpcEndpoint,
      payoutSplit: config.PAYOUT_SPLIT_PERCENT,
      gasReserve: config.GAS_RESERVE_SOL,
      mode: config.IS_DEV ? 'development' : 'production'
    };
  }
}

export default TreasuryService;