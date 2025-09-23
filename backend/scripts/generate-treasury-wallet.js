import { Keypair } from '@solana/web3.js';
import bs58 from 'bs58';

console.log('🏦 Generating Treasury Wallet...\n');

// Generate a new treasury wallet
const treasuryKeypair = Keypair.generate();

console.log('Treasury Wallet Generated:');
console.log('Public Key:', treasuryKeypair.publicKey.toBase58());
console.log('Private Key (Base58):', bs58.encode(treasuryKeypair.secretKey));
console.log('\nAdd this to your .env file:');
console.log(`TREASURY_WALLET_PRIVATE_KEY=${bs58.encode(treasuryKeypair.secretKey)}`);
console.log('\n⚠️  Keep the private key secure!');