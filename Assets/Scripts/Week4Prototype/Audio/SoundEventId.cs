namespace GameLab.Week4
{
    /// <summary>게임 로직이 SoundManager에게 알리는 의미 단위의 사운드 이벤트다.</summary>
    public enum SoundEventId
    {
        None = 0,
        CubePickup,
        CubePlace,
        CubeReturn,
        CubeRotate,
        CubeFall,
        CubeLand,
        BrittleBreak,
        IceMelt,
        LightOn,
        LightOff,
        LaserLoop,
        LightRefract,
        LightSplit,
        LightColorChange,
        LampOn,
        PuzzleSolved,
        StageLoad,
        StageRestart,
        UiClick,
        MainBgm,
        RoomAmbience
    }

    /// <summary>AudioMixer와 AudioSource 채널을 나누는 논리 버스다.</summary>
    public enum SoundBus
    {
        Master = 0,
        Bgm = 1,
        Ambience = 2,
        Sfx = 3,
        Ui = 4
    }
}
