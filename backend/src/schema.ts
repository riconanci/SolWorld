// backend/src/schema.ts
import { z } from 'zod';

// =============================================================================
// FIGHTER SCHEMA
// =============================================================================

export const FighterSchema = z.object({
  wallet: z.string().min(32).max(50), // Solana addresses are typically 44 chars
  kills: z.number().int().min(0).max(20), // Reasonable kill limit
  alive: z.boolean()
});

export type Fighter = z.infer<typeof FighterSchema>;

// =============================================================================
// ARENA REPORT SCHEMA (from RimWorld mod)
// =============================================================================

export const ArenaReportSchema = z.object({
  matchId: z.string().min(1).max(100),
  timestamp: z.number().int().positive(),
  nonce: z.string().optional(),
  winner: z.enum(['Red', 'Blue', 'Tie']),
  red: z.array(FighterSchema).length(10),
  blue: z.array(FighterSchema).length(10),
  roundRewardTotalSol: z.number().positive().max(1000), // Max 1000 SOL per round
  payoutPercent: z.number().min(0.01).max(0.99), // 1% to 99%
  hmacKeyId: z.string().optional(),
  signature: z.string().optional()
}).refine(data => {
  // Ensure all wallets are unique
  const allWallets = [...data.red, ...data.blue].map(f => f.wallet);
  const uniqueWallets = new Set(allWallets);
  return uniqueWallets.size === 20;
}, {
  message: "All fighter wallets must be unique"
}).refine(data => {
  // Ensure timestamp is within reasonable range (last 24 hours to 5 minutes future)
  const now = Date.now();
  const age = now - data.timestamp;
  return age <= (24 * 60 * 60 * 1000) && age >= (-5 * 60 * 1000);
}, {
  message: "Timestamp must be within last 24 hours to 5 minutes in future"
});

export type ArenaReport = z.infer<typeof ArenaReportSchema>;

// =============================================================================
// HOLDERS RESPONSE SCHEMA (to RimWorld mod)
// =============================================================================

export const HoldersResponseSchema = z.object({
  success: z.boolean(),
  data: z.object({
    wallets: z.array(z.string()).length(20),
    roundRewardTotalSol: z.number().positive(),
    payoutPercent: z.number().min(0).max(1)
  }),
  meta: z.object({
    source: z.enum(['blockchain', 'mock', 'mixed']),
    stats: z.object({
      totalAvailable: z.number().int().min(0),
      selected: z.number().int().min(0),
      mockUsed: z.number().int().min(0)
    }),
    timestamp: z.string()
  })
});

export type HoldersResponse = z.infer<typeof HoldersResponseSchema>;

// =============================================================================
// PAYOUT RESULT SCHEMA (to RimWorld mod)
// =============================================================================

export const PayoutResultSchema = z.object({
  success: z.boolean(),
  data: z.object({
    matchId: z.string(),
    winner: z.enum(['Red', 'Blue', 'Tie']),
    txids: z.array(z.string()),
    processing: z.object({
      claimed: z.boolean(),
      split: z.boolean(),
      payout: z.boolean()
    }),
    payoutDetails: z.object({
      totalPaid: z.number().min(0),
      perWinner: z.number().min(0),
      successfulPayouts: z.number().int().min(0),
      failedPayouts: z.number().int().min(0)
    })
  }).optional(),
  error: z.string().optional(),
  meta: z.object({
    timestamp: z.string(),
    processingTimeMs: z.union([z.number(), z.string()])
  }).optional()
});

export type PayoutResult = z.infer<typeof PayoutResultSchema>;

// =============================================================================
// STATUS RESPONSE SCHEMA
// =============================================================================

export const ServiceStatusSchema = z.object({
  holders: z.union([z.literal('healthy'), z.literal('degraded'), z.literal('error')]),
  payouts: z.union([z.literal('healthy'), z.literal('degraded'), z.literal('error')]),
  treasury: z.union([z.literal('healthy'), z.literal('degraded'), z.literal('error')]),
  hmac: z.union([z.literal('healthy'), z.literal('degraded'), z.literal('error')])
});

export const StatusResponseSchema = z.object({
  success: z.boolean(),
  status: z.enum(['operational', 'degraded', 'error']),
  services: ServiceStatusSchema,
  data: z.object({
    holders: z.any().optional(), // Complex holder stats
    payouts: z.any().optional(), // Payout service stats
    treasury: z.any().optional(), // Treasury status
    security: z.any().optional()  // HMAC status
  }),
  config: z.object({
    isDev: z.boolean(),
    tokenMint: z.string(),
    payoutSplit: z.string(),
    roundPool: z.string(),
    gasReserve: z.string()
  }),
  timestamp: z.string()
});

export type StatusResponse = z.infer<typeof StatusResponseSchema>;

// =============================================================================
// TEST REQUEST SCHEMA
// =============================================================================

export const TestRequestSchema = z.object({
  action: z.enum(['holders', 'payout', 'hmac', 'refresh', 'clear'])
});

export type TestRequest = z.infer<typeof TestRequestSchema>;

// =============================================================================
// CONFIGURATION SCHEMA
// =============================================================================

