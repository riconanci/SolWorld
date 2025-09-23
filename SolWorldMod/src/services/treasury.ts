import { 
  Connection, 
  Keypair, 
  Transaction, 
  SystemProgram, 
  sendAndConfirmTransaction,
  LAMPORTS_PER_SOL,
  PublicKey
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
  signatures: string[];
  totalPaid: number;
  perWinner: number;
  failedPayouts: string[];
  error?: string;
}

export class TreasuryService {
  private connection: Connection;
  private creatorKeypair: Keypair;
  private treasuryKeypair: Keypair;
  private devWallet: PublicKey;

  constructor() {
    this.connection = new Connection(config.RPC_ENDPOINT, 'confirmed');
    
    try {
      // Load creator wallet (source of funds)
      const creatorPrivateKey = bs58.decode(config.CREATOR_WALLET_PRIVATE_KEY);
      this.creatorKeypair = Keypair.fromSecretKey(creatorPrivateKey);
      
      // Load treasury wallet (holds 20% for payouts)
      const treasuryPrivateKey = bs58.decode(config.TREASURY_WALLET_PRIVATE_KEY);
      this.treasuryKeypair = Keypair.fromSecretKey(treasuryPrivateKey);
      
      // Dev wallet (receives 80%)
      this.devWallet = new PublicKey(config.DEV_WALLET_ADDRESS);
      
      console.log('Treasury service initialized:', {
        creator: this.creatorKeypair.publicKey.toBase58(),
        treasury: this.treasuryKeypair.publicKey.toBase58(),
        dev: this.devWallet.toBase58(),
        split: `${config.PAYOUT_SPLIT_PERCENT * 100}% to treasury`
      });
      
    } catch (error) {
      throw new Error(`Failed to initialize treasury service: ${error}`);
    }
  }

  /**
   * Split claimed rewards: 80% to dev, 20% to treasury
   */
  async splitRewards(claimedAmount: number): Promise<SplitResult> {
    try {
      if (claimedAmount <= 0) {
        return {
          success: false,
          error: 'No rewards to split'
        };
      }

      console.log(`💰 Splitting ${claimedAmount.toFixed(6)} SOL rewards...`);

      const treasuryAmount = claimedAmount * config.PAYOUT_SPLIT_PERCENT;
      const devAmount = claimedAmount - treasuryAmount;

      console.log(`Treasury allocation: ${treasuryAmount.toFixed(6)} SOL (${config.PAYOUT_SPLIT_PERCENT * 100}%)`);
      console.log(`Dev allocation: ${devAmount.toFixed(6)} SOL (${(1 - config.PAYOUT_SPLIT_PERCENT) * 100}%)`);

      const transaction = new Transaction();

      // Transfer to dev wallet (80%)
      if (devAmount > 0.001) { // Only transfer if meaningful amount
        transaction.add(
          SystemProgram.transfer({
            fromPubkey: this.creatorKeypair.publicKey,
            toPubkey: this.devWallet,
            lamports: Math.floor(devAmount * LAMPORTS_PER_SOL),
          })
        );
      }

      // Transfer to treasury (20%)
      if (treasuryAmount > 0.001) { // Only transfer if meaningful amount
        transaction.add(
          SystemProgram.transfer({
            fromPubkey: this.creatorKeypair.publicKey,
            toPubkey: this.treasuryKeypair.publicKey,
            lamports: Math.floor(treasuryAmount * LAMPORTS_PER_SOL),
          })
        );
      }

      if (transaction.instructions.length === 0) {
        return {
          success: false,
          error: 'Claimed amount too small to split (< 0.001 SOL per recipient)'
        };
      }

      // Send the split transaction
      const signature = await sendAndConfirmTransaction(
        this.connection,
        transaction,
        [this.creatorKeypair],
        {
          commitment: 'confirmed',
          maxRetries: 3,
        }
      );

      console.log('✅ Rewards split successfully:', signature);

      // Get treasury balance after split
      const treasuryBalance = await this.getTreasuryBalance();

      return {
        success: true,
        devTransferSignature: signature,
        treasuryBalance
      };

    } catch (error) {
      console.error('❌ Failed to split rewards:', error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error during split'
      };
    }
  }

