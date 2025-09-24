// backend/src/services/payouts.ts - DYNAMIC POOL VERSION (Complete 378+ lines)
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
  private roundHistory: Array<{
    matchId: string;
    timestamp: number;
    claimed: number;
    paid: number;
    perWinner: number;
  }>;

  constructor() {
    this.pumpPortal = new PumpPortalService();
    this.treasury = new TreasuryService();
    this.processedMatches = new Set();
    this.roundHistory = [];
    
    console.log('🎯 Dynamic Payout Service initialized');
    console.log('   ✅ Pool size scales with actual pump.fun claims');
    console.log('   ✅ No treasury funding required');
    console.log('   ✅ 100% sustainable operation');
  }

  /**
   * Process arena results with dynamic pool sizing
   */
  async processArenaResult(report: ArenaReportPayload): Promise<PayoutResult> {
    const startTime = Date.now();
    console.log(`💰 Processing arena result for match ${report.matchId}...`);
    console.log('🎯 Using DYNAMIC POOL sizing based on actual claims');
    
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
        console.warn(`⚠️ Match ${report.matchId} already processed`);
        result.error = 'Match already processed';
        return result;
      }

      // Validate report structure
      const validation = this.validateReport(report);
      if (!validation.valid) {
        console.error('❌ Invalid report structure:', validation.error);
        result.error = validation.error;
        return result;
      }

      // Get winner wallets
      const winnerWallets = this.extractWinnerWallets(report);
      if (winnerWallets.length === 0) {
        console.log('🤝 Tie game - no payouts needed');
        result.error = 'Tie game - no winners to pay';
        result.success = true; // This is actually successful
        this.processedMatches.add(report.matchId);
        return result;
      }

      console.log(`🏆 ${report.winner} team wins! Processing ${winnerWallets.length} winner payouts...`);

      // ========================================================================
      // PHASE 1: Claim creator fees from pump.fun
      // ========================================================================
      console.log('📥 Phase 1: Claiming creator fees from pump.fun...');
      const claimResult = await this.pumpPortal.claimCreatorFees();
      
      result.processing.claimed = {
        success: claimResult.success,
        amount: claimResult.claimedSol,
        signature: claimResult.signature,
        error: claimResult.error
      };

      let totalClaimedThisRound = 0;
      
      if (claimResult.success && claimResult.claimedSol > 0.001) {
        totalClaimedThisRound = claimResult.claimedSol;
        console.log(`✅ Claimed ${totalClaimedThisRound.toFixed(6)} SOL from pump.fun`);
        
        // Add claim signature to txids
        if (claimResult.signature) {
          result.txids.push(claimResult.signature);
        }
      } else {
        console.log('ℹ️ No creator fees available this round');
      }

      // ========================================================================
      // PHASE 2: Split claimed funds (80% dev, 20% treasury) 
      // ========================================================================
      let treasuryAdditionThisRound = 0;
      
      if (totalClaimedThisRound > 0.001) {
        console.log('🔄 Phase 2: Splitting claimed rewards...');
        
        const devAmount = totalClaimedThisRound * (1 - config.PAYOUT_SPLIT_PERCENT);
        treasuryAdditionThisRound = totalClaimedThisRound * config.PAYOUT_SPLIT_PERCENT;
        
        console.log(`   💼 Dev gets: ${devAmount.toFixed(6)} SOL (${((1 - config.PAYOUT_SPLIT_PERCENT) * 100).toFixed(0)}%)`);
        console.log(`   🏦 Treasury gets: ${treasuryAdditionThisRound.toFixed(6)} SOL (${(config.PAYOUT_SPLIT_PERCENT * 100).toFixed(0)}%)`);
        
        const splitResult = await this.treasury.splitRewards(totalClaimedThisRound);
        
        result.processing.split = {
          success: splitResult.success,
          devAmount: devAmount,
          treasuryAmount: treasuryAdditionThisRound,
          signature: splitResult.devTransferSignature,
          error: splitResult.error
        };

        if (!splitResult.success) {
          console.error('❌ Failed to split rewards:', splitResult.error);
          result.error = 'Failed to split claimed rewards: ' + splitResult.error;
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
          error: 'No new funds to split this round'
        };
      }

      // ========================================================================
      // PHASE 3: Dynamic Pool Calculation & Winner Payouts
      // ========================================================================
      console.log('🎯 Phase 3: Calculating dynamic pool and paying winners...');
      
      if (treasuryAdditionThisRound > 0.001) {
        // DYNAMIC POOL: Use exactly what was added to treasury this round
        const dynamicPoolSize = treasuryAdditionThisRound;
        const perWinnerAmount = dynamicPoolSize / winnerWallets.length;
        
        console.log(`💎 DYNAMIC POOL this round: ${dynamicPoolSize.toFixed(6)} SOL`);
        console.log(`🏆 Per winner payout: ${perWinnerAmount.toFixed(6)} SOL`);
        console.log(`📊 Winners: ${winnerWallets.length} wallets`);
        
        // Pay winners using the dynamic amount
        const payoutResult = await this.treasury.payWinnersDynamic(winnerWallets, perWinnerAmount);
        
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
        if (payoutResult.signatures) {
          result.txids.push(...payoutResult.signatures);
        }

        const successfulPayouts = winnerWallets.length - (payoutResult.failedPayouts?.length || 0);
        console.log(`✅ Paid ${payoutResult.totalPaid.toFixed(6)} SOL to ${successfulPayouts} winners`);
        console.log(`   Per winner: ${payoutResult.perWinner.toFixed(6)} SOL`);
        
        // Record this round in history
        this.roundHistory.push({
          matchId: report.matchId,
          timestamp: Date.now(),
          claimed: totalClaimedThisRound,
          paid: payoutResult.totalPaid,
          perWinner: payoutResult.perWinner
        });
        
        // Keep only last 100 rounds
        if (this.roundHistory.length > 100) {
          this.roundHistory = this.roundHistory.slice(-100);
        }
        
      } else {
        // No fees claimed this round = no payouts
        console.log('💫 No creator fees claimed this round → No payouts this round');
        console.log('   🎮 Arena was still fun! Better luck next round!');
        
        result.processing.payout = {
          success: true,
          totalPaid: 0,
          perWinner: 0,
          signatures: [],
          failedPayouts: [],
          error: 'No fees available - no payouts this round (but arena was successful!)'
        };
        
        // Still record the round
        this.roundHistory.push({
          matchId: report.matchId,
          timestamp: Date.now(),
          claimed: 0,
          paid: 0,
          perWinner: 0
        });
      }

      // Mark as successfully processed
      this.processedMatches.add(report.matchId);
      result.success = true;

      const processingTime = Date.now() - startTime;
      console.log(`🎯 Dynamic pool arena complete for ${report.matchId} in ${processingTime}ms`);
      console.log(`   Total transactions: ${result.txids.length}`);
      console.log(`   System status: 100% sustainable! 🌱`);

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
    
    // Return all wallets from winning team (dead or alive get paid)
    return winningTeam.map(fighter => fighter.wallet);
  }

  /**
   * Get processing statistics for monitoring
   */
  getStats() {
    const recentRounds = this.roundHistory.slice(-20); // Last 20 rounds
    const totalClaimed = recentRounds.reduce((sum, r) => sum + r.claimed, 0);
    const totalPaid = recentRounds.reduce((sum, r) => sum + r.paid, 0);
    const avgPerWinner = recentRounds.length > 0 
      ? recentRounds.reduce((sum, r) => sum + r.perWinner, 0) / recentRounds.length 
      : 0;
    
    return {
      processedMatches: this.processedMatches.size,
      recentRounds: recentRounds.length,
      totalClaimedRecent: totalClaimed,
      totalPaidRecent: totalPaid,
      avgPerWinnerRecent: avgPerWinner,
      systemType: 'Dynamic Pool (Sustainable)',
      treasuryFundingRequired: false,
      services: {
        pumpPortal: 'initialized',
        treasury: 'initialized'
      },
      config: {
        payoutSplit: config.PAYOUT_SPLIT_PERCENT,
        gasReserve: config.GAS_RESERVE_SOL,
        dynamicScaling: true
      }
    };
  }

  /**
   * Get recent round history for analysis
   */
  getRoundHistory(limit: number = 10) {
    return this.roundHistory.slice(-limit).map(round => ({
      ...round,
      claimedFormatted: round.claimed.toFixed(6) + ' SOL',
      paidFormatted: round.paid.toFixed(6) + ' SOL',
      perWinnerFormatted: round.perWinner.toFixed(6) + ' SOL'
    }));
  }

  /**
   * Check if match has been processed
   */
  isMatchProcessed(matchId: string): boolean {
    return this.processedMatches.has(matchId);
  }

  /**
   * Get treasury status for monitoring
   */
  async getTreasuryStatus() {
    try {
      const balance = await this.treasury.getTreasuryBalance();
      const address = this.treasury.getTreasuryAddress();
      
      // With dynamic sizing, we don't need a minimum balance
      return {
        address,
        balance,
        balanceFormatted: balance.toFixed(6) + ' SOL',
        systemType: 'Dynamic Pool',
        fundingRequired: false,
        sustainable: true,
        estimatedRoundsWithCurrentBalance: 'Unlimited (scales with claims)',
        recentActivity: this.getRoundHistory(5)
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
      roundRewardTotalSol: 0, // Dynamic sizing - this gets calculated
      payoutPercent: config.PAYOUT_SPLIT_PERCENT,
      ...mockReport
    };

    console.log('🧪 Testing dynamic payout flow with mock data...');
    
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
        systemType: 'Dynamic Pool Sizing',
        processing: 'skipped (test mode)',
        note: 'Pool size will scale with actual pump.fun claims'
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