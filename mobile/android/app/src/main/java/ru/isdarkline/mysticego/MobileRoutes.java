package ru.isdarkline.mysticego;

import java.net.URI;
import java.util.Map;

/** Deep links are navigation hints only. No session, URL, private identifier or command is accepted. */
final class MobileRoutes {
    static final String ORIGIN = "https://orchestra.isdarkline.ru";
    static final String HOME = ORIGIN + "/audit-portal/";
    static final Map<String, String> ROUTES = Map.ofEntries(
        Map.entry("home", "/audit-portal/"),
        Map.entry("orchestra", "/"),
        Map.entry("profile", "/profile"),
        Map.entry("collective", "/collective"),
        Map.entry("assistant", "/assistant"),
        Map.entry("notifications", "/notifications"),
        Map.entry("organizations", "/organizations"),
        Map.entry("settings", "/settings"),
        Map.entry("audit-profile", "/audit-portal/profile"),
        Map.entry("audit-collective", "/audit-portal/collective"),
        Map.entry("audit-assistant", "/audit-portal/assistant"),
        Map.entry("audit-notifications", "/audit-portal/notifications"),
        Map.entry("audit-organizations", "/audit-portal/organizations"),
        Map.entry("audit-settings", "/audit-portal/settings")
    );

    private MobileRoutes() {}

    static String resolve(String input) {
        if (input == null || input.length() > 200) return HOME;
        try {
            URI uri = URI.create(input);
            if (!"mysticego".equals(uri.getScheme()) || !"open".equals(uri.getHost())
                || uri.getRawUserInfo() != null || uri.getPort() != -1
                || uri.getRawQuery() != null || uri.getRawFragment() != null) return HOME;
            String path = uri.getRawPath();
            if (path == null || !path.startsWith("/")) return HOME;
            String route = ROUTES.get(path.substring(1));
            return route == null ? HOME : ORIGIN + route;
        } catch (IllegalArgumentException ex) { return HOME; }
    }

    static boolean trustedPage(String input) {
        if (input == null) return false;
        try {
            URI uri = URI.create(input);
            return "https".equals(uri.getScheme()) && "orchestra.isdarkline.ru".equals(uri.getHost())
                && uri.getRawUserInfo() == null && (uri.getPort() == -1 || uri.getPort() == 443);
        } catch (IllegalArgumentException ex) { return false; }
    }
}
