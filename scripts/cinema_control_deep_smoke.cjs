#!/usr/bin/env node
'use strict';

const dgram = require('node:dgram');
const fs = require('node:fs');
const path = require('node:path');

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const messages = [];
const pending = new Map();
let ws;
let sequence = 0;
const partialResult = {};
const savePartial = () => fs.writeFileSync(path.join(__dirname, 'cinema_control_deep_partial.json'), JSON.stringify(partialResult, null, 2));

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
      try { frame = JSON.parse(String(event.data)); } catch { return; }
      if (frame.type !== 'ack' || frame.id == null) return;
      const callback = pending.get(String(frame.id));
      if (!callback) return;
      pending.delete(String(frame.id));
      callback(frame);
    });
  });
}

function sliceMessages(since) {
  const result = { cylinder: [], effects: [], states: [], heart: 0, other: [] };
  for (const item of messages.slice(since)) {
    const cmd = item.json?.Cmd;
    const para = item.json?.Para;
    if (cmd === 'GamePlatform_Cylinder') result.cylinder.push({ at: item.at, para });
    else if (cmd === 'GamePlatform_PlatformEffect') result.effects.push({ at: item.at, para });
    else if (cmd === 'GamePlatform_GameState') result.states.push({ at: item.at, state: para?.GameState });
    else if (cmd === 'GamePlatform_Heart') result.heart += 1;
    else if (cmd) result.other.push({ at: item.at, cmd, para });
  }
  return result;
}

function summary(slice) {
  const heights = slice.cylinder.flatMap((item) => item.para?.HeightRatioList || []);
  const openEffects = [...new Set(
    slice.effects
      .filter((item) => item.para?.EffectData?.IsOpen)
      .map((item) => item.para?.EffectType)
      .filter(Boolean),
  )];
  return {
    cylinderCount: slice.cylinder.length,
    heightMin: heights.length ? Math.min(...heights) : null,
    heightMax: heights.length ? Math.max(...heights) : null,
    finalHeight: heights.length ? heights[heights.length - 1] : null,
    distinctHeights: [...new Set(heights.map((value) => Number(value).toFixed(3)))].slice(0, 40),
    effectCount: slice.effects.length,
    openEffects,
    states: slice.states.map((x) => x.state),
    stateEvents: slice.states,
  };
}

function countState(slice, expected) {
  return slice.states.filter((x) => x.state === expected).length;
}

async function snapshot() {
  return (await request('system.snapshot.request')).data;
}

async function waitForSnapshot(predicate, label, timeoutMs = 25000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const current = await snapshot();
    if (predicate(current)) return current;
    await wait(200);
  }
  throw new Error(`timeout waiting for snapshot: ${label}`);
}

async function waitForState(since, expected, timeoutMs = 20000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const found = messages.slice(since).find(
      (item) => item.json?.Cmd === 'GamePlatform_GameState' && item.json?.Para?.GameState === expected,
    );
    if (found) return found;
    await wait(100);
  }
  throw new Error(`timeout waiting for ${expected}`);
}

async function startFilm(filmId, filmName) {
  await request('system.session.selectedFilm.set', { filmId, filmName });
  const ack = await request('system.workflow.action.execute', {
    actionId: 'startShow',
    filmId,
  });
  await waitForSnapshot(
    (s) => s?.media?.state === 'playing'
      && s?.media?.filmId === filmId
      && s?.motion?.activeMediaKind === 'video'
      && s?.motion?.activeMediaId === filmId
      && s?.motion?.sessionActive === true
      && s?.motion?.safeReady === false,
    `active video:${filmId}`,
    30000,
  );
  return ack;
}

