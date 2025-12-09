using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

public class ButtonSfx : MonoBehaviour, IPointerEnterHandler
{
    public enum Kind { Hover, Click, Deny }

    [Header("어떤 소리를 낼지(종류)")]
    public Kind hoverKind = Kind.Hover;
    public Kind clickKind = Kind.Click;

    [Header("필요하면 이 버튼만의 전용 클립으로 덮어쓰기")]
    public AudioClip overrideHover;
    public AudioClip overrideClick;

    Button btn;

    void Awake()
    {
        btn = GetComponent<Button>();
        if (btn) btn.onClick.AddListener(OnClick);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        Play(hoverKind, overrideHover);
    }

    void OnClick()
    {
        Play(clickKind, overrideClick);
    }

    void Play(Kind kind, AudioClip overrideClip)
    {
        if (!UISfx.Instance) return;
        var s = UISfx.Instance;

        // 버튼 전용 오버라이드 클립이 있으면 그걸 재생
        if (overrideClip) { s.Play(overrideClip); return; }

        // 아니면 매니저에 등록된 공통 클립 사용
        switch (kind)
        {
            case Kind.Hover: s.Play(s.hover); break;
            case Kind.Click: s.Play(s.click); break;
            case Kind.Deny:  s.Play(s.deny);  break;
        }
    }
}
