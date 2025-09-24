// Save this as backend/debug-env.js and run: node debug-env.js
const path = require('path');
const fs = require('fs');

console.log('🔍 Debugging SolWorld Environment Variables...\n');

// Step 1: Check current working directory
console.log('1. Current working directory:', process.cwd());
console.log('   Expected: should end with /backend or /solworld/backend\n');

// Step 2: Check if .env file exists
const envPath = path.join(process.cwd(), '.env');
console.log('2. Looking for .env file at:', envPath);
console.log('   Exists:', fs.existsSync(envPath));

if (fs.existsSync(envPath)) {
    console.log('   Size:', fs.statSync(envPath).size, 'bytes');
    console.log('   Content preview:');
    const content = fs.readFileSync(envPath, 'utf8');
    console.log('   ---');
    console.log(content.split('\n').slice(0, 5).join('\n'));
    console.log('   ---\n');
} else {
    console.log('   ❌ .env file not found!\n');
}

// Step 3: Test dotenv loading
console.log('3. Testing dotenv loading...');
try {
    const result = require('dotenv').config();
    console.log('   dotenv result:', result);
    if (result.error) {
        console.log('   ❌ dotenv error:', result.error);
    } else {
        console.log('   ✅ dotenv loaded successfully');
        console.log('   Parsed keys:', Object.keys(result.parsed || {}));
    }
} catch (error) {
    console.log('   ❌ dotenv loading failed:', error.message);
}

console.log('');

// Step 4: Check NODE_ENV specifically
console.log('4. Environment variables:');
console.log('   NODE_ENV:', process.env.NODE_ENV);
console.log('   PORT:', process.env.PORT);
console.log('   CREATOR_WALLET_PRIVATE_KEY:', process.env.CREATOR_WALLET_PRIVATE_KEY ? 'SET' : 'NOT SET');
console.log('   TREASURY_WALLET_PRIVATE_KEY:', process.env.TREASURY_WALLET_PRIVATE_KEY ? 'SET' : 'NOT SET');

console.log('');

// Step 5: Test the config import
console.log('5. Testing config import...');
try {
    // Import config using ES modules or CommonJS depending on your setup
    const { config } = require('./src/env.js');
    console.log('   config.NODE_ENV:', config.NODE_ENV);
    console.log('   config.IS_DEV:', config.IS_DEV);
    console.log('   config.PORT:', config.PORT);
} catch (error) {
    console.log('   ❌ Config import failed:', error.message);
    console.log('   Trying alternative import...');
    
    try {
        const config = require('./src/env.js').default;
        console.log('   config.NODE_ENV:', config.NODE_ENV);
        console.log('   config.IS_DEV:', config.IS_DEV);
    } catch (error2) {
        console.log('   ❌ Alternative import also failed:', error2.message);
    }
}

console.log('\n✅ Debug complete!');
console.log('📋 Next steps:');
console.log('   1. Ensure you\'re in the backend directory');
console.log('   2. Ensure .env file exists with NODE_ENV=development');
console.log('   3. Check for any syntax errors in .env file');
console.log('   4. Try running: NODE_ENV=development npm start');