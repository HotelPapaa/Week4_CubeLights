using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>빛과 전등이 사용하는 네 가지 색이다.</summary>
    public enum PuzzleLightColor
    {
        White,
        Red,
        Blue,
        Yellow
    }

    /// <summary>90도 회전 퍼즐에서 사용하는 여섯 개의 격자 방향이다.</summary>
    public enum CardinalDirection
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }

    /// <summary>격자 바깥에 배치되는 전등 하나의 위치, 정면, 요구 색을 정의한다.</summary>
    [Serializable]
    public struct LampTarget
    {
        public Vector3Int gridPosition;
        public CardinalDirection frontDirection;
        public PuzzleLightColor requiredColor;

        public LampTarget(
            Vector3Int position,
            CardinalDirection lampFrontDirection,
            PuzzleLightColor color = PuzzleLightColor.White)
        {
            gridPosition = position;
            frontDirection = lampFrontDirection;
            requiredColor = color;
        }
    }

    /// <summary>시각화에 전달할 빛의 한 칸짜리 이동 구간이다.</summary>
    public readonly struct LightBeamSegment
    {
        public readonly Vector3Int From;
        public readonly Vector3Int To;
        public readonly PuzzleLightColor Color;

        public LightBeamSegment(Vector3Int from, Vector3Int to, PuzzleLightColor color)
        {
            From = from;
            To = to;
            Color = color;
        }
    }

    /// <summary>전등 하나가 올바른 방향과 색의 빛을 받았는지 기록한다.</summary>
    public sealed class LampLightResult
    {
        public LampTarget Target { get; }
        public bool HasCorrectHit { get; private set; }
        public bool HasInvalidHit { get; private set; }

        public bool IsSatisfied => HasCorrectHit && !HasInvalidHit;

        public LampLightResult(LampTarget target)
        {
            Target = target;
        }

        public void RegisterHit(Vector3Int travelDirection, PuzzleLightColor color)
        {
            // 전등의 frontDirection은 전등이 바라보는 방향이므로 빛은 그 반대 방향으로 들어와야 한다.
            Vector3Int expectedTravelDirection = -LightDirectionUtility.ToVector(Target.frontDirection);
            bool isFrontHit = travelDirection == expectedTravelDirection;
            bool isCorrectColor = color == Target.requiredColor;
            if (isFrontHit && isCorrectColor)
            {
                HasCorrectHit = true;
            }
            else
            {
                HasInvalidHit = true;
            }
        }
    }

    /// <summary>한 번의 점등에서 계산한 광선, 전등, 얼음 반응 결과다.</summary>
    public sealed class LightSimulationResult
    {
        public List<LightBeamSegment> Segments { get; } = new();
        public List<LampLightResult> Lamps { get; } = new();
        public HashSet<PuzzleCubeProperties> IlluminatedIce { get; } = new();

        public bool IsSolved
        {
            get
            {
                if (Lamps.Count == 0) return false;
                foreach (LampLightResult lamp in Lamps)
                {
                    if (!lamp.IsSatisfied) return false;
                }

                return true;
            }
        }
    }

    /// <summary>enum 방향과 Vector3Int 사이의 변환을 한 곳에서 담당한다.</summary>
    public static class LightDirectionUtility
    {
        public static Vector3Int ToVector(CardinalDirection direction)
        {
            return direction switch
            {
                CardinalDirection.PositiveX => Vector3Int.right,
                CardinalDirection.NegativeX => Vector3Int.left,
                CardinalDirection.PositiveY => Vector3Int.up,
                CardinalDirection.NegativeY => Vector3Int.down,
                CardinalDirection.PositiveZ => new Vector3Int(0, 0, 1),
                _ => new Vector3Int(0, 0, -1)
            };
        }

        public static Vector3Int Quantize(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return Vector3Int.zero;

            Vector3 absolute = new(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            {
                return direction.x >= 0f ? Vector3Int.right : Vector3Int.left;
            }

            if (absolute.y >= absolute.z)
            {
                return direction.y >= 0f ? Vector3Int.up : Vector3Int.down;
            }

            return direction.z >= 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
        }

        public static Color ToDisplayColor(PuzzleLightColor color)
        {
            return color switch
            {
                PuzzleLightColor.Red => new Color(1f, 0.12f, 0.08f),
                PuzzleLightColor.Blue => new Color(0.08f, 0.35f, 1f),
                PuzzleLightColor.Yellow => new Color(1f, 0.75f, 0.06f),
                // 주광색(약 6500K)에 가까운 차갑고 중립적인 흰색을 기본 광원색으로 사용한다.
                _ => new Color(0.88f, 0.94f, 1f)
            };
        }
    }
}
