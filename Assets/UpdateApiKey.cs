using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class UpdateApiKey : MonoBehaviour
{
    public TMP_InputField apiKeyInputField;

    public void OnApiKeyChanged()
    {
        if (apiKeyInputField != null)
        {
            if(string.IsNullOrEmpty(apiKeyInputField.text))
                return;
            PlayerPrefs.SetString("SteamPartnerApiKey", apiKeyInputField.text);
            PlayerPrefs.Save();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
