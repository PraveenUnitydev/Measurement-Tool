using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    int index = 0;

    public void Load()
    {
        SceneManager.LoadScene(index);
    }
    private void LoadSceneByIndex(int _index)
    {
       index = _index;
    }

    IEnumerator LoadAsynchronusly()
    {
        yield return null;
        LoadSceneByIndex(0);
    }
}
