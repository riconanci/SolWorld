// backend/src/index.ts
import express from 'express';
import cors from 'cors';
import config from './env';
import arenaRoutes from './routes/arena';
import SolanaService from './services/solana';
import HoldersService from './services/holders';
import PayoutService from './services/payouts';
import HmacService from './services/hmac';

const app = express();

// Middleware
app.use(cors({
  origin: config.IS_DEV ? ['http://localhost:3000', 'http://127.0.0.1:3000'] : false,
  credentials: true
}));
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true }));

// DEBUG: Body parsing verification
app.use((req, res, next) => {
  if (req.path.includes('/arena/report')) {
    console.log('📦 Raw request body type:', typeof req.body);
    console.log('📦 Body keys:', Object.keys(req.body));
    console.log('📦 Blue array length:', req.body.blue?.length);
    console.log('📦 First blue fighter:', JSON.stringify(req.body.blue?.[0]));
    console.log('📦 Red array length:', req.body.red?.length);
    console.log('📦 First red fighter:', JSON.stringify(req.body.red?.[0]));
  }
  next();
});

// Request logging middleware
app.use((req, res, next) => {
  const timestamp = new Date().toISOString();
  console.log(`[${timestamp}] ${req.method} ${req.path} - ${req.ip}`);
  next();
});

// Initialize services on startup
let servicesInitialized = false;
let serviceStatus = {
  solana: false,
  holders: false,
  payouts: false,
  hmac: false
};

async function initializeServices() {
  try {
    console.log('🚀 Initializing SolWorld services...');
    
    // Test Solana connection
    const solanaService = new SolanaService();
    const connectionHealthy = await solanaService.checkConnection();
    serviceStatus.solana = connectionHealthy;
    
    if (connectionHealthy) {
      console.log('✅ Solana connection healthy');
      
      // Get current slot for verification
      const slot = await solanaService.getCurrentSlot();
      console.log(`   Current slot: ${slot}`);
      
      // Get token stats
      const stats = await solanaService.getTokenStats();
      console.log(`   Token holders: ${stats.totalHolders} total, ${stats.eligibleHolders} eligible`);
    } else {
      console.warn('⚠️  Solana connection issues - will use mock data');
    }
    
    // Test other services
    const holdersService = new HoldersService();
    const payoutService = new PayoutService();
    const hmacService = new HmacService();
    
    // Test holders service
    try {
      const holderStats = await holdersService.getHolderStats();
      serviceStatus.holders = holderStats !== null;
      console.log('✅ Holders service initialized');
    } catch (error) {
      console.warn('⚠️  Holders service degraded:', error);
      serviceStatus.holders = false;
    }
    
    // Test payout service
    try {
      const payoutStats = payoutService.getStats();
      serviceStatus.payouts = true;
      console.log('✅ Payout service initialized');
      console.log(`   Processed matches: ${payoutStats.processedMatches}`);
    } catch (error) {
      console.warn('⚠️  Payout service issues:', error);
      serviceStatus.payouts = false;
    }
    
    // Test HMAC service
    try {
      const hmacTest = hmacService.test();
      serviceStatus.hmac = hmacTest.success;
      console.log('✅ HMAC service initialized');
      if (!hmacTest.success) {
        console.warn('   HMAC test failed:', hmacTest.details);
      }
    } catch (error) {
      console.warn('⚠️  HMAC service issues:', error);
      serviceStatus.hmac = false;
    }
    
    servicesInitialized = true;
    console.log('🎯 All services initialized');
    
  } catch (error) {
    console.error('❌ Service initialization failed:', error);
    servicesInitialized = false;
  }
}

// Health check endpoint
app.get('/health', (req, res) => {
  const allHealthy = Object.values(serviceStatus).every(status => status);
  
  res.status(allHealthy ? 200 : 503).json({ 
    status: allHealthy ? 'healthy' : 'degraded',
    service: 'SolWorld Backend',
    environment: config.NODE_ENV,
    timestamp: new Date().toISOString(),
    services: serviceStatus,
    initialized: servicesInitialized,
    config: {
      walletConfigured: config.CREATOR_WALLET_PRIVATE_KEY.length > 0,
      treasuryConfigured: config.TREASURY_WALLET_PRIVATE_KEY.length > 0,
      tokenMint: config.TEST_TOKEN_MINT,
      isDev: config.IS_DEV
    }
  });
});

// Basic info endpoint
app.get('/api/info', (req, res) => {
  res.json({
    name: 'SolWorld Arena Backend',
    version: '1.0.0',
    description: 'Pump.fun integration for automated crypto arena',
    environment: config.NODE_ENV,
    features: [
      'Real token holder fetching',
      'Pump.fun creator fee claiming',
      'Automated treasury management',
      'Winner payout distribution',
      'HMAC authentication',
      'Mock data fallbacks'
    ],
    endpoints: {
      holders: 'GET /api/arena/holders',
      report: 'POST /api/arena/report', 
      status: 'GET /api/arena/status',
      config: 'GET /api/arena/config'
    },
    documentation: 'https://github.com/solworld/arena'
  });
});

// Arena routes
app.use('/api/arena', arenaRoutes);

