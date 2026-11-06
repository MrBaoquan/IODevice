(function (global) {
  'use strict';

  const DEFAULT_PORT = 19101;
  const DEFAULT_ENDPOINT = `http://127.0.0.1:${DEFAULT_PORT}`;

  class BridgeClient {
    constructor(options = {}) {
      this.endpoint = normalizeEndpoint(options.endpoint || global.__IOTOOLKIT_BRIDGE__ || DEFAULT_ENDPOINT);
      this.autoReconnect = options.autoReconnect !== false;
      this.ws = null;
      this.connected = false;
      this.values = Object.create(null);
      this.seq = 0;
      this.listeners = new Map();
      this.pending = new Map();
      this.nextRequestId = 1;
      this.reconnectAttempts = 0;
      this.reconnectTimer = null;
      this.disposed = false;
    }

    async connect() {
      if (this.connected) return this;
      this.disposed = false;
      await this.openSocket();
      return this;
    }

    openSocket() {
      return new Promise((resolve, reject) => {
        const wsUrl = this.endpoint.replace(/^http/i, 'ws') + '/ws';
        let settled = false;
        try {
          this.ws = new WebSocket(wsUrl);
        } catch (error) {
          reject(error);
          return;
        }

        this.ws.onopen = () => {
          this.connected = true;
          this.reconnectAttempts = 0;
          this.emitLocal('connection', { connected: true, endpoint: this.endpoint });
        };

        this.ws.onmessage = event => {
          let message;
          try { message = JSON.parse(event.data); } catch { return; }
          this.handleMessage(message);
          if (message.type === 'hello' && !settled) {
            settled = true;
            resolve(this);
          }
        };

        this.ws.onerror = () => {
          if (!settled) {
            settled = true;
            reject(new Error(`Unable to connect to IOToolkit.WebBridge at ${this.endpoint}`));
          }
        };

        this.ws.onclose = () => {
          this.connected = false;
          this.emitLocal('connection', { connected: false, endpoint: this.endpoint });
          for (const [id, pending] of this.pending.entries()) {
            clearTimeout(pending.timer);
            pending.reject(new Error('WebSocket closed before acknowledgement: ' + id));
          }
          this.pending.clear();
          if (!settled) {
            settled = true;
            reject(new Error(`IOToolkit.WebBridge closed the connection: ${this.endpoint}`));
          }
          if (!this.disposed && this.autoReconnect !== false) this.scheduleReconnect();
        };
      });
    }

    scheduleReconnect() {
      if (this.reconnectTimer) return;
      const delay = Math.min(1000 * Math.pow(1.6, this.reconnectAttempts++), 15000);
      this.reconnectTimer = setTimeout(() => {
        this.reconnectTimer = null;
        if (this.disposed) return;
        this.openSocket().catch(() => {});
      }, delay);
    }

    handleMessage(message) {
      if (message.type === 'hello' || message.type === 'snapshot') {
        const snapshot = message.snapshot || message.payload || message;
        if (snapshot && snapshot.values) {
          for (const [key, value] of Object.entries(snapshot.values)) this.values[key] = Number(value);
        }
        if (typeof snapshot.seq === 'number') this.seq = snapshot.seq;
        this.emitLocal('snapshot', snapshot);
        this.emitLocal('state', this.values);
        return;
      }
      if (message.type === 'ack') {
        const pending = this.pending.get(message.requestId);
        if (!pending) return;
        this.pending.delete(message.requestId);
        clearTimeout(pending.timer);
        if (message.ok) pending.resolve(message);
        else pending.reject(new Error(message.error || 'Command rejected'));
        return;
      }
      if (message.type === 'event') {
        this.emitLocal(message.name, message.payload);
        this.emitLocal('event', message);
      }
    }

    sendRequest(payload, timeoutMs = 5000) {
      if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
        return Promise.reject(new Error('IOToolkit.WebBridge is not connected'));
      }
      const requestId = String(this.nextRequestId++);
      return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
          this.pending.delete(requestId);
          reject(new Error(`IOToolkit request timed out: ${requestId}`));
        }, timeoutMs);
        this.pending.set(requestId, { resolve, reject, timer });
        this.ws.send(JSON.stringify({ ...payload, requestId }));
      });
    }

    on(name, handler) {
      if (!this.listeners.has(name)) this.listeners.set(name, new Set());
      this.listeners.get(name).add(handler);
      return () => this.off(name, handler);
    }

    off(name, handler) {
      this.listeners.get(name)?.delete(handler);
    }

    emitLocal(name, payload) {
      const set = this.listeners.get(name);
      if (!set) return;
      for (const handler of set) {
        try { handler(payload); } catch (error) { console.error(`[IOToolkit] handler "${name}" failed`, error); }
      }
    }

    get(channelId) {
      return this.values[channelId];
    }

    async set(channelId, value) {
      try {
        await this.sendRequest({ type: 'set', channel: channelId, value: Number(value) });
        this.values[channelId] = Number(value);
        return true;
      } catch (error) {
        const response = await fetch(`${this.endpoint}/api/v1/channels/${encodeURIComponent(channelId)}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ value: Number(value) })
        });
        if (!response.ok) throw error;
        this.values[channelId] = Number(value);
        return true;
      }
    }

    async action(actionId) {
      try {
        await this.sendRequest({ type: 'action', action: actionId });
      } catch {
        const response = await fetch(`${this.endpoint}/api/v1/actions/${encodeURIComponent(actionId)}`, { method: 'POST' });
        if (!response.ok) throw new Error(`Action failed: ${actionId}`);
      }
      return true;
    }

    async safeStop() {
      try {
        await this.sendRequest({ type: 'safe-stop' });
      } catch {
        const response = await fetch(`${this.endpoint}/api/v1/safe-stop`, { method: 'POST' });
        if (!response.ok) throw new Error('Safe stop failed');
      }
      return true;
    }

    async mockInput(channelId, eventName = 'pressed') {
      const response = await fetch(`${this.endpoint}/api/v1/mock/input/${encodeURIComponent(channelId)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ event: eventName })
      });
      if (!response.ok) throw new Error(`Mock input failed: ${channelId}.${eventName}`);
      return true;
    }

    async snapshot() {
      const response = await fetch(`${this.endpoint}/api/v1/snapshot`);
      if (!response.ok) throw new Error('Unable to read WebBridge snapshot');
      const snapshot = await response.json();
      for (const [key, value] of Object.entries(snapshot.values || {})) this.values[key] = Number(value);
      return snapshot;
    }

    close() {
      this.disposed = true;
      if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
      this.ws?.close();
      this.ws = null;
      this.connected = false;
    }
  }

  function normalizeEndpoint(endpoint) {
    return String(endpoint || DEFAULT_ENDPOINT).replace(/\/+$/, '');
  }

  global.IOToolkit = {
    connect(options) {
      const client = new BridgeClient(options);
      return client.connect();
    },
    BridgeClient
  };
})(typeof window !== 'undefined' ? window : globalThis);
