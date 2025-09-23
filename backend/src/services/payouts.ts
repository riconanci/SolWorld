// backend/src/services/payouts.ts
import PumpPortalService from './pumpportal';
import TreasuryService from './treasury';
import config from '../env';

export interface ArenaReportPayload {
  matchId: string;
  timestamp: number;
  nonce?: string;
  winner: 'Red' | 'Blue' | 'Tie';
  red: Array<{
    wallet: string;
    kills: number;
    alive: boolean;
  }>;
  blue: Array<{
    wallet: string;
    kills: number;
    alive: boolean;
  }>;
  roundRewardTotalSol: number;
  payoutPercent: number;
  hmacKeyId?: string;
  signature?: string;
}

export interface PayoutResult {
  success: boolean;
  matchId: string;
  winner: 'Red' | 'Blue' | 'Tie';
  processing: {
    claimed: {
      success: boolean;
      amount?: number;
      signature?: string;
      error?: string;
    };
    split: {
      success: boolean;
      devAmount?: number;
      treasuryAmount?: number;
      signature?: string;
      error?: string;
    };
    payout: {
      success: boolean;
      totalPaid?: number;
      perWinner?: number;
      signatures?: string[];
      failedPayouts?: string[];
      error?: string;
    };
  };
  txids: string[];
  error?: string;
}

export class PayoutService {
  private pumpPortal: PumpPortalService;
  private treasury: TreasuryService;
  private processedMatches: Set<string>;

  constructor() {
    this.pumpPortal = new PumpPortalService();
    this.treasury = new TreasuryService();
    this.processedMatches = new Set();
    
    console.log('Payout service initialized');
  }

  /**
   * Process arena results and execute payouts
   */
  async processArenaResult(report: ArenaReportPayload): Promise<PayoutResult> {
    const startTime = Date.now();
    console.log(`💰 Processing arena result for match ${report.matchId}...`);
    
    // Initialize result structure
    const result: PayoutResult = {
      success: false,
      matchId: report.matchId,
      winner: report.winner,
      processing: {
        claimed: { success: false },
        split: { success: false },
        payout: { success: false }
      },
      txids: []
    };

    try {
      // Prevent duplicate processing
      if (this.processedMatches.has(report.matchId)) {
        result.error = 'Match already processed';
        return result;
      }

      // Validate report structure
      const validation = this.validateReport(report);
      if (!validation.valid) {
        result.error = validation.error;
        return result;
      }

      // Get winner wallets
      const winnerWallets = this.extractWinnerWallets(report);
      if (winnerWallets.length === 0) {
        result.error = 'No winners found (tie or invalid data)';
        return result;
      }

      console.log(`🏆 ${report.winner} team wins! Processing ${winnerWallets.length} winner payouts...`);

      // PHASE 1: Claim creator fees from pump.fun
      console.log('📥 Phase 1: Claiming creator fees...');
      const claimResult = await this.pumpPortal.claimCreatorFees();
      result.processing.claimed = {
        success: claimResult.success,
        amount: claimResult.claimedSol,
        signature: claimResult.signature,
        error: claimResult.error
      };

      if (!claimResult.success) {
        console.warn('⚠️ Failed to claim fees, continuing with existing treasury balance');
      } else {
        console.log(`✅ Claimed ${claimResult.claimedSol?.toFixed(6)} SOL from pump.fun`);
        
        // Add claim signature to txids
        if (claimResult.signature) {
          result.txids.push(claimResult.signature);
        }
      }

      // PHASE 2: Split claimed funds (80% dev, 20% treasury) - only if we claimed something
      if (claimResult.success && claimResult.claimedSol && claimResult.claimedSol > 0.001) {
        console.log('🔄 Phase 2: Splitting claimed rewards...');
        const splitResult = await this.treasury.splitRewards(claimResult.claimedSol);
        result.processing.split = {
          success: splitResult.success,
          devAmount: claimResult.claimedSol * (1 - config.PAYOUT_SPLIT_PERCENT),
          treasuryAmount: claimResult.claimedSol * config.PAYOUT_SPLIT_PERCENT,
          signature: splitResult.devTransferSignature,
          error: splitResult.error
        };

        if (!splitResult.success) {
          console.error('❌ Failed to split rewards:', splitResult.error);
          result.error = 'Failed to split claimed rewards';
          return result;
        }

        console.log(`✅ Split complete - Treasury balance: ${splitResult.treasuryBalance?.toFixed(6)} SOL`);
        
        // Add split signature to txids
        if (splitResult.devTransferSignature) {
          result.txids.push(splitResult.devTransferSignature);
        }
      } else {
        console.log('⏭️ Phase 2: Skipping split (no new funds claimed)');
        result.processing.split = {
          success: true,
          devAmount: 0,
          treasuryAmount: 0,
          error: 'No new funds to split'
        };
      }

      // PHASE 3: Pay winners from treasury
      console.log('💸 Phase 3: Paying winners from treasury...');
      const payoutResult = await this.treasury.payWinners(winnerWallets);
      result.processing.payout = {
        success: payoutResult.success,
        totalPaid: payoutResult.totalPaid,
        perWinner: payoutResult.perWinner,
        signatures: payoutResult.signatures,
        failedPayouts: payoutResult.failedPayouts,
        error: payoutResult.error
      };

      if (!payoutResult.success) {
        console.error('❌ Failed to pay winners:', payoutResult.error);
        result.error = 'Failed to pay winners: ' + payoutResult.error;
        return result;
      }

      // Add payout signatures to txids
      result.txids.push(...payoutResult.signatures);

      console.log(`✅ Paid ${payoutResult.totalPaid.toFixed(6)} SOL to ${winnerWallets.length - payoutResult.failedPayouts.length} winners`);
      console.log(`   Per winner: ${payoutResult.perWinner.toFixed(6)} SOL`);

      // Mark as successfully processed
      this.processedMatches.add(report.matchId);
      result.success = true;

      const processingTime = Date.now() - startTime;
      console.log(`🎯 Arena payout complete for ${report.matchId} in ${processingTime}ms`);
      console.log(`   Total transactions: ${result.txids.length}`);

      return result;

    } catch (error) {
      console.error('❌ Critical error processing arena result:', error);
      result.error = error instanceof Error ? error.message : 'Unknown processing error';
      return result;
    }
  }

