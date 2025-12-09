using UnityEngine;

public static class NameStore
{
    const string Key = "playerName";

    public static string Current
    {
        get
        {
            var v = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrWhiteSpace(v))
                v = $"Player{Random.Range(1000,9999)}";
            return v;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            PlayerPrefs.SetString(Key, value.Trim());
            PlayerPrefs.Save();
        }
    }
}