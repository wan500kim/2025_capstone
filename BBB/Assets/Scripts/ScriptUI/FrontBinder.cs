using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class FrontBinder : MonoBehaviour
{
    public RenderTexture blurredRT; // RT_Blurred

    VisualElement blurWall;

    void OnEnable()
    {
        var doc = GetComponent<UIDocument>();
        var root = doc.rootVisualElement;
        blurWall = root.Q<VisualElement>("blur_wall");
        if (blurWall == null)
        {
            Debug.LogError("[FrontBinder] 'blur_wall' 요소를 찾을 수 없습니다.");
            return;
        }
        if (blurredRT != null)
        {
            blurWall.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(blurredRT));
            blurWall.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
        }
        Debug.Log(blurWall.pickingMode);
    }
}
