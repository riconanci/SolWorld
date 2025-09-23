import dotenv from 'dotenv';
import { z } from 'zod';

dotenv.config();

const envSchema = z.object({
  PORT: z.string().default('4000'),
  
  // Solana Configuration
  RPC_ENDPOINT: z.string().url(),
  
  // Pump.fun Integration
  CREATOR_WALLET_PRIVATE_KEY: z.string(), // Base58 encoded private key
  TREASURY_WALLET_PRIVATE_KEY: z.string(), // Base58 encoded private key  
  DEV_WALLET_ADDRESS: z.string(), // Where 80% goes
  TEST_TOKEN_MINT: z.string().default('31JG1RZmcZCRSe3pmX5P18jmCLGBBUyWbV2NuZK7pump'),
  
  // Payout Configuration
  PAYOUT_SPLIT_PERCENT: z.string().default('0.20'), // 20% to treasury
  GAS_RESERVE_SOL: z.string().default('0.015'), // Reserve for transaction fees
  MIN_HOLDER_BALANCE: z.string().default('1'), // Minimum token balance for eligibility
  
  // Security
  HMAC_KEYS: z.string().default('{"default":"supersecret"}'),
  
  // PumpPortal API
  PUMPPORTAL_BASE_URL: z.string().default('https://pumpportal.fun/api/v1'),
  
  // Development
  NODE_ENV: z.string().default('development'),
});

const env = envSchema.parse(process.env);

// Parse JSON fields
export const config = {
  ...env,
  PORT: parseInt(env.PORT),
  PAYOUT_SPLIT_PERCENT: parseFloat(env.PAYOUT_SPLIT_PERCENT),
  GAS_RESERVE_SOL: parseFloat(env.GAS_RESERVE_SOL),
  MIN_HOLDER_BALANCE: parseFloat(env.MIN_HOLDER_BALANCE),
  HMAC_KEYS: JSON.parse(env.HMAC_KEYS) as Record<string, string>,
  IS_DEV: env.NODE_ENV === 'development',
};

// Validation
if (config.PAYOUT_SPLIT_PERCENT <= 0 || config.PAYOUT_SPLIT_PERCENT >= 1) {
  throw new Error('PAYOUT_SPLIT_PERCENT must be between 0 and 1');
}

if (config.GAS_RESERVE_SOL <= 0) {
  throw new Error('GAS_RESERVE_SOL must be positive');
}

console.log('Environment loaded:', {
  NODE_ENV: config.NODE_ENV,
  PAYOUT_SPLIT: `${config.PAYOUT_SPLIT_PERCENT * 100}%`,
  GAS_RESERVE: `${config.GAS_RESERVE_SOL} SOL`,
  TEST_TOKEN: config.TEST_TOKEN_MINT.slice(0, 8) + '...',
});

export default config;