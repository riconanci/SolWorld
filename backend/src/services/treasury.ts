// backend/src/services/treasury.ts - COMPLETE WITH DYNAMIC POOLS
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
  private transactionHistory: Array<{
    type: 'split' | 'payout' | 'drain';
    timestamp: number;
    amount: number;
    signature: string;
    recipients?: number;
  }>;

  constructor() {
    this.connection = new Connection(config.RPC_ENDPOINT, 'confirmed');
    this.transactionHistory = [];
    
    try {
      // Initialize treasury keypair
      const treasurySecretKey = bs58.decode(config.TREASURY_WALLET_PRIVATE_KEY);
      this.treasuryKeypair = Keypair.fromSecretKey(treasurySecretKey);
      
      // Initialize dev wallet public key
      this.devWallet = new PublicKey(config.DEV_WALLET_ADDRESS);
      
      console.log('🏦 Treasury service initialized:');
      console.log('  Treasury:', this.treasuryKeypair.publicKey.toBase58());
      console.log('  Dev wallet:', this.devWallet.toBase58());
      console.log('  Mode: Dynamic Pool Sizing');
      console.log('  No funding required! ✅');
      
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

      console.log(`  💼 Dev (${((1 - config.PAYOUT_SPLIT_PERCENT) * 100).toFixed(0)}%): ${devAmount.toFixed(6)} SOL`);
      console.log(`  🏦 Treasury (${(config.PAYOUT_SPLIT_PERCENT * 100).toFixed(0)}%): ${treasuryAmount.toFixed(6)} SOL`);

      // Check current treasury balance
      const currentBalance = await this.getTreasuryBalance();
      console.log(`  💳 Current treasury balance: ${currentBalance.toFixed(6)} SOL`);

      // In development mode, simulate the transfer
      if (config.IS_DEV) {
        console.log('🛠️ Development mode: Simulating split transfer...');
        
        const mockSignature = this.generateMockSignature();
        const newBalance = currentBalance + treasuryAmount;

        // Record mock transaction
        this.transactionHistory.push({
          type: 'split',
          timestamp: Date.now(),
          amount: devAmount,
          signature: mockSignature
        });

        console.log(`✅ Simulated split complete`);
        console.log(`  🎯 Mock signature: ${mockSignature}`);
        console.log(`  💰 New treasury balance: ${newBalance.toFixed(6)} SOL`);
        
        return {
          success: true,
          devTransferSignature: mockSignature,
          treasuryBalance: newBalance
        };
      }

      // Production mode: Execute actual transfers
      console.log('🚀 Production mode: Executing real split transfer...');
      
      try {
        // Transfer dev portion to dev wallet
        const signature = await this.transferSol(this.devWallet.toBase58(), devAmount);
        
        // The treasury portion stays in the treasury (no transfer needed)
        const newBalance = await this.getTreasuryBalance();
        
        // Record real transaction
        this.transactionHistory.push({
          type: 'split',
          timestamp: Date.now(),
          amount: devAmount,
          signature: signature
        });

        console.log(`✅ Split transfer complete`);
        console.log(`  🎯 Transaction: ${signature}`);
        console.log(`  💰 Treasury balance: ${newBalance.toFixed(6)} SOL`);
        
        return {
          success: true,
          devTransferSignature: signature,
          treasuryBalance: newBalance
        };
      } catch (error) {
        console.error('❌ Failed to execute split transfer:', error);
        return {
          success: false,
          error: error instanceof Error ? error.message : 'Unknown split error'
        };
      }

    } catch (error) {
      console.error('❌ Failed to split rewards:', error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown split error'
      };
    }
  }

  /**
   * Pay winners with dynamic per-winner amount (MAIN DYNAMIC METHOD)
   */
  async payWinnersDynamic(winnerWallets: string[], perWinnerAmount: number): Promise<PayoutResult> {
    try {
      console.log(`💸 Dynamic payout: ${winnerWallets.length} winners × ${perWinnerAmount.toFixed(6)} SOL each`);
      console.log(`🎯 Total dynamic payout: ${(winnerWallets.length * perWinnerAmount).toFixed(6)} SOL`);

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

      if (perWinnerAmount <= 0) {
        console.log('💫 No funds available for payouts this round');
        return {
          success: true,
          totalPaid: 0,
          perWinner: 0,
          signatures: [],
          failedPayouts: [],
          error: 'No funds available for payouts this round'
        };
      }

      console.log(`🎯 Dynamic payout details:`);
      console.log(`  💎 Per winner: ${perWinnerAmount.toFixed(6)} SOL`);
      console.log(`  📊 Total winners: ${winnerWallets.length}`);
      console.log(`  💰 Total payout: ${(perWinnerAmount * winnerWallets.length).toFixed(6)} SOL`);

      // DEVELOPMENT MODE: Skip balance checks and simulate payouts
      if (config.IS_DEV) {
        console.log('🛠️ Development mode: Simulating dynamic payouts...');
        
        // Validate wallet formats (still good to check in dev mode)
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
        
        const signatures = validWallets.map(() => this.generateMockSignature());
        
        console.log(`✅ Simulated ${validWallets.length} dynamic payouts`);
        if (signatures.length > 0) {
          console.log(`  🎯 Sample signature: ${signatures[0]}`);
        }

        // Record mock transaction
        this.transactionHistory.push({
          type: 'payout',
          timestamp: Date.now(),
          amount: validWallets.length * perWinnerAmount,
          signature: signatures[0] || 'no-signatures',
          recipients: validWallets.length
        });
        
        return {
          success: true,
          totalPaid: validWallets.length * perWinnerAmount,
          perWinner: perWinnerAmount,
          signatures,
          failedPayouts: invalidWallets
        };
      }

      // PRODUCTION MODE: Execute real transfers (no balance pre-check needed)
      console.log('🚀 Production mode: Executing dynamic payouts...');
      console.log('   ℹ️ No balance pre-check - using dynamic amounts from actual claims');
      
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

      // Execute real transfers
      const signatures: string[] = [];
      const failedPayouts: string[] = [];

      for (const wallet of validWallets) {
        try {
          const signature = await this.transferSol(wallet, perWinnerAmount);
          signatures.push(signature);
          console.log(`✅ Paid ${wallet}: ${perWinnerAmount.toFixed(6)} SOL → ${signature}`);
        } catch (error) {
          console.error(`❌ Failed to pay ${wallet}:`, error);
          failedPayouts.push(wallet);
        }
      }

      const successfulPayouts = signatures.length;
      const totalPaid = successfulPayouts * perWinnerAmount;

      console.log(`📊 Dynamic payout summary:`);
      console.log(`  ✅ Successful: ${successfulPayouts}/${validWallets.length}`);
      console.log(`  💰 Total paid: ${totalPaid.toFixed(6)} SOL`);
      console.log(`  ❌ Failed: ${failedPayouts.length}`);

      // Record real transaction
      if (signatures.length > 0) {
        this.transactionHistory.push({
          type: 'payout',
          timestamp: Date.now(),
          amount: totalPaid,
          signature: signatures[0],
          recipients: successfulPayouts
        });
      }

      return {
        success: failedPayouts.length === 0,
        totalPaid,
        perWinner: perWinnerAmount,
        signatures,
        failedPayouts: [...failedPayouts, ...invalidWallets]
      };

    } catch (error) {
      console.error('❌ Critical error in dynamic payouts:', error);
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
   * Pay winners from treasury balance - LEGACY METHOD (for backwards compatibility)
   */
  async payWinners(winnerWallets: string[]): Promise<PayoutResult> {
    try {
      console.log(`💸 Legacy payout: ${winnerWallets.length} winners from treasury...`);
      console.log('⚠️ Note: Using legacy fixed-pool method. Consider switching to dynamic pools.');

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

      // Calculate payout amounts using old fixed method
      const totalPayout = config.roundPoolSol * config.PAYOUT_SPLIT_PERCENT;
      const perWinner = totalPayout / winnerWallets.length;
      
      console.log(`📊 Legacy payout calculation:`);
      console.log(`  📦 Total pool: ${config.roundPoolSol} SOL`);
      console.log(`  💰 Payout portion: ${(config.PAYOUT_SPLIT_PERCENT * 100)}%`);
      console.log(`  💎 Total payout: ${totalPayout.toFixed(6)} SOL`);
      console.log(`  🏆 Per winner: ${perWinner.toFixed(6)} SOL`);

      // DEVELOPMENT MODE: Skip balance checks and simulate payouts
      if (config.IS_DEV) {
        console.log('🛠️ Development mode: Simulating legacy payouts...');
        
        // Validate wallet formats
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
        
        const signatures = validWallets.map(() => this.generateMockSignature());
        
        console.log(`✅ Simulated ${validWallets.length} legacy payouts`);
        if (signatures.length > 0) {
          console.log(`  🎯 Sample signature: ${signatures[0]}`);
        }
        
        return {
          success: true,
          totalPaid: validWallets.length * perWinner,
          perWinner,
          signatures,
          failedPayouts: invalidWallets
        };
      }

      // PRODUCTION MODE: Check balance and execute real transfers
      console.log('🚀 Production mode: Executing legacy payouts...');
      
      // Check treasury balance (required for legacy method)
      const treasuryBalance = await this.getTreasuryBalance();
      console.log(`💰 Treasury balance: ${treasuryBalance.toFixed(6)} SOL`);

      const requiredBalance = totalPayout + config.GAS_RESERVE_SOL;
      if (treasuryBalance < requiredBalance) {
        console.error(`❌ Insufficient treasury balance for legacy payouts`);
        console.error(`   Required: ${requiredBalance.toFixed(6)} SOL`);
        console.error(`   Available: ${treasuryBalance.toFixed(6)} SOL`);
        console.error(`   💡 Suggestion: Switch to dynamic pools to eliminate funding requirements`);
        
        return {
          success: false,
          totalPaid: 0,
          perWinner: 0,
          signatures: [],
          failedPayouts: winnerWallets,
          error: `Insufficient treasury balance: ${treasuryBalance.toFixed(6)} SOL < ${requiredBalance.toFixed(6)} SOL required`
        };
      }

      // Execute the same logic as dynamic method
      return this.payWinnersDynamic(winnerWallets, perWinner);

    } catch (error) {
      console.error('❌ Critical error in legacy payouts:', error);
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
   * Get transaction history for monitoring
   */
  getTransactionHistory(limit: number = 20) {
    return this.transactionHistory.slice(-limit).map(tx => ({
      ...tx,
      timestampFormatted: new Date(tx.timestamp).toISOString(),
      amountFormatted: tx.amount.toFixed(6) + ' SOL'
    }));
  }

  /**
   * Get treasury statistics
   */
  async getTreasuryStats() {
    try {
      const balance = await this.getTreasuryBalance();
      const recentTransactions = this.transactionHistory.slice(-10);
      const totalPayoutAmount = recentTransactions
        .filter(tx => tx.type === 'payout')
        .reduce((sum, tx) => sum + tx.amount, 0);
      const totalSplitAmount = recentTransactions
        .filter(tx => tx.type === 'split')
        .reduce((sum, tx) => sum + tx.amount, 0);

      return {
        currentBalance: balance,
        balanceFormatted: balance.toFixed(6) + ' SOL',
        recentTransactions: recentTransactions.length,
        totalRecentPayouts: totalPayoutAmount,
        totalRecentSplits: totalSplitAmount,
        systemType: 'Dynamic Pool Treasury',
        fundingRequired: false,
        sustainable: true
      };
    } catch (error) {
      return {
        error: error instanceof Error ? error.message : 'Failed to get treasury stats'
      };
    }
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
          error: `Insufficient balance to drain (${balance.toFixed(6)} SOL available, ${config.GAS_RESERVE_SOL} SOL gas reserve required)`
        };
      }

      if (config.IS_DEV) {
        console.log(`🛠️ Would drain ${drainAmount.toFixed(6)} SOL to dev wallet`);
        const mockSignature = this.generateMockSignature();
        
        // Record mock drain
        this.transactionHistory.push({
          type: 'drain',
          timestamp: Date.now(),
          amount: drainAmount,
          signature: mockSignature
        });
        
        return {
          success: true,
          signature: mockSignature
        };
      }

      // Execute actual drain in production
      const signature = await this.transferSol(this.devWallet.toBase58(), drainAmount);
      
      // Record real drain
      this.transactionHistory.push({
        type: 'drain',
        timestamp: Date.now(),
        amount: drainAmount,
        signature: signature
      });
      
      console.log(`✅ Emergency drain complete: ${signature}`);
      console.log(`   Drained: ${drainAmount.toFixed(6)} SOL`);
      
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
      const stats = await this.getTreasuryStats();
      
      return {
        success: true,
        details: {
          treasuryAddress: this.getTreasuryAddress(),
          devAddress: this.getDevAddress(),
          treasuryBalance: treasuryBalance.toFixed(6) + ' SOL',
          gasReserve: config.GAS_RESERVE_SOL + ' SOL',
          payoutSplit: (config.PAYOUT_SPLIT_PERCENT * 100) + '%',
          systemType: 'Dynamic Pool Treasury',
          fundingRequired: false,
          connection: this.connection.rpcEndpoint,
          mode: config.IS_DEV ? 'Development (Mock)' : 'Production',
          recentActivity: this.getTransactionHistory(5),
          stats: stats
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
      mode: config.IS_DEV ? 'development' : 'production',
      systemType: 'Dynamic Pool Treasury',
      fundingRequired: false,
      sustainable: true,
      transactionHistory: this.transactionHistory.length
    };
  }
}

export default TreasuryService;