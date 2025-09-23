import express from 'express';
import cors from 'cors';
import config from './env.js';

const app = express();

// Middleware
app.use(cors());
app.use(express.json());

// Basic health check
app.get('/health', (req, res) => {
  res.json({ 
    status: 'healthy', 
    service: 'SolWorld Backend',
    environment: config.NODE_ENV,
    timestamp: new Date().toISOString(),
    walletConfigured: config.CREATOR_WALLET_PRIVATE_KEY.length > 0
  });
});

// Test endpoint
app.get('/api/test', (req, res) => {
  res.json({
    message: 'SolWorld Backend is running!',
    config: {
      payoutSplit: `${config.PAYOUT_SPLIT_PERCENT * 100}%`,
      gasReserve: `${config.GAS_RESERVE_SOL} SOL`,
      testToken: config.TEST_TOKEN_MINT,
      isDev: config.IS_DEV,
      hasWalletKeys: {
        creator: config.CREATOR_WALLET_PRIVATE_KEY.length > 0,
        treasury: config.TREASURY_WALLET_PRIVATE_KEY.length > 0
      }
    }
  });
});

// Wallet status endpoint
app.get('/api/wallet-status', (req, res) => {
  const hasCreator = config.CREATOR_WALLET_PRIVATE_KEY.length > 0;
  const hasTreasury = config.TREASURY_WALLET_PRIVATE_KEY.length > 0;
  
  res.json({
    configured: hasCreator && hasTreasury,
    creator: hasCreator,
    treasury: hasTreasury,
    devWallet: config.DEV_WALLET_ADDRESS,
    message: hasCreator && hasTreasury ? 
      'All wallets configured!' : 
      'Need to configure wallet private keys'
  });
});

// Start server
app.listen(config.PORT, () => {
  console.log(`🚀 SolWorld Backend running on http://localhost:${config.PORT}`);
  console.log(`📊 Environment: ${config.NODE_ENV}`);
  console.log(`💰 Payout Split: ${config.PAYOUT_SPLIT_PERCENT * 100}% to treasury`);
  console.log(`⛽ Gas Reserve: ${config.GAS_RESERVE_SOL} SOL`);
  console.log(`🪙 Test Token: ${config.TEST_TOKEN_MINT}`);
  
  const hasKeys = config.CREATOR_WALLET_PRIVATE_KEY.length > 0 && config.TREASURY_WALLET_PRIVATE_KEY.length > 0;
  if (!hasKeys) {
    console.log('⚠️  No wallet keys configured yet. Generate them with:');
    console.log('   node scripts/generate-treasury-wallet.js');
  } else {
    console.log('✅ Wallet keys configured');
  }
});

export default app;