// 카오모지 게임 프로토타입 — 글자와 상자만으로 만든 7일 빌드
// 수치는 모두 가안. 맨 위 BAL에서 조정한다.

const BAL = {
  DAYS: 7,
  FOLLOWER_GOAL: 500,       // shortcut: 스펙상 미정, 플레이테스트 후 조정
  FAT_OUT: 20, FAT_REPLY: 5, FAT_SHAKY: 80,
  REST_FAT: -40, REST_SUS: -15,
  DAY_SUS: { creepy: 8, wrong: 10 },
  DAY_AFF: { perfect: 2, success: 1 },
  REPLY: { perfect: [40, 0], success: [25, 0], creepy: [60, 12], meh: [0, 0], wrong: [-15, 10] }, // [팔로워, 의심도]
  UPLOAD: { perfect: [30, 0], success: [20, 0], creepy: [40, 8], meh: [5, 0], wrong: [-10, 5] },
  ROOM_UPLOAD: 5,
  CONQUER_SUS_MAX: 70, ABSORB_RATIO: 0.8,
};

const EMO = {
  joy:      { n: '기쁨',   eye: ['^', '^'], mouth: '▽', c: '#ffd166' },
  sad:      { n: '슬픔',   eye: ['T', 'T'], mouth: '﹏', c: '#74a9ff' },
  anger:    { n: '분노',   eye: ['`', '´'], mouth: '益', c: '#ff6b6b' },
  surprise: { n: '놀람',   eye: ['゚', '゚'], mouth: 'Д', c: '#b388ff' },
  shy:      { n: '부끄러움', eye: ['>', '<'], mouth: 'ω', c: '#ff9ecf' },
  neutral:  { n: '중립',   eye: ['・', '・'], mouth: '_', c: '#bbb' },
};
const FEELINGS = ['joy', 'sad', 'anger', 'surprise', 'shy'];
const RES = { perfect: '완벽', success: '성공', creepy: '섬뜩', meh: '시큰둥', wrong: '오답' };

const face = (eye, mouth) => `(${EMO[eye].eye[0]}${EMO[mouth].mouth}${EMO[eye].eye[1]})`;

function judge(eye, mouth, ans) {
  if (eye === ans) return mouth === ans ? 'perfect' : mouth === 'neutral' ? 'success' : 'creepy';
  return eye === 'neutral' ? 'meh' : 'wrong';
}

// 감정 기호 사전: 감정마다 4개
const SYMS = { joy: '☆♪✿◎', sad: '☂◇∴≈', anger: '※▲♯✕', surprise: '◉⁂↯⊙', shy: '♡∽◐❀' };
const SYM = {};
for (const e in SYMS) for (const s of SYMS[e]) SYM[s] = e;
const SYM_TOTAL = Object.keys(SYM).length;

