// Save this as generate-treasury.js and run: node generate-treasury.js
const { Keypair } = require('@solana/web3.js');
const bs58 = require('bs58');

console.log('🏦 Generating new treasury wallet for SolWorld...\n');

// Generate a new keypair
const keypair = Keypair.generate();
const secretKeyBase58 = bs58.encode(keypair.secretKey);

console.log('=== TREASURY WALLET GENERATED ===');
console.log('Public Key: ', keypair.publicKey.toString());
console.log('Private Key:', secretKeyBase58);
console.log('Length:     ', secretKeyBase58.length, 'characters');
console.log('');

console.log('🔧 UPDATE YOUR env.ts:');
console.log('Replace this line:');
console.log('  TREASURY_WALLET_PRIVATE_KEY: process.env.TREASURY_WALLET_PRIVATE_KEY || \'dDSWrD6WkhJnDQqoFSQYeLCneaHAkYP3YAYF214UGTf6nZBb4QMvFVBg2s4TfwhfZXNUQwh\'');
console.log('');
console.log('With this:');
console.log(`  TREASURY_WALLET_PRIVATE_KEY: process.env.TREASURY_WALLET_PRIVATE_KEY || '${secretKeyBase58}'`);
console.log('');

console.log('📁 OR CREATE .env FILE:');
console.log(`TREASURY_WALLET_PRIVATE_KEY=${secretKeyBase58}`);
console.log('');

// Test the key works
try {
  const testKeypair = Keypair.fromSecretKey(bs58.decode(secretKeyBase58));
  console.log('✅ Key validation passed - this will work with Solana!');
  console.log('🚨 IMPORTANT: This is for DEVELOPMENT only - has no SOL!');
} catch (error) {
  console.log('❌ Key validation failed:', error.message);
}