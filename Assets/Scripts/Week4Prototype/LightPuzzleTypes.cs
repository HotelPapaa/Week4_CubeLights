using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>빛과 전등이 사용하는 가산 혼합 색이다.</summary>
    public enum PuzzleLightColor
    {
        // 기존 Stage 애셋의 직렬화 값을 보존하기 위해 기존 네 색의 숫자는 유지한다.
        White = 0,
        Red = 1,
        Blue = 2,
        Yellow = 3,
        Green = 4,
        Cyan = 5,
        Magenta = 6
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

    /// <summary>시각화에 전달할 빛의 이동 구간이다. 격자 밖으로 빠져나가는 마지막 구간은 여러 칸일 수 있다.</summary>
    public readonly struct LightBeamSegment
    {
        public readonly Vector3Int From;
        public readonly Vector3Int To;
        public readonly PuzzleLightColor Color;
        public readonly int SequenceStep;

        public LightBeamSegment(
            Vector3Int from,
            Vector3Int to,
            PuzzleLightColor color,
            int sequenceStep = 0)
        {
            From = from;
            To = to;
            Color = color;
            SequenceStep = sequenceStep;
        }
    }

    /// <summary>논리 광선 구간을 큐브 면에 맞춰 그릴 때 사용하는 공통 판정이다.</summary>
    public static class LightBeamVisualUtility
    {
        /// <summary>
        /// 두 구간이 같은 방향으로 이어지면서 색만 달라지는지 확인한다.
        /// 이 경우 색 경계는 공유 격자의 중심이 아니라 그 격자로 들어가는 면에 놓여야 한다.
        /// </summary>
        public static bool HasColorBoundary(
            LightBeamSegment previous,
            LightBeamSegment next)
        {
            Vector3Int previousDirection = previous.To - previous.From;
            Vector3Int nextDirection = next.To - next.From;
            return previous.To == next.From &&
                   previousDirection == nextDirection &&
                   previous.Color != next.Color;
        }
    }

    /// <summary>전등 하나가 올바른 방향과 색의 빛을 받았는지 기록한다.</summary>
    public sealed class LampLightResult
    {
        private int receivedColorMask;
        private bool hasFrontHit;
        private bool hasWrongDirectionHit;
        private readonly List<PuzzleLightColor> receivedColors = new();

        public LampTarget Target { get; }
        public bool HasCorrectHit { get; private set; }
        public bool HasInvalidHit { get; private set; }
        public bool IsLatchedOn { get; private set; }
        public bool HasFrontHit => hasFrontHit;
        public bool HasWrongDirectionHit => hasWrongDirectionHit;
        public IReadOnlyList<PuzzleLightColor> ReceivedColors => receivedColors;
        public PuzzleLightColor ReceivedColor =>
            LightDirectionUtility.FromAdditiveMask(receivedColorMask);

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
            if (!isFrontHit)
            {
                hasWrongDirectionHit = true;
                HasInvalidHit = true;
                return;
            }

            // 같은 전등의 정면에 도달한 모든 광선의 RGB 채널을 합쳐 최종 색을 만든다.
            // 비트 OR를 사용하므로 광선 계산 순서와 같은 색의 중복 입사에 영향을 받지 않는다.
            hasFrontHit = true;
            if (!receivedColors.Contains(color))
            {
                receivedColors.Add(color);
            }

            receivedColorMask |= LightDirectionUtility.ToAdditiveMask(color);
            HasCorrectHit = ReceivedColor == Target.requiredColor;
            HasInvalidHit = hasWrongDirectionHit || !HasCorrectHit;
        }

        /// <summary>한 번 켜진 전등은 이후 광선 상태와 관계없이 현재 스테이지 동안 점등을 유지한다.</summary>
        public void LatchOn()
        {
            IsLatchedOn = true;
            HasCorrectHit = true;
            HasInvalidHit = false;
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
        private const int RedChannel = 1 << 0;
        private const int GreenChannel = 1 << 1;
        private const int BlueChannel = 1 << 2;
        private const int WhiteChannels = RedChannel | GreenChannel | BlueChannel;

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
                PuzzleLightColor.Red => new Color(1f, 0.02f, 0.02f),
                PuzzleLightColor.Green => new Color(0.02f, 1f, 0.08f),
                PuzzleLightColor.Blue => new Color(0.02f, 0.18f, 1f),
                PuzzleLightColor.Yellow => new Color(1f, 0.92f, 0.02f),
                PuzzleLightColor.Cyan => new Color(0.02f, 1f, 1f),
                PuzzleLightColor.Magenta => new Color(1f, 0.02f, 0.85f),
                // 주광색(약 6500K)에 가까운 차갑고 중립적인 흰색을 기본 광원색으로 사용한다.
                _ => Color.white
            };
        }

        /// <summary>퍼즐 색을 RGB 가산 혼합용 채널 비트로 변환한다.</summary>
        public static int ToAdditiveMask(PuzzleLightColor color)
        {
            return color switch
            {
                PuzzleLightColor.Red => RedChannel,
                PuzzleLightColor.Green => GreenChannel,
                PuzzleLightColor.Blue => BlueChannel,
                PuzzleLightColor.Yellow => RedChannel | GreenChannel,
                PuzzleLightColor.Cyan => GreenChannel | BlueChannel,
                PuzzleLightColor.Magenta => RedChannel | BlueChannel,
                _ => WhiteChannels
            };
        }

        /// <summary>RGB 채널 조합을 퍼즐에서 사용하는 일곱 색 중 하나로 변환한다.</summary>
        public static PuzzleLightColor FromAdditiveMask(int mask)
        {
            return (mask & WhiteChannels) switch
            {
                RedChannel => PuzzleLightColor.Red,
                GreenChannel => PuzzleLightColor.Green,
                BlueChannel => PuzzleLightColor.Blue,
                RedChannel | GreenChannel => PuzzleLightColor.Yellow,
                GreenChannel | BlueChannel => PuzzleLightColor.Cyan,
                RedChannel | BlueChannel => PuzzleLightColor.Magenta,
                _ => PuzzleLightColor.White
            };
        }

        /// <summary>두 빛을 가산 혼합한 결과를 반환한다.</summary>
        public static PuzzleLightColor Mix(PuzzleLightColor first, PuzzleLightColor second)
        {
            return FromAdditiveMask(ToAdditiveMask(first) | ToAdditiveMask(second));
        }

        /// <summary>
        /// 색유리를 통과할 때 첫 색은 기본 흰빛을 착색하고, 두 번째 색부터 기존 색과 가산 혼합한다.
        /// 기본 흰빛과 RGB 혼합 결과인 흰색을 구별하기 위해 누적 여부를 호출자가 함께 관리한다.
        /// </summary>
        public static PuzzleLightColor ApplyColorFilter(
            PuzzleLightColor currentColor,
            PuzzleLightColor filterColor,
            bool hasAccumulatedColor)
        {
            return hasAccumulatedColor
                ? Mix(currentColor, filterColor)
                : filterColor;
        }
    }
}
