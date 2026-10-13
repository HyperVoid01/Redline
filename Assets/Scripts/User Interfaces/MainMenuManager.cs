using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color emissionColor = Color.red;
    [SerializeField] private float intensity = 2f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private Material material;

    private void Awake()
    {
        material = targetRenderer.material;
        material.EnableKeyword("_EMISSION");
        SetIntensity(intensity); // apply the starting intensity
    }

    private void OnDestroy()
    {
        // .material makes a copy, so clean it up
        if (material != null)
        {
            Destroy(material);
        }
    }

    public void SetIntensity(float value)
    {
        material.SetColor(EmissionColorId, emissionColor * value);
    }

    public void OnPlayClick()
    {
        SoundPlayer.Instance.PlaySound(SoundID.Click, Vector3.zero);
        SetIntensity(intensity);
        SceneManager.LoadScene(1);
    }
}