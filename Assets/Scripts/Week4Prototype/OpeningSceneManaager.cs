using UnityEngine;
using UnityEngine.SceneManagement;

public class OpeningSceneManaager : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void ChangeScene(string sceneName)
    {
        SceneManager.LoadScene("SampleScene_Test"); // 씬 이름으로 이동
    }
}

