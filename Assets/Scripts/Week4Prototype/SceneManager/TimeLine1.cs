using UnityEngine;
using UnityEngine.Playables; // PlayableDirector를 사용하기 위해 필요
using UnityEngine.SceneManagement; // 씬 전환을 위해 필요

public class Timeline1 : MonoBehaviour
{
    [SerializeField] private PlayableDirector playableDirector;
    [SerializeField] private string nextSceneName; // 이동할 다음 씬의 이름

    private void OnEnable()
    {
        // 타임라인 재생이 중지/종료되었을 때 실행될 메서드 등록
        playableDirector.stopped += OnTimelineStopped;
    }

    private void OnDisable()
    {
        // 메모리 누수 방지를 위한 이벤트 해제
        playableDirector.stopped -= OnTimelineStopped;
    }

    private void OnTimelineStopped(PlayableDirector director)
    {
        // 이벤트가 발생한 디렉터가 우리가 지정한 디렉터인지 확인 후 씬 전환
        if (director == playableDirector)
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
