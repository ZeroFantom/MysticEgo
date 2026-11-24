namespace MysticEgo.Avatar;
public static class AvatarProviders
{
    public static object List() => new[]
    {
        new { id = "procedural", pricing = "free", mode = "client", state = "available", notice = "idle, blink, nod, wave, dance, talk; no skeletal OBJ rig" },
        new { id = "mixamo", pricing = "free", mode = "manual_import", state = "external", notice = "No supported public API confirmed; manual asset import" },
        new { id = "deepmotion", pricing = "paid", mode = "contract_api", state = "not_connected", notice = "Official API contract required" }
    };
}
