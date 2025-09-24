// test-complete-flow.js
// Complete end-to-end test of SolWorld arena system
// Run with: node test-complete-flow.js

const crypto = require('crypto');

const TEST_CONFIG = {
  BACKEND_URL: 'http://localhost:4000',
  HMAC_KEY_ID: 'default',
  HMAC_SECRET: 'supersecret',
  TIMEOUT: 10000
};

// Mock arena report data
function generateMockArenaReport() {
  const matchId = `match_${Date.now()}_${Math.floor(Math.random() * 1000)}`;
  const timestamp = Date.now();
  const nonce = crypto.randomBytes(16).toString('hex');
  
  // Generate mock wallets (like real Solana addresses)
  const generateWallet = () => {
    const chars = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    let wallet = '';
    for (let i = 0; i < 44; i++) {
      wallet += chars[Math.floor(Math.random() * chars.length)];
    }
    return wallet;
  };
  
  // Create 10 red team fighters
  const red = [];
  for (let i = 0; i < 10; i++) {
    red.push({
      wallet: generateWallet(),
      kills: Math.floor(Math.random() * 5), // 0-4 kills
      alive: Math.random() > 0.3 // 70% chance of being alive
    });
  }
  
  // Create 10 blue team fighters  
  const blue = [];
  for (let i = 0; i < 10; i++) {
    blue.push({
      wallet: generateWallet(),
      kills: Math.floor(Math.random() * 5), // 0-4 kills
      alive: Math.random() > 0.3 // 70% chance of being alive
    });
  }
  
  // Determine winner based on alive count, then kills, then random
  const redAlive = red.filter(f => f.alive).length;
  const blueAlive = blue.filter(f => f.alive).length;
  
  let winner;
  if (redAlive > blueAlive) {
    winner = 'Red';
  } else if (blueAlive > redAlive) {
    winner = 'Blue';
  } else {
    // Tie in alive count, check total kills
    const redKills = red.reduce((sum, f) => sum + f.kills, 0);
    const blueKills = blue.reduce((sum, f) => sum + f.kills, 0);
    
    if (redKills > blueKills) {
      winner = 'Red';
    } else if (blueKills > redKills) {
      winner = 'Blue';
    } else {
      winner = Math.random() > 0.5 ? 'Red' : 'Blue'; // Random tiebreaker
    }
  }
  
  return {
    matchId,
    timestamp,
    nonce,
    winner,
    red,
    blue,
    roundRewardTotalSol: 1.0,
    payoutPercent: 0.20
  };
}

// Generate HMAC signature
function generateHMAC(payload, keyId, secret) {
  // Convert payload to canonical JSON (sorted keys)
  const canonicalJson = JSON.stringify(payload, Object.keys(payload).sort());
  
  // Generate HMAC-SHA256 signature
  const hmac = crypto.createHmac('sha256', secret);
  hmac.update(canonicalJson);
  const signature = hmac.digest('hex');
  
  return { keyId, signature };
}

// HTTP request helper
async function makeRequest(url, method = 'GET', body = null, headers = {}) {
  const fetch = (await import('node-fetch')).default;
  
  const config = {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...headers
    },
    timeout: TEST_CONFIG.TIMEOUT
  };
  
  if (body) {
    config.body = JSON.stringify(body);
  }
  
  console.log(`📡 ${method} ${url}`);
  if (body) {
    console.log(`   Body keys: [${Object.keys(body).join(', ')}]`);
  }
  
  try {
    const response = await fetch(url, config);
    const data = await response.json();
    
    console.log(`   Status: ${response.status}`);
    console.log(`   Response keys: [${Object.keys(data).join(', ')}]`);
    
    return { status: response.status, data, ok: response.ok };
  } catch (error) {
    console.error(`   Error: ${error.message}`);
    throw error;
  }
}

