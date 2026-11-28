package ru.isdarkline.mysticego;

import org.junit.Test;
import static org.junit.Assert.*;

public class MobileRoutesTest {
    @Test public void supportedRoutes() {
        for (var entry : MobileRoutes.ROUTES.entrySet())
            assertEquals(MobileRoutes.ORIGIN + entry.getValue(), MobileRoutes.resolve("mysticego://open/" + entry.getKey()));
    }
    @Test public void unknownOrUnsafeDeepLinksReturnHome() {
        for (String input : new String[]{null, "https://example.invalid", "mysticego://evil/assistant",
                "mysticego://open/../assistant", "mysticego://open/%61ssistant",
                "mysticego://open/assistant?token=private", "mysticego://open/assistant#secret",
                "mysticego://user@open/assistant", "mysticego://open:443/assistant", "mysticego://open/unknown"})
            assertEquals(MobileRoutes.HOME, MobileRoutes.resolve(input));
    }
    @Test public void trustedOriginOnly() {
        assertTrue(MobileRoutes.trustedPage(MobileRoutes.HOME));
        assertTrue(MobileRoutes.trustedPage(MobileRoutes.ORIGIN + ":443/profile"));
        for (String url : new String[]{"http://orchestra.isdarkline.ru", "https://orchestra.isdarkline.ru.example",
                "https://orchestra.isdarkline.ru@evil.invalid", "https://evil@orchestra.isdarkline.ru",
                "https://orchestra.isdarkline.ru:444", "file:///data", "javascript:alert(1)", "intent://open"})
            assertFalse(url, MobileRoutes.trustedPage(url));
    }
}
