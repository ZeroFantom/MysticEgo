package ru.isdarkline.mysticego;

/** Exact local WebView action links; never an exported Android intent or arbitrary target. */
final class MobileLocalActions {
    private MobileLocalActions() {}
    static String resolve(String value) {
        if("mysticego://local-action/signal-device".equals(value))return "signal-device";
        if("mysticego://local-action/open-app-settings".equals(value))return "open-app-settings";
        if("mysticego://local-action/show-device-info".equals(value))return "show-device-info";
        return null;
    }
}
