package ru.isdarkline.mysticego;

import java.net.URI;
import java.util.Map;
import java.util.Collections;
import java.util.LinkedHashMap;

/** Deep links are navigation hints only. No session, URL, private identifier or command is accepted. */
final class MobileRoutes {
    static final String ORIGIN = "https://orchestra.isdarkline.ru";
    static final String HOME = ORIGIN + "/audit-portal/";
    static final Map<String, String> ROUTES;
    static {
        Map<String, String> routes = new LinkedHashMap<>();
        routes.put("home", "/audit-portal/");
        routes.put("orchestra", "/");
        routes.put("profile", "/profile");
        routes.put("collective", "/collective");
        routes.put("assistant", "/assistant");
        routes.put("notifications", "/notifications");
        routes.put("organizations", "/organizations");
        routes.put("settings", "/settings");
        routes.put("audit-profile", "/audit-portal/profile");
        routes.put("audit-collective", "/audit-portal/collective");
        routes.put("audit-assistant", "/audit-portal/assistant");
        routes.put("audit-notifications", "/audit-portal/notifications");
        routes.put("audit-organizations", "/audit-portal/organizations");
        routes.put("audit-settings", "/audit-portal/settings");
        ROUTES = Collections.unmodifiableMap(routes);
    }

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
