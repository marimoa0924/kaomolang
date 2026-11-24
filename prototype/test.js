// node prototype/test.js
const assert = require('assert');
const G = require('./game.js');

// 판정표
assert.strictEqual(G.judge('joy', 'joy', 'joy'), 'perfect');
assert.strictEqual(G.judge('joy', 'neutral', 'joy'), 'success');
assert.strictEqual(G.judge('joy', 'sad', 'joy'), 'creepy');
assert.strictEqual(G.judge('neutral', 'joy', 'joy'), 'meh');
assert.strictEqual(G.judge('sad', 'joy', 'joy'), 'wrong');
assert.strictEqual(G.face('anger', 'anger'), '(`益´)');
assert.strictEqual(G.SYM_TOTAL, 20);

// 첫 만남은 시작 파츠(기쁨 눈)로 깰 수 있고, 상대 감정 파츠를 준다
let S = G.newGame();
let r = G.doEncounter(S, 'station', 'joy', 'neutral');
assert.strictEqual(r.r, 'success');
assert(S.eyes.includes('sad'));

// 기호는 스티커 두 장에 풀린다 (1일째 ☆, 2일째 ☆)
S = G.newGame();
G.doReply(S, 0, 'joy', 'neutral');
assert(!G.solved(S, '☆'));
G.endNight(S);
G.doReply(S, 1, 'joy', 'neutral');
assert(G.solved(S, '☆'));

// 의심도 100이면 즉시 발각, 엔딩 우선순위
S = G.newGame(); S.sus = 95; G.doReply(S, 0, 'neutral', 'neutral'); assert.strictEqual(S.ending, null);
S.eyes.push('sad'); G.doReply(S, 0, 'sad', 'neutral'); assert.strictEqual(S.ending, 'found');
S = G.newGame(); S.followers = 600; assert.strictEqual(G.finalEnding(S), 'conquer');
S.sus = 80; assert.strictEqual(G.finalEnding(S), 'normal');
S.followers = 0; for (const s of Object.keys(G.SYM)) S.stickers[s] = 2; assert.strictEqual(G.finalEnding(S), 'absorb');

// 7일째 밤 끝에 엔딩 판정
S = G.newGame(); for (let d = 0; d < 7; d++) G.endNight(S);
assert.strictEqual(S.ending, 'normal');

console.log('ok');
