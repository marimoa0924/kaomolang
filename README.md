# 카오모지 게임 프로토타입 (Unity)

7일 프로토타입. 글자와 상자만으로 된 빌드다.

- 열기: Unity Hub → Add → 이 폴더. Unity 6000.3.24f1.
- 실행: 아무 씬에서나 Play. `GameView`가 스스로 생성된다. 화면은 세로(폰 비율) Game 뷰를 권장.
- 수치 조정: `Assets/Scripts/GameData.cs`의 `Balance`.
- 테스트: Window → General → Test Runner → EditMode.

구조: `GameData`(콘텐츠, 수치) · `GameState`(규칙, Unity 비의존) · `GameView`(IMGUI 화면, 임시).
