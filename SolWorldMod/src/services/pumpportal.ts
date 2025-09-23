import { Connection, Keypair, Transaction, sendAndConfirmTransaction } from '@solana/web3.js';
import bs58 from 'bs58';
import config from '../env.js';

export interface CollectFeeResponse {
  success: boolean;
  transaction?: string; // Base64 encoded transaction
  error?: string;
}

export interface ClaimResult {
  success: boolean;
  signature?: string;
  claimedSol?: number;
  error?: string;
}

export class PumpPortalService {
  private connection: Connection;
  private creatorKeypair: Keypair;

  constructor() {
    this.connection = new Connection(config.RPC_ENDPOINT, 'confirmed');
    
    try {
      const privateKeyBytes = bs58.decode(config.CREATOR_WALLET_PRIVATE_KEY);
      this.creatorKeypair = Keypair.fromSecretKey(privateKeyBytes);
      
      console.log('PumpPortal service initialized:', {
        creatorWallet: this.creatorKeypair.publicKey.toBase58(),
        endpoint: config.PUMPPORTAL_BASE_URL
      });
    } catch (error) {
      throw new Error(`Failed to load creator wallet: ${error}`);
    }
  }

  /**
   * Claim all available creator fees using PumpPortal API
   */
  async claimCreatorFees(): Promise<ClaimResult> {
    try {
      console.log('🎯 Claiming creator fees via PumpPortal API...');
      
      // Get wallet balance before claiming
      const balanceBefore = await this.connection.getBalance(this.creatorKeypair.publicKey);
      const solBefore = balanceBefore / 1e9;
      
      console.log(`Creator wallet balance before: ${solBefore.toFixed(6)} SOL`);

      // Step 1: Request transaction from PumpPortal
      const txResponse = await this.requestCollectFeeTransaction();
      
      if (!txResponse.success || !txResponse.transaction) {
        return {
          success: false,
          error: txResponse.error || 'Failed to get transaction from PumpPortal'
        };
      }

      // Step 2: Deserialize and sign transaction
      const transaction = Transaction.from(Buffer.from(txResponse.transaction, 'base64'));
      
      // Step 3: Send transaction
      const signature = await sendAndConfirmTransaction(
        this.connection,
        transaction,
        [this.creatorKeypair],
        {
          commitment: 'confirmed',
          maxRetries: 3,
        }
      );

      console.log('✅ Creator fees claimed successfully:', signature);

      // Step 4: Calculate claimed amount
      const balanceAfter = await this.connection.getBalance(this.creatorKeypair.publicKey);
      const solAfter = balanceAfter / 1e9;
      const claimedSol = solAfter - solBefore;

      console.log(`Creator wallet balance after: ${solAfter.toFixed(6)} SOL`);
      console.log(`Claimed amount: ${claimedSol.toFixed(6)} SOL`);

      return {
        success: true,
        signature,
        claimedSol: Math.max(0, claimedSol) // Ensure non-negative
      };

    } catch (error) {
      console.error('❌ Failed to claim creator fees:', error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error during claiming'
      };
    }
  }

  /**
   * Request a collectCreatorFee transaction from PumpPortal
   */
  private async requestCollectFeeTransaction(): Promise<CollectFeeResponse> {
    try {
      const url = `${config.PUMPPORTAL_BASE_URL}/tx/collectCreatorFee`;
      
      const requestBody = {
        publicKey: this.creatorKeypair.publicKey.toBase58(),
        action: 'collectCreatorFee',
        // Note: PumpPortal claims all fees at once, no need to specify mint
      };

      console.log('📡 Requesting transaction from PumpPortal:', { url, publicKey: requestBody.publicKey });

      const response = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
        },
        body: JSON.stringify(requestBody),
      });

      if (!response.ok) {
        const errorText = await response.text();
        return {
          success: false,
          error: `PumpPortal API error (${response.status}): ${errorText}`
        };
      }

      const data = await response.json();
      
      if (!data.transaction) {
        return {
          success: false,
          error: 'No transaction returned from PumpPortal API'
        };
      }

      return {
        success: true,
        transaction: data.transaction
      };

    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Network error calling PumpPortal'
      };
    }
  }

  /**
   * Get current creator wallet balance
   */
  async getCreatorBalance(): Promise<number> {
    try {
      const balance = await this.connection.getBalance(this.creatorKeypair.publicKey);
      return balance / 1e9; // Convert lamports to SOL
    } catch (error) {
      console.error('Failed to get creator balance:', error);
      return 0;
    }
  }

  /**
   * Estimate available creator fees (mock implementation)
   * In production, you might want to call a PumpPortal endpoint that shows pending fees
   */
  async getEstimatedPendingFees(): Promise<number> {
    // TODO: Replace with actual PumpPortal endpoint if available
    // For now, return 0 - the claiming process will show actual amounts
    return 0;
  }
}

export default PumpPortalService;