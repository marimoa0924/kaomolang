# 카오모지 게임 프로토타입 (Unity)

15일 프로토타입 (스펙 v5: 가려진 글과 이모지로 마음을 짐작하는 눈치 게임, 아쉬운 오답, 선형 성장, 휴식 셋). 시스템 날짜와 스위치는 `Assets/Resources/Unlocks.csv`. 글자와 상자만으로 된 빌드다.

- 열기: Unity Hub → Add → 이 폴더. Unity 6000.3.24f1.
- 실행: 아무 씬에서나 Play. `GameView`가 스스로 생성된다. Game 뷰는 16:9 (예: 1920x1080) 권장.
- 수치 조정: `Assets/Scripts/GameData.cs`의 `Balance`.
- 테스트: Window → General → Test Runner → EditMode.

구조: `GameData`(콘텐츠, 수치) · `GameState`(규칙, Unity 비의존) · `GameView`(태블릿 화면, IMGUI 임시).

폰트: `Assets/Resources/Fonts` (DotGothic16, Gowun Dodum, Dongle, 빠진 기호용 Noto Sans JP). 모두 SIL OFL, 라이선스는 `Assets/Fonts/Licenses`.
