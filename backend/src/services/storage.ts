// backend/src/services/storage.ts
export interface MatchRecord {
  matchId: string;
  timestamp: number;
  winner: 'Red' | 'Blue' | 'Tie';
  totalPaid: number;
  txids: string[];
  processedAt: number;
}

export interface NonceRecord {
  nonce: string;
  timestamp: number;
  used: boolean;
}

export class StorageService {
  private processedMatches: Map<string, MatchRecord>;
  private seenNonces: Map<string, NonceRecord>;
  private nonceExpiry: number = 10 * 60 * 1000; // 10 minutes
  private maxMatches: number = 1000; // Keep last 1000 matches

  constructor() {
    this.processedMatches = new Map();
    this.seenNonces = new Map();
    
    // Cleanup old records periodically
    setInterval(() => this.cleanup(), 5 * 60 * 1000); // Every 5 minutes
    
    console.log('Storage service initialized');
  }

  /**
   * Check if match has been processed
   */
  isMatchProcessed(matchId: string): boolean {
    return this.processedMatches.has(matchId);
  }

  /**
   * Record a processed match
   */
  recordMatch(record: Omit<MatchRecord, 'processedAt'>): void {
    const fullRecord: MatchRecord = {
      ...record,
      processedAt: Date.now()
    };
    
    this.processedMatches.set(record.matchId, fullRecord);
    
    // Trim old matches if we exceed max
    if (this.processedMatches.size > this.maxMatches) {
      const oldest = Array.from(this.processedMatches.entries())
        .sort((a, b) => a[1].processedAt - b[1].processedAt)[0];
      
      if (oldest) {
        this.processedMatches.delete(oldest[0]);
      }
    }
    
    console.log(`📝 Recorded match ${record.matchId} - ${this.processedMatches.size} total`);
  }

  /**
   * Get match record
   */
  getMatch(matchId: string): MatchRecord | null {
    return this.processedMatches.get(matchId) || null;
  }

  /**
   * Get all processed matches
   */
  getAllMatches(): MatchRecord[] {
    return Array.from(this.processedMatches.values())
      .sort((a, b) => b.processedAt - a.processedAt);
  }

  /**
   * Check if nonce has been used
   */
  isNonceUsed(nonce: string): boolean {
    const record = this.seenNonces.get(nonce);
    
    if (!record) return false;
    
    // Check if expired
    const age = Date.now() - record.timestamp;
    if (age > this.nonceExpiry) {
      this.seenNonces.delete(nonce);
      return false;
    }
    
    return record.used;
  }

  /**
   * Mark nonce as used
   */
  markNonceUsed(nonce: string): void {
    this.seenNonces.set(nonce, {
      nonce,
      timestamp: Date.now(),
      used: true
    });
  }

  /**
   * Generate unique nonce
   */
  generateNonce(): string {
    let nonce: string;
    let attempts = 0;
    
    do {
      nonce = Math.random().toString(36).substring(2, 15) + 
              Math.random().toString(36).substring(2, 15);
      attempts++;
    } while (this.isNonceUsed(nonce) && attempts < 10);
    
    if (attempts >= 10) {
      throw new Error('Failed to generate unique nonce');
    }
    
    return nonce;
  }

  /**
   * Get storage statistics
   */
  getStats() {
    const now = Date.now();
    const activeNonces = Array.from(this.seenNonces.values())
      .filter(n => (now - n.timestamp) < this.nonceExpiry);
    
    const recentMatches = Array.from(this.processedMatches.values())
      .filter(m => (now - m.processedAt) < 24 * 60 * 60 * 1000); // Last 24 hours
    
    return {
      matches: {
        total: this.processedMatches.size,
        recent24h: recentMatches.length,
        oldestTimestamp: Math.min(...Array.from(this.processedMatches.values())
          .map(m => m.processedAt)),
        newestTimestamp: Math.max(...Array.from(this.processedMatches.values())
          .map(m => m.processedAt))
      },
      nonces: {
        total: this.seenNonces.size,
        active: activeNonces.length,
        expiryMinutes: this.nonceExpiry / 1000 / 60
      },
      memory: {
        maxMatches: this.maxMatches,
        matchesUsage: `${this.processedMatches.size}/${this.maxMatches}`,
        noncesCleanupAge: this.nonceExpiry / 1000 / 60 + ' minutes'
      }
    };
  }

