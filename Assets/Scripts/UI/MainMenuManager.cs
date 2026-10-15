using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject settingsPanel;
    
    [SerializeField] private Renderer titleRenderer;
    [SerializeField] private Renderer settingsRenderer;
    [SerializeField] private Color titleEmissionColor = Color.red;
    [SerializeField] private Color settingsEmissionColor = Color.red;
    [SerializeField] private float titleIntensity = 2f;
    [SerializeField] private float settingsIntensity = 2f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private Material titleMaterial;
    private Material settingsMaterial;

    private void Awake()
    {
        titleMaterial = titleRenderer.material;
        titleMaterial.EnableKeyword("_EMISSION");
        
        settingsMaterial = settingsRenderer.material;
        settingsMaterial.EnableKeyword("_EMISSION");
        
        SetTitleIntensity(titleIntensity); // apply the starting intensity
        SetSettingsIntensity(settingsIntensity);
        
    }

    private void OnDestroy()
    {
        // .material makes a copy, so clean it up
        if (titleMaterial != null)
        {
            Destroy(titleMaterial);
        }

        if (settingsMaterial != null)
        {
            Destroy(settingsMaterial);
        }
    }

    public void ShowSettings()
    {
        SoundPlayer.Instance.PlaySound(SoundID.Click, Vector3.zero);
        SetSettingsIntensity(settingsIntensity);
        mainMenuPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }
    
    public void HideSettings()
    {
        SoundPlayer.Instance.PlaySound(SoundID.Click, Vector3.zero);
        mainMenuPanel.SetActive(true);
        settingsPanel.SetActive(false);
    }

    public void SetTitleIntensity(float value)
    {
        titleMaterial.SetColor(EmissionColorId, titleEmissionColor * value);
    }
    
    public void SetSettingsIntensity(float value)
    {
        settingsMaterial.SetColor(EmissionColorId, settingsEmissionColor * value);
    }

    public void OnPlayClick()
    {
        SoundPlayer.Instance.PlaySound(SoundID.Click, Vector3.zero);
        SetTitleIntensity(titleIntensity);
        SceneManager.LoadScene(1);
    }
}