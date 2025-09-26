// backend/src/routes/arena.ts - COMPLETE HYBRID VERSION (Original + Tiers)
import { Router, Request, Response } from 'express';
import { z } from 'zod';
import HoldersService from '../services/holders';
import PayoutService, { ArenaReportPayload } from '../services/payouts';
import HmacService from '../services/hmac';
import config from '../env';

const router = Router();

// Initialize services
const holdersService = new HoldersService();
const payoutService = new PayoutService();
const hmacService = new HmacService();

// Request schemas
const ReportSchema = z.object({
  matchId: z.string().min(1),
  timestamp: z.number().int().positive(),
  nonce: z.string().optional(),
  winner: z.enum(['Red', 'Blue', 'Tie']),
  red: z.array(z.object({
    wallet: z.string().min(32),
    kills: z.number().int().min(0),
    alive: z.boolean()
  })).length(10),
  blue: z.array(z.object({
    wallet: z.string().min(32),
    kills: z.number().int().min(0),
    alive: z.boolean()
  })).length(10),
  roundRewardTotalSol: z.number().positive(),
  payoutPercent: z.number().min(0).max(1),
  hmacKeyId: z.string().optional(),
  signature: z.string().optional()
});

/**
 * GET /api/arena/holders
 * Returns 20 random token holders for the next round
 * NOW WITH TIER SUPPORT + BACKWARD COMPATIBILITY
 */