export const ConfigResponseSchema = z.object({
  success: z.boolean(),
  config: z.object({
    tokenMint: z.string(),
    minHolderBalance: z.number(),
    payoutPercent: z.number(),
    roundPoolSol: z.number(),
    gasReserveSol: z.number(),
    isDev: z.boolean(),
    hmacRequired: z.boolean(),
    endpoints: z.object({
      holders: z.string(),
      report: z.string(),
      status: z.string()
    })
  }),
  timestamp: z.string()
});

export type ConfigResponse = z.infer<typeof ConfigResponseSchema>;

// =============================================================================
// ERROR RESPONSE SCHEMA
// =============================================================================

export const ErrorResponseSchema = z.object({
  success: z.literal(false),
  error: z.string(),
  message: z.string().optional(),
  details: z.any().optional(),
  code: z.string().optional(),
  timestamp: z.string().optional()
});

export type ErrorResponse = z.infer<typeof ErrorResponseSchema>;

// =============================================================================
// VALIDATION HELPERS
// =============================================================================

/**
 * Validate Solana address format
 */
export const validateSolanaAddress = (address: string): boolean => {
  try {
    // Basic validation - Solana addresses are base58 and typically 44 characters
    if (address.length < 32 || address.length > 50) return false;
    
    // Check for valid base58 characters
    const base58Regex = /^[1-9A-HJ-NP-Za-km-z]+$/;
    return base58Regex.test(address);
  } catch {
    return false;
  }
};

/**
 * Sanitize match ID
 */
export const sanitizeMatchId = (matchId: string): string => {
  return matchId.replace(/[^a-zA-Z0-9\-_]/g, '').substring(0, 100);
};

/**
 * Validate timestamp within reasonable bounds
 */
export const validateTimestamp = (timestamp: number): boolean => {
  const now = Date.now();
  const age = now - timestamp;
  
  // Allow timestamps from 24 hours ago to 5 minutes in future
  return age <= (24 * 60 * 60 * 1000) && age >= (-5 * 60 * 1000);
};

/**
 * Create standardized error response
 */
export const createErrorResponse = (
  error: string, 
  message?: string, 
  details?: any,
  code?: string
): ErrorResponse => {
  return {
    success: false,
    error,
    message,
    details,
    code,
    timestamp: new Date().toISOString()
  };
};

/**
 * Create standardized success response
 */
export const createSuccessResponse = <T>(data: T, meta?: any) => {
  return {
    success: true,
    data,
    meta: {
      timestamp: new Date().toISOString(),
      ...meta
    }
  };
};

// =============================================================================
// MOCK DATA GENERATORS (for testing)
// =============================================================================

/**
 * Generate mock fighter data
 */
export const generateMockFighter = (wallet?: string): Fighter => {
  return {
    wallet: wallet || generateMockWallet(),
    kills: Math.floor(Math.random() * 5),
    alive: Math.random() > 0.3
  };
};

/**
 * Generate mock wallet address
 */
export const generateMockWallet = (): string => {
  const base58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
  let address = '';
  
  for (let i = 0; i < 44; i++) {
    address += base58[Math.floor(Math.random() * base58.length)];
  }
  
  return address;
};

/**
 * Generate mock arena report
 */
export const generateMockArenaReport = (overrides?: Partial<ArenaReport>): ArenaReport => {
  const redTeam = Array.from({ length: 10 }, () => generateMockFighter());
  const blueTeam = Array.from({ length: 10 }, () => generateMockFighter());
  
  // Ensure unique wallets
  const allWallets = new Set();
  [...redTeam, ...blueTeam].forEach(fighter => {
    while (allWallets.has(fighter.wallet)) {
      fighter.wallet = generateMockWallet();
    }
    allWallets.add(fighter.wallet);
  });
  
  const winners = ['Red', 'Blue', 'Tie'] as const;
  const selectedWinner = winners[Math.floor(Math.random() * winners.length)];
  
  const baseReport: ArenaReport = {
    matchId: `mock-${Date.now()}-${Math.floor(Math.random() * 10000)}`,
    timestamp: Date.now(),
    winner: selectedWinner,
    red: redTeam,
    blue: blueTeam,
    roundRewardTotalSol: 1.0,
    payoutPercent: 0.20
  };
  
  return {
    ...baseReport,
    ...overrides
  };
};

// =============================================================================
// SCHEMA VALIDATION MIDDLEWARE HELPER
// =============================================================================

/**
 * Express middleware for schema validation
 */
export const validateSchema = <T>(schema: z.ZodSchema<T>) => {
  return (req: any, res: any, next: any) => {
    try {
      const validation = schema.safeParse(req.body);
      
      if (!validation.success) {
        return res.status(400).json(createErrorResponse(
          'Invalid request format',
          'Schema validation failed',
          validation.error.format(),
          'SCHEMA_VALIDATION_ERROR'
        ));
      }
      
      req.validatedBody = validation.data;
      next();
    } catch (error) {
      return res.status(500).json(createErrorResponse(
        'Validation error',
        error instanceof Error ? error.message : 'Unknown validation error',
        undefined,
        'VALIDATION_ERROR'
      ));
    }
  };
};

export default {
  ArenaReportSchema,
  HoldersResponseSchema,
  PayoutResultSchema,
  StatusResponseSchema,
  TestRequestSchema,
  ConfigResponseSchema,
  ErrorResponseSchema,
  validateSolanaAddress,
  sanitizeMatchId,
  validateTimestamp,
  createErrorResponse,
  createSuccessResponse,
  generateMockArenaReport,
  validateSchema
};