// Test endpoint for quick verification
app.get('/api/test', async (req, res) => {
  try {
    const results = {
      backend: 'operational',
      timestamp: new Date().toISOString(),
      config: {
        payoutSplit: `${config.PAYOUT_SPLIT_PERCENT * 100}%`,
        gasReserve: `${config.GAS_RESERVE_SOL} SOL`,
        roundPool: `${config.roundPoolSol} SOL`,
        tokenMint: config.TEST_TOKEN_MINT,
        isDev: config.IS_DEV
      },
      services: serviceStatus,
      wallets: {
        creator: config.CREATOR_WALLET_PRIVATE_KEY.length > 0,
        treasury: config.TREASURY_WALLET_PRIVATE_KEY.length > 0,
        dev: config.DEV_WALLET_ADDRESS.length > 0
      }
    };
    
    res.json({
      success: true,
      message: 'SolWorld Backend is running!',
      data: results
    });
    
  } catch (error) {
    res.status(500).json({
      success: false,
      error: 'Test endpoint failed',
      message: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// 404 handler
app.use('*', (req, res) => {
  res.status(404).json({
    success: false,
    error: 'Endpoint not found',
    message: `${req.method} ${req.originalUrl} is not a valid endpoint`,
    availableEndpoints: [
      'GET /health',
      'GET /api/info', 
      'GET /api/test',
      'GET /api/arena/holders',
      'POST /api/arena/report',
      'GET /api/arena/status',
      'GET /api/arena/config'
    ]
  });
});

// Error handling middleware
app.use((error: any, req: express.Request, res: express.Response, next: express.NextFunction) => {
  console.error('Unhandled error:', error);
  
  res.status(500).json({
    success: false,
    error: 'Internal server error',
    message: config.IS_DEV ? error.message : 'Something went wrong'
  });
});

// Start server and initialize services
async function startServer() {
  try {
    // Initialize services first
    await initializeServices();
    
    // Start HTTP server
    const server = app.listen(config.PORT, () => {
      console.log('\n' + '='.repeat(60));
      console.log('🚀 SolWorld Backend - Pump.fun Integration');
      console.log('='.repeat(60));
      console.log(`🌐 Server: http://localhost:${config.PORT}`);
      console.log(`📊 Environment: ${config.NODE_ENV}`);
      console.log(`💰 Payout Split: ${config.PAYOUT_SPLIT_PERCENT * 100}% to treasury`);
      console.log(`⛽ Gas Reserve: ${config.GAS_RESERVE_SOL} SOL`);
      console.log(`🎯 Round Pool: ${config.roundPoolSol} SOL`);
      console.log(`🪙 Token: ${config.TEST_TOKEN_MINT}`);
      console.log('='.repeat(60));
      
      const healthyServices = Object.values(serviceStatus).filter(Boolean).length;
      const totalServices = Object.keys(serviceStatus).length;
      console.log(`✅ Services: ${healthyServices}/${totalServices} healthy`);
      
      if (serviceStatus.solana) console.log('   ✓ Solana connection');
      else console.log('   ✗ Solana connection (using mock)');
      
      if (serviceStatus.holders) console.log('   ✓ Token holders');
      else console.log('   ✗ Token holders (degraded)');
      
      if (serviceStatus.payouts) console.log('   ✓ Payout system');
      else console.log('   ✗ Payout system');
      
      if (serviceStatus.hmac) console.log('   ✓ HMAC security');
      else console.log('   ✗ HMAC security');
      
      console.log('='.repeat(60));
      
      const hasWallets = config.CREATOR_WALLET_PRIVATE_KEY.length > 0 && 
                        config.TREASURY_WALLET_PRIVATE_KEY.length > 0;
      
      if (hasWallets) {
        console.log('✅ Wallet keys configured');
        console.log(`   Creator: ${config.CREATOR_WALLET_PRIVATE_KEY.slice(0, 8)}...`);
        console.log(`   Treasury: ${config.TREASURY_WALLET_PRIVATE_KEY.slice(0, 8)}...`);
        console.log(`   Dev: ${config.DEV_WALLET_ADDRESS}`);
      } else {
        console.log('⚠️  Wallet keys missing! Configure:');
        console.log('   CREATOR_WALLET_PRIVATE_KEY');
        console.log('   TREASURY_WALLET_PRIVATE_KEY');
        console.log('   DEV_WALLET_ADDRESS');
      }
      
      console.log('='.repeat(60));
      console.log('📡 API Endpoints:');
      console.log(`   GET  ${config.IS_DEV ? 'http://localhost:' + config.PORT : ''}/api/arena/holders`);
      console.log(`   POST ${config.IS_DEV ? 'http://localhost:' + config.PORT : ''}/api/arena/report`);
      console.log(`   GET  ${config.IS_DEV ? 'http://localhost:' + config.PORT : ''}/api/arena/status`);
      console.log(`   GET  ${config.IS_DEV ? 'http://localhost:' + config.PORT : ''}/health`);
      
      if (config.IS_DEV) {
        console.log('\n🛠️  Development Mode Active:');
        console.log('   • HMAC validation relaxed');
        console.log('   • Test endpoints enabled');
        console.log('   • Detailed error messages');
        console.log('   • Mock data fallbacks');
      }
      
      console.log('\n🎮 Ready for RimWorld mod connections!');
      console.log('='.repeat(60) + '\n');
    });

    // Graceful shutdown
    process.on('SIGTERM', () => {
      console.log('\n🛑 Received SIGTERM, shutting down gracefully...');
      server.close(() => {
        console.log('✅ Server closed');
        process.exit(0);
      });
    });

    process.on('SIGINT', () => {
      console.log('\n🛑 Received SIGINT, shutting down gracefully...');
      server.close(() => {
        console.log('✅ Server closed');
        process.exit(0);
      });
    });

  } catch (error) {
    console.error('❌ Failed to start server:', error);
    process.exit(1);
  }
}

// Start the server
startServer().catch(error => {
  console.error('❌ Startup failed:', error);
  process.exit(1);
});

export default app;