router.get('/holders', async (req: Request, res: Response) => {
  try {
    console.log('📡 Received holders request from RimWorld mod');
    
    const result = await holdersService.getRandomHolders();
    console.log('🔍 ARENA DEBUG: result.fighters =', result.fighters?.length || 'undefined', 'fighters');
    console.log('🔍 ARENA DEBUG: result.stats.tierDistribution =', result.stats.tierDistribution || 'undefined');
    console.log('🔍 ARENA DEBUG: result keys =', Object.keys(result));
    
    // Enhanced logging with tier information
    console.log(`✅ Returning ${result.wallets.length} tiered fighters (${result.source})`);
    if (result.stats.tierDistribution && Object.keys(result.stats.tierDistribution).length > 0) {
      console.log('🏆 Tier breakdown:');
      Object.entries(result.stats.tierDistribution).forEach(([tierName, count]) => {
        console.log(`   ${count}x ${tierName}`);
      });
    }
    
    res.json({
      success: true,
      data: {
        // BACKWARD COMPATIBILITY: Original format
        wallets: result.wallets,
        roundRewardTotalSol: result.roundRewardTotalSol,
        payoutPercent: result.payoutPercent,
        
        // NEW: Tiered fighter data (optional for enhanced RimWorld mod)
        fighters: result.fighters || [],
        tierStats: result.fighters ? {
          distribution: result.stats.tierDistribution || {},
          totalSelected: result.stats.selected,
          averageTier: result.fighters.length > 0 ? 
            result.fighters.reduce((sum, f) => sum + f.tier.tier, 0) / result.fighters.length : 0
        } : undefined
      },
      meta: {
        source: result.source,
        stats: result.stats,
        timestamp: new Date().toISOString(),
        version: 'tiered-v1.0'
      }
    });

  } catch (error) {
    console.error('❌ Failed to get holders:', error);
    
    res.status(500).json({
      success: false,
      error: 'Failed to fetch token holders',
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

/**
 * POST /api/arena/report
 * Process arena results and execute payouts (ORIGINAL ADVANCED LOGIC PRESERVED)
 */
router.post('/report', async (req: Request, res: Response) => {
  try {
    console.log('📡 Received arena report from RimWorld mod');
    console.log('Request body keys:', Object.keys(req.body));
    
    // Validate request schema
    const validation = ReportSchema.safeParse(req.body);
    if (!validation.success) {
      console.warn('❌ Invalid report schema:', validation.error.format());
      return res.status(400).json({
        success: false,
        error: 'Invalid request format',
        details: validation.error.format()
      });
    }

    const report = validation.data as ArenaReportPayload;
    
    // HMAC validation (ORIGINAL LOGIC)
    const hmacValidation = hmacService.validateRequest(report);
    if (!hmacValidation.valid) {
      console.warn('❌ HMAC validation failed:', hmacValidation.error);
      return res.status(401).json({
        success: false,
        error: 'Authentication failed',
        message: hmacValidation.error
      });
    }

    console.log(`🎯 Processing match ${report.matchId} - Winner: ${report.winner} team`);

    // Check if already processed
    if (payoutService.isMatchProcessed(report.matchId)) {
      console.warn(`⚠️ Match ${report.matchId} already processed`);
      return res.status(409).json({
        success: false,
        error: 'Match already processed',
        matchId: report.matchId
      });
    }

    // Process the arena result (ORIGINAL ADVANCED PROCESSING)
    const result = await payoutService.processArenaResult(report);
    
    if (result.success) {
      console.log(`✅ Successfully processed ${report.matchId}`);
      console.log(`   Transactions: ${result.txids.length}`);
      console.log(`   Winner payouts: ${result.processing.payout.success ? 'completed' : 'failed'}`);
      
      res.json({
        success: true,
        data: {
          matchId: result.matchId,
          winner: result.winner,
          txids: result.txids,
          processing: {
            claimed: result.processing.claimed.success,
            split: result.processing.split.success,
            payout: result.processing.payout.success
          },
          payoutDetails: {
            totalPaid: result.processing.payout.totalPaid,
            perWinner: result.processing.payout.perWinner,
            successfulPayouts: (result.processing.payout.signatures?.length || 0),
            failedPayouts: (result.processing.payout.failedPayouts?.length || 0)
          }
        },
        meta: {
          timestamp: new Date().toISOString(),
          processingTimeMs: 'computed_by_service'
        }
      });
      
    } else {
      console.error(`❌ Failed to process ${report.matchId}:`, result.error);
      
      res.status(500).json({
        success: false,
        error: result.error || 'Processing failed',
        data: {
          matchId: result.matchId,
          winner: result.winner,
          txids: result.txids, // May have partial txids
          processing: {
            claimed: result.processing.claimed.success,
            split: result.processing.split.success,
            payout: result.processing.payout.success
          },
          errors: {
            claimed: result.processing.claimed.error,
            split: result.processing.split.error,
            payout: result.processing.payout.error
          }
        }
      });
    }

  } catch (error) {
    console.error('❌ Critical error in report endpoint:', error);
    
    res.status(500).json({
      success: false,
      error: 'Internal server error',
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

/**
 * GET /api/arena/status
 * Get current arena system status (ENHANCED WITH TIER INFO)
 */
router.get('/status', async (req: Request, res: Response) => {
  try {
    const [holderStats, payoutStats, treasuryStatus, hmacStatus] = await Promise.all([
      holdersService.getHolderStats(),
      payoutService.getStats(),
      payoutService.getTreasuryStatus(),
      Promise.resolve(hmacService.getStatus())
    ]);

    // Enhanced with tier information
    const refreshStatus = holdersService.getRefreshStatus();
    const tierStats = holderStats?.tiers || {};

    res.json({
      success: true,
      status: 'operational',
      services: {
        holders: holderStats ? 'healthy' : 'degraded',
        payouts: 'healthy',
        treasury: treasuryStatus.error ? 'error' : 'healthy',
        hmac: 'healthy',
        tiers: tierStats.totalHolders > 0 ? 'healthy' : 'degraded'
      },
      data: {
        holders: holderStats,
        payouts: payoutStats,
        treasury: treasuryStatus,
        security: hmacStatus,
        tiers: tierStats,
        backgroundRefresh: refreshStatus
      },
      config: {
        isDev: config.IS_DEV,
        tokenMint: config.TEST_TOKEN_MINT,
        payoutSplit: `${config.PAYOUT_SPLIT_PERCENT * 100}%`,
        roundPool: `${config.roundPoolSol} SOL`,
        gasReserve: `${config.GAS_RESERVE_SOL} SOL`,
        minHolderBalance: config.MIN_HOLDER_BALANCE.toLocaleString(),
        tierSystem: 'enabled'
      },
      timestamp: new Date().toISOString()
    });

  } catch (error) {
    console.error('Failed to get status:', error);
    
    res.status(500).json({
      success: false,
      error: 'Failed to get system status',
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

/**
 * GET /api/arena/tiers
 * Get detailed tier information and statistics
 */
router.get('/tiers', async (req: Request, res: Response) => {
  try {
    console.log('🏆 Tier information requested');
    
    const holderStats = await holdersService.getHolderStats();
    const refreshStatus = holdersService.getRefreshStatus();
    
    if (!holderStats || !holderStats.tiers) {
      return res.status(503).json({
        success: false,
        error: 'Tier data unavailable',
        message: 'Holder service not ready'
      });
    }
    
    res.json({
      success: true,
      data: {
        tierSystem: holderStats.tiers,
        refreshStatus: refreshStatus,
        summary: {
          totalHolders: holderStats.tiers.totalHolders,
          eligibleForArena: holderStats.blockchain.eligibleHolders,
          whales: Object.values(holderStats.tiers.tierBreakdown)
            .filter((tier: any) => tier.holders > 0 && (tier.name.includes('Warlord') || tier.name.includes('Destroyer')))
            .reduce((sum: number, tier: any) => sum + tier.holders, 0),
          lastUpdate: holderStats.lastUpdate
        }
      },
      timestamp: new Date().toISOString()
    });
    
  } catch (error) {
    console.error('❌ Failed to get tier info:', error);
    
    res.status(500).json({
      success: false,
      error: 'Failed to get tier information', 
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

/**
 * POST /api/arena/test
 * Test endpoints for development (ENHANCED WITH TIER TESTING)
 */
router.post('/test', async (req: Request, res: Response) => {
  if (!config.IS_DEV) {
    return res.status(403).json({
      success: false,
      error: 'Test endpoints only available in development mode'
    });
  }

  try {
    const { action } = req.body;
    
    switch (action) {
      case 'holders': {
        const testResults = await holdersService.testSelection(3);
        res.json({
          success: true,
          action: 'tiered_holders_test',
          results: testResults
        });
        break;
      }
      
      case 'tiers': {
        // Test tier distribution
        const holderStats = await holdersService.getHolderStats();
        res.json({
          success: true,
          action: 'tier_distribution_test',
          results: {
            tierBreakdown: holderStats?.tiers?.tierBreakdown || {},
            totalHolders: holderStats?.tiers?.totalHolders || 0,
            refreshStatus: holdersService.getRefreshStatus()
          }
        });
        break;
      }
      
      case 'payout': {
        const testResults = await payoutService.testPayoutFlow();
        res.json({
          success: true,
          action: 'payout_test',
          results: testResults
        });
        break;
      }
      
      case 'hmac': {
        const testResults = hmacService.test();
        res.json({
          success: true,
          action: 'hmac_test',
          results: testResults
        });
        break;
      }
      
      case 'refresh': {
        const refreshResult = await holdersService.refreshCache();
        res.json({
          success: true,
          action: 'refresh_cache',
          results: refreshResult
        });
        break;
      }
      
      case 'clear': {
        const cleared = payoutService.clearProcessedMatches();
        res.json({
          success: true,
          action: 'clear_processed',
          results: { cleared }
        });
        break;
      }
      
      default:
        res.status(400).json({
          success: false,
          error: 'Unknown test action',
          availableActions: ['holders', 'tiers', 'payout', 'hmac', 'refresh', 'clear']
        });
    }

  } catch (error) {
    console.error('Test endpoint error:', error);
    
    res.status(500).json({
      success: false,
      error: 'Test failed',
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

/**
 * GET /api/arena/config
 * Get current configuration for the mod (PRESERVED FROM ORIGINAL)
 */
router.get('/config', (req: Request, res: Response) => {
  res.json({
    success: true,
    config: {
      tokenMint: config.TEST_TOKEN_MINT,
      minHolderBalance: config.MIN_HOLDER_BALANCE,
      payoutPercent: config.PAYOUT_SPLIT_PERCENT,
      roundPoolSol: config.roundPoolSol,
      gasReserveSol: config.GAS_RESERVE_SOL,
      isDev: config.IS_DEV,
      hmacRequired: !config.IS_DEV,
      tierSystem: true,
      endpoints: {
        holders: '/api/arena/holders',
        report: '/api/arena/report',
        status: '/api/arena/status',
        tiers: '/api/arena/tiers',
        config: '/api/arena/config'
      }
    },
    timestamp: new Date().toISOString()
  });
});

export default router;