// 장소와 만남 이벤트. 첫 만남은 인사(기쁨)로 반응, 두 번째부터는 그 장소의 감정으로 반응.
// teach = 의태해서 배우는 상대 얼굴의 감정
const PLACES = {
  park: { n: '공원', npc: '펫시터', mood: ['joy'], ev: [
    { ans: 'joy', teach: 'joy', say: '⟟⏃⋏ ⍀⟒⟒!', sit: '강아지 세 마리가 펫시터 주위를 빙글빙글 돈다. 펫시터가 활짝 웃으며 손을 흔든다.' },
    { ans: 'joy', teach: 'joy', say: '⏃⌇⟟ ⟟⏃⋏?', sit: '펫시터가 리드줄 하나를 건넨다. 같이 걷자는 것 같다. 강아지가 네 신발을 핥는다.' }] },
  station: { n: '지하철역', npc: '분실물센터 역무원', mood: ['sad'], ev: [
    { ans: 'joy', teach: 'sad', say: '⍜⌿⟒ ⋏⏃…', sit: '처음 보는 역무원이 곰인형을 상자에 넣다 말고 고개를 든다. 눈가가 젖어 있다. 일단 인사를 해야 한다.' },
    { ans: 'sad', teach: 'sad', say: '⍜⌿⟒… ⟊⟒⍀⍜', sit: '곰인형의 주인은 끝내 오지 않았다. 역무원이 인형을 "보관 만료" 상자로 옮긴다.' }] },
  store: { n: '편의점', npc: '야간 알바생', mood: ['anger', 'surprise'], ev: [
    { ans: 'joy', teach: 'anger', say: '⌇⟟⍀!! ⍜⎍⏁!', sit: '진상 손님이 문을 쾅 닫고 나간다. 씩씩대던 알바생이 너를 보고 표정을 고친다. 손님으로서 인사할 차례.' },
    { ans: 'anger', teach: 'surprise', say: '⏚⍀⍜!! ⏃⎅⏃!', sit: '그 손님이 또 왔다. 알바생이 너를 보며 "같이 화 좀 내줘요" 하는 눈빛이다. 그때 손님 주머니에서 개구리가 튀어나온다.' }] },
  cafe: { n: '카페', npc: '신입 바리스타', mood: ['shy'], ev: [
    { ans: 'joy', teach: 'shy', say: '⌇⍜⍀⍀⊬~', sit: '신입 바리스타가 라떼아트를 망쳤다. 하트가 감자가 됐다. 얼굴이 빨개진 채 잔을 내민다. 일단 웃어주자.' },
    { ans: 'shy', teach: 'shy', say: '⊑⟒⏃⍀⏁… ⟟⏁⌇', sit: '하트가 처음으로 성공했다. 바리스타가 잔을 내밀며 눈을 못 마주친다.' }] },
};

// 팬 글 21개: 하루 3개. c[0]은 기본 단서, c[1]은 해독 1레벨부터 보이는 단서
const POSTS = [
  [{ e: 'joy', t: '■■ ☆ ■■■ !!!', c: ['📷 케이크 사진', '🕒 오후 3시'] },
   { e: 'sad', t: '■■■ ☂ ■■ ...', c: ['📷 비 오는 창문 사진', '🕒 새벽 3시 12분'] },
   { e: 'joy', t: '♪ ■■■■ ✿ ■', c: ['📷 꼬리 흔드는 강아지 사진', '💬 반응 😆×20'] }],
  [{ e: 'anger', t: '■■ ※ ■■■■ !!', c: ['💬 반응 😡×8', '🕒 밤 11시 50분'] },
   { e: 'joy', t: '■ ☆ ■■ ◎ ■■', c: ['📷 리본 달린 선물 상자', '💬 반응 🎂×5'] },
   { e: 'sad', t: '◇ ■■■ ☂ ■■', c: ['📷 텅 빈 의자 사진', '🕒 새벽 2시'] }],
  [{ e: 'surprise', t: '◉ ■■ ?!?!', c: ['📷 흔들린 번개 사진', '💬 반응 😲×14'] },
   { e: 'shy', t: '■■ ♡ ■■■ ..', c: ['📷 손편지 사진', '💬 반응 ☺×3'] },
   { e: 'anger', t: '▲ ■■■ ※ ■!!', c: ['📷 금 간 폰 화면', '💬 반응 💢×9'] }],
  [{ e: 'joy', t: '■■■ ♪ ■ ~~', c: ['📷 놀이공원 사진', '🕒 토요일 오후'] },
   { e: 'sad', t: '∴ ■■ ≈ ■■■', c: ['📷 시든 꽃 사진', '🕒 새벽 4시'] },
   { e: 'surprise', t: '■ ⁂ ■■ ◉ !?', c: ['📷 복권 번호 사진', '💬 반응 😱×30'] }],
  [{ e: 'shy', t: '∽ ■■■ ♡ ■', c: ['📷 얼굴을 반쯤 가린 셀카', '💬 반응 🙈×11'] },
   { e: 'anger', t: '■■ ♯ ■ ▲ ■■!!', c: ['📷 구겨진 영수증', '🕒 밤 10시'] },
   { e: 'joy', t: '✿ ■■ ◎ ■■■', c: ['📷 꽃다발 사진', '💬 반응 💐×7'] }],
  [{ e: 'surprise', t: '↯ ■■■ ⊙ ?!', c: ['📷 정전된 거리 사진', '🕒 밤 9시 정각'] },
   { e: 'shy', t: '■ ◐ ■■ ∽ ..', c: ['📷 고백 쪽지 사진', '💬 반응 😳×6'] },
   { e: 'sad', t: '≈ ■■■ ◇ ■', c: ['📷 이삿짐 상자 사진', '🕒 새벽 1시'] }],
  [{ e: 'anger', t: '✕ ■■ ♯ ■■!!!', c: ['📷 새치기 줄 사진', '💬 반응 🤬×15'] },
   { e: 'shy', t: '❀ ■■■ ◐ ■', c: ['📷 커플 머그컵 사진', '💬 반응 🥰×4'] },
   { e: 'surprise', t: '■ ⊙ ■■ ↯ ?!?', c: ['📷 하늘의 이상한 빛 사진', '💬 반응 👽×99'] }],
];

