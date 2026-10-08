public enum PlayerSide
{
    Pmc,
    Scav,
}

// 로비에서 고른 값을 맵 씬으로 넘긴다 (정적 값이라 씬이 바뀌어도 유지됨)
public static class GameSession
{
    public static MapConfig Map;
    public static string MapPath;   // MapCatalog.Entry.configPath (로비 선택 복원용)
    public static PlayerSide Side = PlayerSide.Pmc;

    // 로비를 거치지 않고 맵 씬을 바로 실행하면 false
    public static bool HasSelection => Map != null;
}
