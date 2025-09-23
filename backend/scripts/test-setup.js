import { Keypair } from '@solana/web3.js';
import bs58 from 'bs58';

console.log('🧪 Testing SolWorld Backend Setup...\n');

// Test 1: Generate sample wallets
console.log('1. Generating sample wallets:');
const sampleCreator = Keypair.generate();
const sampleTreasury = Keypair.generate();

console.log('   Creator:', sampleCreator.publicKey.toBase58());
console.log('   Treasury:', sampleTreasury.publicKey.toBase58());

// Test 2: Base58 encoding test
console.log('\n2. Testing Base58 encoding:');
const testKey = bs58.encode(sampleCreator.secretKey);
console.log('   Encoded length:', testKey.length, 'characters');
console.log('   Sample private key:', testKey.slice(0, 10) + '...');

// Test 3: Environment simulation
console.log('\n3. Sample .env configuration:');
console.log(`CREATOR_WALLET_PRIVATE_KEY=${testKey}`);
console.log(`TREASURY_WALLET_PRIVATE_KEY=${bs58.encode(sampleTreasury.secretKey)}`);
console.log(`DEV_WALLET_ADDRESS=${sampleCreator.publicKey.toBase58()}`);

console.log('\n✅ Setup test complete! Your dependencies are working.');
console.log('📝 Next: Configure your .env file with real wallet keys');