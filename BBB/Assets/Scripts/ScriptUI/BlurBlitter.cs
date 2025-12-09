using UnityEngine;

[DefaultExecutionOrder(-50)]
public class BlurBlitter : MonoBehaviour
{
    [Header("IO")]
    public RenderTexture sourceRT;   // PanelSettings_Back.targetTexture
    public RenderTexture blurredRT;  // 최종 출력(RT_Blurred)

    [Header("Blur")]
    public Material blurMat;         // GaussianBlur.shader 머티리얼
    public Shader   blurShader;      // 비워두면 자동 검색
    [Range(0,10)] public int iterations = 2;
    [Range(0.5f,4f)] public float radius = 2f;

    RenderTexture tempA, tempB;

    void OnEnable()
    {
        EnsureMaterial();
        EnsureRTCreated(sourceRT);
        EnsureRTCreated(blurredRT);
        ValidateTemps();
    }

    void OnDisable() => ReleaseTemps();

    void LateUpdate()
    {
        // RT가 런타임 중 파괴되면 즉시 재생성
        EnsureRTCreated(sourceRT);
        EnsureRTCreated(blurredRT);
        EnsureMaterial();
        if (sourceRT == null || blurredRT == null || blurMat == null) return;

        blurMat.SetFloat("_Radius", radius);
        ValidateTemps();

        Graphics.Blit(sourceRT, tempA);

        int it = Mathf.Max(1, iterations);
        for (int i = 0; i < it; i++)
        {
            blurMat.SetVector("_Direction", new Vector2(1f, 0f));
            Graphics.Blit(tempA, tempB, blurMat, 0);

            blurMat.SetVector("_Direction", new Vector2(0f, 1f));
            Graphics.Blit(tempB, tempA, blurMat, 0);
        }

        Graphics.Blit(tempA, blurredRT);
    }

    void EnsureMaterial()
    {
        if (blurMat != null) return;
        if (blurShader == null) blurShader = Shader.Find("UI/GaussianBlurSeparable");
        if (blurShader != null) blurMat = new Material(blurShader) { hideFlags = HideFlags.DontSave };
    }

    static void EnsureRTCreated(RenderTexture rt)
    {
        if (rt == null) return;
        if (rt.width <= 0 || rt.height <= 0) return;
        if (!rt.IsCreated())
        {
            // 에셋 RT도 여기서 안전하게 생성
            rt.Create();
        }
    }

    void ValidateTemps()
    {
        if (sourceRT == null) return;
        if (tempA != null && tempA.width == sourceRT.width && tempA.height == sourceRT.height) return;

        ReleaseTemps();
        tempA = new RenderTexture(sourceRT.width, sourceRT.height, 0, RenderTextureFormat.ARGB32)
        { filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false };
        tempB = new RenderTexture(sourceRT.width, sourceRT.height, 0, RenderTextureFormat.ARGB32)
        { filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false };
        tempA.Create(); tempB.Create();
    }

    void ReleaseTemps()
    {
        if (tempA != null) { tempA.Release(); Destroy(tempA); tempA = null; }
        if (tempB != null) { tempB.Release(); Destroy(tempB); tempB = null; }
    }
}
