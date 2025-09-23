// backend/src/routes/arena.ts
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
 */
router.get('/holders', async (req: Request, res: Response) => {
  try {
    console.log('📡 Received holders request from RimWorld mod');
    
    const result = await holdersService.getRandomHolders();
    
    // Log for monitoring
    console.log(`✅ Returning ${result.wallets.length} holders (${result.source})`);
    
    res.json({
      success: true,
      data: {
        wallets: result.wallets,
        roundRewardTotalSol: result.roundRewardTotalSol,
        payoutPercent: result.payoutPercent
      },
      meta: {
        source: result.source,
        stats: result.stats,
        timestamp: new Date().toISOString()
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
 * Process arena results and execute payouts
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
    
    // HMAC validation
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

    // Process the arena result
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
 * Get current arena system status
 */
router.get('/status', async (req: Request, res: Response) => {
  try {
    const [holderStats, payoutStats, treasuryStatus, hmacStatus] = await Promise.all([
      holdersService.getHolderStats(),
      payoutService.getStats(),
      payoutService.getTreasuryStatus(),
      Promise.resolve(hmacService.getStatus())
    ]);

    res.json({
      success: true,
      status: 'operational',
      services: {
        holders: holderStats ? 'healthy' : 'degraded',
        payouts: 'healthy',
        treasury: treasuryStatus.error ? 'error' : 'healthy',
        hmac: 'healthy'
      },
      data: {
        holders: holderStats,
        payouts: payoutStats,
        treasury: treasuryStatus,
        security: hmacStatus
      },
      config: {
        isDev: config.IS_DEV,
        tokenMint: config.TEST_TOKEN_MINT,
        payoutSplit: `${config.PAYOUT_SPLIT_PERCENT * 100}%`,
        roundPool: `${config.roundPoolSol} SOL`,
        gasReserve: `${config.GAS_RESERVE_SOL} SOL`
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
 * POST /api/arena/test
 * Test endpoints for development
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
          action: 'holders_test',
          results: testResults
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
          availableActions: ['holders', 'payout', 'hmac', 'refresh', 'clear']
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
 * Get current configuration for the mod
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
      endpoints: {
        holders: '/api/arena/holders',
        report: '/api/arena/report',
        status: '/api/arena/status'
      }
    },
    timestamp: new Date().toISOString()
  });
});

export default router;