async function enterIdleAndWaitSafe(since) {
  const stopAt = Date.now();
  await request('system.session.enterIdle');
  const gameOver = await waitForState(since, 'GameOver');
  const ready = await waitForSnapshot(
    (s) => s?.motion?.safeReady === true
      && s?.motion?.safeFault === false
      && s?.media?.state === 'idle'
      && s?.motion?.activeMediaKind === 'idle',
    'idle/safeReady after GameOver',
    20000,
  );
  await wait(400);
  return {
    stopAt,
    gameOverAt: gameOver.at,
    safeStopToGameOverMs: gameOver.at - stopAt,
    snapshot: ready,
    slice: sliceMessages(since),
  };
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function main() {
  const udp = dgram.createSocket('udp4');
  udp.on('message', (bytes, remote) => {
    let json;
    try { json = JSON.parse(bytes.toString('utf8')); } catch { return; }
    messages.push({ at: Date.now(), json });
    if (json?.Cmd === 'GamePlatform_Heart') {
      udp.send(Buffer.from('{"Cmd":"GamePlatform_Heart"}'), remote.port, remote.address);
    }
  });
  await new Promise((resolve, reject) => {
    udp.once('error', reject);
    udp.bind(30120, '127.0.0.1', resolve);
  });

  try {
    await connect();
    await waitForSnapshot(
      (s) => s?.motion?.safeReady === true && s?.motion?.safeFault === false,
      'initial safeReady',
      20000,
    );
    const result = partialResult;
    result.baseline = await snapshot();
    savePartial();
    assert(result.baseline.motion.mode === 'normal', 'CinemaControl is not in normal mode');

    // 1. Full CinemaControl workflow start and motion output.
    let since = messages.length;
    const workflowAck = await startFilm('ydl_platform_demo', '影动力动感演示：平台联动');
    await wait(9000);
    const firstDuring = sliceMessages(since);
    result.workflowStart = {
      ack: workflowAck,
      snapshot: await snapshot(),
      output: summary(firstDuring),
    };
    savePartial();
    assert(countState(firstDuring, 'Ready') === 1, 'workflow start must emit exactly one Ready');
    assert(result.workflowStart.output.cylinderCount > 20, 'workflow produced too few cylinder frames');
    assert(result.workflowStart.output.heightMax > result.workflowStart.output.heightMin, 'cylinder output did not vary');
    assert(result.workflowStart.output.openEffects.length > 0, 'no motion effect opened during workflow');

    // 2. Pause/resume/start must not duplicate lifecycle commands.
    const duplicateSince = messages.length;
    await request('media.playback.pause');
    await wait(350);
    await request('media.playback.resume');
    await wait(700);
    await request('media.playback.start');
    await wait(700);
    const duplicateSlice = sliceMessages(duplicateSince);
    result.pauseResumeDuplicate = summary(duplicateSlice);
    result.pauseResumeSnapshot = await snapshot();
    savePartial();
    assert(countState(duplicateSlice, 'Ready') === 0, 'pause/resume/start emitted duplicate Ready');
    assert(countState(duplicateSlice, 'GameOver') === 0, 'pause/resume/start emitted GameOver');

    // 3. Return to idle: SafeStop must settle before one GameOver.
    since = messages.length;
    const idleRun = await enterIdleAndWaitSafe(since);
    result.returnToIdle = {
      safeStopToGameOverMs: idleRun.safeStopToGameOverMs,
      snapshot: idleRun.snapshot,
      output: summary(idleRun.slice),
    };
    assert(countState(idleRun.slice, 'GameOver') === 1, 'returnToIdle must emit exactly one GameOver');
    assert(result.returnToIdle.output.finalHeight === 0, 'returnToIdle cylinder did not reach 0');
    assert(result.returnToIdle.output.openEffects.length === 0, 'returnToIdle left effects open');
    assert(idleRun.safeStopToGameOverMs >= 200, 'GameOver arrived before SafeStop completion');

    // 4. Switch film while playing: old session closes, then new session starts.
    await startFilm('v7', '飞船发射');
    await wait(2500);
    since = messages.length;
    await startFilm('ydl_platform_demo', '影动力动感演示：平台联动');
    await wait(3000);
    const switchSlice = sliceMessages(since);
    result.switchFilm = { snapshot: await snapshot(), output: summary(switchSlice) };
    assert(countState(switchSlice, 'GameOver') === 1, 'switchFilm must close old session exactly once');
    assert(countState(switchSlice, 'Ready') === 1, 'switchFilm must start new session exactly once');
    assert(result.switchFilm.snapshot.motion.activeMediaId === 'ydl_platform_demo', 'switchFilm did not activate target film');
    assert(result.switchFilm.output.cylinderCount > 20, 'switchFilm produced too few cylinder frames');
    const switchIdleSince = messages.length;
    await request('system.session.enterIdle');
    await waitForState(switchIdleSince, 'GameOver');
    await waitForSnapshot(
      (s) => s?.motion?.safeReady === true && s?.media?.state === 'idle' && s?.motion?.activeMediaKind === 'idle',
      'idle/safeReady after switchIdle',
      20000,
    );

    // 5. Load-only while idle: close idle session but do not start a video session.
    since = messages.length;
    await request('media.playback.load', { filmId: 'v7' });
    await wait(300);
    await waitForState(since, 'GameOver');
    await wait(1200);
    const loadOnlySlice = sliceMessages(since);
    result.loadOnly = { snapshot: await snapshot(), output: summary(loadOnlySlice) };
    assert(countState(loadOnlySlice, 'GameOver') === 1, 'loadOnly must close idle session once');
    assert(countState(loadOnlySlice, 'Ready') === 1, 'loadOnly must restore exactly one idle session');
    assert(result.loadOnly.snapshot.motion.activeMediaKind === 'idle', 'loadOnly must not start a video session');
    assert(result.loadOnly.snapshot.media.state === 'idle', 'loadOnly must leave media idle');
    assert(result.loadOnly.output.openEffects.length === 0, 'loadOnly left effects open');

    // 6. Emergency stop from a moving session.
    await request('system.session.enterIdle');
    await wait(500);
    await startFilm('v7', '飞船发射');
    await wait(2500);
    since = messages.length;
    const emergencyAt = Date.now();
    await request('safety.emergency.stop', { reason: 'cinema-control-deep-test' });
    const emergencyGameOver = await waitForState(since, 'GameOver');
    await waitForSnapshot((s) => s?.motion?.safeReady === true, 'safeReady after emergency', 20000);
    await wait(300);
    const emergencySlice = sliceMessages(since);
    result.emergencyStop = {
      snapshot: await snapshot(),
      safeStopToGameOverMs: emergencyGameOver.at - emergencyAt,
      output: summary(emergencySlice),
    };
    assert(countState(emergencySlice, 'GameOver') === 1, 'emergency stop must emit one GameOver');
    assert(result.emergencyStop.output.finalHeight === 0, 'emergency stop cylinder did not reach 0');
    assert(result.emergencyStop.output.openEffects.length === 0, 'emergency stop left effects open');

    // 7. Start a fresh session after emergency SafeStop.
    since = messages.length;
    await startFilm('v7', '飞船发射');
    await wait(2500);
    const freshDuring = sliceMessages(since);
    result.newSessionAfterEmergency = { snapshot: await snapshot(), output: summary(freshDuring) };
    assert(countState(freshDuring, 'Ready') === 1, 'new session after emergency emitted duplicate Ready');
    assert(result.newSessionAfterEmergency.output.cylinderCount > 20, 'new session produced no motion');

    const finalSliceSince = messages.length;
    await request('system.session.enterIdle');
    await waitForState(finalSliceSince, 'GameOver');
    await waitForSnapshot((s) => s?.motion?.safeReady === true, 'final safeReady', 20000);
    result.final = { snapshot: await snapshot(), output: summary(sliceMessages(finalSliceSince)) };
    assert(result.final.output.finalHeight === 0, 'final cylinder did not reach 0');
    assert(result.final.output.openEffects.length === 0, 'final effects remained open');

    result.pass = true;
    result.generatedAt = new Date().toISOString();
    const outPath = path.join(__dirname, 'cinema_control_deep_smoke.json');
    fs.writeFileSync(outPath, JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
  } finally {
    try { ws?.close(); } catch {}
    try { udp.close(); } catch {}
  }
}

main().catch((error) => {
  try { savePartial(); } catch {}
  console.error(error.stack || error.message);
  process.exitCode = 1;
});
