namespace GameLab.Week4
{
    /// <summary>반복음이나 긴 사운드를 후에 멈출 때 사용하는 재생 핸들이다.</summary>
    public readonly struct SoundHandle
    {
        internal readonly int Token;

        internal SoundHandle(int token)
        {
            Token = token;
        }

        public bool IsValid => Token > 0;
        public static SoundHandle Invalid => default;
    }
}
