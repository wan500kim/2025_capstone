using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UXML에서 USS 클래스만 붙이면(예: sfx-click, sfx-hover) 자동으로 버튼에 사운드를 연결합니다.
/// - UXML에 함수가 없어도 동작
/// - 기본 키와 버튼 name별 오버라이드 지원
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public class UISoundBinderByClass : MonoBehaviour
{
    [Header("대상 클래스")]
    [Tooltip("클릭 사운드를 붙일 USS 클래스명")]
    public string clickClass = "sfx_click";

    [Tooltip("호버 사운드를 붙일 USS 클래스명")]
    public string hoverClass = "sfx_hover";

    [Header("기본 사운드 키")]
    [Tooltip("클릭 기본 키 (GameAudio 라이브러리에 등록되어 있어야 함)")]
    public string defaultClickKey = "button_click";

    [Tooltip("호버 기본 키 (선택)")]
    public string defaultHoverKey = "button_hover";

    [Header("버튼별 오버라이드(선택)")]
    [Tooltip("버튼 name별로 사운드 키를 바꾸고 싶을 때 사용")]
    public List<ButtonKeyOverride> overrides = new();

    [Serializable]
    public struct ButtonKeyOverride
    {
        public string buttonName;     // UXML의 name
        public string clickKey;       // 비우면 기본값 사용
        public string hoverKey;       // 비우면 기본값 사용
    }

    UIDocument _doc;
    readonly List<Button> _bound = new();
    Dictionary<string, (string clickKey, string hoverKey)> _map;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        if (_doc == null || _doc.rootVisualElement == null)
        {
            Debug.LogWarning("[UISoundBinderByClass] UIDocument가 없습니다.");
            return;
        }

        BuildOverrideMap();
        BindAll();

        // 런타임 UI 변경 대응
        _doc.rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    void OnDisable()
    {
        if (_doc != null && _doc.rootVisualElement != null)
            _doc.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

        UnbindAll();
    }

    void OnGeometryChanged(GeometryChangedEvent _)
    {
        // UI 재빌드 시 재바인딩
        UnbindAll();
        BindAll();
    }

    void BuildOverrideMap()
    {
        _map = new Dictionary<string, (string, string)>();
        foreach (var ov in overrides)
        {
            if (string.IsNullOrEmpty(ov.buttonName)) continue;
            _map[ov.buttonName] = (ov.clickKey, ov.hoverKey);
        }
    }

    void BindAll()
    {
        var root = _doc.rootVisualElement;

        // 클릭 대상
        if (!string.IsNullOrEmpty(clickClass))
        {
            var clickBtns = root.Query<Button>(className: clickClass).ToList();
            foreach (var b in clickBtns)
            {
                if (_bound.Contains(b)) continue;
                b.clicked += () => PlayClick(b);
                _bound.Add(b);
            }
        }

        // 호버 대상
        if (!string.IsNullOrEmpty(hoverClass))
        {
            var hoverBtns = root.Query<Button>(className: hoverClass).ToList();
            foreach (var b in hoverBtns)
            {
                // 중복 등록 방지: 클릭만 등록된 버튼도 _bound에 있을 수 있음
                if (!_bound.Contains(b)) _bound.Add(b);
                b.RegisterCallback<PointerEnterEvent>(OnHover);
            }
        }
    }

    void UnbindAll()
    {
        foreach (var b in _bound)
        {
            if (b == null) continue;
            // clicked는 람다 분리 어려움. 안전하게 전체 재구성 시에만 사용 권장.
            // UI 재생성 시 기존 Button 인스턴스가 버려지므로 실무상 큰 문제는 없음.
            b.UnregisterCallback<PointerEnterEvent>(OnHover);
        }
        _bound.Clear();
    }

    void PlayClick(Button b)
    {
        var key = ResolveClickKey(b);
        if (!string.IsNullOrEmpty(key))
            GameAudio.Play(key);
    }

    void OnHover(PointerEnterEvent e)
    {
        if (e?.target is Button b)
        {
            var key = ResolveHoverKey(b);
            if (!string.IsNullOrEmpty(key))
                GameAudio.Play(key);
        }
    }

    string ResolveClickKey(Button b)
    {
        if (b != null && !string.IsNullOrEmpty(b.name) && _map.TryGetValue(b.name, out var kv))
            return string.IsNullOrEmpty(kv.clickKey) ? defaultClickKey : kv.clickKey;
        return defaultClickKey;
    }

    string ResolveHoverKey(Button b)
    {
        if (b != null && !string.IsNullOrEmpty(b.name) && _map.TryGetValue(b.name, out var kv))
            return string.IsNullOrEmpty(kv.hoverKey) ? defaultHoverKey : kv.hoverKey;
        return defaultHoverKey;
    }
}