function newGame() {
  return {
    day: 1, phase: 'day', followers: 0, sus: 0, fat: 0,
    eyes: ['neutral', 'joy'], mouths: ['neutral'],
    stickers: {}, seen: {}, traits: { decode: 0, mimic: 0, empathy: 0 }, points: 0,
    visits: {}, aff: {}, went: null, uploaded: false, replied: [], ending: null,
  };
}

const clamp = (v) => Math.max(0, Math.min(100, v));
const solved = (S, s) => (S.stickers[s] || 0) >= (S.traits.decode >= 2 ? 1 : 2);
const solvedCount = (S) => Object.keys(SYM).filter((s) => solved(S, s)).length;

function addSus(S, n) {
  S.sus = clamp(S.sus + n);
  if (S.sus >= 100) S.ending = 'found';
}
function addFat(S, n) { S.fat = clamp(S.fat + (n > 0 && S.traits.mimic >= 2 ? n / 2 : n)); }

// 낮: 만남 이벤트 하나 치르기. rand는 테스트용 주입
function doEncounter(S, placeId, eye, mouth, rand = Math.random) {
  const p = PLACES[placeId];
  const n = S.visits[placeId] || 0;
  const ev = p.ev[Math.min(n, 1)];
  const msgs = [];
  if (S.fat >= BAL.FAT_SHAKY) { // 의태가 흔들림: 눈이나 입 하나가 무작위로 바뀜
    if (rand() < 0.5) eye = S.eyes[Math.floor(rand() * S.eyes.length)];
    else mouth = S.mouths[Math.floor(rand() * S.mouths.length)];
    msgs.push(`의태가 흔들렸다! 얼굴이 ${face(eye, mouth)} 로 바뀌었다.`);
  }
  const r = judge(eye, mouth, ev.ans);
  S.visits[placeId] = n + 1;
  S.went = placeId;
  addFat(S, BAL.FAT_OUT);
  if (r === 'perfect' || r === 'success') {
    S.aff[placeId] = (S.aff[placeId] || 0) + BAL.DAY_AFF[r];
    const order = [ev.teach, ...p.mood.filter((m) => m !== ev.teach)];
    const missing = order.flatMap((e) => [
      ...(S.eyes.includes(e) ? [] : [['eyes', e]]),
      ...(S.mouths.includes(e) ? [] : [['mouths', e]]),
    ]).slice(0, S.traits.mimic >= 1 ? 2 : 1);
    for (const [k, e] of missing) {
      S[k].push(e);
      msgs.push(`${p.npc}의 얼굴을 의태했다 → ${EMO[e].n} ${k === 'eyes' ? '눈 ' + EMO[e].eye.join(' ') : '입 ' + EMO[e].mouth} 획득`);
    }
    if (!missing.length) msgs.push('더 배울 얼굴은 없었다.');
  } else if (r === 'creepy' || r === 'wrong') {
    addSus(S, BAL.DAY_SUS[r]);
    msgs.push(r === 'creepy' ? '상대가 움찔한다. 주변 사람들이 수군댄다.' : '상대가 어색하게 웃는다. 누군가 너를 빤히 본다.');
  } else msgs.push('상대가 고개를 갸웃한다.');
  return { r, ans: ev.ans, msgs };
}

