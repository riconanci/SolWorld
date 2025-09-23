import dotenv from 'dotenv';

// Load environment variables
dotenv.config();

// Basic configuration with defaults
export const config = {
  PORT: parseInt(process.env.PORT || '4000'),
  NODE_ENV: process.env.NODE_ENV || 'development',
  
  // Solana Configuration
  RPC_ENDPOINT: process.env.RPC_ENDPOINT || 'https://api.mainnet-beta.solana.com',
  
  // Wallet Configuration
  CREATOR_WALLET_PRIVATE_KEY: process.env.CREATOR_WALLET_PRIVATE_KEY || '66BiAXXY1uNMgiiFVjQ9mAnGTKT2WrzjJy8YpqhnupmUWaXeHPAZG2zz3n3WHUa2FtsJpVj1xU7TA4bk7EAJEXEQ',
  TREASURY_WALLET_PRIVATE_KEY: process.env.TREASURY_WALLET_PRIVATE_KEY || 'dDSWrD6WkhJnDQqoFSQYeLCneaHAkYP3YAYF214UGTf6nZBb4QMvFVBg2s4TfwhfZXNRD8KLWuHTXPrZ3XNUQwh',
  DEV_WALLET_ADDRESS: process.env.DEV_WALLET_ADDRESS || 'FQCdaGW2BEDuVoS4aCESsr4bPVQLrushug6iHcdwpoSY',
  
  // Test Token
  TEST_TOKEN_MINT: process.env.TEST_TOKEN_MINT || '31JG1RZmcZCRSe3pmX5P18jmCLGBBUyWbV2NuZK7pump',
  
  // Payout Configuration
  PAYOUT_SPLIT_PERCENT: parseFloat(process.env.PAYOUT_SPLIT_PERCENT || '0.20'),
  GAS_RESERVE_SOL: parseFloat(process.env.GAS_RESERVE_SOL || '0.015'),
  MIN_HOLDER_BALANCE: parseFloat(process.env.MIN_HOLDER_BALANCE || '1'),
  
  // Security
  HMAC_KEYS: JSON.parse(process.env.HMAC_KEYS || '{"default":"supersecret"}') as Record<string, string>,
  
  // PumpPortal
  PUMPPORTAL_BASE_URL: process.env.PUMPPORTAL_BASE_URL || 'https://pumpportal.fun/api/v1',
  
  // Computed
  get IS_DEV() { return this.NODE_ENV === 'development'; }
};

// Validation
if (config.PAYOUT_SPLIT_PERCENT <= 0 || config.PAYOUT_SPLIT_PERCENT >= 1) {
  console.warn('Warning: PAYOUT_SPLIT_PERCENT should be between 0 and 1');
}

console.log('Environment loaded:', {
  NODE_ENV: config.NODE_ENV,
  PORT: config.PORT,
  PAYOUT_SPLIT: `${config.PAYOUT_SPLIT_PERCENT * 100}%`,
  GAS_RESERVE: `${config.GAS_RESERVE_SOL} SOL`,
  TEST_TOKEN: config.TEST_TOKEN_MINT.slice(0, 8) + '...',
  HAS_CREATOR_KEY: config.CREATOR_WALLET_PRIVATE_KEY.length > 0,
  HAS_TREASURY_KEY: config.TREASURY_WALLET_PRIVATE_KEY.length > 0,
});

export default config;