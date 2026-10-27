#!/usr/bin/env node
/**
 * YDL game-session lifecycle smoke.
 *
 * Verifies the UNIPlayer XML contract:
 *   Ready    -> exactly once after a real video start
 *   GameOver -> exactly once after motion end + SafeStop clear
 *
 * The script responds to MP-YDL heartbeats so normal scenarios do not trip
 * link-loss. The final scenario intentionally stops responding.
 */
'use strict';

const dgram = require('node:dgram');
const fs = require('node:fs');
const path = require('node:path');

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const messages = [];
const pending = new Map();
let ws;
let sequence = 0;
let respondToHeartbeat = true;

function request(name, data = {}, timeoutMs = 25000) {
  return new Promise((resolve, reject) => {
    const id = String(++sequence);
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`timeout: ${name}`));
    }, timeoutMs);
    pending.set(id, (frame) => {
      clearTimeout(timer);
      if (!frame.ok) {
        reject(new Error(`${name}: ${JSON.stringify(frame)}`));
        return;
      }
      resolve(frame);
    });
    ws.send(JSON.stringify({ type: 'req', name, id, data }));
  });
}

async function connect() {
  await new Promise((resolve, reject) => {
    ws = new WebSocket('ws://127.0.0.1:9600/ws');
    ws.addEventListener('open', resolve, { once: true });
    ws.addEventListener('error', reject, { once: true });
    ws.addEventListener('message', (event) => {
      let frame;
      try {
        frame = JSON.parse(String(event.data));
      } catch {
        return;
      }
      if (frame.type !== 'ack' || frame.id == null) return;
      const callback = pending.get(String(frame.id));
      if (!callback) return;
      pending.delete(String(frame.id));
      callback(frame);
    });
  });
}

function stateMessages(since) {
  return messages
    .slice(since)
    .map((item) => item.json)
    .filter((json) => json?.Cmd === 'GamePlatform_GameState')
    .map((json) => json.Para?.GameState)
    .filter(Boolean);
}

function count(states, expected) {
  return states.filter((state) => state === expected).length;
}


function sliceMessages(since) {
  const result = { cylinder: [], effects: [], states: [], heart: 0, other: [] };
  for (const item of messages.slice(since)) {
    const cmd = item.json?.Cmd;
    const para = item.json?.Para;
    if (cmd === 'GamePlatform_Cylinder') result.cylinder.push(para);
    else if (cmd === 'GamePlatform_PlatformEffect') result.effects.push(para);
    else if (cmd === 'GamePlatform_GameState') result.states.push(para?.GameState);
    else if (cmd === 'GamePlatform_Heart') result.heart += 1;
    else if (cmd) result.other.push({ cmd, para });
  }
  return result;
}

function motionSummary(slice) {
  const heights = slice.cylinder.flatMap((item) => item?.HeightRatioList || []);
  const openEffects = [...new Set(
    slice.effects.filter((item) => item?.EffectData?.IsOpen).map((item) => item?.EffectType).filter(Boolean),
  )];
  return {
    cylinderCount: slice.cylinder.length,
    heightMin: heights.length ? Math.min(...heights) : null,
    heightMax: heights.length ? Math.max(...heights) : null,
    distinctHeights: [...new Set(heights.map((value) => Number(value).toFixed(3)))].slice(0, 30),
    effectCount: slice.effects.length,
    effects: slice.effects,
    openEffects,
    states: slice.states,
  };
}

async function waitForPlaying(timeoutMs = 20000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const snapshot = await request('system.snapshot.request');
    if (snapshot.data?.media?.state === 'playing') return snapshot;
    await wait(250);
  }
  throw new Error('timed out waiting for playing state');
}

async function waitForGameOver(since, expected, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (count(stateMessages(since), 'GameOver') >= expected) return;
    await wait(100);
  }
  throw new Error(`timed out waiting for GameOver #${expected}`);
}

async function waitForMotionSession(kind, active = true, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const snapshot = await request('system.snapshot.request');
    const motion = snapshot.data?.motion;
    if (motion?.sessionActive === active && (!active || motion.activeMediaKind === kind)) {
      return snapshot;
    }
    await wait(100);
  }
  throw new Error(`timed out waiting for motion session kind=${kind} active=${active}`);
}

