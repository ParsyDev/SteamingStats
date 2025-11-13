using UnityEngine;

public class OpenLink : MonoBehaviour
{
    public string url;

    public void OpenURL()
    {
        Application.OpenURL(url);
    }
}
