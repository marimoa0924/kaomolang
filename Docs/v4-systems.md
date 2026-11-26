# v4 시스템 목록 (개선 지시서 1단계)

기준 커밋: c6bb2e6. 코드는 고치지 않았다.
파일은 세 개다. `GameData.cs`(데이터·수치), `GameState.cs`(규칙, Unity 없이 테스트), `GameView.cs`(IMGUI 화면).
줄 번호는 이 커밋 기준이다.

## 1. 시스템별 위치

| 시스템 | 규칙 (GameState) | 데이터 (GameData) | 화면 (GameView) | 지금 열리는 때 | 지금 스위치 |
|---|---|---|---|---|---|
| 낮 만남·눈 고르기·입 얻기 | `PrepareOuting` 222, `DoEncounter` | `Places`, `Story` | `DrawPlaces`, `DrawEncounter`, `React` | 1일 | 없음 (핵심) |
| 밤 답장·게시물 | `DoReply`, `DoUpload`, `Judge` | `Posts`, `Pool`, `Balance.Reply/Upload` | `DrawSns`, `Composer`, `Tweet`, `MyPost` | 1일 | 없음 (핵심) |
| 피로 구간 (40 새어남, 70 위험) | `Leaking`, `InDanger`, `TiredMultiplier` 513 | `LeakFrom`, `DangerFrom`, `Tired*` | 배경 번짐, 잡음 줄, 사진 촉수 1350쯤, "지친 사진 ×" 1353 | 1일 | 없음 |
| 100 판정 (각성 / 셧다운) | `AddFat` 159, `AwakenChance` 182 | `Awaken*`, `Breakdown*` | 셧다운 연출 `DrawShutdown`, 각성 팝업 249, 얼굴 788 | 1일 | 없음 |
| 정비 카드 | `Maintenance` 318 | 없음 | `ShowMorning` 423 | 셧다운 다음 날 | 없음 |
| 입력 흔들림 | `RollWobble` 187, `WobbleChance` | `InputWobble` | `Wobbled` 1579, `PartRow` | 피로 70 이상 | 없음 |
| 칸별 3색 반응 | `Hits` 139, `ReplyState.EyeHit/MouthHit` | 없음 | `HitGlyph` (✓ △ ·), `Tweet` 1550, 일기 스티커 | 1일 | 없음 |
| 스티커 → 검증 대기 | `Finish`, `IsPending`, `StickerNeed` | `Sym` | 수첩 칸 1811 | 1일 | 없음 |
| 연필 추측 | `SetPencil` 462 | 없음 | `CyclePencil` 1850, 멘션 기호 색 | 1일 | 없음 |
| 그림 페이지 묶음 확정 | `TryConfirm` 447, `CanOpenPage` | `Scenes`, `ConfirmFollowers` | `OpenPage` 1857 | 검증 대기가 셋 모이면 | 없음 |
| 정산 회복 선택 | `ApplyRecovery` 677 | `Rest*` | `DrawRest` 1990 | 1일 (튜토리얼 단계도 있음) | 없음 |
| 장소 호감·능력 | `WinAt` 208, `HasAbility` | `AffNeed`, `Abilities` | 하트·진행 점 980/1020, 능력 팝업, 특성 창 | 2단계·3단계 모두 | `Feature.PlaceAbility` |
| NPC가 기분을 숨김 | `PendingHidden` 231 | 없음 | 만남 대사 "……" | 호감 2단계 | 없음 |
| 단골 친밀도·서운함 | `Bond` 485, `ClueLevel` 482, `RuleFor` | `IntimacyNeed` | 하트 표시, 표정 힌트 1423, 말버릇 1829 | 첫 성공부터 | `Feature.Intimacy` |
| 단골 입력 중 표시 | (친밀도 1) | 없음 | `TypingFor` 1574 | 친밀도 1 | `Feature.Intimacy` |
| 단골 글 어려워짐 | `StartDay` 334 | `Pool` | 없음 | 친밀도 1 | `Feature.Intimacy` |
| 아침 유행 공지 | `PickNotice` 410, `Score` 안 규칙 | `Notices` 14장 | `ShowMorning`, 수치 패널 고정 줄 | 2일 | `Feature.Notices` |
| 거리 말풍선·스크랩 | `Scrap` | 없음 | `Street` 1063, `StreetBubble` 1078 | 1일 (튜토리얼 제외) | `Feature.Street` |
| 빈칸 긴 글 | `DoReplyBlank` 606 | `PoolTable`의 `>` 글 16개 | `Composer`의 □1 □2 | 1일 (풀에 섞임) | `Feature.BlankPosts` |
| 오역 후속 글 | `literalPending` 595, `MisuseFollowup` | `|L` 글 12개 | 멘션의 "→" 글 | 1일 | `Feature.Misuse` |
| 두 번째 기호 세트 | `SecondSetVariant` 391 | `SecondSet` | 수첩 칸 | 6일 | 없음 |
| 장식 조합 | `CombineDecors` 284 | 없음 | `MixPopup` 1231 | 8일 | 없음 |
| 촉수 장식 | `AddFat` | `Tentacle` | 게시 장식 버튼 | 피로 70 첫 진입 | 없음 |
| 자동 저장·되감기 | `Clone` 105, `DayRoll` | 없음 | `Rewind` 411, 작업 표시줄 ◁ | 2일 | 없음 |
| 특성 (3 × 5단계) | `Raise`, 각 규칙 안 | `TraitMax` | `DrawTraits` | 1일 | 없음 |
| 다짐 스티커 | `ApplyPledge` | 없음 | `DrawDiary` | 1일 | 없음 |
| 새 기능 안내 상자 17개 | 없음 | 없음 | `InitTips` 128 | 기능이 처음 보일 때 | 없음 |
| 설정 창 (1차·2차 테스트) | `Off`, `LastDay` | 없음 | `DrawSettings` 1971 | 1일 | 없음 |

## 2. 화면에 숫자가 그대로 보이는 곳 (4단계 대상)

| 곳 | 줄 | 보이는 숫자 |
|---|---|---|
| 수치 패널 목표 줄 | 778 | 목표 팔로워, 남은 날 |
| 팔로워 막대 | 782 | 팔로워 수 |
| 의심 막대 | 784 | 의심 0~100 |
| 피로 막대 | 785 | 피로 / 한계 (예: 63/100) |
| [?] 도움말 | 809, 797 | 구간 경계 40·70, 각성 확률 % |
| 행동 미리보기 | `HoverFat` 468 | 피로 막대에 깜빡이는 증감 |
| 집·회복 카드 배지 | 1185 | "~ -12", "!? +5" |
| 집 결과 줄 | `RestLine` 1192 | 피로·의심 증감 |
| 결과 숫자 튀어오름 | `Floats` 511 | 팔로워·의심·피로 증감 |
| 사진 | 1353 | "지친 사진 ×1.2" |
| 좋아요 | 1372 | 좋아요 수 |
| 멘션 탭 | 1318 | 답한 글 수 1/3 |
| 장소 버튼·만남 창 | 980, 1020 | 하트, 진행 점 ●○ |
| 수첩 | 1797, 1811, 1822 | 기호 N/30, 스티커 N/2, 검증 대기 N/3 |
| 특성 | 758, 1920 | 포인트 수 |
| 막·날 넘김 | 586, 653 | 팔로워 / 목표 |
| 그림 일기 | 372 | 하루 팔로워 증감 스티커 |
| 엔딩 | 2092 | 팔로워, 의심, 해독 수, 시너지 수 |