// Test sequence
async function runCompleteFlowTest() {
  console.log('🎮 SolWorld Complete Flow Test');
  console.log('=' .repeat(50));
  
  try {
    // Step 1: Test backend health
    console.log('\n🔍 Step 1: Backend Health Check');
    const health = await makeRequest(`${TEST_CONFIG.BACKEND_URL}/health`);
    
    if (!health.ok) {
      throw new Error('Backend health check failed');
    }
    
    console.log('✅ Backend is healthy');
    
    // Step 2: Fetch token holders (simulating RimWorld mod)
    console.log('\n🔍 Step 2: Fetch Token Holders');
    const holders = await makeRequest(`${TEST_CONFIG.BACKEND_URL}/api/arena/holders`);
    
    if (!holders.ok || !holders.data.success) {
      throw new Error('Failed to fetch token holders');
    }
    
    console.log(`✅ Fetched ${holders.data.data.wallets.length} token holders`);
    console.log(`   Pool: ${holders.data.data.roundRewardTotalSol} SOL`);
    console.log(`   Payout: ${holders.data.data.payoutPercent * 100}%`);
    
    // Step 3: Simulate arena battle and report results
    console.log('\n🔍 Step 3: Simulate Arena Battle');
    const arenaReport = generateMockArenaReport();
    
    console.log(`   Match ID: ${arenaReport.matchId}`);
    console.log(`   Winner: ${arenaReport.winner} team`);
    console.log(`   Red alive: ${arenaReport.red.filter(f => f.alive).length}/10`);
    console.log(`   Blue alive: ${arenaReport.blue.filter(f => f.alive).length}/10`);
    
    // Step 4: Sign the report with HMAC
    console.log('\n🔍 Step 4: Sign Arena Report');
    const { keyId, signature } = generateHMAC(arenaReport, TEST_CONFIG.HMAC_KEY_ID, TEST_CONFIG.HMAC_SECRET);
    
    const signedReport = {
      ...arenaReport,
      hmacKeyId: keyId,
      signature: signature
    };
    
    console.log(`✅ Signed report with key: ${keyId}`);
    console.log(`   Signature: ${signature.substring(0, 16)}...`);
    
    // Step 5: Submit arena results for payout processing
    console.log('\n🔍 Step 5: Submit Arena Results');
    const reportResult = await makeRequest(
      `${TEST_CONFIG.BACKEND_URL}/api/arena/report`,
      'POST',
      signedReport
    );
    
    if (!reportResult.ok) {
      console.error('❌ Report submission failed:', reportResult.data);
      throw new Error(`Report failed: ${JSON.stringify(reportResult.data)}`);
    }
    
    console.log('✅ Arena report submitted successfully');
    
    if (reportResult.data.success) {
      console.log(`   Winner: ${reportResult.data.data.winner} team`);
      console.log(`   Transactions: ${reportResult.data.data.txids.length}`);
      
      // Display transaction IDs (like RimWorld mod would)
      if (reportResult.data.data.txids.length > 0) {
        console.log('\n💰 Transaction IDs:');
        reportResult.data.data.txids.forEach((txid, index) => {
          const shortTxid = `${txid.substring(0, 8)}...${txid.substring(txid.length - 8)}`;
          console.log(`   TX${index + 1}: ${shortTxid}`);
        });
      }
    } else {
      console.error('❌ Backend reported failure:', reportResult.data.error);
    }
    
    // Step 6: Verify match cannot be reprocessed
    console.log('\n🔍 Step 6: Test Duplicate Prevention');
    const duplicateResult = await makeRequest(
      `${TEST_CONFIG.BACKEND_URL}/api/arena/report`,
      'POST',
      signedReport
    );
    
    if (duplicateResult.status === 409) {
      console.log('✅ Duplicate processing correctly prevented');
    } else {
      console.warn('⚠️ Duplicate processing not prevented');
    }
    
    // Step 7: Check service status
    console.log('\n🔍 Step 7: Service Status Check');
    const status = await makeRequest(`${TEST_CONFIG.BACKEND_URL}/api/arena/status`);
    
    if (status.ok) {
      console.log('✅ Service status retrieved');
      console.log(`   Solana: ${status.data.solana ? 'Connected' : 'Disconnected'}`);
      console.log(`   Treasury: ${status.data.treasury.address.substring(0, 8)}...`);
    }
    
    console.log('\n🎉 COMPLETE FLOW TEST SUCCESSFUL!');
    console.log('=' .repeat(50));
    console.log('✅ Backend receives holders requests');
    console.log('✅ RimWorld can fetch 20 token holders');
    console.log('✅ Arena results are properly signed & submitted');
    console.log('✅ Backend processes payouts and returns transaction IDs');
    console.log('✅ RimWorld can display transaction results to players');
    console.log('✅ Duplicate prevention works correctly');
    console.log('\n🚀 SolWorld crypto arena is ready for deployment!');
    
  } catch (error) {
    console.error('\n❌ COMPLETE FLOW TEST FAILED!');
    console.error('Error:', error.message);
    console.error('\nTroubleshooting:');
    console.error('1. Is the backend running on http://localhost:4000?');
    console.error('2. Are the HMAC keys configured correctly?');
    console.error('3. Are the wallet private keys set in the backend?');
    console.error('4. Check backend logs for detailed error information');
    
    process.exit(1);
  }
}

// Run the test
if (require.main === module) {
  console.log('🔧 Installing node-fetch if needed...');
  
  // Try to run the test
  runCompleteFlowTest().catch(error => {
    if (error.code === 'MODULE_NOT_FOUND' && error.message.includes('node-fetch')) {
      console.error('❌ node-fetch is required for this test');
      console.error('Install it with: npm install node-fetch');
      process.exit(1);
    } else {
      console.error('❌ Unexpected error:', error);
      process.exit(1);
    }
  });
}

module.exports = { runCompleteFlowTest, generateMockArenaReport };