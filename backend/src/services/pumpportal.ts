// backend/src/services/pumpportal.ts
import { Keypair, Connection, Transaction, sendAndConfirmTransaction } from '@solana/web3.js';
import bs58 from 'bs58';
import config from '../env.js';

export interface ClaimResult {
  success: boolean;
  claimedSol: number;
  signature?: string;
  error?: string;
}

export interface PendingFeesResult {
  success: boolean;
  pendingSol: number;
  error?: string;
}

export class PumpPortalService {
  private connection: Connection;
  private creatorKeypair: Keypair;

  constructor() {
    this.connection = new Connection(config.RPC_ENDPOINT, 'confirmed');
    
    try {
      const secretKey = bs58.decode(config.CREATOR_WALLET_PRIVATE_KEY);
      this.creatorKeypair = Keypair.fromSecretKey(secretKey);
      console.log('PumpPortal service initialized with creator wallet:', this.creatorKeypair.publicKey.toBase58());
    } catch (error) {
      console.error('Failed to initialize creator keypair:', error);
      throw new Error('Invalid creator wallet private key');
    }
  }

  /**
   * Claim creator fees from pump.fun via PumpPortal API
   */
  async claimCreatorFees(): Promise<ClaimResult> {
    try {
      console.log('💰 Claiming creator fees from pump.fun...');

      // Check if there are pending fees first
      const pendingCheck = await this.checkPendingFees();
      if (!pendingCheck.success || pendingCheck.pendingSol <= 0.001) {
        console.log('ℹ️ No significant pending fees to claim');
        return {
          success: true,
          claimedSol: 0,
          error: 'No pending fees to claim'
        };
      }

      console.log(`📊 Pending fees: ${pendingCheck.pendingSol.toFixed(6)} SOL`);

      // In a real implementation, this would:
      // 1. Call PumpPortal API to get claim transaction
      // 2. Sign and submit the transaction
      // 3. Return the result
      
      // For now, we'll simulate the claim process
      const claimResult = await this.simulateClaimProcess(pendingCheck.pendingSol);
      
      if (claimResult.success) {
        console.log(`✅ Successfully claimed ${claimResult.claimedSol.toFixed(6)} SOL`);
        console.log(`   Transaction: ${claimResult.signature}`);
      } else {
        console.error('❌ Failed to claim fees:', claimResult.error);
      }

      return claimResult;

    } catch (error) {
      console.error('❌ Error claiming creator fees:', error);
      return {
        success: false,
        claimedSol: 0,
        error: error instanceof Error ? error.message : 'Unknown claim error'
      };
    }
  }

  /**
   * Check pending creator fees
   */
  async checkPendingFees(): Promise<PendingFeesResult> {
    try {
      console.log('🔍 Checking pending creator fees...');

      // In production, this would call PumpPortal API:
      // const response = await fetch(`${config.PUMPPORTAL_BASE_URL}/fees/pending`, {
      //   method: 'POST',
      //   headers: { 'Content-Type': 'application/json' },
      //   body: JSON.stringify({
      //     mint: config.TEST_TOKEN_MINT,
      //     creator: this.creatorKeypair.publicKey.toBase58()
      //   })
      // });

      // For development, simulate pending fees
      const simulatedPending = await this.simulatePendingFees();

      return {
        success: true,
        pendingSol: simulatedPending
      };

    } catch (error) {
      console.error('Failed to check pending fees:', error);
      return {
        success: false,
        pendingSol: 0,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }

  /**
   * Get creator wallet balance
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
   * Get creator wallet address
   */
  getCreatorAddress(): string {
    return this.creatorKeypair.publicKey.toBase58();
  }

  /**
   * Simulate claiming process (for development/testing)
   * In production, this would be replaced with actual PumpPortal API calls
   */
  private async simulateClaimProcess(pendingAmount: number): Promise<ClaimResult> {
    try {
      // Simulate API call delay
      await new Promise(resolve => setTimeout(resolve, 1000));

      // In development mode, simulate a successful claim
      if (config.IS_DEV) {
        const mockSignature = this.generateMockSignature();
        
        return {
          success: true,
          claimedSol: pendingAmount,
          signature: mockSignature
        };
      }

      // In production, this would make the actual API call
      // and handle the real transaction
      return {
        success: false,
        claimedSol: 0,
        error: 'Production claiming not implemented - integrate with PumpPortal API'
      };

    } catch (error) {
      return {
        success: false,
        claimedSol: 0,
        error: error instanceof Error ? error.message : 'Simulation failed'
      };
    }
  }

  /**
   * Simulate pending fees (for development)
   */
  private async simulatePendingFees(): Promise<number> {
    // In development, return a random amount between 0.1 and 2.0 SOL
    if (config.IS_DEV) {
      return Math.random() * 1.9 + 0.1;
    }

    // In production, this would query the actual PumpPortal API
    return 0;
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
   * Test the PumpPortal service
   */
  async testService(): Promise<{ success: boolean; details: any }> {
    try {
      const creatorBalance = await this.getCreatorBalance();
      const pendingFees = await this.checkPendingFees();
      
      return {
        success: true,
        details: {
          creatorAddress: this.getCreatorAddress(),
          creatorBalance: creatorBalance.toFixed(6) + ' SOL',
          pendingFees: pendingFees.success ? pendingFees.pendingSol.toFixed(6) + ' SOL' : 'Error',
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
   * Estimate gas cost for claiming transaction
   */
  async estimateClaimGasCost(): Promise<number> {
    try {
      // Typical claim transaction costs around 0.000005 SOL
      return 0.000005;
    } catch (error) {
      console.error('Failed to estimate gas cost:', error);
      return 0.00001; // Conservative estimate
    }
  }

  /**
   * Check if creator wallet has enough SOL for gas
   */
  async hasEnoughSolForGas(): Promise<boolean> {
    try {
      const balance = await this.getCreatorBalance();
      const estimatedGas = await this.estimateClaimGasCost();
      
      return balance >= (estimatedGas + 0.001); // Add small buffer
    } catch (error) {
      console.error('Failed to check gas balance:', error);
      return false;
    }
  }

  /**
   * Get service status for monitoring
   */
  getStatus() {
    return {
      service: 'PumpPortal',
      creatorWallet: this.creatorKeypair.publicKey.toBase58(),
      rpcEndpoint: this.connection.rpcEndpoint,
      tokenMint: config.TEST_TOKEN_MINT,
      mode: config.IS_DEV ? 'development' : 'production',
      baseUrl: config.PUMPPORTAL_BASE_URL
    };
  }
}

export default PumpPortalService;