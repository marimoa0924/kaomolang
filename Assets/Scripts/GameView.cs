// 프로토타입 화면: 글자와 상자만. IMGUI로 코드만으로 그린다.
// shortcut: IMGUI는 프로토타입용. 그림을 입힐 단계에서 uGUI/UI Toolkit 화면으로 교체하고 GameState는 그대로 쓴다.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Kaomoji.GameData;

namespace Kaomoji
{
    public class GameView : MonoBehaviour
    {
        // 씬 설정 없이 Play만 누르면 시작되게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<GameView>() == null) new GameObject("KaomojiGame").AddComponent<GameView>();
        }

        enum Screen_ { Home, Places, Encounter, Sns, Notebook, Traits, Ending }

        const float W = 400, H = 800; // 폰 화면 가상 해상도
        GameState S = new();
        Screen_ screen;
        Emo selEye = Emo.Neutral, selMouth = Emo.Neutral;
        string msg = "", place;
        Vector2 scroll;
        Font font;
        GUIStyle label, big, faceStyle, box, button, selButton;

        void Awake()
        {
            // 한글과 카오모지 글리프가 있는 OS 폰트
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial Unicode MS" }, 16);
        }

        void InitStyles()
        {
            if (label != null) return;
            GUI.skin.font = font;
            label = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, fontSize = 15 };
            big = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            faceStyle = new GUIStyle(label) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8), alignment = TextAnchor.UpperLeft };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16, richText = true, wordWrap = true, padding = new RectOffset(8, 8, 6, 6) };
            selButton = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            selButton.normal.textColor = selButton.hover.textColor = Color.yellow;
        }

        void Go(Screen_ s) { screen = s; msg = ""; scroll = Vector2.zero; }

        void OnGUI()
        {
            InitStyles();
            float k = Screen.height / H;
            float x = (Screen.width / k - W) / 2;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1));
            GUILayout.BeginArea(new Rect(Mathf.Max(0, x), 0, W, H));
            if (S.Ending != Ending.None) screen = Screen_.Ending;
            else GUILayout.Label($"{S.Day}/{Balance.Days}일 · {(S.IsNight ? "밤" : "낮")}        @kao_kao", label);
            if (msg != "") GUILayout.Box(msg, box);

            scroll = GUILayout.BeginScrollView(scroll);
            switch (screen)
            {
                case Screen_.Home: Home(); break;
                case Screen_.Places: Places_(); break;
                case Screen_.Encounter: EncounterScreen(); break;
                case Screen_.Sns: Sns(); break;
                case Screen_.Notebook: Notebook(); break;
                case Screen_.Traits: TraitsScreen(); break;
                case Screen_.Ending: EndingScreen(); break;
            }
            GUILayout.EndScrollView();
            if (screen is Screen_.Encounter or Screen_.Sns) Builder();
            GUILayout.EndArea();
        }

        // 상태를 바꾸는 버튼. 누르면 동작 후 이번 GUI 이벤트를 끊어 레이아웃 불일치 오류를 막는다
        void Btn(string t, System.Action act, bool on = true)
        {
            GUI.enabled = on;
            var r = GUILayout.Button(t, button);
            GUI.enabled = true;
            if (r) { act(); GUIUtility.ExitGUI(); }
        }

        void Back() => Btn("← 홈", () => Go(Screen_.Home));

        void Stat(string name, float v, float max, string color)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v / max * 20), 0, 20);
            GUILayout.Label($"{name}  <color={color}>{new string('█', n)}</color><color=#555>{new string('░', 20 - n)}</color>  {Mathf.RoundToInt(v)}", label);
        }

        void Home()
        {
            GUILayout.Label(S.IsNight ? "밤 — SNS에서 업로드하고 팬 글 3개에 답장" : "낮 — 외출하거나 휴식", big);
            Stat("팔로워", S.Followers, Balance.FollowerGoal, "#a68bff");
            Stat("의심도", S.Sus, 100, "#ff6b6b");
            Stat("의태피로", S.Fat, 100, S.Fat >= Balance.FatShaky ? "#ff3b3b" : "#f4a261");
            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            Btn("외출", () => Go(Screen_.Places), !S.IsNight);
            Btn("휴식", () => { S.DoRest(); msg = "방에서 쉬었다. 의태 피로와 의심도가 내려갔다.\n\n밤이 되었다."; }, !S.IsNight);
            Btn("SNS", () => { S.MarkSeen(); Go(Screen_.Sns); }, S.IsNight);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Btn("수첩", () => Go(Screen_.Notebook));
            Btn(S.Points > 0 ? $"특성 ({S.Points})" : "특성", () => Go(Screen_.Traits));
            GUILayout.EndHorizontal();
        }

        void Places_()
        {
            Back();
            GUILayout.Label("어디로 갈까", big);
            foreach (var p in GameData.Places)
            {
                var aff = S.Aff.GetValueOrDefault(p.Id);
                Btn($"<b>{p.Name}</b> · {p.Npc}\n가본 횟수 {S.Visits.GetValueOrDefault(p.Id)} · 호감 {(aff > 0 ? new string('♥', aff) : "-")}",
                    () => { place = p.Id; selEye = selMouth = Emo.Neutral; Go(Screen_.Encounter); });
            }
        }

        void EncounterScreen()
        {
            var p = PlaceById(place);
            var ev = S.NextEvent(place);
            GUILayout.Label($"{p.Name} — {p.Npc}", big);
            GUILayout.Box($"[상황] {ev.Situation}\n\n<size=22>“{ev.Say}”</size>", box);
            GUILayout.Label("어떤 표정으로 반응할까?" + (S.Fat >= Balance.FatShaky ? " <color=red>(의태 피로가 높다. 얼굴이 멋대로 바뀔지도)</color>" : ""), label);
        }

        void Sns()
        {
            Back();
            GUILayout.Label($"피드 — {S.Day}일째 밤", big);
            string photo = S.Went is null or "room" ? "방 사진" : $"{PlaceById(S.Went).Name} 사진";
            GUILayout.BeginVertical(box);
            GUILayout.Label($"<b>업로드</b> · {photo}", label);
            if (S.Uploaded) GUILayout.Label("올림 ✔", label);
            else Btn("지금 얼굴을 달아서 올리기", () =>
            {
                var (r, f) = S.DoUpload(selEye, selMouth);
                msg = r.HasValue ? $"업로드 {Face(selEye, selMouth)} — {ResultName(r.Value)} (팔로워 {f:+0;-0;0})" : $"방 사진을 올렸다 (팔로워 +{f})";
            });
            GUILayout.EndVertical();

            for (int i = 0; i < 3; i++)
            {
                var post = S.TodayPosts[i];
                var old = GUI.backgroundColor;
                if (S.Traits[Trait.Empathy] >= 1 && ColorUtility.TryParseHtmlString(E[post.Emotion].Color, out var c)) GUI.backgroundColor = Color.Lerp(Color.white, c, 0.6f);
                GUILayout.BeginVertical(box);
                GUI.backgroundColor = old;
                GUILayout.Label($"<size=22>{PostText(post.Text)}</size>", label);
                GUILayout.Label($"<color=#aaa>{string.Join(" · ", post.Clues.Take(S.Traits[Trait.Decode] >= 1 ? 2 : 1))}</color>", label);
                var rep = S.Replied[i];
                if (rep.HasValue) GUILayout.Label($"↳ 답장 {rep.Value.face} — <b>{ResultName(rep.Value.r)}</b>", label);
                else
                {
                    int idx = i;
                    Btn("이 글에 답장", () =>
                    {
                        var (r, f, newly) = S.DoReply(idx, selEye, selMouth);
                        msg = $"{Fan(r)}\n{ResultName(r)} · 팔로워 {f:+0;-0;0}" + (newly.Count > 0 ? $"\n기호가 풀렸다: {string.Join(" ", newly)}" : "");
                    });
                }
                GUILayout.EndVertical();
            }
            if (S.NightDone) Btn($"잠들기 ({S.Day}일째 끝)", () =>
            {
                S.EndNight();
                Go(Screen_.Home);
                if (S.Ending == Ending.None) msg = $"특성 포인트 +1\n{S.Day}일째 아침.";
            });
        }

        static string Fan(Result r) => r switch
        {
            Result.Perfect => "팬: (꺄아아!!)", Result.Success => "팬: (좋아해 줬다)", Result.Creepy => "팬: (…무서운데 계속 보게 돼)",
            Result.Meh => "팬: (읽씹당한 기분)", _ => "팬: (??? 뭐임)",
        };

        string PostText(string t) => string.Join(" ", t.Split(' ').Select(w =>
            !Sym.ContainsKey(w) ? w : S.Solved(w) ? $"<color=#ffd166>[{E[Sym[w]].Name}]</color>" : $"<color=#a68bff><b>{w}</b></color>"));

        // 카오모지 조립 바: 만남 이벤트와 SNS가 같이 쓴다
        void Builder()
        {
            GUILayout.BeginVertical(box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("눈", label, GUILayout.Width(28));
            foreach (var e in S.Eyes) if (GUILayout.Button(E[e].Eyes, e == selEye ? selButton : button)) selEye = e;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("입", label, GUILayout.Width(28));
            foreach (var e in S.Mouths) if (GUILayout.Button(E[e].Mouth, e == selMouth ? selButton : button)) selMouth = e;
            GUILayout.EndHorizontal();
            GUILayout.Label(Face(selEye, selMouth), faceStyle, GUILayout.Height(50));
            if (screen == Screen_.Encounter) Btn("이 표정으로 반응하기", () =>
            {
                var (r, ans, msgs) = S.DoEncounter(place, selEye, selMouth);
                Go(Screen_.Home);
                msg = $"{ResultName(r)}! (정답 감정: {E[ans].Name})\n{string.Join("\n", msgs)}\n\n밤이 되었다.";
            });
            GUILayout.EndVertical();
        }

        void Notebook()
        {
            Back();
            GUILayout.Label("해독 수첩", big);
            GUILayout.Label($"규칙: 기호 하나는 감정 하나. 스티커 {(S.Traits[Trait.Decode] >= 2 ? 1 : 2)}장이면 풀린다.\n풀린 기호 {S.SolvedCount}/{Sym.Count}", label);
            foreach (var e in Feelings)
            {
                var syms = Sym.Keys.Where(s => Sym[s] == e && S.Stickers.ContainsKey(s))
                    .Select(s => S.Solved(s) ? $"<b>{s}</b>✔" : $"{s}●{S.Stickers[s]}");
                GUILayout.Label($"<color={E[e].Color}>{E[e].Name}</color>   <size=20>{string.Join("  ", syms)}</size>", label);
            }
            var unk = S.Seen.Where(s => !S.Stickers.ContainsKey(s)).ToList();
            GUILayout.Label($"미분류   <size=20>{(unk.Count > 0 ? string.Join("  ", unk) : "-")}</size>", label);
        }

        void TraitsScreen()
        {
            Back();
            GUILayout.Label($"특성 — 남은 포인트 {S.Points}", big);
            TraitRow(Trait.Decode, "해독", "팬 글마다 문맥 단서 하나 더", "기호가 스티커 한 장으로 풀림");
            TraitRow(Trait.Mimic, "의태", "낮 성공 시 파츠 두 개", "의태 피로 증가 절반");
            TraitRow(Trait.Empathy, "공감", "팬 글 감정이 흐릿한 색으로 보임", "섬뜩이어도 의심도 그대로");
        }

        void TraitRow(Trait t, string n, string l1, string l2)
        {
            GUILayout.BeginVertical(box);
            GUILayout.Label($"<b>{n}</b> Lv{S.Traits[t]}/2\n1: {l1}\n2: {l2}", label);
            Btn("올리기", () => S.Raise(t), S.CanRaise(t));
            GUILayout.EndVertical();
        }

        void EndingScreen()
        {
            var (title, text) = Endings[S.Ending];
            GUILayout.Space(200);
            GUILayout.Label(title, big);
            GUILayout.Label(text, label);
            GUILayout.Label($"<color=#aaa>팔로워 {S.Followers} · 의심도 {Mathf.RoundToInt(S.Sus)} · 풀린 기호 {S.SolvedCount}/{Sym.Count}</color>", label);
            Btn("처음부터", () => { S = new GameState(); Go(Screen_.Home); });
        }
    }
}
