// backend/src/env.ts
import dotenv from 'dotenv';

// Load environment variables from .env file
dotenv.config();

// Configuration object with all environment variables
export const config = {
  // Server Configuration
  PORT: parseInt(process.env.PORT || '4001'),
  NODE_ENV: process.env.NODE_ENV || 'development',
  
  // Solana Blockchain Configuration
  RPC_ENDPOINT: process.env.RPC_ENDPOINT || 'https://api.mainnet-beta.solana.com',
  TEST_TOKEN_MINT: process.env.TEST_TOKEN_MINT || '31mtqJVnyfN98d1Qie3ijsRLsLq11S5VPVtKKvzypump',
  MIN_HOLDER_BALANCE: parseFloat(process.env.MIN_HOLDER_BALANCE || '50000'),
  
  // Wallet Configuration  
  CREATOR_WALLET_PRIVATE_KEY: process.env.CREATOR_WALLET_PRIVATE_KEY || '',
  TREASURY_WALLET_PRIVATE_KEY: process.env.TREASURY_WALLET_PRIVATE_KEY || '',
  DEV_WALLET_ADDRESS: process.env.DEV_WALLET_ADDRESS || 'CediUaTLvgBpw7Z875LJir4P24WjCVf2auxoSg29zCT9',
  
  // Payout Configuration
  PAYOUT_SPLIT_PERCENT: parseFloat(process.env.PAYOUT_SPLIT_PERCENT || '0.20'),
  GAS_RESERVE_SOL: parseFloat(process.env.GAS_RESERVE_SOL || '0.015'),
  roundPoolSol: parseFloat(process.env.roundPoolSol || '1.0'),
  
  // Security Configuration
  HMAC_KEYS: JSON.parse(process.env.HMAC_KEYS || '{"="}') as Record<string, string>,
  
  // PumpPortal API Configuration
  PUMPPORTAL_BASE_URL: process.env.PUMPPORTAL_BASE_URL || 'https://pumpportal.fun/api/v1',
  
  // Computed Properties
  get IS_DEV(): boolean {
    return this.NODE_ENV === 'development';
  },
  
  get IS_PRODUCTION(): boolean {
    return this.NODE_ENV === 'production';
  }
};

// Configuration validation
function validateConfig() {
  const errors: string[] = [];
  
  // Validate payout split percentage
  if (config.PAYOUT_SPLIT_PERCENT <= 0 || config.PAYOUT_SPLIT_PERCENT >= 1) {
    errors.push('PAYOUT_SPLIT_PERCENT must be between 0 and 1');
  }
  
  // Validate round pool size
  if (config.roundPoolSol <= 0) {
    errors.push('roundPoolSol must be positive');
  }
  
  // Validate gas reserve
  if (config.GAS_RESERVE_SOL <= 0) {
    errors.push('GAS_RESERVE_SOL must be positive');
  }
  
  // Validate minimum holder balance
  if (config.MIN_HOLDER_BALANCE <= 0) {
    errors.push('MIN_HOLDER_BALANCE must be positive');
  }
  
  // Validate port
  if (config.PORT <= 0 || config.PORT > 65535) {
    errors.push('PORT must be between 1 and 65535');
  }
  
  // Check for required fields in production
  if (config.IS_PRODUCTION) {
    if (!process.env.CREATOR_WALLET_PRIVATE_KEY) {
      errors.push('CREATOR_WALLET_PRIVATE_KEY is required in production');
    }
    
    if (!process.env.TREASURY_WALLET_PRIVATE_KEY) {
      errors.push('TREASURY_WALLET_PRIVATE_KEY is required in production');
    }
    
    if (!process.env.DEV_WALLET_ADDRESS) {
      errors.push('DEV_WALLET_ADDRESS is required in production');
    }
  }
  
  if (errors.length > 0) {
    console.error('❌ Configuration validation failed:');
    errors.forEach(error => console.error(`   • ${error}`));
    throw new Error('Invalid configuration');
  }
}

// Run validation
try {
  validateConfig();
  console.log('✅ Configuration validation passed');
} catch (error) {
  console.error('💥 Configuration validation failed:', error);
  process.exit(1);
}

// Log configuration summary
console.log('🔧 SolWorld Backend Configuration:');
console.log('=' .repeat(50));
console.log(`Environment: ${config.NODE_ENV}`);
console.log(`Port: ${config.PORT}`);
console.log(`RPC Endpoint: ${config.RPC_ENDPOINT}`);
console.log(`Token Mint: ${config.TEST_TOKEN_MINT.slice(0, 8)}...`);
console.log(`Payout Split: ${(config.PAYOUT_SPLIT_PERCENT * 100).toFixed(1)}% to treasury`);
console.log(`Round Pool: ${config.roundPoolSol} SOL`);
console.log(`Gas Reserve: ${config.GAS_RESERVE_SOL} SOL`);
console.log(`Min Holder Balance: ${config.MIN_HOLDER_BALANCE} tokens`);
console.log(`Creator Wallet: ${config.CREATOR_WALLET_PRIVATE_KEY.length > 0 ? '✅ Configured' : '❌ Missing'}`);
console.log(`Treasury Wallet: ${config.TREASURY_WALLET_PRIVATE_KEY.length > 0 ? '✅ Configured' : '❌ Missing'}`);
console.log(`Dev Wallet: ${config.DEV_WALLET_ADDRESS}`);
console.log(`HMAC Keys: ${Object.keys(config.HMAC_KEYS).length} configured`);
console.log(`PumpPortal: ${config.PUMPPORTAL_BASE_URL}`);
console.log('=' .repeat(50));

// Export as default for easy importing
export default config;