  /**
   * Clean up expired records
   */
  private cleanup(): void {
    const now = Date.now();
    let cleanedNonces = 0;
    let cleanedMatches = 0;
    
    // Clean expired nonces
    for (const [nonce, record] of this.seenNonces.entries()) {
      if ((now - record.timestamp) > this.nonceExpiry) {
        this.seenNonces.delete(nonce);
        cleanedNonces++;
      }
    }
    
    // Clean very old matches (keep last 1000 but remove anything older than 7 days)
    const weekAgo = now - (7 * 24 * 60 * 60 * 1000);
    for (const [matchId, record] of this.processedMatches.entries()) {
      if (record.processedAt < weekAgo && this.processedMatches.size > 100) {
        this.processedMatches.delete(matchId);
        cleanedMatches++;
      }
    }
    
    if (cleanedNonces > 0 || cleanedMatches > 0) {
      console.log(`🧹 Cleaned up ${cleanedNonces} nonces, ${cleanedMatches} old matches`);
    }
  }

  /**
   * Clear all data (for testing)
   */
  clearAll(): { matches: number; nonces: number } {
    const matchCount = this.processedMatches.size;
    const nonceCount = this.seenNonces.size;
    
    this.processedMatches.clear();
    this.seenNonces.clear();
    
    console.log(`🗑️ Cleared ${matchCount} matches and ${nonceCount} nonces`);
    
    return { matches: matchCount, nonces: nonceCount };
  }

  /**
   * Export data for backup
   */
  exportData() {
    return {
      matches: Array.from(this.processedMatches.entries()),
      nonces: Array.from(this.seenNonces.entries()),
      timestamp: Date.now()
    };
  }

  /**
   * Import data from backup
   */
  importData(data: any): { success: boolean; imported: { matches: number; nonces: number } } {
    try {
      let importedMatches = 0;
      let importedNonces = 0;
      
      if (data.matches && Array.isArray(data.matches)) {
        for (const [matchId, record] of data.matches) {
          this.processedMatches.set(matchId, record);
          importedMatches++;
        }
      }
      
      if (data.nonces && Array.isArray(data.nonces)) {
        const now = Date.now();
        for (const [nonce, record] of data.nonces) {
          // Only import non-expired nonces
          if ((now - record.timestamp) < this.nonceExpiry) {
            this.seenNonces.set(nonce, record);
            importedNonces++;
          }
        }
      }
      
      console.log(`📥 Imported ${importedMatches} matches, ${importedNonces} nonces`);
      
      return {
        success: true,
        imported: { matches: importedMatches, nonces: importedNonces }
      };
      
    } catch (error) {
      console.error('Failed to import data:', error);
      return {
        success: false,
        imported: { matches: 0, nonces: 0 }
      };
    }
  }

  /**
   * Get recent match activity
   */
  getRecentActivity(hours: number = 24): MatchRecord[] {
    const cutoff = Date.now() - (hours * 60 * 60 * 1000);
    
    return Array.from(this.processedMatches.values())
      .filter(m => m.processedAt >= cutoff)
      .sort((a, b) => b.processedAt - a.processedAt);
  }

  /**
   * Get match statistics
   */
  getMatchStats() {
    const matches = Array.from(this.processedMatches.values());
    
    if (matches.length === 0) {
      return {
        total: 0,
        winners: { Red: 0, Blue: 0, Tie: 0 },
        totalPaid: 0,
        averagePayout: 0,
        totalTxs: 0
      };
    }
    
    const winners = matches.reduce((acc, m) => {
      acc[m.winner]++;
      return acc;
    }, { Red: 0, Blue: 0, Tie: 0 } as Record<string, number>);
    
    const totalPaid = matches.reduce((sum, m) => sum + m.totalPaid, 0);
    const totalTxs = matches.reduce((sum, m) => sum + m.txids.length, 0);
    
    return {
      total: matches.length,
      winners,
      totalPaid,
      averagePayout: totalPaid / matches.length,
      totalTxs
    };
  }
}

export default StorageService;