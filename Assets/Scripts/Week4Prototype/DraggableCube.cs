using System.Collections;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>마우스로 집을 수 있는 큐브 한 개의 원위치, 현재 격자 칸, 회전을 관리한다.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class DraggableCube : MonoBehaviour
    {
        // 씬을 다시 열거나 Play 모드에 진입해도 격자 참조가 유지되도록 직렬화한다.
        [SerializeField] private GridBoard board;

        [Header("동작 애니메이션")]
        [Min(0f)] [SerializeField] private float returnDuration = 0.28f;
        [Min(0f)] [SerializeField] private float rotationDuration = 0.14f;

        // 직렬화하지 않고 매번 게임 시작 시점의 실제 위치를 원위치로 새로 기록한다.
        private Vector3 gameStartPosition;
        private Quaternion targetRotation;
        private Coroutine returnCoroutine;
        private Coroutine rotationCoroutine;
        private Coroutine fallCoroutine;

        public bool IsPlaced { get; private set; }
        public Vector2Int Cell { get; private set; }

        private void Awake()
        {
            ResolveBoardReference();
            gameStartPosition = transform.position;
            targetRotation = transform.rotation;
        }

        /// <summary>씬 생성 시 이 큐브가 사용할 격자를 연결한다.</summary>
        public void Initialize(GridBoard targetBoard)
        {
            board = targetBoard;

            // 물리 낙하 대신 퍼즐 규칙이 위치를 결정하므로 Rigidbody는 고정한다.
            if (TryGetComponent(out Rigidbody body))
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        /// <summary>드래그 시작 위치를 저장하고 기존 스택에서 잠시 제외한다.</summary>
        public void BeginDrag()
        {
            ResolveBoardReference();

            // 원위치로 복귀하던 중 다시 잡으면 현재 위치에서 즉시 드래그를 이어 간다.
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }

            if (IsPlaced && board != null)
            {
                board.RemoveCube(this, Cell);
                IsPlaced = false;
            }
        }

        /// <summary>마우스를 따라 큐브를 이동시킨다.</summary>
        public void DragTo(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }

        /// <summary>유효한 격자면 자석처럼 붙이고, 아니면 이번 게임의 시작 위치로 되돌린다.</summary>
        public bool EndDrag()
        {
            ResolveBoardReference();
            if (board == null)
            {
                // 잘못 구성된 씬에서도 예외를 발생시키지 않고 게임 시작 위치로 복귀한다.
                ReturnHome();
                Debug.LogError($"{name}: GridBoard를 찾지 못해 큐브를 배치할 수 없습니다.", this);
                return false;
            }

            if (board.TryGetNearestCell(transform.position, out Vector2Int targetCell))
            {
                Cell = targetCell;
                IsPlaced = true;
                SnapImmediately(board.PlaceCube(this, targetCell));
                return true;
            }

            // 격자에 놓였던 큐브라도 밖으로 빼면 이전 칸이 아닌 이번 게임의 시작 자리로 돌려보낸다.
            ReturnHome();
            return false;
        }

        /// <summary>WASD 입력을 90도 단위의 월드 회전으로 적용한다.</summary>
        public void RotateBy(Vector3 axis, float degrees)
        {
            if (axis.sqrMagnitude < 0.001f)
            {
                return;
            }

            // 월드축 회전을 기존 목표에 누적해 빠르게 연속 입력해도 정확한 90도 배수를 유지한다.
            Quaternion delta = Quaternion.AngleAxis(degrees, axis.normalized);
            targetRotation = delta * targetRotation;

            if (rotationCoroutine != null)
            {
                StopCoroutine(rotationCoroutine);
            }

            rotationCoroutine = StartCoroutine(AnimateRotation(transform.rotation, targetRotation));
        }

        /// <summary>스택 재정렬과 드롭 스냅에서 큐브를 즉시 정확한 중심에 배치한다.</summary>
        public void SnapImmediately(Vector3 worldPosition)
        {
            if (fallCoroutine != null)
            {
                StopCoroutine(fallCoroutine);
                fallCoroutine = null;
            }

            transform.position = worldPosition;
        }

        /// <summary>받침이 사라졌을 때 아래 스택 위치까지 짧게 낙하하는 애니메이션을 재생한다.</summary>
        public void FallTo(Vector3 worldPosition, float duration = 0.22f)
        {
            if (fallCoroutine != null)
            {
                StopCoroutine(fallCoroutine);
            }

            fallCoroutine = StartCoroutine(AnimateFall(transform.position, worldPosition, duration));
        }

        /// <summary>용해나 파괴 규칙이 큐브를 제거할 때 격자 등록을 안전하게 해제한다.</summary>
        public void RemoveFromBoardForRule(bool animateCollapse = false)
        {
            ResolveBoardReference();
            if (IsPlaced && board != null)
            {
                board.RemoveCube(this, Cell, animateCollapse);
            }

            IsPlaced = false;
        }

        /// <summary>이전 버전에서 만든 씬처럼 참조가 비어 있으면 현재 씬의 격자를 자동으로 찾는다.</summary>
        private void ResolveBoardReference()
        {
            if (board == null)
            {
                board = FindFirstObjectByType<GridBoard>();
            }
        }

        /// <summary>큐브를 격자에서 해제하고 게임 시작 위치로 부드럽게 되돌린다.</summary>
        private void ReturnHome()
        {
            IsPlaced = false;

            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
            }

            returnCoroutine = StartCoroutine(AnimateReturnHome());
        }

        /// <summary>현재 위치에서 이번 Play 시작 시 기록한 위치까지 Ease Out 보간한다.</summary>
        private IEnumerator AnimateReturnHome()
        {
            Vector3 startPosition = transform.position;
            if (returnDuration <= 0f)
            {
                transform.position = gameStartPosition;
                returnCoroutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < returnDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / returnDuration);
                float easedTime = 1f - Mathf.Pow(1f - normalizedTime, 3f);
                transform.position = Vector3.LerpUnclamped(startPosition, gameStartPosition, easedTime);
                yield return null;
            }

            transform.position = gameStartPosition;
            returnCoroutine = null;
        }

        /// <summary>현재 회전에서 누적된 90도 목표 회전까지 부드럽게 보간한다.</summary>
        private IEnumerator AnimateRotation(Quaternion startRotation, Quaternion endRotation)
        {
            if (rotationDuration <= 0f)
            {
                transform.rotation = endRotation;
                rotationCoroutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < rotationDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / rotationDuration);
                float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);
                transform.rotation = Quaternion.Slerp(startRotation, endRotation, easedTime);
                yield return null;
            }

            transform.rotation = endRotation;
            rotationCoroutine = null;
        }

        private IEnumerator AnimateFall(Vector3 startPosition, Vector3 endPosition, float duration)
        {
            if (duration <= 0f)
            {
                transform.position = endPosition;
                fallCoroutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                float easedTime = normalizedTime * normalizedTime;
                transform.position = Vector3.LerpUnclamped(startPosition, endPosition, easedTime);
                yield return null;
            }

            transform.position = endPosition;
            fallCoroutine = null;
        }
    }
}
