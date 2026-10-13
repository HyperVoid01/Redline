using UnityEngine;

[CreateAssetMenu(fileName = "SettingsData", menuName = "Scriptable Objects/Settings Data")]
public class SettingsData : ScriptableObject
{
    [Range(0f, 1f)] public float mainVolume;
    [Range(0f, 1f)] public float musicVolume;
    [Range(0f, 1f)] public float sfxVolume;
}