  /**
   * Validate arena report structure
   */
  private validateReport(report: ArenaReportPayload): { valid: boolean; error?: string } {
    // Check required fields
    if (!report.matchId || !report.winner || !report.red || !report.blue) {
      return { valid: false, error: 'Missing required fields' };
    }

    // Check team sizes
    if (report.red.length !== 10 || report.blue.length !== 10) {
      return { 
        valid: false, 
        error: `Invalid team sizes: red=${report.red.length}, blue=${report.blue.length} (expected 10 each)` 
      };
    }

    // Check for valid wallets
    const allWallets = [...report.red, ...report.blue].map(f => f.wallet);
    const uniqueWallets = new Set(allWallets);
    
    if (uniqueWallets.size !== 20) {
      return { 
        valid: false, 
        error: `Duplicate wallets detected: ${allWallets.length} total, ${uniqueWallets.size} unique` 
      };
    }

    // Check winner is valid
    if (!['Red', 'Blue', 'Tie'].includes(report.winner)) {
      return { valid: false, error: `Invalid winner: ${report.winner}` };
    }

    // Check timestamp is reasonable (within last hour to 5 minutes in future)
    const now = Date.now();
    const age = now - report.timestamp;
    if (age > 60 * 60 * 1000 || age < -5 * 60 * 1000) {
      return { 
        valid: false, 
        error: `Invalid timestamp: ${new Date(report.timestamp).toISOString()} (age: ${age}ms)` 
      };
    }

    return { valid: true };
  }

  /**
   * Extract winner wallet addresses from report
   */
  private extractWinnerWallets(report: ArenaReportPayload): string[] {
    if (report.winner === 'Tie') {
      // For ties, no payouts
      return [];
    }

    const winningTeam = report.winner === 'Red' ? report.red : report.blue;
    
    // Return all wallets from winning team (dead or alive)
    return winningTeam.map(fighter => fighter.wallet);
  }

  /**
   * Get processing statistics for monitoring
   */
  getStats() {
    return {
      processedMatches: this.processedMatches.size,
      services: {
        pumpPortal: 'initialized',
        treasury: 'initialized'
      },
      config: {
        payoutSplit: config.PAYOUT_SPLIT_PERCENT,
        gasReserve: config.GAS_RESERVE_SOL,
        roundPool: config.roundPoolSol
      }
    };
  }

  /**
   * Check if match has been processed
   */
  isMatchProcessed(matchId: string): boolean {
    return this.processedMatches.has(matchId);
  }

  /**
   * Get treasury balance for monitoring
   */
  async getTreasuryStatus() {
    try {
      const balance = await this.treasury.getTreasuryBalance();
      const address = this.treasury.getTreasuryAddress();
      
      return {
        address,
        balance,
        balanceFormatted: balance.toFixed(6) + ' SOL',
        canPayout: balance > config.GAS_RESERVE_SOL,
        estimatedPayouts: Math.floor((balance - config.GAS_RESERVE_SOL) / (config.roundPoolSol * config.PAYOUT_SPLIT_PERCENT / 10))
      };
    } catch (error) {
      return {
        error: error instanceof Error ? error.message : 'Failed to get treasury status'
      };
    }
  }

  /**
   * Emergency function to clear processed matches (for testing)
   */
  clearProcessedMatches(): number {
    const count = this.processedMatches.size;
    this.processedMatches.clear();
    console.log(`🧹 Cleared ${count} processed match records`);
    return count;
  }

  /**
   * Test the payout flow without real transactions
   */
  async testPayoutFlow(mockReport?: Partial<ArenaReportPayload>) {
    const testReport: ArenaReportPayload = {
      matchId: 'test-' + Date.now(),
      timestamp: Date.now(),
      winner: 'Red',
      red: Array.from({ length: 10 }, (_, i) => ({
        wallet: `TestRedWallet${i}${'x'.repeat(35)}`,
        kills: Math.floor(Math.random() * 5),
        alive: Math.random() > 0.3
      })),
      blue: Array.from({ length: 10 }, (_, i) => ({
        wallet: `TestBlueWallet${i}${'x'.repeat(34)}`,
        kills: Math.floor(Math.random() * 5),
        alive: Math.random() > 0.3
      })),
      roundRewardTotalSol: 1.0,
      payoutPercent: 0.2,
      ...mockReport
    };

    console.log('🧪 Testing payout flow with mock data...');
    
    try {
      // Just validate - don't actually process
      const validation = this.validateReport(testReport);
      const winners = this.extractWinnerWallets(testReport);
      
      return {
        success: true,
        validation,
        winners: {
          count: winners.length,
          team: testReport.winner,
          sampleWallet: winners[0]?.slice(0, 10) + '...'
        },
        processing: 'skipped (test mode)'
      };
      
    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Test failed'
      };
    }
  }
}

export default PayoutService;