  /**
   * Pay winners from treasury wallet
   */
  async payWinners(winnerWallets: string[]): Promise<PayoutResult> {
    try {
      if (winnerWallets.length !== 10) {
        return {
          success: false,
          signatures: [],
          totalPaid: 0,
          perWinner: 0,
          failedPayouts: [],
          error: `Expected 10 winners, got ${winnerWallets.length}`
        };
      }

      console.log('💸 Paying winners from treasury...');

      // Get treasury balance
      const treasuryBalance = await this.getTreasuryBalance();
      
      if (treasuryBalance <= config.GAS_RESERVE_SOL) {
        return {
          success: false,
          signatures: [],
          totalPaid: 0,
          perWinner: 0,
          failedPayouts: [],
          error: `Insufficient treasury balance: ${treasuryBalance.toFixed(6)} SOL (need > ${config.GAS_RESERVE_SOL} SOL for gas)`
        };
      }

      // Calculate payout per winner (reserve gas)
      const availableForPayouts = treasuryBalance - config.GAS_RESERVE_SOL;
      const perWinner = availableForPayouts / 10;

      console.log(`Treasury balance: ${treasuryBalance.toFixed(6)} SOL`);
      console.log(`Available for payouts: ${availableForPayouts.toFixed(6)} SOL`);
      console.log(`Per winner: ${perWinner.toFixed(6)} SOL`);

      if (perWinner < 0.001) {
        return {
          success: false,
          signatures: [],
          totalPaid: 0,
          perWinner: 0,
          failedPayouts: [],
          error: `Payout too small: ${perWinner.toFixed(6)} SOL per winner (< 0.001 SOL minimum)`
        };
      }

      // Validate all winner addresses
      const validWinners: PublicKey[] = [];
      const failedPayouts: string[] = [];

      for (const wallet of winnerWallets) {
        try {
          const pubkey = new PublicKey(wallet);
          validWinners.push(pubkey);
        } catch (error) {
          console.warn(`Invalid winner wallet address: ${wallet}`);
          failedPayouts.push(wallet);
        }
      }

      if (validWinners.length === 0) {
        return {
          success: false,
          signatures: [],
          totalPaid: 0,
          perWinner: 0,
          failedPayouts: winnerWallets,
          error: 'No valid winner wallet addresses'
        };
      }

      // Send payouts (batch transactions if needed)
      const signatures: string[] = [];
      const lamportsPerWinner = Math.floor(perWinner * LAMPORTS_PER_SOL);

      // Process in batches of 5 to avoid transaction size limits
      for (let i = 0; i < validWinners.length; i += 5) {
        const batch = validWinners.slice(i, i + 5);
        
        try {
          const transaction = new Transaction();
          
          for (const winner of batch) {
            transaction.add(
              SystemProgram.transfer({
                fromPubkey: this.treasuryKeypair.publicKey,
                toPubkey: winner,
                lamports: lamportsPerWinner,
              })
            );
          }

          const signature = await sendAndConfirmTransaction(
            this.connection,
            transaction,
            [this.treasuryKeypair],
            {
              commitment: 'confirmed',
              maxRetries: 3,
            }
          );

          signatures.push(signature);
          console.log(`✅ Batch ${Math.floor(i/5) + 1} paid:`, signature);

        } catch (error) {
          console.error(`❌ Failed to pay batch ${Math.floor(i/5) + 1}:`, error);
          
          // Add failed wallets to failedPayouts
          for (const winner of batch) {
            failedPayouts.push(winner.toBase58());
          }
        }
      }

      const totalPaid = (validWinners.length - failedPayouts.length) * perWinner;

      console.log(`💰 Payout complete - Total: ${totalPaid.toFixed(6)} SOL to ${validWinners.length - failedPayouts.length} winners`);

      return {
        success: signatures.length > 0,
        signatures,
        totalPaid,
        perWinner,
        failedPayouts,
        error: failedPayouts.length > 0 ? `${failedPayouts.length} payouts failed` : undefined
      };

    } catch (error) {
      console.error('❌ Failed to pay winners:', error);
      return {
        success: false,
        signatures: [],
        totalPaid: 0,
        perWinner: 0,
        failedPayouts: winnerWallets,
        error: error instanceof Error ? error.message : 'Unknown error during payouts'
      };
    }
  }

  /**
   * Get current treasury balance
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
   * Get treasury wallet address for reference
   */
  getTreasuryAddress(): string {
    return this.treasuryKeypair.publicKey.toBase58();
  }

  /**
   * Emergency: Transfer all treasury funds back to creator
   */
  async emergencyDrain(): Promise<{ success: boolean; signature?: string; error?: string }> {
    try {
      const balance = await this.connection.getBalance(this.treasuryKeypair.publicKey);
      
      if (balance <= 5000) { // Keep some for rent
        return {
          success: false,
          error: 'Treasury balance too low to drain'
        };
      }

      const transaction = new Transaction().add(
        SystemProgram.transfer({
          fromPubkey: this.treasuryKeypair.publicKey,
          toPubkey: this.creatorKeypair.publicKey,
          lamports: balance - 5000, // Keep minimum rent
        })
      );

      const signature = await sendAndConfirmTransaction(
        this.connection,
        transaction,
        [this.treasuryKeypair],
        { commitment: 'confirmed' }
      );

      return { success: true, signature };

    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Emergency drain failed'
      };
    }
  }
}

export default TreasuryService;