function assertEqual(label, actual, expected) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${expected}, got ${actual}`);
  }
}

async function reset() {
  await request('media.playback.stop');
  await request('system.session.enterIdle');
  const snapshot = await request('system.snapshot.request');
  if (snapshot.data?.motion?.mode === 'custom') {
    // custom 明确禁止 Idle 会话；测试从无 Idle GameReady 的状态直接进入 Video。
    return;
  }
  // enterIdle 必须幂等地建立 normal Idle 会话；不要依赖页面是否刚好重新显示。
  await waitForMotionSession('idle', true);
}

async function start(filmId, filmName) {
  await request('system.session.selectedFilm.set', { filmId, filmName });
  await request('media.playback.load', { filmId });
  await wait(350);

  // Load is asynchronous (AVPro opens large files in the background). Retry
  // the start request only until the public snapshot confirms playback.
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    await request('media.playback.start');
    for (let poll = 0; poll < 20; poll += 1) {
      await wait(250);
      const snapshot = await request('system.snapshot.request');
      if (snapshot.data?.media?.state === 'playing') return;
    }
  }
  throw new Error(`video did not enter playing state: ${filmId}`);
}

async function main() {
  const udp = dgram.createSocket('udp4');
  udp.on('message', (bytes, remote) => {
    let json;
    try {
      json = JSON.parse(bytes.toString('utf8'));
    } catch {
      return;
    }
    messages.push({ at: Date.now(), json });
    if (respondToHeartbeat && json?.Cmd === 'GamePlatform_Heart') {
      udp.send(Buffer.from('{"Cmd":"GamePlatform_Heart"}'), remote.port, remote.address);
    }
  });
  await new Promise((resolve, reject) => {
    udp.once('error', reject);
    udp.bind(30120, '127.0.0.1', resolve);
  });

  try {
    await connect();
    await reset();
    const result = {};

    if (process.env.YDL_SMOKE_ONLY === 'workflow') {
      await request('system.session.selectedFilm.set', { filmId: 'ydl_platform_demo', filmName: '影动力动感演示：平台联动' });
      const since = messages.length;
      const workflowAck = await request('system.workflow.action.execute', {
        actionId: 'startShow',
        filmId: 'ydl_platform_demo',
      });
      await waitForPlaying();
      const sampleMs = Number(process.env.YDL_WORKFLOW_CAPTURE_MS || 12000);
      await wait(sampleMs);
      const duringSnapshot = await request('system.snapshot.request');
      const during = sliceMessages(since);
      const idleSince = messages.length;
      await request('system.session.enterIdle');
      await waitForGameOver(idleSince, 1);
      await wait(600);
      const postIdle = sliceMessages(idleSince);
      result.workflowStartShow = {
        workflowAck,
        media: duringSnapshot.data?.media,
        sampleMs,
        during: motionSummary(during),
        postIdle: motionSummary(postIdle),
      };
      assertEqual('workflow.ready', count(during.states, 'Ready'), 1);
      assertEqual('workflow.gameOver', count(postIdle.states, 'GameOver'), 1);
      if (result.workflowStartShow.during.cylinderCount === 0) throw new Error('workflow: no cylinder output');
      if ((result.workflowStartShow.during.heightMax ?? 0) <= (result.workflowStartShow.during.heightMin ?? 0)) {
        throw new Error('workflow: cylinder output did not vary');
      }
      if (result.workflowStartShow.during.openEffects.length === 0) throw new Error('workflow: no effect opened');
      if (!result.workflowStartShow.postIdle.distinctHeights.includes('0.000')) throw new Error('workflow: cylinder did not return to 0');
      result.pass = true;
      result.generatedAt = new Date().toISOString();
      fs.writeFileSync(path.join(__dirname, 'ydl_workflow_smoke.json'), JSON.stringify(result, null, 2));
      console.log(JSON.stringify(result, null, 2));
      return;
    }

    if (process.env.YDL_SMOKE_ONLY === 'new-session') {
      const cycles = [];
      for (let cycle = 1; cycle <= 2; cycle += 1) {
        const since = messages.length;
        await start('v7', '飞船发射');
        await wait(2500);
        const during = sliceMessages(since);
        const stopAt = Date.now();
        const idleSince = messages.length;
        await request('system.session.enterIdle');
        await waitForGameOver(idleSince, 1);
        const gameOverMessage = messages
          .slice(idleSince)
          .find((item) => item.json?.Cmd === 'GamePlatform_GameState' && item.json?.Para?.GameState === 'GameOver');
        await wait(500);
        const postIdle = sliceMessages(idleSince);
        const cycleResult = {
          cycle,
          ready: count(during.states, 'Ready'),
          gameOver: count(postIdle.states, 'GameOver'),
          safeStopToGameOverMs: gameOverMessage ? gameOverMessage.at - stopAt : null,
          during: motionSummary(during),
          postIdle: motionSummary(postIdle),
        };
        assertEqual(`newSession${cycle}.ready`, cycleResult.ready, 1);
        assertEqual(`newSession${cycle}.gameOver`, cycleResult.gameOver, 1);
        if (cycleResult.during.cylinderCount === 0) throw new Error(`newSession${cycle}: no cylinder output`);
        if (!cycleResult.postIdle.distinctHeights.includes('0.000')) throw new Error(`newSession${cycle}: cylinder did not return to 0`);
        if (cycleResult.postIdle.openEffects.length !== 0) throw new Error(`newSession${cycle}: effects remained open`);
        if ((cycleResult.safeStopToGameOverMs ?? 0) < 200) throw new Error(`newSession${cycle}: GameOver arrived before SafeStop completion`);
        cycles.push(cycleResult);
      }
      result.newSessions = cycles;
      result.pass = true;
      result.generatedAt = new Date().toISOString();
      fs.writeFileSync(path.join(__dirname, 'ydl_new_session_smoke.json'), JSON.stringify(result, null, 2));
      console.log(JSON.stringify(result, null, 2));
      return;
    }

    if (process.env.YDL_SMOKE_ONLY === 'link-loss') {
      const since = messages.length;
      await start('v2', '视频示例2');
      respondToHeartbeat = false;
      await wait(9000);
      const states = stateMessages(since);
      result.linkLoss = {
        ready: count(states, 'Ready'),
        gameOver: count(states, 'GameOver'),
        states,
      };
      assertEqual('linkLoss.ready', result.linkLoss.ready, 1);
      assertEqual('linkLoss.gameOver', result.linkLoss.gameOver, 0);
      result.pass = true;
      result.generatedAt = new Date().toISOString();
      fs.writeFileSync(path.join(__dirname, 'ydl_link_loss_smoke.json'), JSON.stringify(result, null, 2));
      console.log(JSON.stringify(result, null, 2));
      return;
    }
    if (process.env.YDL_SMOKE_ONLY === 'natural-end') {
      const since = messages.length;
      await start('v2', '视频示例2');
      await wait(30000);
      const states = stateMessages(since);
      result.naturalEndNoMotion = {
        ready: count(states, 'Ready'),
        gameOver: count(states, 'GameOver'),
        states,
      };
      assertEqual('naturalEndNoMotion.ready', result.naturalEndNoMotion.ready, 1);
      assertEqual('naturalEndNoMotion.gameOver', result.naturalEndNoMotion.gameOver, 1);
      result.pass = true;
      result.generatedAt = new Date().toISOString();
      fs.writeFileSync(path.join(__dirname, 'ydl_natural_end_smoke.json'), JSON.stringify(result, null, 2));
      console.log(JSON.stringify(result, null, 2));
      return;
    }

    // 1. 从 Idle 会话进入影片：旧 Idle GameOver → 影片 Ready。
    // 重复 pause/resume/start 不得额外产生 Ready。
    let since = messages.length;
    await start('v7', '飞船发射');
    await request('media.playback.pause');
    await wait(300);
    await request('media.playback.resume');
    await wait(800);
    await request('media.playback.start');
    await wait(800);
    let states = stateMessages(since);
    result.duplicateEvents = {
      ready: count(states, 'Ready'),
      gameOver: count(states, 'GameOver'),
      states,
    };

    // 同一影片会话重复 enterIdle 只能产生一次 GameOver，并恢复一个 Idle 会话。
    since = messages.length;
    await request('system.session.enterIdle');
    await request('system.session.enterIdle');
    await waitForGameOver(since, 1);
    await waitForMotionSession('idle', true);
    await request('system.session.enterIdle'); // 已处于 Idle，应完全幂等
    await wait(500);
    states = stateMessages(since);
    result.repeatedTerminalEvents = {
      ready: count(states, 'Ready'),
      gameOver: count(states, 'GameOver'),
      states,
    };

    // 对同一个 Idle 会话重复 stop 只能产生一次 GameOver，且不得自动重开会话。
    since = messages.length;
    await request('media.playback.stop');
    await waitForGameOver(since, 1);
    await request('media.playback.stop');
    await wait(1800);
    states = stateMessages(since);
    result.repeatedIdleStop = {
      ready: count(states, 'Ready'),
      gameOver: count(states, 'GameOver'),
      states,
    };

    // 2. Switching while playing: old session gets one GameOver, then the
    // newly started film gets a new Ready and its own GameOver.
    await start('v7', '飞船发射');
    since = messages.length;
    await request('media.playback.load', { filmId: 'v2' });
    await waitForGameOver(since, 1);
    await start('v2', '视频示例2');
    await request('system.session.enterIdle');
    await waitForGameOver(since, 2);
    await waitForMotionSession('idle', true);
    states = stateMessages(since);
    result.switchFilm = {
      ready: count(states, 'Ready'),
      gameOver: count(states, 'GameOver'),
      states,
    };

    // 3. 仅 load 不产生新的 Ready；但离开 Idle 会话仍需一次 GameOver。
    since = messages.length;
    await request('media.playback.load', { filmId: 'v2' });
    await waitForGameOver(since, 1);
    await wait(600);
    states = stateMessages(since);
    result.loadOnly = {
      ready: count(states, 'Ready'),
      gameOver: count(states, 'GameOver'),
      states,
    };

    // 4. Emergency stop must terminate the active session after SafeStop clear;
    // a repeated emergency request must not duplicate GameOver.
    await start('v2', '视频示例2');
    since = messages.length;
    await request('safety.emergency.stop', { reason: 'smoke' });
    await waitForGameOver(since, 1);
    await waitForMotionSession('idle', true);
    await request('safety.emergency.stop', { reason: 'smoke-repeat' });
    await waitForGameOver(since, 2);
    await wait(600);
    states = stateMessages(since);
    result.emergencyStop = {
      ready: count(states, 'Ready'),
      gameOver: count(states, 'GameOver'),
      states,
    };


    assertEqual('duplicateEvents.ready', result.duplicateEvents.ready, 1);
    assertEqual('duplicateEvents.gameOver', result.duplicateEvents.gameOver, 1);
    assertEqual('repeatedTerminalEvents.ready', result.repeatedTerminalEvents.ready, 1);
    assertEqual('repeatedTerminalEvents.gameOver', result.repeatedTerminalEvents.gameOver, 1);
    assertEqual('repeatedIdleStop.ready', result.repeatedIdleStop.ready, 0);
    assertEqual('repeatedIdleStop.gameOver', result.repeatedIdleStop.gameOver, 1);
    assertEqual('switchFilm.ready', result.switchFilm.ready, 2);
    assertEqual('switchFilm.gameOver', result.switchFilm.gameOver, 2);
    assertEqual('loadOnly.ready', result.loadOnly.ready, 0);
    assertEqual('loadOnly.gameOver', result.loadOnly.gameOver, 1);
    assertEqual('emergencyStop.ready', result.emergencyStop.ready, 1);
    assertEqual('emergencyStop.gameOver', result.emergencyStop.gameOver, 2);

    result.pass = true;
    result.generatedAt = new Date().toISOString();
    fs.writeFileSync(path.join(__dirname, 'ydl_game_session_smoke.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
  } finally {
    try { ws?.close(); } catch {}
    try { udp.close(); } catch {}
  }
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exitCode = 1;
});
