using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

public class UISfx : MonoBehaviour
{
    public static UISfx Instance { get; private set; }

    [Header("Common UI Clips")]
    public AudioClip hover;
    public AudioClip click;
    public AudioClip deny;

    AudioSource src;

    void Awake()
    {
        Instance = this;
        src = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f; // 2D
    }

    public void Play(AudioClip clip, float vol = 1f)
    {
        if (!clip) return;
        src.PlayOneShot(clip, vol); // AudioSource.clip 에 아무것도 안 넣어도 됨
    }
}

