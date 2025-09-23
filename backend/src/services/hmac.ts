// backend/src/services/hmac.ts
import crypto from 'crypto';
import config from '../env';

export interface HmacValidationResult {
  valid: boolean;
  error?: string;
  keyId?: string;
}

export interface SignedRequest {
  hmacKeyId?: string;
  signature?: string;
  nonce?: string;
  timestamp?: number;
}

export class HmacService {
  private keys: Record<string, string>;
  private seenNonces: Set<string>;
  private nonceExpiry: number = 10 * 60 * 1000; // 10 minutes
  
  constructor() {
    this.keys = config.HMAC_KEYS;
    this.seenNonces = new Set();
    
    // Clean up old nonces periodically
    setInterval(() => this.cleanupNonces(), 5 * 60 * 1000); // Every 5 minutes
    
    console.log('HMAC service initialized:', {
      keyCount: Object.keys(this.keys).length,
      isDev: config.IS_DEV,
      maxNonceAge: this.nonceExpiry / 1000 / 60 + ' minutes'
    });
  }

  /**
   * Generate canonical JSON string for HMAC signing
   */
  canonicalJson(obj: any): string {
    // Remove signature and hmac fields from the object for signing
    const { signature, hmacKeyId, ...cleanObj } = obj;
    
    // Sort keys recursively and stringify
    return JSON.stringify(cleanObj, Object.keys(cleanObj).sort());
  }

  /**
   * Generate HMAC signature for an object
   */
  sign(obj: any, keyId: string = 'default'): { signature: string; keyId: string } {
    const secret = this.keys[keyId];
    if (!secret) {
      throw new Error(`HMAC key '${keyId}' not found`);
    }

    const canonical = this.canonicalJson(obj);
    const signature = crypto
      .createHmac('sha256', secret)
      .update(canonical, 'utf8')
      .digest('hex');

    return { signature, keyId };
  }

  /**
   * Verify HMAC signature for an object
   */
  verify(obj: any, providedSignature: string, keyId: string = 'default'): HmacValidationResult {
    try {
      // Check if we have the key
      const secret = this.keys[keyId];
      if (!secret) {
        return {
          valid: false,
          error: `Unknown HMAC key ID: ${keyId}`
        };
      }

      // Generate expected signature
      const canonical = this.canonicalJson(obj);
      const expectedSignature = crypto
        .createHmac('sha256', secret)
        .update(canonical, 'utf8')
        .digest('hex');

      // Timing-safe comparison
      const valid = crypto.timingSafeEqual(
        Buffer.from(expectedSignature, 'hex'),
        Buffer.from(providedSignature, 'hex')
      );

      if (!valid) {
        console.warn('HMAC signature verification failed:', {
          keyId,
          expectedLength: expectedSignature.length,
          providedLength: providedSignature.length,
          canonical: canonical.substring(0, 100) + '...'
        });
      }

      return {
        valid,
        keyId,
        error: valid ? undefined : 'Signature mismatch'
      };

    } catch (error) {
      return {
        valid: false,
        error: error instanceof Error ? error.message : 'Verification failed'
      };
    }
  }

  /**
   * Validate a complete signed request
   */
  validateRequest(body: SignedRequest): HmacValidationResult {
    // In development mode, allow requests without HMAC
    if (config.IS_DEV && !body.hmacKeyId && !body.signature) {
      console.log('🛠️ Dev mode: Allowing request without HMAC');
      return { valid: true };
    }

    // Production mode requires HMAC
    if (!body.hmacKeyId || !body.signature) {
      return {
        valid: false,
        error: 'Missing HMAC key ID or signature'
      };
    }

    // Check nonce (replay protection)
    if (body.nonce) {
      if (this.seenNonces.has(body.nonce)) {
        return {
          valid: false,
          error: 'Duplicate nonce (replay attack?)'
        };
      }
      
      // Mark nonce as seen
      this.seenNonces.add(body.nonce);
    }

    // Check timestamp (optional, helps with replay protection)
    if (body.timestamp) {
      const now = Date.now();
      const age = now - body.timestamp;
      
      if (age > this.nonceExpiry) {
        return {
          valid: false,
          error: 'Request too old (timestamp expired)'
        };
      }
      
      if (age < -60000) { // Allow 1 minute clock skew
        return {
          valid: false,
          error: 'Request timestamp too far in future'
        };
      }
    }

    // Verify HMAC signature
    return this.verify(body, body.signature, body.hmacKeyId);
  }

  /**
   * Generate a secure nonce
   */
  generateNonce(): string {
    return crypto.randomBytes(16).toString('hex');
  }

  /**
   * Generate a complete signed request
   */
  signRequest(payload: any, keyId: string = 'default'): any {
    const nonce = this.generateNonce();
    const timestamp = Date.now();
    
    const requestWithMeta = {
      ...payload,
      nonce,
      timestamp
    };
    
    const { signature } = this.sign(requestWithMeta, keyId);
    
    return {
      ...requestWithMeta,
      hmacKeyId: keyId,
      signature
    };
  }

  /**
   * Clean up old nonces to prevent memory leaks
   */
  private cleanupNonces(): void {
    const beforeSize = this.seenNonces.size;
    
    // Clear all nonces (simple approach - in production you'd want timestamp-based cleanup)
    this.seenNonces.clear();
    
    console.log(`🧹 Cleaned up ${beforeSize} nonces`);
  }

  /**
   * Get HMAC service status for monitoring
   */
  getStatus() {
    return {
      keyCount: Object.keys(this.keys).length,
      isDev: config.IS_DEV,
      seenNonces: this.seenNonces.size,
      nonceExpiryMinutes: this.nonceExpiry / 1000 / 60,
      availableKeys: Object.keys(this.keys)
    };
  }

  /**
   * Test HMAC functionality
   */
  test(): { success: boolean; details: any } {
    try {
      const testPayload = {
        test: true,
        data: 'hello world',
        number: 12345
      };

      // Test signing
      const { signature, keyId } = this.sign(testPayload);
      
      // Test verification
      const verification = this.verify(testPayload, signature, keyId);
      
      // Test request validation
      const signedRequest = this.signRequest({ action: 'test' });
      const requestValidation = this.validateRequest(signedRequest);
      
      return {
        success: verification.valid && requestValidation.valid,
        details: {
          signature: signature.substring(0, 16) + '...',
          verification,
          requestValidation,
          signedRequestKeys: Object.keys(signedRequest)
        }
      };
      
    } catch (error) {
      return {
        success: false,
        details: {
          error: error instanceof Error ? error.message : 'Unknown error'
        }
      };
    }
  }
}

export default HmacService;