function doRest(S) {
  addFat(S, BAL.REST_FAT);
  addSus(S, BAL.REST_SUS);
  S.went = 'room';
}

function doUpload(S, eye, mouth) {
  S.uploaded = true;
  if (S.went === 'room' || !S.went) { S.followers += BAL.ROOM_UPLOAD; return { r: null, f: BAL.ROOM_UPLOAD }; }
  const rank = ['wrong', 'meh', 'creepy', 'success', 'perfect'];
  const r = PLACES[S.went].mood.map((m) => judge(eye, mouth, m)).sort((a, b) => rank.indexOf(b) - rank.indexOf(a))[0];
  const [f, s] = BAL.UPLOAD[r];
  S.followers = Math.max(0, S.followers + f);
  addSus(S, s);
  return { r, f };
}

function doReply(S, idx, eye, mouth) {
  const post = POSTS[S.day - 1][idx];
  const r = judge(eye, mouth, post.e);
  let [f, s] = BAL.REPLY[r];
  if (r === 'creepy' && S.traits.empathy >= 2) s = 0;
  S.followers = Math.max(0, S.followers + f);
  addSus(S, s);
  addFat(S, BAL.FAT_REPLY);
  const newly = [];
  if (r === 'perfect' || r === 'success') {
    for (const t of post.t.split(' ')) if (SYM[t]) {
      const was = solved(S, t);
      S.stickers[t] = (S.stickers[t] || 0) + 1;
      if (!was && solved(S, t)) newly.push(t);
    }
  }
  S.replied[idx] = { r, face: face(eye, mouth) };
  return { r, f, newly };
}

function endNight(S) {
  S.points += 1;
  if (S.day >= BAL.DAYS) { S.ending = finalEnding(S); return; }
  Object.assign(S, { day: S.day + 1, phase: 'day', went: null, uploaded: false, replied: [] });
}

function finalEnding(S) {
  if (S.sus >= 100) return 'found';
  if (S.followers >= BAL.FOLLOWER_GOAL && S.sus < BAL.CONQUER_SUS_MAX) return 'conquer';
  if (solvedCount(S) >= SYM_TOTAL * BAL.ABSORB_RATIO && S.followers < BAL.FOLLOWER_GOAL) return 'absorb';
  return 'normal';
}

const ENDINGS = {
  found: ['발각 엔딩', '의태가 풀린 사진 한 장이 퍼졌다. 촉수가, 눈이 일곱 개가. 피드가 난리가 났다.'],
  conquer: ['정복 엔딩', '어느 날부터 팬들이 글 대신 카오모지만으로 대화한다. (^▽^) (^▽^) (^▽^). 행성이 너의 말을 배웠다.'],
  absorb: ['물듦 엔딩', '너는 행성의 글자로 긴 글을 쓴다. 다 쓰고 나니, 원래 어디서 왔는지 잘 기억나지 않는다.'],
  normal: ['평범 엔딩', '그럭저럭 알려진 계정으로 남았다. 알림이 하나 온다. 모행성: "보고서 언제 보냄?"'],
};

if (typeof module !== 'undefined') module.exports = { BAL, EMO, SYM, SYM_TOTAL, POSTS, PLACES, face, judge, newGame, doEncounter, doRest, doUpload, doReply, endNight, finalEnding, solved, solvedCount };
