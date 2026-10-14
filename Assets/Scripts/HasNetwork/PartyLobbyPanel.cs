using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Member = PartyProtocol.Member;

// 로비 오른쪽 아래 파티 칸. 파티 버전이면 PartyEdition이 로비 캔버스에 붙인다.
//   파티 없음 : 닉네임, [파티 만들기], 초대 코드 입력 + [참가]
//   방장      : 포트 열기 결과, 초대 코드(가림) [복사] [새 코드], 남은 시간, 참가자 + [내보내기], [파티 닫기]
//   파티원    : 상태, 참가자, [파티 나가기]
[RequireComponent(typeof(RectTransform))]
public class PartyLobbyPanel : MonoBehaviour
{
    public float width = 340f;
    public Vector2 margin = new Vector2(24f, 24f);
    public float fontSize = 16f;

    PartySession session;
    TMP_FontAsset font;
    RectTransform panel, body;
    PartySession.Role builtRole = (PartySession.Role)(-1);
    TMP_Text status, inviteLine, timeLine;
    RectTransform memberList;
    readonly List<(ushort id, TMP_Text location)> locationLabels = new List<(ushort, TMP_Text)>();
    TMP_InputField nicknameInput, codeInput;
    float nextTick;
    float copiedUntil;

    void OnEnable()
    {
        session = PartySession.Ensure();
        font = PartyUi.FindFont();
        Build();
        session.Changed += Refresh;
    }

    void OnDisable()
    {
        if (session != null) session.Changed -= Refresh;
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
    }

