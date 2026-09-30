using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneLoader : MonoBehaviour
{

    public static SceneLoader Instance;

    [SerializeField] private Slider progressBar;

    private Animator _animLoader;

    string sceneName = "";

    private void Awake()
    {
        if(Instance == null)
        {
            Instance = this;
        }
    }
    private void Start()
    {
        _animLoader = GetComponent<Animator>();

        if (progressBar != null)
            progressBar.value = 0;

    }
    public void Load()
    {
        progressBar.gameObject.SetActive(true);
        StartCoroutine(LoadSceneAsync());
       
    }
    public void LoadSceneByIndex(string _sceneName)
    {
      sceneName = _sceneName;
        _animLoader.SetTrigger("Load");
    }


    private IEnumerator LoadSceneAsync()
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        operation.allowSceneActivation = false;

        float displayedProgress = 0f;

        while (operation.progress < 0.9f)
        {
            float target = operation.progress / 0.9f;

            displayedProgress = Mathf.Lerp(
                displayedProgress,
                target,
                Time.deltaTime * 5f);

            progressBar.value = displayedProgress;

            yield return null;
        }

        // Stop at 95%
        while (displayedProgress < 0.95f)
        {
            displayedProgress = Mathf.MoveTowards(
                displayedProgress,
                0.95f,
                Time.deltaTime * 0.5f);

            progressBar.value = displayedProgress;
            yield return null;
        }

        operation.allowSceneActivation = true;
    }

}
