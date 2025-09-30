// backend/src/services/pumpportal.ts - PRODUCTION VERSION
import { 
  Keypair, 
  Connection, 
  VersionedTransaction,
  sendAndConfirmTransaction
} from '@solana/web3.js';
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
  private readonly PUMPPORTAL_LOCAL_API = 'https://pumpportal.fun/api/trade-local';

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
   * Claim creator fees from pump.fun using PumpPortal Local Transaction API
   */
  async claimCreatorFees(): Promise<ClaimResult> {
    try {
      console.log('💰 Claiming creator fees from pump.fun via PumpPortal...');

      // Development mode: Use mock claiming
      if (config.IS_DEV) {
        return this.simulateClaimProcess();
      }

      // Production mode: Real PumpPortal API integration
      console.log('🚀 Production mode: Using real PumpPortal API');

      // Step 1: Get balance before claiming
      const balanceBefore = await this.getCreatorBalance();
      console.log(`💼 Creator balance before: ${balanceBefore.toFixed(6)} SOL`);

      // Step 2: Request claim transaction from PumpPortal
      console.log('🔄 Requesting claim transaction from PumpPortal...');
      
      const claimResponse = await fetch(this.PUMPPORTAL_LOCAL_API, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          publicKey: this.creatorKeypair.publicKey.toBase58(),
          action: 'collectCreatorFee',
          priorityFee: 0.000001, // 0.000001 SOL priority fee
          // Note: pump.fun claims ALL creator fees at once, no need to specify mint
        })
      });

      if (!claimResponse.ok) {
        const errorText = await claimResponse.text();
        console.error('❌ PumpPortal API error:', claimResponse.status, errorText);
        
        // If no fees to claim, return success with 0
        if (errorText.includes('no fees') || errorText.includes('nothing to claim')) {
          return {
            success: true,
            claimedSol: 0,
            error: 'No creator fees available to claim'
          };
        }
        
        throw new Error(`PumpPortal API error: ${claimResponse.status} ${errorText}`);
      }

      // Step 3: Get unsigned transaction from response
      const transactionBytes = await claimResponse.arrayBuffer();
      console.log('📝 Received unsigned transaction from PumpPortal');

      // Step 4: Sign and send transaction
      console.log('✍️ Signing and sending claim transaction...');
      
      const unsignedTx = VersionedTransaction.deserialize(new Uint8Array(transactionBytes));
      unsignedTx.sign([this.creatorKeypair]);

      const signature = await this.connection.sendTransaction(unsignedTx, {
        skipPreflight: false,
        preflightCommitment: 'confirmed',
      });

      console.log('⏳ Confirming transaction...');
      await this.connection.confirmTransaction({
        signature,
        blockhash: unsignedTx.message.recentBlockhash!,
        lastValidBlockHeight: await this.connection.getBlockHeight() + 150,
      });

      // Step 5: Calculate claimed amount
      const balanceAfter = await this.getCreatorBalance();
      const claimedAmount = balanceAfter - balanceBefore;

      if (claimedAmount > 0) {
        console.log(`✅ Successfully claimed ${claimedAmount.toFixed(6)} SOL`);
        console.log(`   Transaction: ${signature}`);
        console.log(`   Creator balance after: ${balanceAfter.toFixed(6)} SOL`);
        
        return {
          success: true,
          claimedSol: claimedAmount,
          signature
        };
      } else {
        console.log('ℹ️ No fees were claimed (balance unchanged)');
        return {
          success: true,
          claimedSol: 0,
          signature,
          error: 'No fees were available to claim'
        };
      }

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
   * Check pending creator fees (estimation)
   * Note: PumpPortal doesn't have a direct "check pending" endpoint,
   * so we'll estimate based on recent activity or return a small amount
   */
  async checkPendingFees(): Promise<PendingFeesResult> {
    try {
      console.log('🔍 Checking potential pending creator fees...');

      if (config.IS_DEV) {
        // Development: simulate pending fees
        const simulatedPending = Math.random() * 1.9 + 0.1;
        return {
          success: true,
          pendingSol: simulatedPending
        };
      }

      // Production: Since pump.fun doesn't provide a direct pending check,
      // we'll assume there might be fees and let the claim attempt determine
      // if there's actually anything to claim
      console.log('📊 Production mode: Will attempt claim to check for fees');
      
      return {
        success: true,
        pendingSol: 0.01 // Minimal estimate - actual amount determined during claim
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
   * Get creator keypair for treasury split operations
   */
  getCreatorKeypair(): Keypair {
    return this.creatorKeypair;
  }

  /**
   * Simulate claiming process (for development/testing)
   */
  private async simulateClaimProcess(): Promise<ClaimResult> {
    try {
      console.log('🛠️ Development mode: Simulating pump.fun fee claim...');
      
      // Simulate API call delay
      await new Promise(resolve => setTimeout(resolve, 2000));

      const simulatedAmount = Math.random() * 1.9 + 0.1; // 0.1 to 2.0 SOL
      const mockSignature = this.generateMockSignature();
      
      console.log(`✅ Simulated claim: ${simulatedAmount.toFixed(6)} SOL`);
      console.log(`   Mock signature: ${mockSignature}`);
      
      return {
        success: true,
        claimedSol: simulatedAmount,
        signature: mockSignature
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
          mode: config.IS_DEV ? 'Development (Mock)' : 'Production (Real PumpPortal)',
          api: config.IS_DEV ? 'Mock simulation' : this.PUMPPORTAL_LOCAL_API
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
      // PumpPortal claiming typically costs around 0.000006 SOL + priority fee
      return 0.000001 + 0.000006; // priority + base fee
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
      
      const needed = estimatedGas + 0.001; // Add small buffer
      const hasEnough = balance >= needed;
      
      if (!hasEnough) {
        console.warn(`⚠️ Creator wallet might not have enough SOL for gas:`);
        console.warn(`   Current: ${balance.toFixed(6)} SOL`);
        console.warn(`   Needed: ${needed.toFixed(6)} SOL`);
      }
      
      return hasEnough;
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
      apiEndpoint: config.IS_DEV ? 'mock' : this.PUMPPORTAL_LOCAL_API,
      integration: 'Local Transaction API'
    };
  }
}

export default PumpPortalService;