    void Build()
    {
        var self = (RectTransform)transform;
        SelectButton.Inset(self, 0f, 0f, 0f, 0f);

        panel = PartyUi.Vertical(transform, "PartyPanel", 8f, new RectOffset(14, 14, 12, 14), PartyUi.Background);
        // 오른쪽 아래 (오른쪽 위는 창이 좁으면 맵 선택 버튼을 가린다). 내용이 늘면 위로 자란다
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-margin.x, margin.y);
        panel.sizeDelta = new Vector2(width, 0f);
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        PartyUi.Text(panel, "파티", font, fontSize + 2f, PartyUi.TextColor, FontStyles.Bold);
        body = PartyUi.Vertical(panel, "Body", 8f);
        Refresh();
    }

    // 역할이 바뀌면 칸을 새로 만들고, 아니면 글자만 고친다 (입력 중인 글자가 지워지지 않게)
    void Refresh()
    {
        if (body == null) return;
        if (session.CurrentRole != builtRole) BuildBody();
        if (memberList != null) BuildMembers();
        UpdateLabels();
    }

    void BuildBody()
    {
        builtRole = session.CurrentRole;
        for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
        status = inviteLine = timeLine = null;
        memberList = null;
        nicknameInput = codeInput = null;
        var buttonSize = new Vector2(150f, 34f);

        switch (builtRole)
        {
            case PartySession.Role.None:
            {
                RectTransform nameRow = PartyUi.Horizontal(body, "Nickname", 8f, 32f);
                PartyUi.Text(nameRow, "닉네임", font, fontSize, PartyUi.SubColor);
                nicknameInput = PartyUi.Input(nameRow, "파티원에게 보일 이름", font, fontSize, 32f, PartyProtocol.MaxNickname);
                nicknameInput.text = session.Nickname;
                nicknameInput.onEndEdit.AddListener(v => session.Nickname = v);

                RectTransform hostRow = PartyUi.Horizontal(body, "Host", 8f);
                PartyUi.Button(hostRow, "파티 만들기", font, new Vector2(width - 28f, 36f), fontSize, () =>
                {
                    SaveNickname();
                    session.Host();
                });

                RectTransform joinRow = PartyUi.Horizontal(body, "Join", 8f, 32f);
                codeInput = PartyUi.Input(joinRow, "초대 코드 붙여넣기", font, fontSize - 2f, 32f, 80);
                PartyUi.Button(joinRow, "참가", font, new Vector2(70f, 32f), fontSize, () =>
                {
                    SaveNickname();
                    session.Join(codeInput.text);
                });

                status = PartyUi.Text(body, "", font, fontSize - 2f, PartyUi.SubColor, wrap: true);
                PartyUi.Text(body, "믿는 파티원끼리만 쓰세요. 방장 PC에 직접 연결하므로 파티원은 방장의 IP를 알 수 있어요.",
                    font, fontSize - 4f, PartyUi.SubColor, FontStyles.Italic, wrap: true);
                break;
            }
            case PartySession.Role.Starting:
            case PartySession.Role.Joining:
                status = PartyUi.Text(body, "", font, fontSize - 1f, PartyUi.SubColor, wrap: true);
                PartyUi.Button(body, "취소", font, buttonSize, fontSize, session.Leave);
                break;

            case PartySession.Role.Hosting:
            {
                status = PartyUi.Text(body, "", font, fontSize - 2f, PartyUi.SubColor, wrap: true);
                RectTransform codeRow = PartyUi.Horizontal(body, "Invite", 8f, 34f);
                inviteLine = PartyUi.Text(codeRow, "", font, fontSize, PartyUi.TextColor, flexible: true);
                PartyUi.Button(codeRow, "복사", font, new Vector2(64f, 32f), fontSize - 1f, CopyInvite);
                PartyUi.Button(codeRow, "새 코드", font, new Vector2(80f, 32f), fontSize - 1f, session.NewInvite);
                timeLine = PartyUi.Text(body, "", font, fontSize - 3f, PartyUi.SubColor);
                memberList = PartyUi.Vertical(body, "Members", 4f);
                PartyUi.Button(body, "파티 닫기", font, buttonSize, fontSize, session.Leave);
                break;
            }
            case PartySession.Role.Joined:
                status = PartyUi.Text(body, "", font, fontSize - 2f, PartyUi.SubColor, wrap: true);
                memberList = PartyUi.Vertical(body, "Members", 4f);
                PartyUi.Button(body, "파티 나가기", font, buttonSize, fontSize, session.Leave);
                break;
        }
    }

    void SaveNickname()
    {
        if (nicknameInput != null) session.Nickname = nicknameInput.text;
    }

    void CopyInvite()
    {
        string code = session.InviteCode;
        if (code == null) return;
        GUIUtility.systemCopyBuffer = code;
        copiedUntil = Time.unscaledTime + 2f;
        UpdateLabels();
    }

    void UpdateLabels()
    {
        if (status != null)
        {
            status.text = session.StatusText;
            status.color = session.StatusOk ? (session.IsInParty ? PartyUi.OkColor : PartyUi.SubColor) : PartyUi.ErrorColor;
            status.gameObject.SetActive(!string.IsNullOrEmpty(session.StatusText));
        }

        if (inviteLine != null)
        {
            string code = session.InviteCode;
            // 코드는 가려서 보여 준다 (방송 화면 등에 IP와 키가 노출되지 않게). [복사]로만 꺼낸다
            inviteLine.text = code == null ? "<color=#888888>코드 만료 — [새 코드]</color>"
                : Time.unscaledTime < copiedUntil ? "<color=#73D973>복사됨 — 디스코드에 붙여넣으세요</color>"
                : "초대 코드  ●●●●-●●●●-●●●●";
        }
        if (timeLine != null)
        {
            double left = (session.InviteExpiresUtc - System.DateTime.UtcNow).TotalSeconds;
            timeLine.text = session.InviteCode == null ? "만료된 코드로는 더 참가할 수 없어요 (이미 들어온 사람은 그대로)"
                : $"남은 시간 {(int)left / 60}:{(int)left % 60:00} · 새 코드를 만들면 이전 코드는 못 써요";
        }

        // 참가자 줄은 파티가 바뀔 때만 새로 만들고(버튼이 눌리는 중에 사라지지 않게), 여기서는 위치 글자만 고친다
        foreach (var (id, label) in locationLabels)
        {
            Member m = session.Members.FirstOrDefault(x => x.id == id);
            if (m != null && label != null) label.text = Location(m);
        }
    }

    void BuildMembers()
    {
        for (int i = memberList.childCount - 1; i >= 0; i--) Destroy(memberList.GetChild(i).gameObject);
        locationLabels.Clear();
        PartyUi.Text(memberList, $"참가자 ({session.Members.Count}/{PartyProtocol.MaxMembers})", font, fontSize - 2f, PartyUi.SubColor);
        foreach (Member m in session.Members.OrderBy(x => x.isHost ? 0 : 1).ThenBy(x => x.id))
        {
            RectTransform row = PartyUi.Horizontal(memberList, m.nickname, 6f, 28f);
            PartyUi.Text(row, "●", font, fontSize, PartyUi.MemberColor(m.color));
            string me = m.id == session.MyId ? " <color=#888888>(나)</color>" : "";
            string star = m.isHost ? " <color=#C3B190>★</color>" : "";
            PartyUi.Text(row, $"<noparse>{m.nickname}</noparse>{star}{me}", font, fontSize, PartyUi.TextColor, flexible: true);
            locationLabels.Add((m.id, PartyUi.Text(row, Location(m), font, fontSize - 2f, PartyUi.SubColor)));
            if (session.IsHost && !m.isHost)
            {
                ushort id = m.id;
                PartyUi.Button(row, "내보내기", font, new Vector2(78f, 26f), fontSize - 3f, () => session.Kick(id));
            }
        }
    }

    static string Location(Member m)
    {
        if (!m.connected) return "연결 끊김";
        if (m.state.status == PartyProtocol.Status.Lobby) return "로비";
        string map = PartyUi.MapName(m.state.map);
        return m.state.hasPosition ? $"{map} · {PartyUi.Age(PartySession.PositionAge(m))}" : map;
    }

    void Update()
    {
        // 남은 시간, 위치 경과 시간 갱신 (1초마다)
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 1f;
        if (session.CurrentRole == PartySession.Role.Hosting || session.CurrentRole == PartySession.Role.Joined) UpdateLabels();
    }
}
